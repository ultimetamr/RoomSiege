using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if ENABLE_PICO_OPENXR_SDK
using ByteDance.PICO.OpenXR;
using ByteDance.PICO.XR;
#endif

namespace RoomSiege.Scan
{
    public interface IRoomScanner
    {
        bool TryEnablePassthrough();
        Task<RoomSnapshot> ScanAsync(Transform trackingSpace,CancellationToken token,Action<string> progress,bool requestCapture=false);
    }
    // Verified against SDK 6.1.1 PXR_MixedReality and PXR_SceneCaptureManager sources.
    public sealed class PicoRoomScanner:IRoomScanner
    {
        public bool TryEnablePassthrough()=>SetPassthrough(true);
        private readonly RoomScanQueue queue = new RoomScanQueue();
        private int providerRevision=-1;
        public Task<RoomSnapshot> ScanAsync(Transform trackingSpace, CancellationToken token, Action<string> progress,bool requestCapture=false)
        {
            return queue.Run(()=>ReadRoom(trackingSpace, token, progress,requestCapture),token);
        }

        private async Task<RoomSnapshot> ReadRoom(Transform trackingSpace, CancellationToken token, Action<string> progress,bool requestCapture)
        {
#if ENABLE_PICO_OPENXR_SDK && UNITY_ANDROID && !UNITY_EDITOR
            // Cold startup can reach Start before OpenXR advertises its extensions.
            for(int attempt=0;attempt<40&&(!PICOSceneCapture.isEnable||!PassthroughFeature.isExtensionEnable);attempt++)
            {
                progress("正在恢复已保存的真实房间，等待空间追踪就绪…");
                await Task.Delay(250,token);
            }
            if (!PICOSceneCapture.isEnable || !PassthroughFeature.isExtensionEnable)
                throw new NotSupportedException("运行时缺少 Scene Capture 或 Passthrough 扩展。");
            progress("请在设备上处理空间数据权限；系统确认由你选择。");
            await EnsureSpatialPermission(token);
            progress("正在等待头显回到应用并稳定定位…");
            await EnsureLocatedProvider(token);
            // An explicit button press means rescan even when stale or incomplete
            // anchors exist. Startup/background reads never open a system page.
            if(requestCapture)
            {
                progress("已请求系统房间扫描：请在头显中完成地板、门洞和房间边界，然后保存返回；权限由你选择");
                Require(await PXR_MixedReality.StartSceneCaptureAsync(token),"系统房间扫描");
                token.ThrowIfCancellationRequested();
                progress("系统扫描已返回，正在恢复房间定位…");
                await EnsureLocatedProvider(token,true);
            }
            RoomSnapshot room=null;
            var anchors = new List<(ulong handle, string id, PxrSemanticLabel label, Vector3 p, Quaternion r)>();
            string diagnostic="";
            // Native capture can finish before the located geometry is ready.
            // Query fresh snapshots rather than trusting cached default poses.
            for(int attempt=0;attempt<8;attempt++)
            {
                progress($"正在定位房间地板… 重试 {attempt+1} 次");
                var query=await RoomAnchorRecovery.ReadAsync(
                    t=>EnsureLocatedProvider(t),
                    async t=>
                    {
                        int queryRevision=RoomTrackingReady.Revision;
                        var snapshot=await PXR_MixedReality.QuerySceneAnchorAsync(t);
                        t.ThrowIfCancellationRequested();
                        // A successful native query spanning suspension may contain
                        // stale handles; discard it and reconnect on the next read.
                        if(queryRevision!=RoomTrackingReady.Revision)
                            return (result:PxrResult.SUCCESS,anchorDictionary:new Dictionary<ulong,Guid>());
                        if(snapshot.result==PxrResult.ERROR_SESSION_NOT_RUNNING||
                           snapshot.result==PxrResult.ERROR_SESSION_NOT_READY||
                           snapshot.result==PxrResult.ERROR_FILE_ACCESS_ERROR)
                        {
                            Debug.LogWarning("[RoomSiege.Scan] Transient room read failure: "+snapshot.result);
                            return (result:PxrResult.SUCCESS,anchorDictionary:new Dictionary<ulong,Guid>());
                        }
                        Require(snapshot.result,"读取房间锚点");
                        return snapshot;
                    },
                    snapshot=>snapshot.anchorDictionary==null||snapshot.anchorDictionary.Count==0,
                    async t=>
                    {
                        progress("房间锚点暂未就绪，正在重连空间数据服务…");
                        Debug.LogWarning("[RoomSiege.Scan] Empty anchor snapshots; restarting Scene Capture provider once.");
                        await EnsureLocatedProvider(t,true);
                    },t=>Task.Delay(500,t),token);
                token.ThrowIfCancellationRequested();Require(query.result,"读取房间锚点");
                room=new RoomSnapshot();anchors.Clear();
                int floors=0,missingPose=0,missingPolygon=0,rejected=0;
                foreach(var item in query.anchorDictionary)
                {
                    if(PXR_MixedReality.GetSceneSemanticLabel(item.Key,out var label)!=PxrResult.SUCCESS)continue;
                    if(label==PxrSemanticLabel.Floor)floors++;
                    if(PXR_MixedReality.LocateAnchor(item.Key,out var p,out var r)!=PxrResult.SUCCESS||
                        !Finite(p)||!ValidRotation(r)){missingPose++;continue;}
                    anchors.Add((item.Key,item.Value.ToString(),label,p,r));
                    if(label!=PxrSemanticLabel.Floor)continue;
                    var result=PXR_MixedReality.GetScenePolygonData(item.Key,out var vertices);
                    if(result!=PxrResult.SUCCESS||vertices==null||vertices.Length<3)
                    {
                        // Some captures expose a bounded floor plane instead of a polygon.
                        if(PXR_MixedReality.GetSceneBox2DData(item.Key,out var offset,out var extent)!=PxrResult.SUCCESS||extent.x<=0||extent.y<=0)
                        {missingPolygon++;continue;}
                        vertices=new[]{offset+new Vector2(-extent.x,-extent.y)*.5f,offset+new Vector2(extent.x,-extent.y)*.5f,
                            offset+new Vector2(extent.x,extent.y)*.5f,offset+new Vector2(-extent.x,extent.y)*.5f};
                    }
                    if(!TryAddFloor(room,item.Value.ToString(),trackingSpace,p,r,vertices))
                    {
                        rejected++;
                        Debug.LogWarning($"[RoomSiege.Scan] Rejected floor pose={p:F3} rotation={r.eulerAngles:F2} vertices={vertices.Length}");
                    }
                }
                diagnostic=$"锚点{query.anchorDictionary.Count} / 地板{floors} / 无效位姿{missingPose} / 无边界{missingPolygon} / 非水平或无效地板{rejected}";
                Debug.Log("[RoomSiege.Scan] "+diagnostic);
                if(room.Surfaces.Count>0)break;
                if(attempt<7)await Task.Delay(500,token);
            }
            if(room.Surfaces.Count==0)throw new InvalidOperationException("没有有效地板（"+diagnostic+"）。请点扫描真实房间，完成系统扫描并保存");
            room.FloorHeight=room.Surfaces[0].Height;
            var floorCenter=Vector3.zero;
            int floorVertexCount=0;
            foreach(var surface in room.Surfaces)
            foreach(var v in surface.Polygon) { floorCenter+=new Vector3(v.x,room.FloorHeight,v.y); floorVertexCount++; }
            floorCenter/=floorVertexCount;
            foreach(var anchor in anchors)
            {
                if (anchor.label==PxrSemanticLabel.Door || anchor.label==PxrSemanticLabel.Opening)
                {
                    if(PXR_MixedReality.GetSceneBox2DData(anchor.handle,out var offset,out var extent)!=PxrResult.SUCCESS ||
                       extent.x<0.5f || extent.y<1.2f) continue;
                    var bottom=ToWorld(trackingSpace,anchor.p+anchor.r*new Vector3(offset.x,offset.y-extent.y/2,0f));
                    bottom.y=room.FloorHeight;
                    var forward=ToWorldRotation(trackingSpace,anchor.r)*Vector3.forward;
                    forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
                    if(Vector3.Dot(forward,floorCenter-bottom)<0f) forward=-forward;
                    if(forward.sqrMagnitude<0.5f) continue;
                    room.Entrances.Add(new SpawnPoint(anchor.id,bottom+forward*0.2f,forward,new Vector2(extent.x,0.8f)));
                    continue;
                }
                if(PXR_MixedReality.GetSceneBox3DData(anchor.handle,out var center,out var rotation,out var size)!=PxrResult.SUCCESS) continue;
                if(!Finite(size) || size.x<=0f || size.y<=0f || size.z<=0f) continue;
                var worldCenter=ToWorld(trackingSpace,anchor.p+anchor.r*center);
                var worldRotation=BoxWorldRotation(trackingSpace,anchor.r,rotation);
                bool horizontal=Vector3.Dot(worldRotation*Vector3.up,Vector3.up)>0.97f;
                float top=worldCenter.y+size.y/2f;
                bool deploy=horizontal && (anchor.label==PxrSemanticLabel.Table || anchor.label==PxrSemanticLabel.Cabinet) &&
                            top-room.FloorHeight>=0.25f && top-room.FloorHeight<=1.2f;
                room.Volumes.Add(new RoomVolume { Id=anchor.id,Center=worldCenter,Rotation=worldRotation,Size=size,SupportsDeployment=deploy });
                if(!deploy) continue;
                var corners=new[] {new Vector3(-1,1,-1),new Vector3(1,1,-1),new Vector3(1,1,1),new Vector3(-1,1,1)};
                for(int i=0;i<corners.Length;i++) corners[i]=worldCenter+worldRotation*Vector3.Scale(corners[i],size/2f);
                AddSurface(room,anchor.id,anchor.label==PxrSemanticLabel.Table ? SurfaceKind.Table : SurfaceKind.LowCabinet,corners);
            }
            return room;
#else
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            throw new NotSupportedException("Editor/非 Android 无原生房间感知，使用模拟沙盘。");
#endif
        }

#if ENABLE_PICO_OPENXR_SDK
        private static async Task EnsureSpatialPermission(CancellationToken token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string permission="com.picovr.permission.SPATIAL_DATA";
            if(UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))return;
            var completion=new TaskCompletionSource<bool>();
            var callbacks=new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted+=_=>completion.TrySetResult(true);
            callbacks.PermissionDenied+=_=>completion.TrySetResult(false);
            callbacks.PermissionDeniedAndDontAskAgain+=_=>completion.TrySetResult(false);
            using(token.Register(()=>completion.TrySetCanceled()))
            {
                UnityEngine.Android.Permission.RequestUserPermission(permission,callbacks);
                if(!await completion.Task)throw new InvalidOperationException("空间数据权限未允许，请在系统应用权限中开启后重试");
            }
#else
            await Task.CompletedTask;
#endif
            token.ThrowIfCancellationRequested();
        }

        private static async Task EnsureProvider(CancellationToken token)
        {
            var result=PXR_MixedReality.GetSenseDataProviderState(PxrSenseDataProviderType.SceneCapture,out var state);
            if(result==PxrResult.SUCCESS&&state==PxrSenseDataProviderState.Running)return;
            Require(await PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.SceneCapture,token),"启动 Scene Capture");
            token.ThrowIfCancellationRequested();
        }
        private async Task EnsureLocatedProvider(CancellationToken token,bool reconnect=false)
        {
            while(true)
            {
                await RoomTrackingReady.Wait(token);
                int current=RoomTrackingReady.Revision;
                if(reconnect||(providerRevision>=0&&providerRevision!=current))await RestartProvider(token);
                else await EnsureProvider(token);
                providerRevision=current;
                if(current==RoomTrackingReady.Revision)return;
                reconnect=true;
            }
        }
        private static async Task RestartProvider(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require(PXR_MixedReality.GetSenseDataProviderState(PxrSenseDataProviderType.SceneCapture,out var state),"读取房间服务状态");
            if(state==PxrSenseDataProviderState.Running)
                Require(PXR_MixedReality.StopSenseDataProvider(PxrSenseDataProviderType.SceneCapture),"停止旧房间服务");
            await RoomTrackingReady.Wait(token);
            await EnsureProvider(token);
        }
        private static void Require(PxrResult result,string stage)
        { if(result!=PxrResult.SUCCESS) throw new InvalidOperationException(stage+"失败："+result); }
#endif
        private static Vector3 ToWorld(Transform space,Vector3 p) => space==null ? p : space.TransformPoint(p);
        private static Quaternion ToWorldRotation(Transform space,Quaternion r) => space==null ? r : space.rotation*r;
        public static Quaternion BoxWorldRotation(Transform space,Quaternion anchorRotation,Quaternion localRotation)
        {
            // SDK SceneCaptureManager documents unsupported box-local rotation;
            // native captures can return (0,0,0,0). Keep the located anchor orientation.
            return ToWorldRotation(space,anchorRotation*(ValidRotation(localRotation)?localRotation:Quaternion.identity));
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x+v.y+v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        private static bool ValidRotation(Quaternion r)
        {
            float norm=r.x*r.x+r.y*r.y+r.z*r.z+r.w*r.w;
            return !float.IsNaN(norm)&&!float.IsInfinity(norm)&&norm>.9f&&norm<1.1f;
        }

        public static bool TryAddFloor(RoomSnapshot room,string id,Transform trackingSpace,Vector3 position,Quaternion rotation,Vector2[] vertices)
        {
            if(room==null||!Finite(position)||!ValidRotation(rotation)||vertices==null||vertices.Length<3)return false;
            var first=new Vector3[vertices.Length];var second=new Vector3[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            { first[i]=ToWorld(trackingSpace,position+rotation*new Vector3(vertices[i].x,vertices[i].y,0));
              second[i]=ToWorld(trackingSpace,position+rotation*new Vector3(vertices[i].x,0,vertices[i].y)); }
            int before=room.Surfaces.Count;
            if(!TryAddSurface(room,id,first))TryAddSurface(room,id,second);
            if(room.Surfaces.Count==before)return false;
            room.Surfaces[room.Surfaces.Count-1].HasAnchorPose=true;
            room.Surfaces[room.Surfaces.Count-1].AnchorPose=new Pose(ToWorld(trackingSpace,position),ToWorldRotation(trackingSpace,rotation));
            return true;
        }
        private static bool TryAddSurface(RoomSnapshot room,string id,Vector3[] points)
        {
            if(points==null||points.Length<3)return false;
            float height=0;var normal=Vector3.zero;
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];if(!Finite(p))return false;
                height+=p.y;
                normal+=Vector3.Cross(p-points[0],points[(i+1)%points.Length]-points[0]);
            }
            // A valid 3-degree capture pose can span more than 25cm vertically in
            // a large room. Validate the plane angle independently of room width.
            if(normal.sqrMagnitude<.000001f||Mathf.Abs(Vector3.Dot(normal.normalized,Vector3.up))<Mathf.Cos(3f*Mathf.Deg2Rad))return false;
            height/=points.Length;
            var leveled=new Vector3[points.Length];
            for(int i=0;i<points.Length;i++)leveled[i]=new Vector3(points[i].x,height,points[i].z);
            int before=room.Surfaces.Count;AddSurface(room,id,SurfaceKind.Floor,leveled);return room.Surfaces.Count>before;
        }

        private static void AddSurface(RoomSnapshot room,string id,SurfaceKind kind,Vector3[] points)
        {
            float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
            var polygon=new Vector2[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                if(!Finite(points[i])) return;
                polygon[i]=new Vector2(points[i].x,points[i].z);
                minY=Mathf.Min(minY,points[i].y); maxY=Mathf.Max(maxY,points[i].y);
            }
            if(maxY-minY>0.05f || GridBuilder.Area(polygon)<0.15f) return;
            room.Surfaces.Add(new DeploySurface { Id=id,Kind=kind,Height=(minY+maxY)/2f,Polygon=polygon });
        }

        public static bool SetPassthrough(bool enabled)
        {
#if ENABLE_PICO_OPENXR_SDK && UNITY_ANDROID && !UNITY_EDITOR
            if(!PassthroughFeature.isExtensionEnable) return false;
            PassthroughFeature.EnableVideoSeeThrough=enabled;
            return true;
#else
            return !enabled;
#endif
        }
    }
}
