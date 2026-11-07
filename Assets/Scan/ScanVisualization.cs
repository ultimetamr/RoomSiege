using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RoomSiege.Visuals;

namespace RoomSiege.Scan
{
    public sealed class ScanVisualization : MonoBehaviour
    {
        private GameObject geometry;
        private GameObject overlay;
        private Mesh gridMesh;
        private Material lines;
        private Material gridLines;
        private GridModel drawn;
        private Transform coreRoot;
        private Vector3 initialCore;
        private LineRenderer clearance;
        private LineRenderer calibrationTarget;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<LineRenderer> pulseRings = new List<LineRenderer>();
        private readonly Color gridColor = RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.58f);
        private Vector3 visualTargetCore;
        private Vector3 visualCore;
        private bool visualCoreReady;
        private bool presentationVisible=true;
        public Vector3 VisualCoreCenter => visualCore;
        public void SetPresentationVisible(bool visible)
        {presentationVisible=visible;if(geometry!=null&&geometry.activeSelf!=visible)geometry.SetActive(visible);}

        public void Draw(RoomSnapshot room,GridModel model,bool visible)
        {
            if(drawn!=null&&ReferenceEquals(drawn.SupportedCells,model.SupportedCells))
            {
                SetCoreTarget(model.SafeZoneCenter);
                if(!SameReserved(drawn,model))UpdateGrid(model);
                UpdateGridExclusion(model.SafeZoneCenter);
                drawn=model;SetOverlay(visible);return;
            }
            Clear();
            drawn=model;initialCore=model.SafeZoneCenter;
            visualTargetCore=model.SafeZoneCenter;
            visualCore=model.SafeZoneCenter;
            visualCoreReady=true;
            geometry=new GameObject("Scan room geometry"); geometry.transform.SetParent(transform,false);
            overlay=new GameObject("Virtual grid overlay"); overlay.transform.SetParent(geometry.transform,false);
            lines=CreateMaterial(Color.white,false);
            gridLines=CreateMaterial(Color.white,false);
            gridLines.SetFloat("_ClipSafeCells",1f);
            UpdateGridExclusion(model.SafeZoneCenter);
            if(model.Mode==ScanMode.Sandbox) DrawLivingRoom(room,model.SafeZoneCenter);
            gridMesh=new Mesh { name="RoomSiege deployment outlines", indexFormat=IndexFormat.UInt32 };
            UpdateGrid(model);
            var grid=new GameObject("Placeable cells",typeof(MeshFilter),typeof(MeshRenderer));
            grid.transform.SetParent(overlay.transform,false);
            grid.GetComponent<MeshFilter>().sharedMesh=gridMesh;
            grid.GetComponent<MeshRenderer>().sharedMaterial=gridLines;
            coreRoot=new GameObject("Moving core marker").transform;coreRoot.SetParent(geometry.transform,false);
            var safeRing=Ring("Safe zone 0.8m",coreRoot,model.SafeZoneCenter+Vector3.up*0.018f,GridModel.SafeRadius,RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.8f),0.024f);
            safeRing.useWorldSpace=false;
            clearance=Ring("Core construction footprint",overlay.transform,model.SafeZoneCenter+Vector3.up*0.01f,GridModel.CoreBuildRadius,RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.2f),0.01f);
            clearance.useWorldSpace=false;
            GuardCoreVisual.Create(coreRoot,model.SafeZoneCenter);
            var label=new GameObject("Core label");label.transform.SetParent(coreRoot,false);label.transform.position=model.SafeZoneCenter+Vector3.up*.5f;
            var text=label.AddComponent<TextMesh>();text.text="守护核心";text.fontSize=84;text.characterSize=.009f;text.anchor=TextAnchor.MiddleCenter;text.color=RoomSiegeStyle.Friendly;
            label.AddComponent<ScanBillboard>();
            foreach(var spawn in model.SpawnPoints)
            {
                var ring=Ring("Spawn "+spawn.AnchorId,geometry.transform,spawn.Position+Vector3.up*0.025f,
                    Mathf.Max(0.25f,Mathf.Min(spawn.FootprintSize.x,spawn.FootprintSize.y)*0.5f),RoomSiegeStyle.Enemy,0.045f);
                pulseRings.Add(ring);
                Ring("Spawn inner",geometry.transform,spawn.Position+Vector3.up*0.026f,0.12f,RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy,.8f),0.018f);
            }
            SetOverlay(visible);
            SetPresentationVisible(presentationVisible);
        }

        public void SetCoreTarget(Vector3 position)
        {
            if(float.IsNaN(position.x)||float.IsNaN(position.y)||float.IsNaN(position.z)||
                float.IsInfinity(position.x)||float.IsInfinity(position.y)||float.IsInfinity(position.z)) return;
            if(!visualCoreReady){visualCore=position;visualCoreReady=true;}
            visualTargetCore=position;
        }

        public void SetCalibrationTarget(Vector3? position,bool valid)
        {
            if(!position.HasValue||geometry==null)
            {if(calibrationTarget!=null)calibrationTarget.gameObject.SetActive(false);return;}
            if(calibrationTarget==null)
                calibrationTarget=Ring("Entrance calibration target",geometry.transform,Vector3.zero,.22f,Color.white,.018f);
            calibrationTarget.gameObject.SetActive(true);
            var color=valid?RoomSiegeStyle.Friendly:RoomSiegeStyle.Enemy;
            calibrationTarget.startColor=calibrationTarget.endColor=color;
            for(int i=0;i<calibrationTarget.positionCount;i++)
            {
                float angle=i*Mathf.PI*2/calibrationTarget.positionCount;
                calibrationTarget.SetPosition(i,position.Value+Vector3.up*.035f+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.22f);
            }
        }

        private void UpdateGridExclusion(Vector3 center)
        {
            // All four strokes carry the cell centre. The shader hides the whole
            // forbidden cell without clearing/re-uploading room geometry.
            gridLines.SetVector("_SafeCenter",new Vector4(center.x,center.z,GridModel.CellSize*.5f,GridModel.CoreBuildRadius-.0001f));
        }

        private void UpdateCoreVisual(float dt)
        {
            if(!visualCoreReady||coreRoot==null||clearance==null) return;
            float blend=1f-Mathf.Exp(-14f*Mathf.Max(0f,dt));
            visualCore=Vector3.Lerp(visualCore,visualTargetCore,blend);
            coreRoot.position=visualCore-initialCore;
            clearance.transform.position=visualCore-initialCore;
        }

        private void UpdateGrid(GridModel model)
        {
            var vertices=new List<Vector3>(); var indices=new List<int>(); var colors=new List<Color>();
            var centres=new List<Vector2>();
            foreach(var cell in model.SupportedCells)
            {
                bool reserved=false;
                foreach(var route in model.ReservedRouteCells)
                    if(route.SurfaceId==cell.SurfaceId&&route.Index==cell.Index){reserved=true;break;}
                if(reserved)continue;
                float h=GridModel.CellSize/2f;
                var c=cell.Center+Vector3.up*0.012f;
                var corners=new[] {c+new Vector3(-h,0,-h),c+new Vector3(h,0,-h),c+new Vector3(h,0,h),c+new Vector3(-h,0,h)};
                for(int i=0;i<4;i++) AddStroke(corners[i],corners[(i+1)%4],0.009f,gridColor,vertices,indices,colors);
                for(int i=0;i<16;i++)centres.Add(new Vector2(cell.Center.x,cell.Center.z));
            }
            gridMesh.Clear();
            gridMesh.SetVertices(vertices); gridMesh.SetTriangles(indices,0); gridMesh.SetColors(colors); gridMesh.RecalculateBounds();
            gridMesh.SetUVs(1,centres);
        }

        private static bool SameReserved(GridModel a,GridModel b)
        {
            if(a.ReservedRouteCells.Count!=b.ReservedRouteCells.Count)return false;
            for(int i=0;i<a.ReservedRouteCells.Count;i++)
                if(a.ReservedRouteCells[i].SurfaceId!=b.ReservedRouteCells[i].SurfaceId||a.ReservedRouteCells[i].Index!=b.ReservedRouteCells[i].Index)return false;
            return true;
        }

        private void DrawLivingRoom(RoomSnapshot room,Vector3 safe)
        {
            var floor=room.Surfaces.Find(s=>s.Kind==SurfaceKind.Floor);
            var center=Vector3.zero;
            foreach(var p in floor.Polygon) center+=new Vector3(p.x,room.FloorHeight,p.y);
            center/=floor.Polygon.Length;
            Box("Living room floor",center-Vector3.up*0.05f,new Vector3(6f,0.1f,6f),Quaternion.identity,RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.18f));
            Box("Back wall left",center+new Vector3(-1.8f,0.7f,3f),new Vector3(2.4f,1.4f,0.08f),Quaternion.identity,RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.08f));
            Box("Back wall right",center+new Vector3(1.8f,0.7f,3f),new Vector3(2.4f,1.4f,0.08f),Quaternion.identity,RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.08f));
            foreach(var volume in room.Volumes)
            {
                var color=RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,volume.SupportsDeployment?.36f:.22f);
                Box(volume.Id,volume.Center,volume.Size,volume.Rotation,color);
            }
        }

        private GameObject Box(string name,Vector3 center,Vector3 size,Quaternion rotation,Color color)
        {
            // Room furniture is a visual outline; deployment still uses the original RoomSnapshot.
            var box=new GameObject(name);
            box.name=name; box.transform.SetParent(geometry.transform,false);
            box.transform.SetPositionAndRotation(center,rotation);
            Vector3 h=size*.5f;
            Vector3 a=new Vector3(-h.x,-h.y,-h.z),b=new Vector3(h.x,-h.y,-h.z),
                c=new Vector3(h.x,-h.y,h.z),d=new Vector3(-h.x,-h.y,h.z),lift=Vector3.up*size.y;
            var wire=box.AddComponent<LineRenderer>();wire.sharedMaterial=lines;wire.useWorldSpace=false;
            wire.startColor=wire.endColor=color;wire.startWidth=wire.endWidth=name=="Guard core"?.006f:.003f;
            var points=new[]{a,b,c,d,a,a+lift,b+lift,b,b+lift,c+lift,c,c+lift,d+lift,d,d+lift,a+lift};
            wire.positionCount=points.Length;wire.SetPositions(points);
            wire.shadowCastingMode=ShadowCastingMode.Off;wire.receiveShadows=false;
            return box;
        }

        private Material CreateMaterial(Color color,bool writeDepth)
        {
            var shader=Resources.Load<Shader>("RoomSiegeScanOverlay");
            if(shader==null) throw new System.InvalidOperationException("RoomSiegeScanOverlay shader missing.");
            var material=new Material(shader);
            material.SetColor("_Color",color); material.SetInt("_ZWrite",writeDepth?1:0);
            material.renderQueue=writeDepth?2000:3000;
            ownedMaterials.Add(material); return material;
        }

        private LineRenderer Ring(string name,Transform parent,Vector3 center,float radius,Color color,float width)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false);
            var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=lines;
            line.useWorldSpace=true; line.loop=true; line.widthMultiplier=width;
            line.startColor=color; line.endColor=color;
            line.positionCount=64;
            for(int i=0;i<64;i++)
            { float a=i*Mathf.PI*2f/64; line.SetPosition(i,center+new Vector3(Mathf.Cos(a),0f,Mathf.Sin(a))*radius); }
            line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
            return line;
        }

        private static void AddStroke(Vector3 a,Vector3 b,float width,Color color,List<Vector3> v,List<int> t,List<Color> c)
        {
            var side=Vector3.Cross(Vector3.up,(b-a).normalized)*width/2f;
            int n=v.Count;
            v.Add(a-side); v.Add(a+side); v.Add(b+side); v.Add(b-side);
            t.Add(n); t.Add(n+1); t.Add(n+2); t.Add(n); t.Add(n+2); t.Add(n+3);
            for(int i=0;i<4;i++) c.Add(color);
        }
        public void SetOverlay(bool visible) { if(overlay!=null) overlay.SetActive(visible); }
        private void Update()
        {
            float pulse=(Mathf.Sin(Time.unscaledTime*3.5f)+1f)/2f;
            foreach(var ring in pulseRings)
            {
                if(ring==null) continue;
                var color=RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy,Mathf.Lerp(0.35f,0.95f,pulse));
                ring.startColor=color; ring.endColor=color; ring.widthMultiplier=Mathf.Lerp(0.025f,0.06f,pulse);
            }
        }
        private void LateUpdate()=>UpdateCoreVisual(Time.unscaledDeltaTime);
        public void Clear()
        {
            if(geometry!=null) { geometry.SetActive(false); Destroy(geometry); }
            if(gridMesh!=null) Destroy(gridMesh);
            foreach(var material in ownedMaterials) if(material!=null) Destroy(material);
            ownedMaterials.Clear(); pulseRings.Clear(); geometry=null; overlay=null;
            drawn=null;coreRoot=null;clearance=null;gridLines=null;calibrationTarget=null;
            visualCoreReady=false;
        }
        private void OnDestroy() => Clear();
    }
    public sealed class ScanBillboard:MonoBehaviour
    {
        private void LateUpdate()
        {var camera=Camera.main;if(camera!=null)transform.rotation=Quaternion.LookRotation(transform.position-camera.transform.position);}
    }
}
