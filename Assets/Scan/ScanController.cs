using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
#if ENABLE_PICO_OPENXR_SDK
using ByteDance.PICO.OpenXR;
#endif

namespace RoomSiege.Scan
{
    public sealed class ScanController : MonoBehaviour
    {
        public static ScanController Instance { get; private set; }
        public GridModel CurrentGrid { get; private set; }
        public RoomSnapshot CurrentRoom { get; private set; }
        public ScanMode RequestedMode { get; private set; }
        public string Status { get; private set; } = "准备扫描";
        public bool OverlayVisible { get; private set; } = true;
        public bool IsScanning { get; private set; }
        public int ScanStep { get; private set; }
        public const int ScanStepCount=8;
        public bool CaptureRequested { get; private set; }
        public bool CanRequestCapture => !BattleLayoutLocked && (!IsScanning || !CaptureRequested);
        public bool BattleLayoutLocked { get; set; }
        public bool CoreFollowsPlayer { get; private set; }
        public bool EntranceCalibrationPending { get; private set; }
        public bool CalibrationArmed => calibrationArmed;
        public Func<GridModel,bool> CanMoveCore { get; set; }
        public Func<GridModel,string> LayoutFailure { get; set; }
        public Func<bool> CoreMovementPaused { get; set; }
        public event Action<GridModel> GridChanged;
        public event Action ViewChanged;
        private IRoomScanner scanner = new PicoRoomScanner();
        private CancellationTokenSource cancellation;
        private int revision;
        private Camera head;
        private GameObject previousMenu;
        private ScanVisualization visualization;
        private float nextRefresh;
        private bool roomDirty;
        private bool refreshing;
        private Color previousBackground;
        private CameraClearFlags previousClear;
        private bool previousHdr;
        private bool calibrationArmed;
        // The defended core belongs to the current campaign. Head movement must not
        // silently move the objective or invalidate towers that were already placed.
        private Vector3 safeZoneCenter;
        private bool safeZoneLocked;
        private float nextCoreFollow;
        private bool followBlocked;
        private bool restorePassthrough;
        private float nextPassthroughRestore;
        private bool showScanProgress;
        private string scanStage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if(SceneManager.GetActiveScene().name=="RoomSiege_Start" && FindObjectOfType<ScanController>()==null)
                new GameObject("RoomSiege Scan").AddComponent<ScanController>();
        }

        private void Awake()
        {
            if(Instance!=null && Instance!=this) { Destroy(gameObject); return; }
            Instance=this;
            head=Camera.main;
            if(head==null) { enabled=false; Debug.LogError("[RoomSiege.Scan] MainCamera missing."); return; }
            previousBackground=head.backgroundColor; previousClear=head.clearFlags; previousHdr=head.allowHDR;
            previousMenu=GameObject.Find("RoomSiegeWorldSpacePanel");
            if(previousMenu!=null) previousMenu.SetActive(false);
            visualization=gameObject.AddComponent<ScanVisualization>();
            gameObject.AddComponent<ScanPanel>().Initialize(this,head);
#if ENABLE_PICO_OPENXR_SDK
            OpenXRExtensions.SceneAnchorDataUpdated+=MarkRoomDirty;
#endif
        }

        private void Start()
        {
            restorePassthrough=true;
#if UNITY_ANDROID && !UNITY_EDITOR
            // Do not let a startup permission dialog hold the scan button hostage.
            if(UnityEngine.Android.Permission.HasUserAuthorizedPermission("com.picovr.permission.SPATIAL_DATA"))ReadExistingRoom();
            else { UseSandbox(); SetStatus("请选择扫描真实房间，并在头显中允许空间数据权限；也可直接使用模拟沙盘"); }
#else
            UseSandbox();
#endif
        }

        public void UseSandbox()
        {
            if(BattleLayoutLocked){SetStatus("本局房间已固定，请返回主菜单后更换房间");return;}
            CancelScan(); RequestedMode=ScanMode.Sandbox;
            safeZoneLocked=false;
            ActivateSandbox("模拟沙盘：内置虚拟客厅");
        }

        public void ScanRealRoom()=>ScanRoom(true);
        public void BeginGameRoomScan()=>ScanRoom(false,true);
        private void ReadExistingRoom()=>ScanRoom(false);
        public void UseRealRoom()
        {
            if(BattleLayoutLocked){SetStatus("本局房间已固定，请返回主菜单后更换房间");return;}
            if(IsScanning&&RequestedMode==ScanMode.RealRoom)return;
            if(CurrentGrid!=null&&CurrentRoom!=null&&CurrentGrid.Mode==ScanMode.RealRoom)
            {
                RequestedMode=ScanMode.RealRoom;restorePassthrough=true;
                var model=CurrentGrid;
                bool prepared=TryPrepareAutomaticEntrance(CurrentRoom,ref model);
                string failure=ValidateLayout(CurrentRoom,model);
                if(!string.IsNullOrEmpty(failure)){ActivateSandbox("真实房间暂不适合布防，已自动准备模拟沙盘："+failure);return;}
                EntranceCalibrationPending=false;
                if(prepared)Publish(model,"真实房间与入口已自动准备，可以进入建造");
                else SetStatus("已恢复真实房间，可以进入建造");
                return;
            }
            ReadExistingRoom();
        }

        private async void ScanRoom(bool requestCapture,bool visibleProgress=false)
        {
            if(BattleLayoutLocked){SetStatus("本局房间已固定，请返回主菜单后重新扫描");return;}
            if(head==null) return;
            if(IsScanning && (!requestCapture || CaptureRequested)&&!(visibleProgress&&!showScanProgress))
            {SetStatus("扫描正在进行，请完成或取消头显内的系统扫描；也可使用模拟沙盘");return;}
            CancelScan(); RequestedMode=ScanMode.RealRoom;
            // A newly selected room starts at the current player position, not
            // at a fixed core inherited from an unrelated sandbox/capture.
            safeZoneLocked=false;
            var request=revision;
            cancellation=new CancellationTokenSource();
            var token=cancellation.Token;
            IsScanning=true;
            showScanProgress=visibleProgress||requestCapture;
            CaptureRequested=requestCapture;
            CurrentGrid=null; CurrentRoom=null;
            GridChanged?.Invoke(null);
            visualization.Clear();
            bool passthrough=scanner.TryEnablePassthrough();
            ConfigureCamera(passthrough);
            SetStatus("真实扫描中… [S] 可随时切回沙盘");
            try
            {
                await ShowScanStep(1,"开启现实透视与空间追踪",request,token);
                await ShowScanStep(2,"读取真实房间锚点",request,token);
                var operation=scanner.ScanAsync(head.transform.parent,token,
                    message=> { if(this!=null && request==revision) SetStatus(showScanProgress?$"房间扫描 {ScanStep}/8 · {scanStage}\n{message}":message); },requestCapture);
                var timeout=Task.Delay(TimeSpan.FromSeconds(requestCapture?180:45),token);
                if(await Task.WhenAny(operation,timeout)!=operation)
                {
                    // Observe a late native exception; never publish data from a superseded request.
                    _=ObserveLate(operation);
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException(requestCapture?"3 分钟未完成系统扫描，请完成后重试":"45 秒未取得房间数据");
                }
                var room=await operation;
                if(this==null || request!=revision || token.IsCancellationRequested) return;
                if(!passthrough)
                {passthrough=scanner.TryEnablePassthrough();ConfigureCamera(passthrough);}
                if(!passthrough) throw new NotSupportedException("Passthrough 不可用");
                await ShowScanStep(3,"确认地板、桌面与房间边界",request,token);
                if(room==null||room.Surfaces.Count==0)throw new InvalidOperationException("没有有效地板，请完成系统房间扫描并保存");
                await ShowScanStep(4,"生成 0.4m 部署格",request,token);
                var model=GridBuilder.Build(room,head.transform.position,ScanMode.RealRoom);
                await ShowScanStep(5,"确定核心安全区",request,token);
                bool coreAdjusted=false;
                if(GridBuilder.TryFindNearbyCore(model,out var usableCore)&&GridBuilder.HorizontalDistance(usableCore,model.SafeZoneCenter)>.01f)
                {model=GridBuilder.WithSafeZone(model,usableCore);coreAdjusted=true;}
                await ShowScanStep(6,"恢复并准备出怪口",request,token);
                bool entranceRestored=RoomEntranceMemory.TryRestore(room,PlayerPrefs.GetString(RoomEntranceMemory.PreferenceKey,""),model.SafeZoneCenter,out var savedEntrance);
                bool entranceAdjusted=false;
                if(entranceRestored)
                {
                    if(GridBuilder.TryFitManualEntrance(room,model,savedEntrance,LayoutFailure,out var fitted))
                    {entranceAdjusted=GridBuilder.HorizontalDistance(savedEntrance.Position,fitted.Position)>.01f;savedEntrance=fitted;}
                    room.Entrances.Insert(0,savedEntrance);model=GridBuilder.Build(room,model.SafeZoneCenter,ScanMode.RealRoom);
                }
                bool automaticEntrance=TryPrepareAutomaticEntrance(room,ref model);
                await ShowScanStep(7,"检查 Boss 通路与可放区域",request,token);
                var failure=ValidateLayout(room,model);
                if(!string.IsNullOrEmpty(failure))throw new InvalidOperationException(failure);
                await ShowScanStep(8,"房间就绪，正在显示战场",request,token);
                CurrentRoom=room;
                if(entranceAdjusted&&!automaticEntrance)SaveEntrance(room,savedEntrance);
                nextRefresh=Time.unscaledTime+5f;
                Publish(model,(coreAdjusted?"核心已避开家具，放在附近开阔地板；":"")+
                    (entranceAdjusted?"入口已就近对齐可通行地板；":"")+
                    (automaticEntrance?"真实房间与入口已自动准备，可以进入建造":entranceRestored?"已自动恢复真实房间与入口，可以进入建造":
                    requestCapture?"真实扫描：房间语义锚点已定位":"已自动恢复真实房间，可以进入建造"));
            }
            catch(OperationCanceledException) { }
            catch(Exception error)
            {
                if(this!=null && request==revision)
                {
                    cancellation.Cancel();
                    ActivateSandbox("真实扫描不可用 → 沙盘："+error.Message);
                }
            }
            finally
            {
                if(this!=null && request==revision) { IsScanning=false; CaptureRequested=false; showScanProgress=false; ViewChanged?.Invoke(); }
            }
        }

        private async Task ShowScanStep(int step,string label,int request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(this==null||request!=revision)throw new OperationCanceledException(token);
            if(!showScanProgress)return;
            ScanStep=step;scanStage=label;
            SetStatus($"房间扫描 {step}/{ScanStepCount} · {label}");
            // Keep each real preparation stage visible, including fast reads of saved rooms.
            // This is application preparation progress, not eight new system capture points.
            await Task.Delay(350,token);
            token.ThrowIfCancellationRequested();
            if(this==null||request!=revision)throw new OperationCanceledException(token);
        }

        private static async Task ObserveLate(Task operation)
        { try { await operation; } catch(Exception) { /* The controller already reported timeout/cancellation. */ } }

        private bool TryPrepareAutomaticEntrance(RoomSnapshot room,ref GridModel model)
        {
            if(string.IsNullOrEmpty(ValidateLayout(room,model)))return false;
            if(!GridBuilder.TryCreateAutomaticEntrance(room,model,LayoutFailure,out var entrance))return false;
            room.Entrances.Clear();room.Entrances.Add(entrance);
            model=GridBuilder.Build(room,model.SafeZoneCenter,ScanMode.RealRoom);
            SaveEntrance(room,entrance);
            Debug.Log("[RoomSiege.Scan] Automatic entrance prepared on connected real floor");
            return true;
        }
        private string ValidateLayout(RoomSnapshot room,GridModel model)
        {
            string failure=GridBuilder.PlayableFailure(room,model);
            return !string.IsNullOrEmpty(failure)?failure:LayoutFailure?.Invoke(model)??"";
        }

        private void ActivateSandbox(string reason)
        {
            if(head==null) return;
            // Sandbox is a data fallback; it must not turn an MR application into an opaque VR room.
            bool passthrough=PicoRoomScanner.SetPassthrough(true); ConfigureCamera(passthrough);
            Debug.Log("[RoomSiege.Scan] Sandbox fallback passthrough="+passthrough);
            var origin=head.GetComponentInParent<XROrigin>();
            float floor=origin!=null ? origin.transform.position.y : head.transform.position.y-1.6f;
            var center = safeZoneLocked ? safeZoneCenter : new Vector3(head.transform.position.x,floor,head.transform.position.z);
            CurrentRoom=SandboxRoom.Create(center);
            Publish(GridBuilder.Build(CurrentRoom,center,ScanMode.Sandbox),reason);
        }

        public void ToggleOverlay()
        {
            OverlayVisible=!OverlayVisible;
            visualization.SetOverlay(OverlayVisible);
            Debug.Log("[RoomSiege.Scan] Overlay="+OverlayVisible);
            ViewChanged?.Invoke();
        }

        // Exposed by the room-setup page only, before a campaign starts.
        public void PlaceCoreAtPlayer()
        {
            if(head==null||CurrentRoom==null||CurrentGrid==null||IsScanning||BattleLayoutLocked)return;
            var candidate=GridBuilder.Build(CurrentRoom,head.transform.position,CurrentGrid.Mode);
            if(!GridBuilder.IsPlayable(CurrentRoom,candidate))
            {SetStatus("这里空间不足，请换到开阔地板后再设置守护核心");return;}
            if(CanMoveCore!=null&&!CanMoveCore(candidate))
            {SetStatus("这里被家具阻挡或敌人不可达，请换到开阔地板");return;}
            CoreFollowsPlayer=false;followBlocked=false;
            safeZoneCenter=candidate.SafeZoneCenter;safeZoneLocked=true;
            Publish(candidate,"守护核心已固定在脚下；武器保持原位");
        }

        public void ToggleCoreMode()=>SetCoreFollowsPlayer(!CoreFollowsPlayer);
        public void SetCoreFollowsPlayer(bool follow)
        {
            CoreFollowsPlayer=follow;followBlocked=false;nextCoreFollow=0;
            SetStatus(follow?"核心跟随已开启；已有武器保留，建造请留出 Boss 通路":"核心已固定在当前位置；走动不会移动核心或武器");
        }

        // Uses the existing room and portals. Never regenerates furniture or
        // removes purchases merely because their cells enter the safety circle.
        public bool TryFollowPlayer(Vector3 position)
        {
            if(!CoreFollowsPlayer||IsScanning||CurrentRoom==null||CurrentGrid==null||
                (CoreMovementPaused!=null&&CoreMovementPaused()))return false;
            if(float.IsNaN(position.x+position.z)||float.IsInfinity(position.x)||float.IsInfinity(position.z))return false;
            if(GridBuilder.HorizontalDistance(position,safeZoneCenter)<.01f)return false;
            var candidate=GridBuilder.WithSafeZone(CurrentGrid,position);
            bool onFloor=CurrentRoom.Surfaces.Exists(s=>s.Kind==SurfaceKind.Floor&&
                GridBuilder.Contains(s.Polygon,new Vector2(position.x,position.z)));
            bool valid=onFloor&&candidate.WalkMap!=null&&candidate.WalkMap.Open(candidate.WalkMap.Index(candidate.SafeZoneCenter));
            foreach(var spawn in candidate.SpawnPoints)
                if(GridBuilder.HorizontalDistance(spawn.Position,candidate.SafeZoneCenter)<1.5f)valid=false;
            if(valid&&CanMoveCore!=null)valid=CanMoveCore(candidate);
            if(!valid)
            {
                if(!followBlocked)SetStatus("核心暂留最后有效位置：请回到开阔地板、远离入口并保留敌人通路；武器不会消失");
                followBlocked=true;return false;
            }
            safeZoneCenter=candidate.SafeZoneCenter;CurrentGrid=candidate;safeZoneLocked=true;
            visualization.Draw(CurrentRoom,candidate,OverlayVisible);
            GridChanged?.Invoke(candidate);
            if(followBlocked){followBlocked=false;SetStatus("核心已恢复跟随；已有武器保持原位");}
            return true;
        }

        public void CalibrateEntrance()
        {
            if(head!=null)CalibrateEntrance(new Ray(head.transform.position,head.transform.forward));
        }

        public void CalibrateEntrance(Ray ray)
        {
            if(BattleLayoutLocked){SetStatus("本局入口已固定，请返回主菜单后标定");return;}
            if(CurrentRoom==null || CurrentGrid==null) return;
            if(IsScanning)return;
            Debug.Log($"[RoomSiege.Scan] Calibration confirm origin={ray.origin:F2} direction={ray.direction:F2}");
            if(!GridBuilder.TryCalibrateEntrance(CurrentRoom,CurrentGrid,ray,LayoutFailure,out var spawn,out _,out var failure))
            {SetStatus("标定未完成："+failure+"；可直接重新指向地板确认");return;}
            CurrentRoom.Entrances.RemoveAll(s=>s.IsManual);
            CurrentRoom.Entrances.Insert(0,spawn);
            var model=GridBuilder.Build(CurrentRoom,CurrentGrid.SafeZoneCenter,CurrentGrid.Mode);
            EntranceCalibrationPending=false;calibrationArmed=false;
            if(CurrentGrid.Mode==ScanMode.RealRoom)
            {
                SaveEntrance(CurrentRoom,spawn);
            }
            nextRefresh=Time.unscaledTime+5f;
            Publish(model,"出怪口与核心通路已确认，可以进入建造（最多 2 个）");
        }

        private static void SaveEntrance(RoomSnapshot room,SpawnPoint entrance)
        {
            var saved=RoomEntranceMemory.Encode(room,entrance);
            if(!string.IsNullOrEmpty(saved))
            {PlayerPrefs.SetString(RoomEntranceMemory.PreferenceKey,saved);PlayerPrefs.Save();Debug.Log("[RoomSiege.Scan] Entrance saved relative to floor anchor");}
            else Debug.LogWarning("[RoomSiege.Scan] Entrance could not be saved: no containing located floor anchor");
        }

        public void BeginCalibration()
        {
            if(CurrentGrid==null||IsScanning||BattleLayoutLocked) return;
            calibrationArmed=true;
            SetStatus("标定已开启：将手柄 / 手势射线指向门洞或过道地板，扣扳机 / 捏合确认。需距核心至少 1.8m。");
        }

        public void CancelCalibration()
        {
            if(!calibrationArmed)return;
            calibrationArmed=false;
            SetStatus(EntranceCalibrationPending?"标定已取消；请标定出怪口后进入建造":"标定已取消");
        }
        private static bool Finite(Vector3 value)=>!float.IsNaN(value.x+value.y+value.z)&&
            !float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);

        private void Update()
        {
            if(restorePassthrough&&head!=null&&Time.unscaledTime>=nextPassthroughRestore)
            {
                nextPassthroughRestore=Time.unscaledTime+1f;
                bool passthrough=PicoRoomScanner.SetPassthrough(true);
                ConfigureCamera(passthrough);
                if(passthrough)
                {
                    restorePassthrough=false;
                    if(RequestedMode==ScanMode.RealRoom&&!BattleLayoutLocked&&!IsScanning&&
                        CurrentGrid!=null&&CurrentGrid.Mode==ScanMode.Sandbox)ReadExistingRoom();
                }
#if UNITY_EDITOR
                restorePassthrough=false;
#endif
            }
            var keys=Keyboard.current;
            if(keys!=null)
            {
                if(keys.rKey.wasPressedThisFrame) ScanRealRoom();
                if(keys.sKey.wasPressedThisFrame) UseSandbox();
                if(keys.gKey.wasPressedThisFrame) ToggleOverlay();
                if(keys.cKey.wasPressedThisFrame) CalibrateEntrance();
            }
            if(CurrentRoom==null || CurrentGrid==null || IsScanning || head==null) return;
            if(CoreFollowsPlayer&&Time.unscaledTime>=nextCoreFollow)
            {nextCoreFollow=Time.unscaledTime+.1f;TryFollowPlayer(head.transform.position);}
            if(!BattleLayoutLocked&&!EntranceCalibrationPending&&CurrentGrid!=null && CurrentGrid.Mode==ScanMode.RealRoom && !refreshing && (roomDirty || Time.unscaledTime>=nextRefresh))
            {
                roomDirty=false; nextRefresh=Time.unscaledTime+5f;
                RefreshLocatedRoom();
            }
        }

        private async void RefreshLocatedRoom()
        {
            var request=revision;
            refreshing=true;
            try
            {
                var token=cancellation.Token;
                var operation=scanner.ScanAsync(head.transform.parent,token,_=>{});
                if(await Task.WhenAny(operation,Task.Delay(TimeSpan.FromSeconds(15),token))!=operation)
                {
                    _=ObserveLate(operation);
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("锚点更新超时");
                }
                var updated=await operation;
                if(this==null || request!=revision||BattleLayoutLocked) return;
                updated.Entrances.InsertRange(0,CurrentRoom.Entrances.FindAll(s=>s.IsManual));
                CurrentRoom=updated;
                var model=GridBuilder.Build(updated,safeZoneLocked?safeZoneCenter:head.transform.position,ScanMode.RealRoom);
                bool automaticEntrance=TryPrepareAutomaticEntrance(updated,ref model);
                var failure=ValidateLayout(updated,model);
                if(string.IsNullOrEmpty(failure))Publish(model,automaticEntrance?"真实房间与入口已自动恢复，可以进入建造":"真实扫描：锚点与安全区已更新");
                else ActivateSandbox("房间更新后"+failure+" → 模拟沙盘");
            }
            catch(Exception error)
            { if(this!=null && request==revision&&!BattleLayoutLocked) { cancellation.Cancel(); ActivateSandbox("锚点更新失败 → 沙盘："+error.Message); } }
            finally { if(this!=null && request==revision) refreshing=false; }
        }

        private void MarkRoomDirty() => roomDirty=true;
        private void OnApplicationFocus(bool focused)
        {if(focused){restorePassthrough=true;nextPassthroughRestore=Time.unscaledTime+.25f;}}
        private void OnApplicationPause(bool paused)
        {if(!paused){restorePassthrough=true;nextPassthroughRestore=Time.unscaledTime+.25f;}}
        private void Publish(GridModel model,string status)
        {
            if(!safeZoneLocked)
            {
                safeZoneCenter=model.SafeZoneCenter;
                safeZoneLocked=true;
            }
            else if(GridBuilder.HorizontalDistance(model.SafeZoneCenter,safeZoneCenter)>.001f)
            {
                model=GridBuilder.Build(CurrentRoom,safeZoneCenter,model.Mode);
            }
            CurrentGrid=model;
            visualization.Draw(CurrentRoom,model,OverlayVisible);
            SetStatus(status);
            Debug.Log($"[RoomSiege.Scan] GridModel mode={model.Mode} cells={model.PlaceableCells.Count} spawns={model.SpawnPoints.Count} safe={model.SafeZoneCenter:F2}");
            GridChanged?.Invoke(model);
        }
        private void SetStatus(string message) { Status=message; Debug.Log("[RoomSiege.Scan] "+message); ViewChanged?.Invoke(); }
        private void ConfigureCamera(bool passthrough)
        { head.clearFlags=CameraClearFlags.SolidColor; head.backgroundColor=passthrough ? Color.clear : new Color(0.025f,0.045f,0.075f,1f); head.allowHDR=false; }
        private void CancelScan()
        {
            revision++;
            EntranceCalibrationPending=false;
            calibrationArmed=false;
            refreshing=false;
            if(cancellation!=null) { cancellation.Cancel(); cancellation.Dispose(); cancellation=null; }
            IsScanning=false;
            CaptureRequested=false;
            ScanStep=0;showScanProgress=false;
        }
        private void OnDestroy()
        {
            CancelScan();
#if ENABLE_PICO_OPENXR_SDK
            OpenXRExtensions.SceneAnchorDataUpdated-=MarkRoomDirty;
#endif
            PicoRoomScanner.SetPassthrough(false);
            if(head!=null) { head.clearFlags=previousClear; head.backgroundColor=previousBackground; head.allowHDR=previousHdr; }
            if(previousMenu!=null) previousMenu.SetActive(true);
            if(Instance==this) Instance=null;
        }
    }
}
