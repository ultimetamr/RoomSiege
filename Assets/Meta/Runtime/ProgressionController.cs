using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq;
using RoomSiege.Audio;
using RoomSiege.Enemies;
using RoomSiege.Progression;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Waves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomSiege.Meta
{
    [DefaultExecutionOrder(11250)]
    public sealed class ProgressionController:MonoBehaviour
    {
        public static ProgressionController Instance {get;private set;}
        public ProgressionCatalog Catalog {get;private set;}
        public ProgressionStore Store {get;private set;}
        public bool IsReady {get;private set;}
        public bool LoadoutLocked {get;private set;}
        public string Status {get;private set;}="";
        public bool CanConfigure=>IsReady&&Store.Writable&&(manager.State==GameState.RESULT||!LoadoutLocked&&(manager.State==GameState.MENU||manager.State==GameState.SCAN||manager.State==GameState.BUILD));
        public bool CanShop=>IsReady&&(manager.State==GameState.MENU||manager.State==GameState.SCAN||manager.State==GameState.RESULT);
        public float SupplyCooldown=>cooldown;
        public event Action Changed;
        private GameManager manager;
        private TowerHost towers;
        private EnemyController enemies;
        private WaveController waves;
        private WorldPanelHost panels;
        private ScanController scanner;
        private ProgressionPanelUi ui;
        private string runId="",pendingSupply;
        private List<string> runWeapons=new List<string>();
        private Dictionary<string,SkinDefinition> runSkins=new Dictionary<string,SkinDefinition>();
        private readonly List<EnemyActor> affected=new List<EnemyActor>();
        private readonly List<EnemyActor> fieldTargets=new List<EnemyActor>();
        private int nextFieldSource=-13000;
        public int ActiveSupportCount=>markers.Count;
        public IEnumerable<string> ActiveSupplyIds()=>markers.Where(x=>x.field!=null&&!x.field.Finished).Select(x=>x.id)
            .Concat(towers.Placement.Towers.Where(x=>!x.Removed&&x.OverloadRemaining>0).Select(x=>"overload")).Distinct().ToArray();
        private float cooldown;
        private bool ownsRun;
        private readonly List<SupportMarker> markers=new List<SupportMarker>();
        private Material material;
        private LineRenderer aimRing;
        private sealed class SupportMarker
        {
            public string id;public Vector3 point;public float remaining,duration,radius;public LineRenderer ring;public bool detonated;
            public SupportField field;public SupportFieldVisual visual;public GameObject device;public TowerInstance tower;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if(SceneManager.GetActiveScene().name=="RoomSiege_Start"&&FindObjectOfType<ProgressionController>()==null)
                new GameObject("RoomSiege Progression").AddComponent<ProgressionController>();
        }
        private void Awake()
        {
            if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;
            try{Catalog=ProgressionCatalog.Load();Store=new ProgressionStore(Catalog,new PlayerPrefsProgressionStorage());}
            catch(Exception error){Status="局外系统无法加载："+error.Message;Debug.LogError("[RoomSiege.Progression] "+Status);enabled=false;}
        }
        private IEnumerator Start()
        {
            while(Store==null||(manager=GameManager.Instance)==null||(towers=TowerHost.Instance)==null||towers.Placement==null||
                (enemies=EnemyController.Instance)==null||!enemies.IsReady||(waves=WaveController.Instance)==null||!waves.IsReady||
                (panels=WorldPanelHost.Instance)==null||panels.Panels==null||(scanner=ScanController.Instance)==null)yield return null;
            // A crashed/closed run keeps wave rewards, but is never resumed as a second payout.
            if(!Store.RunSettled)Store.Settle(Store.RunId,false);
            // Retire historical trial grants on both device and editor startup.
            bool playtestFailed=!PlaytestAccess.Load().Apply(Store);
            material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            manager.CampaignReset+=ResetRun;manager.StateChanged+=StateChanged;manager.CoreDamaged+=CoreDamaged;
            towers.Placement.Changed+=TowersChanged;waves.WaveFinished+=WaveFinished;waves.CampaignFinished+=CloseRun;waves.Changed+=RefreshReinforcedFields;
            Store.Changed+=StoreChanged;towers.ExternalCancelled+=CancelSupply;
            towers.Placement.WeaponAllowed=id=>Store.OwnsWeapon(id)&&(ownsRun?runWeapons.Contains(id):Store.Loadout.Contains(id));
            towers.Placement.SkinForWeapon=id=>ownsRun&&runSkins.TryGetValue(id,out var skin)?skin:null;
            towers.Placement.WeaponOrder=Store.Loadout;
            towers.Placement.Perks.ActiveSupplyTargets=ActiveSupplyIds;
            IsReady=true;ui=gameObject.AddComponent<ProgressionPanelUi>();ui.Initialize(this,panels);
            if(manager.State==GameState.BUILD||manager.State==GameState.WAVE)BeginRun();
            SetStatus(playtestFailed?Store.Error:Store.PlaytestUnlocked?"试玩全解锁：18 武器 / 54 皮肤；12 种道具测试库存已就绪，每局选 4 武器。":Store.Writable?"局外购买道具入库；局内从道具背包使用，未使用的库存保留。":Store.Error);
            Debug.Log($"[RoomSiege.Progression] ready skins={Catalog.skins.Length} supplies={Catalog.supplies.Length} fragments={Store.Fragments} writable={Store.Writable}");
            Debug.Log($"[RoomSiege.Progression] Access: playtest={Store.PlaytestUnlocked}, weapons={Catalog.weapons.Count(Store.OwnsWeapon)}/{Catalog.weapons.Length}, skins={Catalog.skins.Count(x=>Store.Owns(x.id))}/{Catalog.skins.Length}, supplyUnits={Catalog.supplies.Sum(x=>Store.Count(x.id))}, loadout={string.Join(",",Store.Loadout)}");
        }
        private void StoreChanged(){if(!ownsRun)towers.Placement.WeaponOrder=Store.Loadout;Changed?.Invoke();}
        public void SetStatus(string value){Status=value;if(pendingSupply!=null&&towers!=null)towers.ExternalPlacementHint=value+"\n扣扳机 / 捏合确认 · 取消不扣库存";Changed?.Invoke();}
        private void StateChanged(GameState state)
        {
            if(state==GameState.BUILD&&!ownsRun)BeginRun();
            if(state!=GameState.WAVE)CancelSupply();
            if(state==GameState.MENU||state==GameState.SCAN||state==GameState.RESULT)ClearMarkers();
        }
        private void BeginRun()
        {
            string id=Guid.NewGuid().ToString("N");
            if(!Store.BeginRun(id,manager.Difficulty)){SetStatus(Store.Error.Length>0?Store.Error:"局外奖励记录未能开始，战斗仍可进行");return;}
            runId=id;ownsRun=true;LoadoutLocked=false;cooldown=0;CaptureLoadout();
        }
        private void CaptureLoadout()
        {
            runWeapons=Store.Loadout.ToList();runSkins.Clear();
            foreach(string id in runWeapons)runSkins[id]=Store.EquippedDefinition(id);
            towers.Placement.WeaponOrder=runWeapons;
        }
        public void ConfigurationChanged()
        {
            if(ownsRun&&!LoadoutLocked)CaptureLoadout();Changed?.Invoke();
        }
        private void TowersChanged()
        {
            if(!ownsRun||LoadoutLocked)return;
            if(towers.Placement.Towers.Any(x=>!x.Removed)){LoadoutLocked=true;Changed?.Invoke();}
        }
        private void CoreDamaged(float amount){if(ownsRun)Store.RecordDamage(runId);}
        private void WaveFinished(WaveResult value)
        {
            if(ownsRun&&value.Reason==WaveEndReason.Cleared)
            {
                bool saved=Store.WaveCleared(runId,value.WaveNumber);
                SetStatus(saved?$"第 {value.WaveNumber} 波奖励已入账 · 本局获得 {Store.LastReward} 核心碎片":Store.Error.Length>0?Store.Error:"该波奖励已记录");
            }
        }
        public void CloseRun(CampaignOutcome outcome)
        {
            if(!ownsRun||outcome==CampaignOutcome.None)return;
            CancelSupply();
            if(Store.Settle(runId,outcome==CampaignOutcome.Victory))
            {
                if(outcome==CampaignOutcome.Victory&&towers.Placement.Towers.Count<=6)Store.CompleteChallenge("six-tower-victory",100);
                if(outcome==CampaignOutcome.Victory&&towers.Placement.Towers.Any(x=>x.Definition.id=="Trap"&&x.Consumed))Store.CompleteChallenge("trap-victory",100);
            }
            SetStatus($"本局共获得 {Store.LastReward} 核心碎片（含已入账波次奖励） · 余额 {Store.Fragments}");
        }
        private void ResetRun()
        {
            if(ownsRun&&!Store.RunSettled)Store.Settle(runId,false);
            CancelSupply();ClearMarkers();ownsRun=false;runId="";LoadoutLocked=false;cooldown=0;
            towers.Placement.WeaponOrder=Store.Loadout;Changed?.Invoke();
        }
        public string RewardSummary=>$"本局核心碎片 +{Store?.LastReward??0} · 总余额 {Store?.Fragments??0}";
        public bool TryUseSupply(string id)
        {
            WorldInputPointer.ConsumePagePress();
            if(!ownsRun||manager.State!=GameState.WAVE||!waves.IsWaveActive||waves.SimulationPaused||cooldown>0||Catalog.Supply(id)==null||Store.Count(id)<=0)
            {SetStatus(cooldown>0?$"补给共享冷却 {cooldown:0.0}s":"该道具库存不足或当前不在战斗中");return false;}
            var item=Catalog.Supply(id);
            if(item.target=="core")return ApplySupply(item,scanner.CurrentGrid.SafeZoneCenter,null);
            towers.Cancel();pendingSupply=id;towers.ExternalWorldTap=TapSupply;towers.ExternalWorldPreview=PreviewSupply;
            towers.ExternalPlacementHint=item.name+(item.target=="tower"?" · 瞄准非陷阱塔":" · 瞄准可通行地板")+"，扣扳机/捏合确认；取消不消耗";
            panels.Navigation.CloseCurrent();towers.SuppressTap();SetStatus(towers.ExternalPlacementHint);return true;
        }
        public void CancelSupply()
        {
            pendingSupply=null;if(aimRing!=null)aimRing.gameObject.SetActive(false);
            if(towers!=null){towers.ExternalWorldTap=null;towers.ExternalWorldPreview=null;towers.ExternalPlacementHint="";}
            if(panels!=null)panels.SetPlacementMode(false);
        }
        private void PreviewSupply(Ray ray)
        {
            var item=Catalog.Supply(pendingSupply);if(item==null)return;
            Vector3 point=ray.GetPoint(2);bool valid;
            if(item.target=="tower")
            {
                var view=TowerAt(ray);valid=view!=null&&!view.Tower.Removed&&!view.Tower.Definition.singleUse&&view.Tower.OverloadRemaining<=0;
                if(view!=null)point=view.Tower.Cell.Center;
            }
            else valid=TryGroundPoint(ray,out point);
            if(aimRing==null)aimRing=TowerVisuals.Ring(transform,"Supply aim preview",material,TowerVisuals.Blue,1,.01f);
            aimRing.gameObject.SetActive(true);aimRing.transform.position=point+Vector3.up*.025f;
            aimRing.transform.localScale=Vector3.one*(item.target=="tower"?.2f:Mathf.Max(.2f,item.radius*towers.Placement.Perks.SupplyRange(item.id)));
            aimRing.startColor=aimRing.endColor=valid?SupportFieldVisual.ColorFor(item):TowerVisuals.Red;
        }
        private static TowerView TowerAt(Ray ray)
        {
            foreach(var hit in Physics.RaycastAll(ray,10,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance))
            {
                if(TowerTargetRegistry.FromCollider(hit.collider)!=null)continue;
                return hit.collider.GetComponentInParent<TowerView>();
            }
            return null;
        }
        private bool TryGroundPoint(Ray ray,out Vector3 point)
        {
            point=ray.GetPoint(2);var grid=scanner.CurrentGrid;
            if(grid==null||!new Plane(Vector3.up,new Vector3(0,grid.SafeZoneCenter.y,0)).Raycast(ray,out float d)||d>10||d<0)return false;
            point=ray.GetPoint(d);var target=point;
            return grid.SupportedCells.Any(cell=>cell.Surface==SurfaceKind.Floor&&Vector3.Distance(cell.Center,target)<=.3f)&&
                (grid.WalkMap==null||towers.Placement.Routes!=null&&towers.Placement.Routes.CanReach(point))&&
                !towers.Placement.Towers.Any(t=>!t.Removed&&!t.Definition.singleUse&&Vector3.Distance(t.Cell.Center,target)<.3f);
        }
        private bool TapSupply(Ray ray)
        {
            var item=Catalog.Supply(pendingSupply);
            if(item==null)return false;
            if(item.target=="tower")
            {
                TowerView view=TowerAt(ray);
                if(view==null||view.Tower.Removed||view.Tower.Definition.singleUse){SetStatus("请瞄准已有的非陷阱塔；未消耗");return false;}
                return ApplySupply(item,view.Tower.Cell.Center,view.Tower);
            }
            if(!TryGroundPoint(ray,out var point)){SetStatus("请选择可通行且未占用的地板；未消耗");return false;}
            return ApplySupply(item,point,null);
        }
        private bool ApplySupply(SupplyDefinition item,Vector3 point,TowerInstance tower)
        {
            if(!ownsRun||manager.State!=GameState.WAVE||!waves.IsWaveActive||waves.SimulationPaused||cooldown>0)return false;
            affected.Clear();
            foreach(var actor in enemies.ActiveEnemies)
                if(actor!=null&&actor.CanBeTargeted&&GridBuilder.HorizontalDistance(actor.Position,point)<=item.radius*towers.Placement.Perks.SupplyRange(item.id))affected.Add(actor);
            TowerRouteMap decoy=null;
            if(item.id=="repair"&&manager.Lives>=manager.InitialLives){SetStatus("核心满血，维修未消耗");return false;}
            if(item.id=="overload"&&(tower==null||tower.Removed||tower.OverloadRemaining>0)){SetStatus("该塔已超载或不存在；未消耗");return false;}
            if(item.id=="decoy")
            {
                decoy=new TowerRouteMap(GridBuilder.WithSafeZone(scanner.CurrentGrid,point),towers.Placement.Towers);
                if(!decoy.CanReach(point)){SetStatus("信标无法连通可通行地板；未消耗");return false;}
            }
            // Validate everything before committing; all effects below are local synchronous commands.
            if(!Store.Consume(runId,item.id)){SetStatus(Store.Error.Length>0?Store.Error:"道具库存为 0，未触发效果");return false;}
            cooldown=3;
            if(item.id=="repair")manager.Heal(item.magnitude*towers.Placement.Perks.SupplyPower(item.id));
            if(item.id=="overload")
            {
                tower.OverloadStrength=item.magnitude;tower.OverloadRangeBonus=.2f;tower.OverloadControlBonus=.2f;
                tower.OverloadLifetimeGranted=towers.Placement.Perks.SupplyLifetime(item.id);
                tower.OverloadRemaining=item.duration+tower.OverloadLifetimeGranted;
            }
            AddMarker(item,point,decoy,tower);CancelSupply();panels.Navigation.CloseCurrent();
            AudioDirector.Instance?.PlaySupport(item.id,point);
            SetStatus(item.name+" 已使用 · 库存 "+Store.Count(item.id)+" · 共享冷却 3s");
            Debug.Log("[RoomSiege.Progression] supply="+item.id+" run="+runId);return true;
        }
        private void AddMarker(SupplyDefinition item,Vector3 point,TowerRouteMap decoy=null,TowerInstance tower=null)
        {
            var ring=TowerVisuals.Ring(transform,item.name+" world marker",material,SupportFieldVisual.ColorFor(item),1,.014f);
            ring.transform.position=point+Vector3.up*.03f;
            float duration=SupportField.Supports(item.id)?item.duration+.45f:1;
            var marker=new SupportMarker{id=item.id,point=point,remaining=duration,duration=duration,radius=Mathf.Max(.2f,item.radius),ring=ring};
            if(SupportField.Supports(item.id))
            {
                marker.field=new SupportField(item,point,nextFieldSource--,manager.CurrentWave,decoy,towers.Placement.Perks);
                marker.field.Step(.00001f,affected,null,value=>manager.Heal(value));
                var go=new GameObject(item.name+" sigil");go.transform.SetParent(transform,false);marker.visual=go.AddComponent<SupportFieldVisual>();marker.visual.Build(item,material,point);marker.visual.Draw(marker.field,0);
            }
            else if(item.id=="overload")
            {
                marker.tower=tower;marker.remaining=marker.duration=tower!=null?tower.OverloadRemaining:item.duration;
                marker.device=SupportDeviceModel.Create(transform,item);
                if(marker.device!=null){marker.device.transform.position=point+new Vector3(.19f,.04f,0);marker.device.transform.localScale=Vector3.one*.65f;}
            }
            markers.Add(marker);
        }
        private void RefreshReinforcedFields()
        {
            foreach(var marker in markers)if(marker.field!=null&&!marker.field.Finished)
            {
                marker.radius=Mathf.Max(.2f,marker.field.Radius);marker.remaining=marker.field.Remaining+.45f;
                marker.ring.transform.localScale=Vector3.one*marker.radius;marker.visual.Draw(marker.field,0);
            }
        }
        private void Update()
        {
            if(!IsReady)return;
            if(manager.State!=GameState.WAVE)return;
            if(waves.IsPaused)return;
            float dt=Time.deltaTime;cooldown=Mathf.Max(0,cooldown-dt);
            TickMarkers(dt);
        }
        private void TickMarkers(float dt)
        {
            fieldTargets.Clear();fieldTargets.AddRange(enemies.ActiveEnemies);
            for(int i=markers.Count-1;i>=0;i--)
            {
                var marker=markers[i];marker.remaining=marker.tower!=null?(marker.tower.Removed?0:marker.tower.OverloadRemaining):marker.remaining-dt;
                if(marker.field!=null)
                {
                    if(!marker.field.Finished)
                    {
                        marker.radius=Mathf.Max(.2f,marker.field.Radius);
                        marker.field.Step(dt,fieldTargets,point=>{marker.visual.Pulse(point);AudioDirector.Instance?.PlaySupport(marker.id,point);},value=>manager.Heal(value));
                        marker.remaining=marker.field.Finished?.45f:marker.field.Remaining+.45f;
                    }
                    marker.visual.Draw(marker.field,dt);
                    marker.ring.transform.localScale=Vector3.one*marker.radius*(marker.field.Finished?1+(.45f-marker.remaining)*2:1);
                    var tint=SupportFieldVisual.ColorFor(Catalog.Supply(marker.id));tint.a=marker.field.Finished?Mathf.Clamp01(marker.remaining/.45f):.65f;
                    marker.ring.startColor=marker.ring.endColor=tint;
                    if(marker.remaining<=0){Destroy(marker.ring.gameObject);Destroy(marker.visual.gameObject);markers.RemoveAt(i);}
                    continue;
                }
                float age=marker.duration-marker.remaining;float size=marker.id=="strike"?marker.radius:marker.radius*Mathf.Min(1,.2f+age*2);
                marker.ring.transform.localScale=Vector3.one*size;
                var markerTint=SupportFieldVisual.ColorFor(Catalog.Supply(marker.id));markerTint.a=Mathf.Clamp01(marker.remaining);
                marker.ring.startColor=marker.ring.endColor=marker.id=="strike"?new Color(1,.35f,.2f,Mathf.PingPong(age*2,1)*.5f+.4f):markerTint;
                if(marker.remaining<=0){Destroy(marker.ring.gameObject);if(marker.device!=null)Destroy(marker.device);markers.RemoveAt(i);}
            }
        }
        private void ClearMarkers(){foreach(var marker in markers){if(marker.ring!=null)Destroy(marker.ring.gameObject);if(marker.visual!=null)Destroy(marker.visual.gameObject);if(marker.device!=null)Destroy(marker.device);}markers.Clear();fieldTargets.Clear();}
        public void OpenShop()=>ui?.ShowShop();
        public void OpenLoadout()=>ui?.ShowLoadout();
        private void OnDestroy()
        {
            if(manager!=null){manager.CampaignReset-=ResetRun;manager.StateChanged-=StateChanged;manager.CoreDamaged-=CoreDamaged;}
            if(waves!=null){waves.WaveFinished-=WaveFinished;waves.CampaignFinished-=CloseRun;waves.Changed-=RefreshReinforcedFields;}
            if(towers!=null){towers.ExternalCancelled-=CancelSupply;if(towers.Placement!=null){towers.Placement.Changed-=TowersChanged;towers.Placement.WeaponAllowed=null;towers.Placement.SkinForWeapon=null;towers.Placement.Perks.ActiveSupplyTargets=null;}}
            if(Store!=null)Store.Changed-=StoreChanged;
            CancelSupply();ClearMarkers();if(material!=null)Destroy(material);if(Instance==this)Instance=null;
        }
    }
}
