using System.Collections;
using System.Collections.Generic;
using RoomSiege.Data;
using RoomSiege.Scan;
using RoomSiege.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.UI;

namespace RoomSiege.Towers
{
    [DefaultExecutionOrder(11000)]
    public sealed class TowerHost : MonoBehaviour
    {
        public static TowerHost Instance {get;private set;}
        public Camera Head {get;private set;}
        public TowerPlacement Placement {get;private set;}
        public BalanceCatalog Catalog {get;private set;}
        public readonly GazeFocus Focus=new GazeFocus();
        private readonly Dictionary<int,TowerView> views=new Dictionary<int,TowerView>();
        private readonly Dictionary<int,TowerCombat> combat=new Dictionary<int,TowerCombat>();
        private readonly List<ITowerTarget> targets=new List<ITowerTarget>();
        private readonly HashSet<ITowerTarget> assignedTargets=new HashSet<ITowerTarget>();
        private readonly List<TowerTestTarget> stubs=new List<TowerTestTarget>();
        private readonly List<UnityEngine.XR.InputDevice> eyes=new List<UnityEngine.XR.InputDevice>();
        private readonly List<RaycastResult> uiHits=new List<RaycastResult>();
        private readonly RaycastHit[] worldHits=new RaycastHit[64];
        private XRRayInteractor[] rays;
        private readonly List<XRInputButtonReader> gripReaders=new List<XRInputButtonReader>();
        private GameManager manager;
        private ScanController scanner;
        private WorldPanelHost panels;
        private TowerPanelUi ui;
        private Material hologram;
        private TrapExplosionVisuals trapExplosions;
        private LineRenderer ghost;
        private LineRenderer invalid;
        private string pending;
        private GridCell previewCell;
        private bool previewHasCell;
        private float redUntil;
        private int suppressFrame=-1;
        private bool leftTrigger,rightTrigger;
        private bool simulatedGaze;
        private TowerInstance selected;
        private bool ready;
        public bool CanAct => ready && !panels.Follower.IsPaused && (manager.State==GameState.BUILD || manager.State==GameState.WAVE);
        public string PendingTowerId => pending;
        public System.Func<Ray,bool> ExternalWorldTap {get;set;}
        public System.Action<Ray> ExternalWorldPreview {get;set;}
        public System.Action ExternalCancelled {get;set;}
        public string ExternalPlacementHint {get;set;}
        public bool ExternalPlacementMode=>ExternalWorldTap!=null;
        public TowerInstance ManagedTower => ui!=null&&ui.ManagementVisible&&selected!=null&&!selected.Removed?selected:null;
        public string Status {get;private set;}="进入建造后，从左栏抓取/选塔，再 Tap 格子";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if(SceneManager.GetActiveScene().name=="RoomSiege_Start" && FindObjectOfType<TowerHost>()==null)
                new GameObject("RoomSiege Towers").AddComponent<TowerHost>();
        }
        private void Awake(){if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;}
        private IEnumerator Start()
        {
            while((panels=WorldPanelHost.Instance)==null || panels.Panels==null || (scanner=ScanController.Instance)==null)yield return null;
            Head=Camera.main;manager=GameManager.Instance;
            if(Head==null||manager==null){Debug.LogError("[RoomSiege.Towers] Camera or GameManager missing.");yield break;}
            var json=Resources.Load<TextAsset>("RoomSiegeTowerBalance");
            if(json==null){Debug.LogError("[RoomSiege.Towers] Runtime balance asset missing; run Prepare Towers.");yield break;}
            Catalog=BalanceJson.Parse(json.text);
            var difficulty=Catalog.GetDifficulty(manager.Difficulty==0?"Casual":manager.Difficulty==2?"Hardcore":"Standard");
            manager.InitializeEconomy(difficulty.initialGold);
            Placement=new TowerPlacement(Catalog,new GameTowerWallet(manager));
            hologram=new Material(Resources.Load<Shader>("RoomSiegeHologram")){name="RoomSiege tower holograms"};
            RoomSiege.Visuals.RoomSiegeStyle.ApplyWorldDetail(hologram);
            trapExplosions=gameObject.AddComponent<TrapExplosionVisuals>();trapExplosions.Build(hologram);
            ghost=TowerVisuals.Ring(transform,"Placement preview",hologram,TowerVisuals.Blue,.18f,.01f);ghost.gameObject.SetActive(false);
            invalid=TowerVisuals.Ring(transform,"Invalid red feedback",hologram,TowerVisuals.Red,.2f,.015f);invalid.gameObject.SetActive(false);
            ui=gameObject.AddComponent<TowerPanelUi>();ui.Initialize(this,panels,hologram);
            rays=FindObjectsOfType<XRRayInteractor>();
            foreach(var ray in rays)
            {
                if(ray.GetComponent<WorldInputPointer>()!=null)continue;
                string hand=ray.name.Contains("Left")?"Left":"Right";
                var reader=new XRInputButtonReader("Tower Grip",inputSourceMode:XRInputButtonReader.InputSourceMode.InputAction);
                reader.inputActionPerformed.AddBinding("<XRController>{"+hand+"Hand}/gripPressed");
                reader.inputActionValue.AddBinding("<XRController>{"+hand+"Hand}/grip");
                ray.selectInput=reader;reader.inputActionPerformed.Enable();reader.inputActionValue.Enable();gripReaders.Add(reader);
            }
            Placement.Changed+=Refresh;scanner.GridChanged+=GridChanged;manager.CoinsChanged+=CoinsChanged;manager.CampaignReset+=ResetCampaign;
            scanner.CanMoveCore=Placement.CanMoveCore;
            scanner.LayoutFailure=grid=>new TowerRouteMap(grid,Placement.Towers).ReadinessFailure;
            scanner.CoreMovementPaused=()=>panels.Follower.IsPaused||panels.Navigation.HasOpenPage||manager.State==GameState.RESULT;
            ready=true;GridChanged(scanner.CurrentGrid);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            gameObject.AddComponent<TowerRuntimeProbe>();
#endif
            Debug.Log($"[RoomSiege.Towers] Ready; gold={manager.Coins}; OpenXR grip / pointer / tap; test doubles only.");
        }
        private void CoinsChanged(int coins)
        {
            if(pending!=null && !Placement.CanPurchase(pending,out string reason))
            {pending=null;ghost.gameObject.SetActive(false);SetStatus(reason+"；已取消待建造");}
            if(ui!=null)ui.Refresh();
        }
        private void GridChanged(GridModel grid)
        {
            bool changedMode=Placement.Grid!=null && grid!=null && Placement.Grid.Mode!=grid.Mode;
            int before=manager.Coins;Placement.SetGrid(grid);
            if(grid==null || changedMode)Focus.Clear();
            if(manager.Coins>before)SetStatus($"部署格变化，失效塔全额退还 {manager.Coins-before}G");
            if(changedMode)ClearStubs();
            if(grid==null){pending=null;if(ghost!=null)ghost.gameObject.SetActive(false);}
        }
        public void EnterBuild()
        {
            SuppressTap();
            if(manager.State==GameState.WAVE){panels.SetConstructionOpen(true);return;}
            if(manager.State==GameState.MENU)manager.SetState(GameState.SCAN);
        }
        private void ResetCampaign()
        {
            pending=null;selected=null;Focus.Clear();ClearStubs();ui.HideUpgrade();
            Placement.ResetCampaign();ghost.gameObject.SetActive(false);invalid.gameObject.SetActive(false);
            trapExplosions.Clear();
            leftTrigger=rightTrigger=false;
            SetStatus("选择左侧防御塔 → 点蓝色格部署；布防完成后开始第一波");
        }
        public bool CanChoose(string id,out string reason)
        {
            if(!CanAct||panels.Navigation.HasOpenPage||panels.DisplayedState!=GameState.BUILD)
            {reason="请打开建造栏，或关闭暂停/功能页后再选择";return false;}
            return Placement.CanPurchase(id,out reason);
        }
        public bool Choose(string id)
        {
            SuppressTap();
            if(!CanChoose(id,out string reason))
            {pending=null;ghost.gameObject.SetActive(false);SetStatus(reason);return false;}
            pending=id;selected=null;ui.HideUpgrade();SetStatus("选塔免费，蓝格建新塔；点已有塔打开升级，不重复扣造价\n"+TowerStrategyHints.For(id));
            return true;
        }
        public void Cancel(){ExternalCancelled?.Invoke();pending=null;ghost.gameObject.SetActive(false);ui.HideUpgrade();Focus.Clear();SetStatus("已取消 / 清除集火");SuppressTap();}
        public void SuppressTap(){suppressFrame=Time.frameCount;}
        public void SetStatus(string status){Status=status;if(ui!=null)ui.Refresh();Debug.Log("[RoomSiege.Towers] "+status);}

        private void Update()
        {
            if(!ready)return;
            if(Time.time>redUntil)invalid.gameObject.SetActive(false);
            float dt=CanAct && !panels.Navigation.HasOpenPage?Time.deltaTime:0;
            trapExplosions.Tick(Time.deltaTime,panels.Follower.IsPaused||panels.Navigation.HasOpenPage&&manager.State!=GameState.RESULT);
            if(manager.State==GameState.MENU||manager.State==GameState.SCAN)trapExplosions.Clear();
            targets.Clear();
            assignedTargets.Clear();
            foreach(var stub in stubs)if(stub!=null){stub.Tick(dt,Head);targets.Add(stub);}
            TowerTargetRegistry.AppendAliveTo(targets);
            TowerAuraSystem.Resolve(Placement.Towers,!Placement.Suspended&&dt>0);
            foreach(var tower in Placement.Towers)
            {
                if(tower.Removed || !views.TryGetValue(tower.Id,out var view) || view==null)continue;
                if(tower.OverloadRemaining>0)
                {float lifetime=Placement.Perks.SupplyLifetime("overload");tower.OverloadRemaining+=Mathf.Max(0,lifetime-tower.OverloadLifetimeGranted);tower.OverloadLifetimeGranted=lifetime;}
                if(Placement.Suspended || !CanAct){view.StopFire();continue;}
                var engine=combat[tower.Id];
                engine.Step(dt,Placement.Grid.SafeZoneCenter,targets,Focus.Focused,Focus.Focused==null?assignedTargets:null);
                if(Focus.Focused==null&&engine.Target!=null&&engine.Target.IsAlive)assignedTargets.Add(engine.Target);
                if(manager.State==GameState.WAVE)tower.OverloadRemaining=Mathf.Max(0,tower.OverloadRemaining-dt);
                view.Aim(engine.Target,dt);
            }
            UpdateGaze(dt);
            foreach(var stub in stubs)if(stub!=null)stub.SetFocused(ReferenceEquals(Focus.Focused,stub));
            foreach(var target in targets)
                if(target is ITowerFocusVisual visual)visual.SetFocused(ReferenceEquals(Focus.Focused,target));
            ProcessPointer();
            if(Keyboard.current!=null)
            {
                if(Keyboard.current.escapeKey.wasPressedThisFrame)Cancel();
                if(Keyboard.current.bKey.wasPressedThisFrame)EnterBuild();
            }
            ui.UpdateUpgrade(selected,Head);
        }
        private void ProcessPointer()
        {
            if(pending!=null && (panels.DisplayedState!=GameState.BUILD||panels.Navigation.HasOpenPage||!CanAct))
            {pending=null;ghost.gameObject.SetActive(false);ui.Refresh();}
            // Full function pages own input. The inline tower inspector keeps construction available.
            if(panels.Navigation!=null && panels.Navigation.HasOpenPage){ghost.gameObject.SetActive(false);return;}
            bool lt=Trigger(XRNode.LeftHand),rt=Trigger(XRNode.RightHand);
            bool lDown=lt&&!leftTrigger,rDown=rt&&!rightTrigger;leftTrigger=lt;rightTrigger=rt;
            // Use the same device and press edge as the UI ray, including hand aim/pinch.
            foreach(var ray in rays)
            {
                var pointer=ray!=null?ray.GetComponent<WorldInputPointer>():null;
                if(pointer==null)continue;
                if(ray.name.Contains("Left"))lDown=pointer.Tracked&&pointer.PressedThisFrame;
                else rDown=pointer.Tracked&&pointer.PressedThisFrame;
            }
            Ray current=new Ray(Head.transform.position,Head.transform.forward);XRRayInteractor active=null;
            foreach(var ray in rays)
            {
                if(ray==null || !ray.isActiveAndEnabled)continue;
                bool left=ray.name.Contains("Left");
                if((lDown&&left)||(rDown&&!left) || (active==null && !left) || ray.hasSelection)
                {active=ray;var origin=ray.rayOriginTransform!=null?ray.rayOriginTransform:ray.transform;current=new Ray(origin.position,origin.forward);}
            }
            bool mouse=Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame;
            if(mouse || (Application.isEditor && Mouse.current!=null)){current=Head.ScreenPointToRay(Mouse.current.position.ReadValue());active=null;}
            bool uiBlocked=active!=null && active.TryGetCurrentUIRaycastResult(out _);
            if(active==null && Mouse.current!=null && EventSystem.current!=null)
            {
                var data=new PointerEventData(EventSystem.current){position=Mouse.current.position.ReadValue()};
                uiHits.Clear();EventSystem.current.RaycastAll(data,uiHits);uiBlocked=uiHits.Count>0;
            }
            if(ExternalPlacementMode&&!uiBlocked&&CanAct)ExternalWorldPreview?.Invoke(current);
            if(pending!=null && !uiBlocked && CanAct)Preview(current);
            else if(uiBlocked)ghost.gameObject.SetActive(false);
            if((lDown||rDown||mouse) && suppressFrame<Time.frameCount-1 && !uiBlocked && CanAct)
            {
                bool holding=false;foreach(var ray in rays)if(ray!=null&&ray.hasSelection)holding=true;
                if(!holding)Tap(current);
            }
        }
        private static bool Trigger(XRNode node) => UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node).TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton,out bool value)&&value;
        public void Preview(Ray ray)
        {
            if(pending==null||!CanAct)return;
            previewHasCell=TryCell(ray,out previewCell,out var hit);
            TowerInstance existing=previewHasCell?Placement.FindActiveAtCell(previewCell):null;
            if(TryWorldHit(ray,10,true,out var towerHit))
            {
                var view=towerHit.collider.GetComponentInParent<TowerView>();
                if(view!=null&&!view.Tower.Removed)existing=view.Tower;
            }
            if(existing!=null)
            {
                ghost.gameObject.SetActive(true);ghost.transform.position=existing.Cell.Center+Vector3.up*.015f;
                ghost.startColor=ghost.endColor=RoomSiege.Visuals.RoomSiegeStyle.Gold;return;
            }
            bool valid=previewHasCell && Placement.CanBuild(pending,previewCell,out _,out _);
            ghost.gameObject.SetActive(true);ghost.transform.position=(previewHasCell?previewCell.Center:hit)+Vector3.up*.015f;
            ghost.startColor=ghost.endColor=valid?TowerVisuals.Blue:TowerVisuals.Red;
        }
        public bool Tap(Ray ray)
        {
            SuppressTap();if(!CanAct)return false;
            if(ExternalWorldTap!=null)return ExternalWorldTap(ray);
            // While placing, an occupied blue cell is an explicit upgrade
            // target. This takes priority over buying a duplicate tower.
            if(pending!=null && TryCell(ray,out var occupiedCell,out _))
            {
                var occupied=Placement.FindActiveAtCell(occupiedCell);
                if(occupied!=null){OpenTowerManagement(occupied);return true;}
            }
            // An existing tower always opens its inspector rather than buying through it.
            if(TryWorldHit(ray,10,pending!=null,out var physicsHit))
            {
                var target=TowerTargetRegistry.FromCollider(physicsHit.collider);
                if(target!=null&&target.IsAlive)
                {Focus.Mark(target);SetStatus("指挥集火已标记：射程内防御塔优先攻击该敌人");return true;}
                var view=physicsHit.collider.GetComponentInParent<TowerView>();
                if(view!=null&&!view.Tower.Removed)
                {OpenTowerManagement(view.Tower);return true;}
            }
            if(pending==null)return false;
            Preview(ray);
            string reason="不可放格 / 核心本体 / 出怪口";
            if(previewHasCell && Placement.TryBuild(pending,previewCell,out var tower,out reason))
            {
                // One placement restores the shop; no unexpected repeat purchase on the next tap.
                selected=null;ui.HideUpgrade();ghost.gameObject.SetActive(false);
                pending=null;
                SetStatus($"建造 {tower.Definition.displayName} -{tower.Definition.buildCost}G；余额 {manager.Coins}G。"+
                    "建造栏已恢复，选择塔卡继续部署");
                return true;
            }
            invalid.gameObject.SetActive(true);invalid.transform.position=ghost.transform.position;redUntil=Time.time+.6f;SetStatus(reason);return false;
        }
        private void OpenTowerManagement(TowerInstance tower)
        {
            pending=null;ghost.gameObject.SetActive(false);selected=tower;panels.SetConstructionOpen(true);ui.ShowUpgrade(selected);
            SetStatus($"已选中 {tower.Definition.displayName} Lv{tower.Level}，可升级或卖出");
        }
        private bool TryCell(Ray ray,out GridCell cell,out Vector3 hit)
        {
            // Moving enemies do not steal a placement tap. Furniture still blocks it.
            bool found=TowerCellPicker.TryPick(Placement.Grid,ray,out cell,out hit,
                d=>TryWorldHit(ray,Mathf.Max(0,d-.02f),true,out var blocker)&&blocker.collider.GetComponentInParent<TowerView>()==null);
            if(!found)
            {var floor=new Plane(Vector3.up,new Vector3(0,scanner.CurrentRoom!=null?scanner.CurrentRoom.FloorHeight:0,0));if(floor.Raycast(ray,out float d)&&d<10)hit=ray.GetPoint(d);}
            return found;
        }
        private bool TryWorldHit(Ray ray,float distance,bool ignoreEnemies,out RaycastHit hit)
        {
            hit=default;
            if(!ignoreEnemies)return Physics.Raycast(ray,out hit,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            int count=Physics.RaycastNonAlloc(ray,worldHits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            // A saturated buffer must not accidentally omit an opaque blocker.
            var hits=count==worldHits.Length?Physics.RaycastAll(ray,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore):worldHits;
            if(hits!=worldHits)count=hits.Length;
            float nearest=float.PositiveInfinity;bool found=false;
            for(int i=0;i<count;i++)
            {
                var candidate=hits[i];
                if(TowerTargetRegistry.FromCollider(candidate.collider)!=null||candidate.distance>=nearest)continue;
                nearest=candidate.distance;hit=candidate;found=true;
            }
            return found;
        }
        public void Upgrade(int level)
        {
            SuppressTap();if(!CanAct)return;
            if(Placement.TryUpgrade(selected,level,out string reason))SetStatus($"{selected.Definition.displayName} Lv{selected.Level}；余额 {manager.Coins}G");else SetStatus(reason);
        }
        public void Sell()
        {
            SuppressTap();if(!CanAct)return;
            if(Placement.TrySell(selected,out int refund)){selected=null;ui.HideUpgrade();SetStatus($"卖出返还 {refund}G；余额 {manager.Coins}G");}
        }
        private void Refresh()
        {
            foreach(var tower in Placement.Towers)
            {
                if(tower.Removed)
                {if(views.TryGetValue(tower.Id,out var old)&&old!=null){old.gameObject.SetActive(false);Destroy(old.gameObject);}views.Remove(tower.Id);combat.Remove(tower.Id);if(ReferenceEquals(selected,tower)){selected=null;ui.HideUpgrade();}continue;}
                if(!views.TryGetValue(tower.Id,out var view))
                {
                    var go=new GameObject(tower.Definition.id+" Tower "+tower.Id);go.transform.SetParent(transform,false);view=go.AddComponent<TowerView>();view.Build(tower,hologram);views.Add(tower.Id,view);
                    var engine=new TowerCombat(tower,Placement.Consume,()=>Placement.Towers);engine.Fired+=view.Fire;engine.Launched+=view.Launch;combat.Add(tower.Id,engine);
                }
                view.Refresh();
            }
            if(ui!=null)ui.Refresh();
        }
        private void UpdateGaze(float dt)
        {
            Ray ray;bool valid=false;
            ray=new Ray(Head.transform.position,Head.transform.forward);
            if(simulatedGaze)valid=true;
            else
            {
                eyes.Clear();UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking,eyes);
                foreach(var device in eyes)
                {
                    if(!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool tracked)||!tracked)continue;
                    if(!device.TryGetFeatureValue(EyeTrackingUsages.gazePosition,out var p)||!device.TryGetFeatureValue(EyeTrackingUsages.gazeRotation,out var q))continue;
                    if(!WorldPanelMotion.Valid(p)||!WorldPanelMotion.Valid(q*Vector3.forward))continue;
                    Transform tracking=Head.transform.parent;
                    ray=new Ray(tracking!=null?tracking.TransformPoint(p):p,tracking!=null?tracking.TransformDirection(q*Vector3.forward):q*Vector3.forward);valid=true;break;
                }
            }
            ITowerTarget candidate=null;
            if(valid && Physics.Raycast(ray,out var hit,10,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))candidate=TowerTargetRegistry.FromCollider(hit.collider);
            var before=Focus.Focused;Focus.Step(candidate,dt);
            if(!ReferenceEquals(before,Focus.Focused) && Focus.Focused!=null)SetStatus("注视满 0.5s：已标记集火；仅射程内塔优先攻击");
            ui.SetGazeStatus(simulatedGaze?"模拟头向注视":valid?"真实眼动就绪":"眼动不可用");
        }
        public void ToggleSimulatedGaze(){simulatedGaze=!simulatedGaze;Focus.Clear();SetStatus(simulatedGaze?"模拟头向注视开启（非真实眼动）":"已切回真实眼动；无追踪时不集火");SuppressTap();}
        public void EnableRealEyeTracking()
        {
            simulatedGaze=false;Focus.Clear();SuppressTap();
#if UNITY_ANDROID && !UNITY_EDITOR
            const string permission="com.picovr.permission.EYE_TRACKING";
            if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))
            {
                SetStatus("请在系统弹窗选择眼动权限；拒绝后仍可用模拟注视");
                UnityEngine.Android.Permission.RequestUserPermission(permission);
                return;
            }
#endif
            SetStatus("真实眼动已选；只有追踪有效时才累积 0.5s 集火");
        }
        public void SpawnStubs()
        {
            SuppressTap();if(!CanAct || Placement.Grid==null){SetStatus("请先进入建造并完成扫描");return;}
            if(manager.State!=GameState.BUILD){SetStatus("波次进行中，请在准备期使用测试桩");return;}
            ClearStubs();Vector3 center=selected!=null&&!selected.Removed?selected.Cell.Center:Placement.Grid.SafeZoneCenter+Vector3.forward*1.8f;
            if(selected==null || selected.Removed)
                foreach(var tower in Placement.Towers)if(!tower.Removed){center=tower.Cell.Center;break;}
            var stats=DifficultyCalculator.Apply(Catalog.GetEnemy("Grunt"),Catalog.GetDifficulty(manager.Difficulty==0?"Casual":manager.Difficulty==2?"Hardcore":"Standard"));
            for(int i=0;i<3;i++)
            {
                Vector3 a=center+new Vector3(i*.10f,0,.32f+i*.32f)+Vector3.up*.16f;
                Vector3 b=center+new Vector3(-.35f,0,-.45f)+Vector3.up*.16f;
                var go=new GameObject("Tower test dummy "+i);go.transform.SetParent(transform,false);var stub=go.AddComponent<TowerTestTarget>();
                stub.Initialize(a,b,i==0,stats.HitPoints,stats.MoveSpeed,hologram);stubs.Add(stub);
            }
            SetStatus("已生成 3 个 Grunt 测试桩（1 个往返移动）；不掉金币、不扣生命");
        }
        public void ClearStubs()
        {Focus.Clear();foreach(var stub in stubs)if(stub!=null){stub.gameObject.SetActive(false);Destroy(stub.gameObject);}stubs.Clear();targets.Clear();SuppressTap();}
        private void OnDestroy()
        {
            if(scanner!=null){scanner.CanMoveCore=null;scanner.CoreMovementPaused=null;scanner.LayoutFailure=null;}
            if(scanner!=null)scanner.GridChanged-=GridChanged;if(manager!=null)manager.CoinsChanged-=CoinsChanged;
            if(manager!=null)manager.CampaignReset-=ResetCampaign;
            if(Placement!=null)Placement.Changed-=Refresh;
            foreach(var reader in gripReaders){reader.inputActionPerformed.Disable();reader.inputActionValue.Disable();}
            if(hologram!=null)Destroy(hologram);if(Instance==this)Instance=null;
        }
    }
}
