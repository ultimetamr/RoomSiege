using RoomSiege.UI;
using RoomSiege.Visuals;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.Towers
{
    // Extends the existing canvases at runtime; their follow/reset implementation remains unchanged.
    public sealed class TowerPanelUi : MonoBehaviour
    {
        private TowerHost host;
        private WorldPanelHost panels;
        private Font font;
        private Material material;
        private Text status,gold,upgradeTitle,upgrade2,upgrade3,sellLabel,coach,skinStatus;
        private Button level2Button,level3Button;
        private sealed class ShopEntry
        {public string Id,Role,ArtworkKey;public Button Button;public Text Label,Price,Detail;public TowerGrabProxy Grip;public RawImage Portrait;}
        private readonly List<ShopEntry> shop=new List<ShopEntry>();
        private GameObject upgrade;
        private TowerInstance tower;
        public bool ManagementVisible => upgrade!=null&&upgrade.activeInHierarchy;
        public void Initialize(TowerHost host,WorldPanelHost panels,Material hologram)
        {
            this.host=host;this.panels=panels;font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panels.PlacementCancelRequested+=host.Cancel;
            material=new Material(Resources.Load<Shader>("RoomSiegeWorldPanel")){name="Tower UI"};
            var main=panels.Panels[0].transform;var left=panels.Panels[1].transform;var right=panels.Panels[2].transform;
            status=panels.BuildControls.Find("Tower status").GetComponent<Text>();
            coach=Text(panels.BuildControls,"Build teaching","",new Vector2(-85,-95),new Vector2(560,50),19);
            Button(panels.BuildControls,"操作说明",new Vector2(295,-95),new Vector2(150,42),panels.ShowHelp);
            Button(panels.BuildControls,"取消选择",new Vector2(-90,-190),new Vector2(170,50),host.Cancel);
            string[] ids={"MG","Frost","Laser","Trap"}; // replaced at runtime by ProgressionController loadout
            string[] roles={"持续火力 / 近程","范围减速 / 辅助","穿透光束 / 破盾","单次爆发 / 补漏"};
            string[] names={"MG 机枪","Frost 冰墙","Laser 激光","Trap 陷阱"};
            for(int i=0;i<ids.Length;i++)
            {
                string id=ids[i];float y=105-i*84;
                var slot=left.Find("Tower slot "+i);slot.GetComponent<Image>().raycastTarget=true;
                var shopButton=slot.gameObject.AddComponent<Button>();shopButton.targetGraphic=slot.GetComponent<Image>();RoomSiegeStyle.StyleButton(shopButton);
                var drag=slot.gameObject.AddComponent<TowerShopDrag>();drag.Host=host;drag.TowerId=id;
                var text=left.Find("Tower label "+i).GetComponent<Text>();
                RoomSiegeStyle.StyleText(text,22);text.rectTransform.anchoredPosition=new Vector2(4,y+16);text.rectTransform.sizeDelta=new Vector2(132,28);text.alignment=TextAnchor.MiddleLeft;text.text=RoomSiege.Progression.ProgressionCatalog.WeaponName(id);
                var price=Text(left,id+" shop price","",new Vector2(98,y+16),new Vector2(58,28),18);price.alignment=TextAnchor.MiddleRight;
                var detail=Text(left,id+" shop role",roles[i],new Vector2(30,y-17),new Vector2(186,26),18);detail.fontSize=16;detail.alignment=TextAnchor.MiddleLeft;detail.color=RoomSiegeStyle.Muted;
                panels.RegisterContent(price.gameObject,GameState.BUILD);panels.RegisterContent(detail.gameObject,GameState.BUILD);
                var icon=new GameObject(id+" grab icon");icon.transform.SetParent(left,false);icon.transform.localPosition=new Vector3(-95,y,-48);icon.transform.localScale=Vector3.one*1000;
                var portrait=Rect(icon.transform,"Weapon portrait",Vector2.zero,new Vector2(.064f,.064f)).AddComponent<RawImage>();
                portrait.texture=Resources.Load<Texture2D>("Codex/Towers/CodexTower_"+id);portrait.material=material;portrait.raycastTarget=false;
                var grip=icon.AddComponent<TowerGrabProxy>();grip.Initialize(host,id);
                shop.Add(new ShopEntry{Id=id,Role=roles[i],Button=shopButton,Label=text,Price=price,Detail=detail,Grip=grip,Portrait=portrait});
                panels.RegisterContent(icon,GameState.BUILD);
            }
            var usage=Text(left,"Tower usage","选择 / 抓取武器\n放置后扣款",new Vector2(0,-207),new Vector2(252,44),16);usage.color=RoomSiegeStyle.Muted;usage.alignment=TextAnchor.MiddleLeft;
            panels.RegisterContent(usage.gameObject,GameState.BUILD);
            gold=right.Find("Info label 2").GetComponent<Text>();
            gold.color=RoomSiegeStyle.Gold;
            var lives=right.Find("Info label 1").GetComponent<Text>();lives.text="生命\n"+GameManager.Instance.Lives;
            BuildUpgrade();Refresh();
        }
        private void BuildUpgrade()
        {
            upgrade=Rect(panels.BuildControls,"Tower management",new Vector2(0,60),new Vector2(760,240));
            Image(upgrade.transform,"Background",Vector2.zero,new Vector2(760,240),RoomSiegeStyle.Overlay,true);
            upgradeTitle=Text(upgrade.transform,"Upgrade title","",new Vector2(0,78),new Vector2(710,60),23);upgradeTitle.alignment=TextAnchor.MiddleLeft;
            RoomSiegeStyle.AddRule(upgrade.transform,material,new Vector2(0,39),new Vector2(710,1),RoomSiegeStyle.Rule);
            level2Button=Button(upgrade.transform,"Upgrade Lv2",new Vector2(-185,6),new Vector2(340,44),()=>host.Upgrade(2));upgrade2=level2Button.GetComponentInChildren<Text>();
            level3Button=Button(upgrade.transform,"Upgrade Lv3",new Vector2(185,6),new Vector2(340,44),()=>host.Upgrade(3));upgrade3=level3Button.GetComponentInChildren<Text>();
            RoomSiegeStyle.PrimaryButton(level2Button);RoomSiegeStyle.PrimaryButton(level3Button);
            sellLabel=Button(upgrade.transform,"卖出",new Vector2(-185,-55),new Vector2(340,42),host.Sell).GetComponentInChildren<Text>();
            sellLabel.color=RoomSiegeStyle.Gold;
            Button(upgrade.transform,"收起管理",new Vector2(185,-55),new Vector2(340,42),()=>{HideUpgrade();host.SuppressTap();});
            skinStatus=Text(upgrade.transform,"Skin status","",new Vector2(0,-94),new Vector2(730,24),16);
            upgrade.SetActive(false);
        }
        public void Refresh()
        {
            if(host.Placement==null)return;
            RefreshShop();
            if(status!=null)status.text=host.Status;
            if(gold!=null)gold.text="金币\n"+GameManager.Instance.Coins+"G";
            if(tower==null||tower.Removed)return;
            var stats=tower.Stats;
            upgradeTitle.text=$"{tower.Definition.displayName} Lv{tower.Level} · {tower.Range:0.##}m\n"+ActualStats(tower);
            skinStatus.text=(tower.Skin==null?"基础全息":tower.Skin.rarity+" · "+tower.Skin.name)+(tower.FrostEnchanted?" · 冰霜附魔":"")+(tower.OverloadRemaining>0?$" · 超载 {tower.OverloadRemaining:0}s":"")+((tower.Perks?.Rank(tower.Definition.id,0)??0)+(tower.Perks?.Rank(tower.Definition.id,1)??0)+(tower.Perks?.Rank(tower.Definition.id,2)??0)>0?" · 本局强化已生效":"")+" · 卖出返 70%";
            bool can2=host.Placement.CanUpgrade(tower,2,out int p2,out _),can3=host.Placement.CanUpgrade(tower,3,out int p3,out _);
            upgrade2.text=UpgradeLabel(2,p2);upgrade3.text=UpgradeLabel(3,p3);
            level2Button.interactable=can2&&host.CanAct;level3Button.interactable=can3&&host.CanAct;
            sellLabel.text="卖出 +"+host.Placement.Refund(tower)+"G";
        }
        private static string ActualStats(TowerInstance t)
        {
            var s=t.Stats;float damage=t.DamageMultiplier;string id=t.Definition.id;
            switch(id)
            {
                case "Frost":return $"减速 {Mathf.Min(.8f,s.slowFraction+t.ControlBonus+(t.OverloadRemaining>0?t.OverloadControlIncrease:0)):P0} · 残留 {s.slowDurationSeconds.value+t.MasteryRank:0.#}s";
                case "Gravity":return $"减速 {TowerSkinRules.GravityStrength(t,null):P0} · 残留 {t.MasteryRank+.15f:0.#}s";
                case "Beacon":case "Prism":return $"光环 +{s.effectMagnitude*t.EffectMultiplier*t.OverloadOutputMultiplier:P0} · 协同 +{.05f*t.MasteryRank:P0}";
                case "Cryo":return $"每{1/(s.shotsPerSecond*t.RateMultiplier):0.##}s 寒霜+{s.effectMagnitude+t.PowerRank:0}层";
                case "Sunder":return $"破甲{Mathf.Min(.9f,s.effectMagnitude+.1f*t.PowerRank):P0} · {s.effectDuration+t.MasteryRank:0.#}s";
                case "Disruptor":return $"削盾{Mathf.Min(.95f,s.effectMagnitude+.1f*t.PowerRank):P0} · 间隔{1/(s.shotsPerSecond*t.RateMultiplier):0.##}s";
                case "Laser":return $"{s.damagePerSecond*damage*(1+t.AuraRateBonus)*t.OverloadOutputMultiplier:0.##}伤/s · 穿{s.pierceTargetCount+t.MasteryRank}";
                case "Flame":return $"喷焰{s.damagePerSecond*damage*t.RateMultiplier:0.#}伤/s · 余焰{s.effectDuration+t.MasteryRank:0.#}s";
                case "Trap":return $"触发{s.damagePerHit*damage:0.##}伤 · 回收{t.Perks?.TrapRefundFraction??0:P0}";
                default:return $"{s.damagePerHit*damage:0.##}伤 × {s.shotsPerSecond*t.RateMultiplier*(id=="MG"?t.Perks?.MgRate??1:1):0.##}次/s";
            }
        }
        private static string UpgradeLabel(int level,int price)
        {
            if(price<0)return "Lv"+level+" 已达";
            int missing=price-GameManager.Instance.Coins;
            return $"升 Lv{level} · {price}G"+(missing>0?$" · 差 {missing}G":"");
        }
        private void RefreshShop()
        {
            int coins=GameManager.Instance.Coins;
            bool placing=host.PendingTowerId!=null&&host.CanAct;
            string hint="";
            if(placing)
            {
                var pending=host.Catalog.GetTower(host.PendingTowerId);
                hint=$"{pending.displayName} · {pending.buildCost}G · 余额 {coins}G\n射线点蓝格放置，红色禁放\n{TowerStrategyHints.For(host.PendingTowerId)}";
            }
            panels.SetPlacementMode(placing||host.ExternalPlacementMode,host.ExternalPlacementMode?host.ExternalPlacementHint:hint);
            for(int slot=0;slot<shop.Count;slot++)
            {
                var item=shop[slot];
                if(host.Placement.WeaponOrder!=null&&slot<host.Placement.WeaponOrder.Count)
                {
                    string id=host.Placement.WeaponOrder[slot];
                    if(item.Id!=id)
                    {item.Id=id;item.Button.GetComponent<TowerShopDrag>().TowerId=id;item.Grip.SetTowerId(id);item.Label.text=RoomSiege.Progression.ProgressionCatalog.WeaponName(id);item.Role=RoomSiege.Data.WeaponRoster.Role(id);}
                }
                var definition=host.Catalog.GetTower(item.Id);bool available=host.CanChoose(item.Id,out _);
                item.Button.interactable=available;item.Grip.SetAvailable(available&&!panels.PlacementMode);
                int missing=definition.buildCost-coins;
                item.Price.text=definition.buildCost+"G";item.Price.color=RoomSiegeStyle.Gold;
                var skin=host.Placement.SkinForWeapon?.Invoke(item.Id);
                string artworkKey=item.Id+"/"+(skin?.id??"");
                if(item.ArtworkKey!=artworkKey){item.Portrait.texture=WeaponArtwork.Load(item.Id,skin?.id);item.ArtworkKey=artworkKey;}
                item.Detail.text=missing>0?$"金币不足 / 还差 {missing}G":skin!=null?skin.rarity+" · "+skin.name:item.Role;
                item.Detail.color=missing>0?RoomSiegeStyle.Gold:RoomSiegeStyle.Muted;
                item.Label.color=available?RoomSiegeStyle.Text:RoomSiegeStyle.Muted;
                item.Portrait.color=available?Color.white:new Color(.4f,.43f,.43f,.5f);
            }
            if(coach!=null)
            {
                coach.gameObject.SetActive(!panels.Tutorial.Visible&&!ManagementVisible);
                int count=0;foreach(var placed in host.Placement.Towers)if(!placed.Removed)count++;
                coach.color=RoomSiegeStyle.Muted;
                coach.text=GameManager.Instance.State==GameState.WAVE?"战斗中可继续部署、升级与回收":
                    host.PendingTowerId!=null?"瞄准蓝格部署":count==0?"选择武器，布置你的第一道防线":
                    GameManager.Instance.CurrentWave==0?"防线就绪后，开始第 1 波":"补充防线，准备迎接下一波";
            }
        }
        public void ShowUpgrade(TowerInstance value)
        {tower=value;WorldInputPointer.ConsumePagePress();upgrade.SetActive(true);Refresh();host.SetStatus("管理已选塔：升级按显示价格扣款；选择塔卡可直接继续建造");}
        public void HideUpgrade()
        {if(upgrade!=null&&upgrade.activeSelf){WorldInputPointer.ConsumePagePress();upgrade.SetActive(false);}tower=null;}
        public void UpdateUpgrade(TowerInstance value,Camera head)
        {
            RefreshShop();
            if(upgrade==null||!upgrade.activeSelf)return;
            if(value==null||value.Removed||panels.DisplayedState!=GameState.BUILD){HideUpgrade();return;}
        }
        public void SetGazeStatus(string state){}
        private Image Image(Transform parent,string name,Vector2 pos,Vector2 size,Color color,bool raycast)
        {var go=Rect(parent,name,pos,size);var image=go.AddComponent<RoundedGlassImage>();image.material=material;image.color=color;image.raycastTarget=raycast;return image;}
        private Text Text(Transform parent,string name,string value,Vector2 pos,Vector2 size,int fontSize)
        {var go=Rect(parent,name,pos,size);var text=go.AddComponent<Text>();text.font=font;text.material=material;text.text=value;RoomSiegeStyle.StyleText(text,fontSize);text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;return text;}
        private Button Button(Transform parent,string value,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction callback)
        {var image=Image(parent,value,pos,size,RoomSiegeStyle.Button,true);var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;RoomSiegeStyle.StyleButton(button);button.onClick.AddListener(()=>{host.SuppressTap();callback();});Text(image.transform,"Label",value,Vector2.zero,size-new Vector2(16,6),19);return button;}
        private static GameObject Rect(Transform parent,string name,Vector2 pos,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);var rect=go.GetComponent<RectTransform>();rect.anchoredPosition=pos;rect.sizeDelta=size;return go;}
        private void OnDestroy(){if(panels!=null){panels.PlacementCancelRequested-=host.Cancel;panels.SetPlacementMode(false);}if(material!=null)Destroy(material);}
    }
}
