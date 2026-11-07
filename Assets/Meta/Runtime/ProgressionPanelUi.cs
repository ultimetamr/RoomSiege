using System;
using System.Collections.Generic;
using System.Linq;
using RoomSiege.Audio;
using RoomSiege.Data;
using RoomSiege.Progression;
using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Visuals;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.Meta
{
    public sealed class ProgressionPanelUi:MonoBehaviour
    {
        private ProgressionController controller;
        private WorldPanelHost panels;
        private GameObject root,body;
        private Text sample,balance,status,heading,context;
        private int tab,skinPage,selectedSkin,supplyPage,selectedWeapon=4,replaceSlot,carryPage,supplyFilter,weaponPage,weaponFilter,loadoutPage;
        private readonly List<GameObject> content=new List<GameObject>();
        private readonly List<Button> tabs=new List<Button>();
        private RenderTexture previewTexture;
        private GameObject previewRoot;
        private Camera previewCamera;
        private Material previewMaterial,previewSurfaceMaterial;
        private TowerView preview;
        private int observedRevision=-1;
        private float nextRefresh;
        private bool cooldownWasReady=true;
        private Button buildEntry;
        private Image wallet;
        private bool observedSupplyMode;
        private bool InCampaign=>GameManager.Instance.State==GameState.BUILD||GameManager.Instance.State==GameState.WAVE;
        private bool SupplyUseMode=>InCampaign&&tab==3;
        private static readonly string[] TabNames={"全息皮肤","四武器阵容","战术补给","道具库存","收藏挑战","武器研发"};
        public bool Visible=>root!=null&&root.activeSelf;

        public void Initialize(ProgressionController controller,WorldPanelHost panels)
        {
            this.controller=controller;this.panels=panels;sample=panels.Panels[0].transform.Find("Title").GetComponent<Text>();
            root=Rect(panels.Panels[0].transform,"Progression shop overlay",Vector2.zero,new Vector2(848,488));
            root.transform.localPosition=new Vector3(0,0,-14);
            var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=panels.Panels[0].worldCamera;canvas.overrideSorting=true;canvas.sortingOrder=110;
            WorldUiSampling.Configure(canvas);
            root.AddComponent<GraphicRaycaster>();root.AddComponent<TrackedDeviceGraphicRaycaster>();
            Image(root.transform,"Shop glass",Vector2.zero,new Vector2(848,488),RoomSiegeStyle.Overlay,true);
            GameUiChrome.Frame(root.transform,sample.material);
            heading=Label(root.transform,"军械库",new Vector2(-226,207),new Vector2(310,38),28);heading.alignment=TextAnchor.MiddleLeft;
            wallet=Image(root.transform,"Fragment wallet",new Vector2(129,207),new Vector2(242,40),RoomSiegeStyle.Card,false);
            balance=Label(root.transform,"",new Vector2(129,207),new Vector2(222,30),18);balance.color=RoomSiegeStyle.Gold;
            Button(root.transform,"关闭",new Vector2(344,207),new Vector2(96,40),()=>panels.Navigation.Close(root));
            int[] nav={5,0,2,1,3,4};
            for(int i=0;i<TabNames.Length;i++){int choice=nav[i];var button=Button(root.transform,TabNames[choice],new Vector2(-330+i*132,150),new Vector2(124,40),()=>ShowTab(choice));Image(button.transform,"Selected marker",new Vector2(0,-19),new Vector2(76,2),RoomSiegeStyle.Friendly,false);tabs.Add(button);}
            tabs.Sort((a,b)=>Array.IndexOf(TabNames,a.name).CompareTo(Array.IndexOf(TabNames,b.name)));
            context=Label(root.transform,"",new Vector2(0,106),new Vector2(776,24),16);context.fontSize=16;context.alignment=TextAnchor.MiddleLeft;context.color=RoomSiegeStyle.Muted;
            body=Rect(root.transform,"Shop content",Vector2.zero,new Vector2(800,416));
            status=Label(root.transform,"",new Vector2(0,-226),new Vector2(776,28),16);status.fontSize=16;status.color=RoomSiegeStyle.Friendly;
            panels.Navigation.Register(root);
            var menu=panels.Panels[0].transform.Find("Main menu page");
            Button(menu,"军械商店 / 出战配置",new Vector2(-207,-139),new Vector2(310,42),ShowShop);
            var setup=panels.Panels[0].transform.Find("Room setup page");
            var calibration=setup.Find("标定出怪口").GetComponent<RectTransform>();calibration.anchoredPosition=new Vector2(195,-125);calibration.sizeDelta=new Vector2(350,38);
            Button(setup,"出战配置",new Vector2(-195,-125),new Vector2(350,38),ShowLoadout);
            var battle=panels.Panels[0].transform.Find("Battle controls");
            var old=battle.Find("操作说明");
            if(old!=null)
            {
                var button=old.GetComponent<Button>();button.onClick.RemoveAllListeners();button.onClick.AddListener(ShowSupplies);
                button.gameObject.name="使用道具";button.GetComponentInChildren<Text>().text="使用道具";
            }
            var build=panels.BuildControls;
            buildEntry=Button(build,"使用道具",new Vector2(295,-42),new Vector2(150,40),ShowSupplies);
            panels.RegisterContent(buildEntry.gameObject,GameState.BUILD);
            controller.Changed+=RefreshHeader;
            RefreshHeader();
        }
        public void ShowShop(){if(!controller.CanShop){controller.SetStatus("商店在主菜单、房间准备或结算时开放");return;}ShowTab(5);}
        public void ShowSkins()=>ShowTab(0);
        public void SelectSkin(string id){int index=Array.FindIndex(controller.Catalog.skins,x=>x.id==id);if(index<0)return;selectedSkin=index;skinPage=Array.IndexOf(controller.Catalog.weapons,controller.Catalog.skins[index].weaponId);ShowTab(0);}
        public void ShowWeapons()=>ShowTab(5);
        public void ShowLoadout()=>ShowTab(1);
        public void ShowSupplies()=>ShowTab(InCampaign?3:2);
        private void ShowTab(int value)
        {
            if(GameManager.Instance.State==GameState.WAVE)value=3;
            TowerHost.Instance?.Cancel();tab=value;panels.Navigation.Show(root);BuildBody();RefreshHeader();
            Debug.Log("[RoomSiege.Progression] page="+TabNames[tab]);
        }
        private void RefreshHeader()
        {
            if(controller.Store==null)return;
            bool use=SupplyUseMode;heading.text=use?"道具背包":"军械库";balance.gameObject.SetActive(!use);wallet.gameObject.SetActive(!use);
            balance.text=$"核心碎片  {controller.Store.Fragments:N0}";
            string phase=GameManager.Instance.State==GameState.RESULT?"配置下一局":controller.LoadoutLocked?"本局配置已锁定":controller.Store.PlaytestUnlocked?"试玩全解锁 · 每局选 4 武器":"首座塔部署前可调整配置";
            string[] hints={"选择外观 · 预览技能 · 穿戴生效","选中左侧槽位，再选择替换武器","核心碎片购买 · 一次性道具入库存","已购库存直接使用 · 无需携带配置","完成挑战，积累核心碎片","选择武器，查看模型与部署属性"};
            context.text=use?(GameManager.Instance.State==GameState.WAVE?"选择已购道具 · 生效后扣 1 件 · 取消不消耗 · 共享冷却 3 秒":"已购道具均已入库 · 开波后可使用，无需出战配置"):hints[tab]+"    /    "+phase;
            context.rectTransform.anchoredPosition=new Vector2(0,use?150:106);
            string message=controller.Status.Replace('\n',' ');status.text=message.Length>47?message.Substring(0,46)+"…":message;
            for(int i=0;i<tabs.Count;i++){tabs[i].gameObject.SetActive(!use);RoomSiegeStyle.NavigationButton(tabs[i],i==tab);tabs[i].transform.Find("Selected marker").gameObject.SetActive(i==tab);}
        }
        private void BuildBody()
        {
            foreach(var go in content)if(go!=null){go.SetActive(false);Destroy(go);}content.Clear();
            ClearPreview();
            if(tab==0)BuildSkins();else if(tab==1)BuildLoadout();else if(tab==2)BuildSupplies();else if(tab==3)BuildCarry();else if(tab==4)BuildChallenges();else BuildWeapons();
            observedRevision=controller.Store.Revision;observedSupplyMode=SupplyUseMode;
        }
        private Text Text(string value,Vector2 pos,Vector2 size,int fontSize=19)
        {var text=Label(body.transform,value,pos,size,fontSize);content.Add(text.gameObject);return text;}
        private Button Action(string value,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction action,bool enabled=true)
        {var button=Button(body.transform,value,pos,size,action);button.interactable=enabled;content.Add(button.gameObject);return button;}
        private void Surface(string name,Vector2 position,Vector2 size)
        {var surface=Image(body.transform,name,position,size,RoomSiegeStyle.Card,false);content.Add(surface.gameObject);}
        private Text Caption(string value,Vector2 position,Vector2 size)
        {var label=Text(value,position,size,16);label.fontSize=16;label.color=RoomSiegeStyle.Muted;label.alignment=TextAnchor.MiddleLeft;return label;}
        private void Columns()
        {Surface("Catalog surface",new Vector2(-202,-57),new Vector2(384,298));Surface("Detail surface",new Vector2(202,-57),new Vector2(384,298));}
        private void BuildSkins()
        {
            Columns();
            var skins=controller.Catalog.skins;int pages=controller.Catalog.weapons.Length;skinPage=Mathf.Clamp(skinPage,0,pages-1);
            string weapon=controller.Catalog.weapons[skinPage];
            var group=Enumerable.Range(0,skins.Length).Where(i=>skins[i].weaponId==weapon).ToArray();
            if(!group.Contains(selectedSkin))selectedSkin=group[0];
            Caption(ProgressionCatalog.WeaponName(weapon)+$"  /  专属外观  {skinPage+1:00}/{pages:00}",new Vector2(-202,76),new Vector2(352,24));
            for(int row=0;row<group.Length;row++)
            {
                int index=group[row];var skin=skins[index];bool owned=controller.Store.Owns(skin.id);float y=26-row*78;
                var button=Card("Skin product "+skin.id,new Vector2(-202,y),new Vector2(352,70),RarityColor(skin),index==selectedSkin,()=>{selectedSkin=index;BuildBody();});
                SkinPortrait(button.transform,skin,new Vector2(-128,0),new Vector2(70,60));
                Label(button.transform,skin.name,new Vector2(38,14),new Vector2(232,26),22).alignment=TextAnchor.MiddleLeft;
                var state=Label(button.transform,skin.rarity+"  /  "+(owned?controller.Store.Equipped(skin.weaponId)==skin.id?"已穿戴":"已拥有":skin.price+" 碎片"),new Vector2(38,-14),new Vector2(232,22),16);state.fontSize=16;state.color=RarityColor(skin);state.alignment=TextAnchor.MiddleLeft;
                button.name=ProgressionCatalog.WeaponName(skin.weaponId)+" · "+skin.name+"\n"+skin.rarity+"  /  "+(owned?controller.Store.Equipped(skin.weaponId)==skin.id?"已穿戴":"已拥有":skin.price+" 碎片");
            }
            Action("上一组",new Vector2(-294,-186),new Vector2(168,32),()=>{skinPage=(skinPage+pages-1)%pages;BuildBody();});
            Action("下一组",new Vector2(-110,-186),new Vector2(168,32),()=>{skinPage=(skinPage+1)%pages;BuildBody();});
            var selected=skins[selectedSkin];Color tint=RarityColor(selected);
            var name=Text(selected.name,new Vector2(202,68),new Vector2(352,30),25);name.alignment=TextAnchor.MiddleLeft;
            Caption(ProgressionCatalog.WeaponName(selected.weaponId)+"  /  "+selected.rarity+"外观",new Vector2(202,40),new Vector2(352,22)).color=tint;
            PreviewStage(new Vector2(202,-24),new Vector2(352,88),tint);
            CreatePreview(selected,new Vector2(202,-24),new Vector2(330,84));
            var description=Text(selected.description,new Vector2(202,-104),new Vector2(352,68),16);description.fontSize=16;description.alignment=TextAnchor.UpperLeft;
            Action("试听武器",new Vector2(106,-154),new Vector2(160,28),()=>PlaySample(selected.weaponId));
            Action("恢复基础全息",new Vector2(290,-154),new Vector2(176,28),()=>
            {controller.Store.Equip(selected.weaponId,"",out var reason);controller.ConfigurationChanged();controller.SetStatus(reason);BuildBody();},controller.CanConfigure&&!string.IsNullOrEmpty(controller.Store.Equipped(selected.weaponId)));
            bool has=controller.Store.Owns(selected.id);
            bool weaponOwned=controller.Store.OwnsWeapon(selected.weaponId);
            var cta=Action(!weaponOwned?"先在武器研发解锁此武器":has?controller.Store.Equipped(selected.weaponId)==selected.id?"已穿戴":"穿戴":$"购买 · {selected.price} 碎片",new Vector2(202,-190),new Vector2(352,32),()=>
            {
                string reason;
                if(!has)controller.Store.Buy(selected.id,out reason);
                else if(controller.CanConfigure){controller.Store.Equip(selected.weaponId,selected.id,out reason);controller.ConfigurationChanged();}
                else reason="本局已锁定，请在主菜单配置下一局";
                controller.SetStatus(reason);BuildBody();RefreshHeader();
            },weaponOwned&&(has?controller.CanConfigure&&controller.Store.Equipped(selected.weaponId)!=selected.id:controller.CanShop&&controller.Store.Fragments>=selected.price&&controller.Store.Writable));
            RoomSiegeStyle.PrimaryButton(cta);
        }
        private void BuildWeapons()
        {
            Columns();
            string[] ids=controller.Catalog.weapons;selectedWeapon=Mathf.Clamp(selectedWeapon,0,ids.Length-1);
            var filtered=ids.Select((id,index)=>index).Where(index=>weaponFilter==0||(weaponFilter==2)==WeaponRoster.IsSupport(ids[index])).ToArray();
            int pages=Mathf.Max(1,(filtered.Length+5)/6);weaponPage=Mathf.Clamp(weaponPage,0,pages-1);
            if(!filtered.Skip(weaponPage*6).Take(6).Contains(selectedWeapon))selectedWeapon=filtered[weaponPage*6];
            string[] filters={"全部武器","作战武器","辅助武器"};
            for(int f=0;f<3;f++){int choice=f;var filter=Action(filters[f],new Vector2(-322+f*120,74),new Vector2(112,28),()=>{weaponFilter=choice;weaponPage=0;BuildBody();});RoomSiegeStyle.NavigationButton(filter,f==weaponFilter);}
            for(int row=0;row<6&&weaponPage*6+row<filtered.Length;row++)
            {
                int i=filtered[weaponPage*6+row];int index=i;string id=ids[i];bool owned=controller.Store.OwnsWeapon(id);var unlock=controller.Catalog.Weapon(id);
                Color tint=RoomSiegeStyle.Friendly;var card=Card("Weapon "+id,new Vector2(-294+(row%2)*184,18-(row/2)*78),new Vector2(168,68),tint,i==selectedWeapon,()=>{selectedWeapon=index;BuildBody();});
                Portrait(card.transform,id,new Vector2(-48,0),new Vector2(60,60));
                var title=Label(card.transform,ProgressionCatalog.WeaponName(id),new Vector2(28,14),new Vector2(96,26),19);title.alignment=TextAnchor.MiddleLeft;
                var flag=Label(card.transform,owned?controller.Store.Loadout.Contains(id)?"已出战":"已拥有":unlock.price+" 碎片",new Vector2(28,-13),new Vector2(96,22),16);flag.fontSize=16;flag.color=owned?RoomSiegeStyle.Muted:RoomSiegeStyle.Gold;flag.alignment=TextAnchor.MiddleLeft;
            }
            Action("上一页武器",new Vector2(-316,-188),new Vector2(124,30),()=>{weaponPage=(weaponPage+pages-1)%pages;BuildBody();});
            var page=Text($"{weaponPage+1:00}/{pages:00}",new Vector2(-202,-188),new Vector2(88,26),16);page.fontSize=16;page.color=RoomSiegeStyle.Muted;
            Action("下一页武器",new Vector2(-88,-188),new Vector2(124,30),()=>{weaponPage=(weaponPage+1)%pages;BuildBody();});
            string weaponId=ids[selectedWeapon];var product=controller.Catalog.Weapon(weaponId);var definition=TowerHost.Instance.Catalog.GetTower(weaponId);Color accent=TowerVisuals.ColorFor(weaponId);
            var name=Text(product?.name??ProgressionCatalog.WeaponName(weaponId),new Vector2(202,68),new Vector2(352,30),25);name.alignment=TextAnchor.MiddleLeft;
            Caption(product?.role??WeaponRole(weaponId),new Vector2(136,37),new Vector2(220,24));
            Action("专属皮肤",new Vector2(320,37),new Vector2(116,28),()=>SelectSkin(SelectedSkinFor(weaponId)));
            PreviewStage(new Vector2(202,-24),new Vector2(352,88),RoomSiegeStyle.Friendly);
            CreatePreview(weaponId,controller.Store.EquippedDefinition(weaponId),new Vector2(202,-24),new Vector2(330,84));
            var copy=Text(product?.description??WeaponDescription(weaponId),new Vector2(202,-103),new Vector2(352,66),16);copy.fontSize=16;copy.alignment=TextAnchor.UpperLeft;
            var stats=Text($"部署 {definition.buildCost}G · 射程 {definition.rangeMeters.value:0.#}m · 升级 {definition.levels[1].upgradeCost}/{definition.levels[2].upgradeCost}G",new Vector2(202,-154),new Vector2(352,30),16);stats.fontSize=16;stats.color=RoomSiegeStyle.Gold;stats.alignment=TextAnchor.MiddleLeft;
            bool has=controller.Store.OwnsWeapon(weaponId);int price=product?.price??0;
            string caption=has?"配置到四武器阵容":controller.Store.Fragments>=price?$"永久解锁 · {price} 碎片":$"解锁 {price}  /  还差 {price-controller.Store.Fragments}";
            var buy=Action(caption,new Vector2(202,-190),new Vector2(352,32),()=>
            {if(has){ShowLoadout();return;}controller.Store.Buy(weaponId,out var reason);controller.SetStatus(reason);BuildBody();},has?controller.CanConfigure:controller.CanShop&&controller.Store.Writable&&controller.Store.Fragments>=price);
            RoomSiegeStyle.PrimaryButton(buy);
        }
        private void BuildLoadout()
        {
            Columns();
            var order=controller.Store.Loadout.ToList();
            Caption("本局阵容  /  4 个武器槽",new Vector2(-202,76),new Vector2(352,24));
            for(int i=0;i<4;i++)
            {
                int slot=i;string id=order[i];float y=30-i*64;
                var card=Card("Loadout slot "+i,new Vector2(-202,y),new Vector2(352,56),RoomSiegeStyle.Friendly,replaceSlot==i,()=>{replaceSlot=slot;BuildBody();});
                Portrait(card.transform,id,new Vector2(-139,0),new Vector2(54,48));
                var title=Label(card.transform,$"{i+1:00}  {ProgressionCatalog.WeaponName(id)}",new Vector2(-28,12),new Vector2(154,24),20);title.alignment=TextAnchor.MiddleLeft;
                var skin=controller.Store.EquippedDefinition(id);var subtitle=Label(card.transform,skin?.name??"基础全息",new Vector2(-28,-13),new Vector2(154,22),16);subtitle.fontSize=16;subtitle.alignment=TextAnchor.MiddleLeft;subtitle.color=RoomSiegeStyle.Muted;
                var button=Button(card.transform,"换肤",new Vector2(126,0),new Vector2(76,34),()=>SelectSkin(SelectedSkinFor(id)));button.interactable=controller.CanConfigure;
            }
            Caption($"替换槽位 {replaceSlot+1:00}  /  选择武器",new Vector2(202,76),new Vector2(352,24));
            int pages=(controller.Catalog.weapons.Length+5)/6;loadoutPage=Mathf.Clamp(loadoutPage,0,pages-1);
            for(int i=loadoutPage*6;i<Mathf.Min(controller.Catalog.weapons.Length,loadoutPage*6+6);i++)
            {
                string id=controller.Catalog.weapons[i];bool owned=controller.Store.OwnsWeapon(id);int existing=order.IndexOf(id);
                var button=Action(ProgressionCatalog.WeaponName(id)+"  "+(owned?existing>=0?"已在槽 "+(existing+1):"替换":"未解锁"),new Vector2(202,32-(i%6)*38),new Vector2(352,32),()=>
                {
                    int prior=order.IndexOf(id);if(prior>=0){string old=order[replaceSlot];order[replaceSlot]=id;order[prior]=old;}else order[replaceSlot]=id;
                    controller.Store.SetLoadout(order,out var reason);controller.ConfigurationChanged();controller.SetStatus(reason);BuildBody();
                },controller.CanConfigure&&owned&&existing!=replaceSlot);
                RoomSiegeStyle.SetButtonSurface(button,existing>=0?RoomSiegeStyle.Selected:RoomSiegeStyle.Button);
            }
            Action("上一组武器",new Vector2(86,-190),new Vector2(120,28),()=>{loadoutPage=(loadoutPage+pages-1)%pages;BuildBody();});
            Text($"{loadoutPage+1:00} / {pages:00}",new Vector2(202,-190),new Vector2(88,26),16).fontSize=16;
            Action("下一组武器",new Vector2(318,-190),new Vector2(120,28),()=>{loadoutPage=(loadoutPage+1)%pages;BuildBody();});
        }
        private void BuildSupplies()
        {
            string[] filters={"全部","技能","陷阱","阵法"};string[] kinds={"","skill","trap","formation"};
            for(int i=0;i<filters.Length;i++)
            {
                int choice=i;var filter=Action(filters[i],new Vector2(-297+i*198,76),new Vector2(186,28),()=>{supplyFilter=choice;supplyPage=0;BuildBody();});
                RoomSiegeStyle.NavigationButton(filter,supplyFilter==i);
            }
            var items=controller.Catalog.supplies.Where(x=>supplyFilter==0||x.category==kinds[supplyFilter]).ToArray();int pages=Mathf.Max(1,(items.Length+2)/3);supplyPage=Mathf.Clamp(supplyPage,0,pages-1);
            for(int col=0;col<3&&supplyPage*3+col<items.Length;col++)
            {
                var item=items[supplyPage*3+col];float x=-264+col*264;
                var card=Card("Supply product "+item.id,new Vector2(x,-61),new Vector2(248,232),RoomSiegeStyle.Friendly,false,null);
                Glyph(card.transform,item,new Vector2(-67,60),new Vector2(96,100));
                var name=Label(card.transform,item.name,new Vector2(48,84),new Vector2(112,28),22);name.alignment=TextAnchor.MiddleLeft;
                var inventory=Label(card.transform,SupplyKind(item)+"\n库存 "+controller.Store.Count(item.id),new Vector2(48,42),new Vector2(112,48),16);inventory.fontSize=16;inventory.alignment=TextAnchor.MiddleLeft;inventory.color=RoomSiegeStyle.Muted;
                var description=Label(card.transform,SupplySummary(item),new Vector2(0,-33),new Vector2(216,76),16);description.fontSize=16;description.alignment=TextAnchor.UpperLeft;
                bool enough=controller.Store.Fragments>=item.price;
                var buy=Button(card.transform,$"购买 {item.price} 碎片"+(enough?"":$" · 差{item.price-controller.Store.Fragments}"),new Vector2(0,-91),new Vector2(216,34),()=>
                {controller.Store.Buy(item.id,out var reason);controller.SetStatus(reason);BuildBody();RefreshHeader();});
                RoomSiegeStyle.PrimaryButton(buy);buy.interactable=controller.CanShop&&enough&&controller.Store.Count(item.id)<99&&controller.Store.Writable;
            }
            Action("上一页",new Vector2(-306,-193),new Vector2(168,28),()=>{supplyPage=(supplyPage+pages-1)%pages;BuildBody();});
            var page=Text($"{supplyPage+1:00} / {pages:00}  ·  共 {items.Length} 种战术补给",new Vector2(0,-193),new Vector2(410,26),16);page.fontSize=16;page.color=RoomSiegeStyle.Muted;
            Action("下一页",new Vector2(306,-193),new Vector2(168,28),()=>{supplyPage=(supplyPage+1)%pages;BuildBody();});
        }
        private void Glyph(Transform parent,SupplyDefinition item,Vector2 position,Vector2 size)
        {
            var portrait=Resources.Load<Texture2D>("SupportDevices/Portraits/"+item.id);
            if(portrait!=null){var image=Rect(parent,"Device portrait "+item.id,position,size).AddComponent<RawImage>();image.texture=portrait;image.material=sample.material;image.raycastTarget=false;return;}
            var icon=Rect(parent,"Vector supply icon",position,size).AddComponent<ArmoryGlyph>();icon.Id=item.id;icon.color=SupplyColor(item.id);icon.material=sample.material;icon.raycastTarget=false;
        }
        private void BuildCarry()
        {
            bool use=SupplyUseMode,battle=GameManager.Instance.State==GameState.WAVE;
            var items=controller.Catalog.supplies;int pages=Mathf.Max(1,(items.Length+2)/3);carryPage=Mathf.Clamp(carryPage,0,pages-1);
            if(!use)Caption("道具库存 / 购买后自动入库，局内直接使用",new Vector2(0,76),new Vector2(776,24));
            float top=use?84:24,spacing=use?98:82;
            for(int row=0;row<3&&carryPage*3+row<items.Length;row++)
            {
                var item=items[carryPage*3+row];string id=item.id;int stock=controller.Store.Count(id);
                var card=Card("Inventory item "+id,new Vector2(0,top-row*spacing),new Vector2(776,use?84:72),RoomSiegeStyle.Friendly,false,null);
                Glyph(card.transform,item,new Vector2(-348,0),new Vector2(42,42));
                var name=Label(card.transform,item.name,new Vector2(-213,15),new Vector2(210,28),22);name.alignment=TextAnchor.MiddleLeft;
                var count=Label(card.transform,SupplyKind(item)+" / 库存 "+stock,new Vector2(-213,-17),new Vector2(210,24),16);count.fontSize=16;count.alignment=TextAnchor.MiddleLeft;count.color=stock>0?RoomSiegeStyle.Muted:RoomSiegeStyle.Enemy;
                var description=Label(card.transform,SupplySummary(item),new Vector2(60,0),new Vector2(310,56),16);description.fontSize=16;description.alignment=TextAnchor.MiddleLeft;description.color=RoomSiegeStyle.Muted;
                string label=stock<=0?"库存为 0":!use?"已入库":!battle?"开波后使用":controller.SupplyCooldown>0?"冷却中":"使用";
                var action=Button(card.transform,label,new Vector2(310,0),new Vector2(124,38),()=>{controller.TryUseSupply(id);if(Visible)BuildBody();});
                RoomSiegeStyle.PrimaryButton(action);action.interactable=use&&battle&&stock>0&&controller.SupplyCooldown<=0;
            }
            Action("上一组道具",new Vector2(-306,-190),new Vector2(168,28),()=>{carryPage=(carryPage+pages-1)%pages;BuildBody();});
            var page=Text($"{carryPage+1:00} / {pages:00}  ·  共 {items.Length} 种道具",new Vector2(0,-190),new Vector2(390,26),16);page.fontSize=16;page.color=RoomSiegeStyle.Muted;
            Action("下一组道具",new Vector2(306,-190),new Vector2(168,28),()=>{carryPage=(carryPage+1)%pages;BuildBody();});
        }
        private void BuildChallenges()
        {
            string[] ids={"first-victory-0","first-victory-1","first-victory-2","flawless","no-supplies","six-tower-victory","trap-victory"};
            string[] names={"首次休闲通关","首次标准通关","首次硬核通关","无伤通关","不使用商店道具通关","总建造不超过 6 座通关","使用陷阱并通关"};
            for(int i=0;i<ids.Length;i++)
            {
                bool done=controller.Store.Achievements.Contains(ids[i]);float x=i%2==0?-202:202,y=58-i/2*58;
                var card=Card("Challenge "+ids[i],new Vector2(x,y),new Vector2(384,50),RoomSiegeStyle.Friendly,false,null);
                var title=Label(card.transform,names[i],new Vector2(-27,10),new Vector2(294,24),18);title.alignment=TextAnchor.MiddleLeft;
                var state=Label(card.transform,done?"已完成":"待挑战",new Vector2(-27,-13),new Vector2(294,20),16);state.fontSize=16;state.alignment=TextAnchor.MiddleLeft;state.color=done?RoomSiegeStyle.Friendly:RoomSiegeStyle.Muted;
                var reward=Label(card.transform,i<3?"+300":"+100",new Vector2(145,0),new Vector2(64,26),18);reward.color=RoomSiegeStyle.Gold;
            }
            var rules=Caption("每波 10 / 通关 150 / 无伤 50；难度 ×1 / ×1.25 / ×1.5（逐笔取整）。\n失败保留波次奖励；挑战另计。金评级外观继续免费解锁。",new Vector2(0,-184),new Vector2(776,44));rules.alignment=TextAnchor.MiddleLeft;
        }
        private Button Card(string name,Vector2 pos,Vector2 size,Color accent,bool selected,UnityEngine.Events.UnityAction click)
        {
            var image=Image(body.transform,name,pos,size,selected?RoomSiegeStyle.Selected:RoomSiegeStyle.Card,click!=null);content.Add(image.gameObject);
            Image(image.transform,"Rarity rail",new Vector2(-size.x*.5f+2,0),new Vector2(selected?3:2,size.y-16),RoomSiegeStyle.Alpha(accent,selected?1:.35f),false);
            var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;
            if(click==null){button.transition=Selectable.Transition.None;return button;}
            RoomSiegeStyle.StyleButton(button);
            button.onClick.AddListener(()=>{WorldInputPointer.ConsumePagePress();TowerHost.Instance?.SuppressTap();click();});return button;
        }
        private void Portrait(Transform parent,string id,Vector2 pos,Vector2 size)
        {
            var go=Rect(parent,"Weapon artwork "+id,pos,size);var image=go.AddComponent<RawImage>();image.material=sample.material;image.texture=WeaponArtwork.Load(id,controller.Store.Equipped(id));image.raycastTarget=false;
        }
        private string SelectedSkinFor(string weaponId)=>controller.Store.EquippedDefinition(weaponId)?.id??controller.Catalog.skins.First(x=>x.weaponId==weaponId).id;
        private void SkinPortrait(Transform parent,SkinDefinition skin,Vector2 pos,Vector2 size)
        {
            var go=Rect(parent,"Skin artwork "+skin.id,pos,size);var image=go.AddComponent<RawImage>();image.material=sample.material;
            image.texture=Resources.Load<Texture2D>("Codex/Skins/Skin_"+skin.id);image.raycastTarget=false;
        }
        private void PreviewStage(Vector2 pos,Vector2 size,Color tint)
        {
            var image=Image(body.transform,"Weapon display stage",pos,size,new Color(.035f,.065f,.10f,.85f),false);content.Add(image.gameObject);
            Image(image.transform,"Platform line",new Vector2(0,-size.y*.5f+5),new Vector2(size.x-24,1),RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.3f),false);
        }
        private void PlaySample(string id)
        {bool played=AudioDirector.Instance!=null&&AudioDirector.Instance.PlayCodexSample(id,false);controller.SetStatus(played?"正在回放该武器空间音效":"音频尚未准备，请稍后试听");}
        private static string WeaponRole(string id)=>WeaponRoster.IsNew(id)?WeaponRoster.Role(id):id=="MG"?"持续火力 / 近程":id=="Frost"?"范围减速 / 辅助":id=="Laser"?"持续穿透 / 破盾":"爆炸拦截 / 一次性";
        private static string WeaponDescription(string id)=>WeaponRoster.IsNew(id)?WeaponRoster.Description(id):id=="MG"?"稳定的单体火力。三级解锁小范围溅射，与冰墙配合延长输出时间。":id=="Frost"?"范围减速控制。多座冰墙额外叠加减速，最高80%；需要输出塔配合。":id=="Laser"?"持续光束克制护盾。三级穿透两个目标，沿通路排列更有效。":"经过时触发爆炸并消耗。适合通路补漏，对后期小怪保持爆发能力。";
        private Color SupplyColor(string id)=>SupportFieldVisual.ColorFor(controller.Catalog.Supply(id));
        private static string SupplyKind(SupplyDefinition item)=>item.category=="formation"?"阵法":item.category=="trap"?"陷阱":"技能";
        private static string SupplySummary(SupplyDefinition item)
        {
            string duration=$"{item.duration:0}s";
            switch(item.id)
            {
                case "firefield":return $"{item.radius:0.#}m / {duration} · 灼烧{item.magnitude:0}伤/s";
                case "gravity":return $"{item.radius:0.#}m / {duration} · 减速{item.magnitude:P0}，Boss减半";
                case "mine":return $"守候{duration} · 一次爆炸{item.magnitude:0}伤";
                case "thunder":return $"{duration} · 每秒落雷{item.magnitude:0}伤";
                case "corrosion":return $"{duration}破甲区 · 破甲{item.magnitude:P0} / 残留{item.statusSeconds:0}s";
                case "frostsigil":return $"{duration} · 减速60% / 每秒寒霜+{item.magnitude:0}";
                case "freeze":return $"核心{item.radius:0.#}m / {duration}\n减速{item.magnitude:P0} · 每{item.pulseInterval:0}s周期冻结\nBoss冻结{item.bossDuration:0.#}s";
                case "emp":return $"{duration}电磁区 · 入场削盾{item.magnitude:P0}\n减速50% · Boss减速30%";
                case "overload":return $"{duration} · 输出+{(item.magnitude-1)*100:0}% / 射程+20%";
                case "decoy":return $"{duration} · 持续牵引 / 减速65%";
                case "repair":return $"即回{item.magnitude:0}生命 · {duration}持续维修";
                default:return $"{duration} · 每{item.pulseInterval:0}s轰击{item.magnitude:0}伤";
            }
        }

        private void CreatePreview(SkinDefinition skin,Vector2 pos,Vector2 size)=>CreatePreview(skin.weaponId,skin,pos,size);
        private void CreatePreview(string id,SkinDefinition skin,Vector2 pos,Vector2 size)
        {
            var imageGo=Rect(body.transform,"Actual weapon preview",pos,size);content.Add(imageGo);
            var image=imageGo.AddComponent<RawImage>();image.material=sample.material;image.raycastTarget=false;
            previewRoot=new GameObject("Armory preview isolation");previewRoot.transform.position=new Vector3(0,-100,0);
            previewMaterial=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            var definition=TowerHost.Instance.Catalog.GetTower(id);
            var instance=TowerInstance.CreatePreview(definition,new RoomSiege.Scan.GridCell("preview",RoomSiege.Scan.SurfaceKind.Floor,Vector2Int.zero,previewRoot.transform.position));instance.Skin=skin;
            var model=new GameObject("Preview "+(skin?.id??id));model.transform.SetParent(previewRoot.transform,false);preview=model.AddComponent<TowerView>();preview.Build(instance,previewMaterial);
            foreach(var collider in model.GetComponentsInChildren<Collider>())collider.enabled=false;
            // Stable showroom surfaces avoid additive overlapping faces and world-only dissolve detail.
            previewSurfaceMaterial=new Material(Resources.Load<Shader>("RoomSiegePreviewSurface"));
            foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>())renderer.sharedMaterial=previewSurfaceMaterial;
            preview.enabled=false;
            foreach(var child in model.GetComponentsInChildren<Transform>())child.gameObject.layer=30;
            var cameraObject=new GameObject("Armory preview camera");cameraObject.transform.SetParent(previewRoot.transform,false);previewCamera=cameraObject.AddComponent<Camera>();
            previewCamera.depth=-100;previewCamera.allowHDR=false;previewCamera.allowMSAA=true;
            var cameraData=previewCamera.GetUniversalAdditionalCameraData();
            cameraData.allowXRRendering=false;cameraData.renderPostProcessing=false;cameraData.renderShadows=false;
            previewCamera.stereoTargetEye=StereoTargetEyeMask.None;previewCamera.cullingMask=1<<30;previewCamera.clearFlags=CameraClearFlags.SolidColor;previewCamera.backgroundColor=new Color(.025f,.04f,.07f,1);
            previewCamera.nearClipPlane=.01f;previewCamera.farClipPlane=3;previewCamera.orthographic=true;previewCamera.orthographicSize=id=="Trap"?.105f:.17f;
            previewCamera.transform.position=previewRoot.transform.position+new Vector3(.36f,.30f,.46f);previewCamera.transform.LookAt(previewRoot.transform.position+Vector3.up*(id=="Trap"?.025f:.14f));
            previewTexture=new RenderTexture(1024,384,24){name="Stable armory preview",antiAliasing=4,useMipMap=true,autoGenerateMips=true,filterMode=FilterMode.Trilinear,anisoLevel=4,wrapMode=TextureWrapMode.Clamp};previewTexture.Create();previewCamera.targetTexture=previewTexture;previewCamera.aspect=size.x/size.y;image.texture=previewTexture;
            // Only the preview camera sees this layer; prevent distant objects in both XR eyes.
            if(Camera.main!=null)Camera.main.cullingMask&=~(1<<30);
        }
        private void Update()
        {
            if(buildEntry!=null)buildEntry.gameObject.SetActive(panels.DisplayedState==GameState.BUILD&&!panels.Navigation.HasOpenPage&&TowerHost.Instance!=null&&TowerHost.Instance.ManagedTower==null);
            if(!Visible){if(previewCamera!=null)previewCamera.enabled=false;return;}
            if(previewCamera!=null)previewCamera.enabled=true;
            // Keep the inspection pose still so small details remain readable in the headset.
            if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.25f;
            RefreshHeader();
            bool cooldownReady=controller.SupplyCooldown<=0;
            if(observedRevision!=controller.Store.Revision||observedSupplyMode!=SupplyUseMode||tab==3&&cooldownReady!=cooldownWasReady)BuildBody();
            cooldownWasReady=cooldownReady;
        }
        private void ClearPreview()
        {
            if(previewCamera!=null){previewCamera.enabled=false;previewCamera.targetTexture=null;}
            if(previewRoot!=null){previewRoot.SetActive(false);Destroy(previewRoot);}previewRoot=null;preview=null;previewCamera=null;
            if(previewTexture!=null){previewTexture.Release();Destroy(previewTexture);}previewTexture=null;
            if(previewMaterial!=null)Destroy(previewMaterial);previewMaterial=null;
            if(previewSurfaceMaterial!=null)Destroy(previewSurfaceMaterial);previewSurfaceMaterial=null;
        }
        private Color RarityColor(SkinDefinition skin)=>ColorUtility.TryParseHtmlString(skin.color,out var color)?color:RoomSiegeStyle.Friendly;
        private GameObject Rect(Transform parent,string name,Vector2 pos,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);var rect=go.GetComponent<RectTransform>();rect.anchoredPosition=pos;rect.sizeDelta=size;return go;}
        private Image Image(Transform parent,string name,Vector2 pos,Vector2 size,Color color,bool raycast)
        {var go=Rect(parent,name,pos,size);var image=go.AddComponent<RoundedGlassImage>();image.material=sample.material;image.color=color;image.raycastTarget=raycast;return image;}
        private Text Label(Transform parent,string value,Vector2 pos,Vector2 size,int fontSize)
        {var text=Rect(parent,value.Length>50?"Description":value,pos,size).AddComponent<Text>();text.font=sample.font;text.material=sample.material;text.text=value;RoomSiegeStyle.StyleText(text,fontSize);text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;return text;}
        private Button Button(Transform parent,string value,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction action)
        {
            var image=Image(parent,value,pos,size,RoomSiegeStyle.Button,true);var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;RoomSiegeStyle.StyleButton(button);
            Label(image.transform,value,Vector2.zero,size-new Vector2(12,4),18);
            button.onClick.AddListener(()=>{WorldInputPointer.ConsumePagePress();TowerHost.Instance?.SuppressTap();action();});return button;
        }
        private void OnDestroy(){if(controller!=null)controller.Changed-=RefreshHeader;ClearPreview();}
    }
}
