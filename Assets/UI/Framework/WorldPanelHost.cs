using System;
using System.Collections.Generic;
using RoomSiege.Scan;
using RoomSiege.Visuals;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.UI
{
    public sealed class WorldPanelHost : MonoBehaviour
    {
        public static WorldPanelHost Instance { get; private set; }
        public WorldPanelFollower Follower { get; private set; }
        public Canvas[] Panels { get; private set; }
        public WorldPanelNavigation Navigation { get; private set; }
        public Transform BuildControls { get; private set; }
        public bool CampaignReady { get; set; }
        public Func<bool> RoomReady { get; set; }
        public Func<string> RoomFailure { get; set; }
        public Func<int, int> StartingGold { get; set; }
        public bool ConstructionOpen { get; private set; }
        public bool PlacementMode { get; private set; }
        public bool CalibrationMode { get; private set; }
        public event Action PlacementCancelRequested;
        private CanvasGroup[] placementGroups;
        private Canvas placementHud;
        private Text placementHint;
        private Canvas calibrationHud;
        private Text calibrationHint;
        private float nextCalibrationPreview;
        public FirstRunTutorial Tutorial { get; } = new FirstRunTutorial();
        public event Action NewCampaignRequested, StartWaveRequested, RestartRequested, MenuRequested;
        public event Action LibraryRequested, AudioRequested;
        private readonly Dictionary<GameObject, GameState[]> contentStates = new Dictionary<GameObject, GameState[]>();
        private readonly List<GameObject> hiddenPanels = new List<GameObject>();
        private readonly List<Button> difficulties = new List<Button>();
        private Font font;
        private Material material;
        private Text scanStatus, difficultyLabel, battleStatus, startWaveLabel, title, resultHint;
        private Button enterBuild, startWave, library, audio;
        private Button scanReal,coreMode;
        private Text scanRealLabel,coreModeLabel,calibrateLabel;
        private string lastRoomFailure;
        private Button calibrate;
        private GameObject pausePage;
        private GameObject helpPage;
        private Text helpTitle, helpBody, helpIndex, helpNextLabel;
        private Button helpPrevious;
        private int helpStep;
        private GameObject tutorialBanner;
        private Text tutorialTitle, tutorialBody, tutorialDismissLabel;
        private Button tutorialDefer;
        private GameObject tutorialProgressRing, tutorialProgressLabel;
        private GameObject hudGlass, hudLife, hudGold;
        private float tutorialCompletedSeconds;
        private GameObject choicePage;
        private Text choiceTitle;
        private readonly List<Text> choiceLabels=new List<Text>();
        private Action<int> choosePerk;
        private Button tacticalButton;
        private Text tacticalLabel;
        public event Action TacticalRequested;
        private ScanController scanner;
        private ScanVisualization roomVisuals;
        private GameManager manager;
        private float nextInputProbe;
        public GameState DisplayedState => manager == null ? GameState.MENU :
            manager.State == GameState.WAVE && ConstructionOpen ? GameState.BUILD : manager.State;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name == "RoomSiege_Start" && FindObjectOfType<WorldPanelHost>() == null)
                new GameObject("RoomSiege World Panels").AddComponent<WorldPanelHost>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }
        private void Start() { Build(Camera.main); }

        public void Build(Camera camera)
        {
            if (Follower != null || camera == null) return;
            gameObject.layer = 5;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var shader = Resources.Load<Shader>("RoomSiegeWorldPanel");
            if (shader == null) throw new InvalidOperationException("RoomSiegeWorldPanel shader missing.");
            material = new Material(shader) { name = "RoomSiege world UI", hideFlags = HideFlags.DontSave };
            foreach (var old in FindObjectsOfType<Canvas>(true))
                if ((old.name == "RoomSiegeWorldSpacePanel" || old.name == "RoomSiege Scan Panel") && old.gameObject.activeSelf)
                { hiddenPanels.Add(old.gameObject); old.gameObject.SetActive(false); }
            var group = gameObject.AddComponent<CanvasGroup>();
            Follower = gameObject.AddComponent<WorldPanelFollower>();
            var main = Panel("Main Battlefield", Vector2.zero, new Vector2(860,500), camera);
            var left = Panel("Left Construction", new Vector2(-.64f,-.025f), new Vector2(300,460), camera);
            var right = Panel("Right Wave Intel", new Vector2(.64f,.005f), new Vector2(300,400), camera);
            var top = Panel("Top Wave Progress", new Vector2(0,.34f), new Vector2(800,218), camera,false);
            Panels = new[] {main,left,right,top};
            Navigation = gameObject.AddComponent<WorldPanelNavigation>();
            Navigation.Initialize(Panels);
            Navigation.ContentRestored += RefreshPhaseVisibility;
            RegisterContent(main.transform.Find("Main Battlefield background").gameObject,GameState.MENU,GameState.SCAN,GameState.RESULT);
            main.transform.Find("Main Battlefield background").GetComponent<Image>().color=RoomSiegeStyle.Overlay;
            MenuKeyArt(main.transform.Find("Main Battlefield background"));
            title = Label(main.transform,"Title","RoomSiege",new Vector2(0,194),new Vector2(780,52),30);
            title.fontSize=32;title.color=RoomSiegeStyle.Text;
            var emblem=Rect(main.transform,"RoomSiege brand emblem",new Vector2(-390,194),new Vector2(42,42)).AddComponent<RawImage>();
            emblem.texture=Resources.Load<Texture2D>("Branding/RoomSiegeEmblem");emblem.material=material;emblem.raycastTarget=false;
            RegisterContent(emblem.gameObject,GameState.MENU);
            RegisterContent(title.gameObject,GameState.MENU,GameState.SCAN,GameState.RESULT);

            var menu = Rect(main.transform,"Main menu page",Vector2.zero,new Vector2(820,360));
            var eyebrow=Label(menu.transform,"Menu eyebrow","房间防线  /  MR TOWER DEFENSE",new Vector2(-207,127),new Vector2(310,28),18);
            eyebrow.alignment=TextAnchor.MiddleLeft;eyebrow.color=RoomSiegeStyle.Muted;
            var play=MakeButton(menu.transform,"开始游戏",new Vector2(-207,77),new Vector2(310,48),()=>OpenSetup(ScanMode.RealRoom));
            RoomSiegeStyle.PrimaryButton(play);
            RoomSiegeStyle.AddRule(play.transform,material,new Vector2(-153,0),new Vector2(3,32),RoomSiegeStyle.Friendly);
            Label(menu.transform,"Campaign promise","18 选 4 武器    /    5 类入侵者    /    10 波防守",new Vector2(0,-207),new Vector2(730,28),18).color=RoomSiegeStyle.Muted;
            library=MakeButton(menu.transform,"图鉴与排行",new Vector2(-207,23),new Vector2(310,42),()=>LibraryRequested?.Invoke());
            audio=MakeButton(menu.transform,"声音与布局",new Vector2(-207,-31),new Vector2(310,42),()=>AudioRequested?.Invoke());
            MakeButton(menu.transform,"新手教学 / 重新学习",new Vector2(-207,-85),new Vector2(310,42),StartTutorial);
            RoomSiegeStyle.AddRule(menu.transform,material,new Vector2(0,-179),new Vector2(730,1),RoomSiegeStyle.Rule);
            RegisterContent(menu,GameState.MENU);

            var setup=Rect(main.transform,"Room setup page",Vector2.zero,new Vector2(820,380));
            scanStatus=Label(setup.transform,"Scan status","正在读取房间…",new Vector2(0,115),new Vector2(790,80),18);
            scanReal=MakeButton(setup.transform,"扫描真实房间",new Vector2(-195,43),new Vector2(350,42),()=>scanner?.ScanRealRoom());
            scanRealLabel=scanReal.GetComponentInChildren<Text>();
            MakeButton(setup.transform,"使用模拟沙盘",new Vector2(195,43),new Vector2(350,42),()=>scanner?.UseSandbox());
            string[] names={"休闲","标准","硬核"};
            for(int i=0;i<3;i++)
            {
                int choice=i;
                difficulties.Add(MakeButton(setup.transform,names[i],new Vector2((i-1)*245,-10),new Vector2(225,40),
                    ()=>{manager?.SelectDifficulty(choice);RefreshSetup();}));
            }
            difficultyLabel=Label(setup.transform,"Difficulty details","",new Vector2(0,-45),new Vector2(760,28),18);
            coreMode=MakeButton(setup.transform,"核心：固定位置",new Vector2(-195,-80),new Vector2(350,40),()=>scanner?.ToggleCoreMode());
            coreModeLabel=coreMode.GetComponentInChildren<Text>();
            MakeButton(setup.transform,"固定在脚下",new Vector2(195,-80),new Vector2(350,40),()=>{if(manager.State==GameState.SCAN)scanner?.PlaceCoreAtPlayer();});
            calibrate=MakeButton(setup.transform,"标定出怪口",new Vector2(0,-125),new Vector2(400,38),()=>
            {
                WorldInputPointer.ConsumePagePress();
                if(scanner==null)return;
                if(scanner.CalibrationArmed)scanner.CancelCalibration();else scanner.BeginCalibration();
            });
            calibrateLabel=calibrate.GetComponentInChildren<Text>();
            MakeButton(setup.transform,"返回",new Vector2(-195,-173),new Vector2(350,42),RequestMenu);
            enterBuild=MakeButton(setup.transform,"进入建造",new Vector2(195,-173),new Vector2(350,42),()=>NewCampaignRequested?.Invoke());
            RoomSiegeStyle.PrimaryButton(enterBuild);
            Label(setup.transform,"Setup hint","扫描完成后自动准备战场 · 已扫描房间自动恢复 · 可按需调整入口",new Vector2(0,-216),new Vector2(790,30),16);
            RegisterContent(setup,GameState.SCAN);

            var build=Rect(main.transform,"Build controls",Vector2.zero,new Vector2(820,500));
            BuildControls=build.transform;
            battleStatus=Label(build.transform,"Tower status","",new Vector2(0,-150),new Vector2(790,36),18);
            startWave=MakeButton(build.transform,"开始第 1 波",new Vector2(185,-190),new Vector2(330,50),()=>StartWaveRequested?.Invoke());
            startWaveLabel=startWave.GetComponentInChildren<Text>();
            RoomSiegeStyle.PrimaryButton(startWave);
            MakeButton(build.transform,"暂停菜单",new Vector2(-285,-190),new Vector2(170,50),OpenPause);
            RegisterContent(build,GameState.BUILD);
            var battle=Rect(main.transform,"Battle controls",Vector2.zero,new Vector2(820,500));
            MakeButton(battle.transform,"建造",new Vector2(-280,-208),new Vector2(180,46),()=>SetConstructionOpen(true));
            MakeButton(battle.transform,"操作说明",new Vector2(0,-208),new Vector2(180,46),ShowHelp);
            MakeButton(battle.transform,"暂停菜单",new Vector2(280,-208),new Vector2(180,46),OpenPause);
            RegisterContent(battle,GameState.WAVE);

            var result=Rect(main.transform,"Result actions page",Vector2.zero,new Vector2(820,380));
            resultHint=Label(result.transform,"Result hint","本局已结束\n可重新布防，或返回主菜单选择难度",new Vector2(0,58),new Vector2(760,132),22);
            MakeButton(result.transform,"再来一局",new Vector2(0,-46),new Vector2(420,58),RequestRestart);
            MakeButton(result.transform,"返回主菜单",new Vector2(0,-130),new Vector2(420,48),RequestMenu);
            RegisterContent(result,GameState.RESULT);
            BuildPausePage(main.transform);
            BuildHelpPage(main.transform);
            BuildChoicePage(main.transform);
            tacticalButton=MakeButton(main.transform,"应急冰冻",new Vector2(0,237),new Vector2(500,40),()=>TacticalRequested?.Invoke());
            tacticalLabel=tacticalButton.GetComponentInChildren<Text>();
            RegisterContent(tacticalButton.gameObject,GameState.BUILD,GameState.WAVE);
            BuildSidebars(left.transform,right.transform,top.transform);
            BuildTutorial(top.transform);
            placementGroups=new CanvasGroup[Panels.Length];
            for(int i=0;i<Panels.Length;i++)placementGroups[i]=Panels[i].gameObject.AddComponent<CanvasGroup>();
            // A compact strip above the battlefield remains reachable without covering floor cells.
            placementHud=Panel("Placement HUD",new Vector2(0,.56f),new Vector2(740,108),camera,false);
            Image(placementHud.transform,"Placement glass",Vector2.zero,new Vector2(740,108),RoomSiegeStyle.Glass);
            placementHint=Label(placementHud.transform,"Placement hint","",new Vector2(-94,0),new Vector2(530,94),19);
            MakeButton(placementHud.transform,"取消放置",new Vector2(270,0),new Vector2(164,48),()=>
            {WorldInputPointer.ConsumePagePress();PlacementCancelRequested?.Invoke();});
            placementHud.gameObject.SetActive(false);
            calibrationHud=Panel("Calibration HUD",new Vector2(0,.56f),new Vector2(740,120),camera,false);
            Image(calibrationHud.transform,"Calibration glass",Vector2.zero,new Vector2(740,120),RoomSiegeStyle.Glass);
            calibrationHint=Label(calibrationHud.transform,"Calibration hint","射线指向地板 · 扳机 / 捏合确认",new Vector2(-94,0),new Vector2(530,108),19);
            MakeButton(calibrationHud.transform,"取消标定",new Vector2(270,0),new Vector2(164,48),()=>
            {WorldInputPointer.ConsumePagePress();scanner?.CancelCalibration();});
            calibrationHud.gameObject.SetActive(false);
            Follower.Initialize(camera,group,Panels);
            AttachManager();AttachScanner();RefreshPhaseVisibility();
            Debug.Log("[RoomSiege.UI] Phase pages ready: Menu / Room setup / Build / Battle / Result.");
            Debug.Log($"[RoomSiege.UI] Clarity: glyphDensity={WorldUiSampling.GlyphDensity}, quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}, pipeline={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name}, MSAA={QualitySettings.antiAliasing}, cameraMSAA={camera.allowMSAA}");
        }

        private void BuildSidebars(Transform left,Transform right,Transform top)
        {
            var buildTitle=Label(left,"Build title","武器库",new Vector2(0,192),new Vector2(252,40),29);buildTitle.alignment=TextAnchor.MiddleLeft;
            var buildSubtitle=Label(left,"Build subtitle","ARSENAL / 04",new Vector2(0,157),new Vector2(252,28),18);buildSubtitle.color=RoomSiegeStyle.Muted;buildSubtitle.alignment=TextAnchor.MiddleLeft;
            RoomSiegeStyle.AddRule(left,material,new Vector2(0,137),new Vector2(252,1),RoomSiegeStyle.Rule);
            string[] slots={"MG 机枪","Frost 冰墙","Laser 激光","Trap 陷阱"};
            for(int i=0;i<slots.Length;i++)
            {
                float y=105-i*84;
                Image(left,"Tower slot "+i,new Vector2(0,y),new Vector2(264,70),RoomSiegeStyle.Card);
                Label(left,"Tower label "+i,slots[i]+"\n加载配置中",new Vector2(0,y),new Vector2(250,66),22);
            }
            var intel=Label(right,"Wave title","战场情报",new Vector2(0,162),new Vector2(252,40),29);intel.alignment=TextAnchor.MiddleLeft;
            RoomSiegeStyle.AddRule(right,material,new Vector2(0,136),new Vector2(252,1),RoomSiegeStyle.Rule);
            for(int i=0;i<3;i++)
            {
                float y=89-i*104;
                Image(right,"Info row "+i,new Vector2(0,y),new Vector2(264,88),RoomSiegeStyle.Card);
                Label(right,"Info label "+i,"",new Vector2(0,y),new Vector2(250,82),25);
            }
            Label(right,"Info hint","",new Vector2(0,-197),new Vector2(270,40),18);
            hudGlass=Image(top,"HUD glass",Vector2.zero,new Vector2(770,88),RoomSiegeStyle.Glass).gameObject;
            RoomSiegeStyle.AddRule(hudGlass.transform,material,new Vector2(30,0),new Vector2(1,42),RoomSiegeStyle.Rule);
            RoomSiegeStyle.AddRule(hudGlass.transform,material,new Vector2(195,0),new Vector2(1,42),RoomSiegeStyle.Rule);
            var ring=Rect(top,"Progress ring",new Vector2(-333,0),new Vector2(60,60)).AddComponent<ProgressRingGraphic>();
            ring.material=material;ring.color=RoomSiegeStyle.Friendly;ring.raycastTarget=false;
            Label(top,"Progress label","",new Vector2(-130,0),new Vector2(318,78),22);
            hudLife=Label(top,"HUD life","生命  —",new Vector2(111,0),new Vector2(140,70),22).gameObject;
            var gold=Label(top,"HUD gold","金币  —",new Vector2(285,0),new Vector2(174,70),22);gold.color=RoomSiegeStyle.Gold;hudGold=gold.gameObject;
            foreach(Transform child in left)RegisterContent(child.gameObject,GameState.BUILD);
            foreach(Transform child in right)RegisterContent(child.gameObject,GameState.BUILD,GameState.WAVE);
            foreach(Transform child in top)RegisterContent(child.gameObject,GameState.BUILD,GameState.WAVE);
        }

        private void BuildPausePage(Transform main)
        {
            pausePage=Rect(main,"Pause menu page",Vector2.zero,new Vector2(848,488));
            Image(pausePage.transform,"Pause background",Vector2.zero,new Vector2(848,488),RoomSiegeStyle.Overlay,true);
            GameUiChrome.Frame(pausePage.transform,material);
            Label(pausePage.transform,"Pause title","已暂停",new Vector2(-226,207),new Vector2(310,38),28).alignment=TextAnchor.MiddleLeft;
            RoomSiegeStyle.PrimaryButton(MakeButton(pausePage.transform,"继续游戏",new Vector2(0,100),new Vector2(470,52),()=>Navigation.Close(pausePage)));
            MakeButton(pausePage.transform,"建造管理",new Vector2(-185,24),new Vector2(340,46),()=>{Navigation.Close(pausePage);SetConstructionOpen(true);});
            MakeButton(pausePage.transform,"声音与布局",new Vector2(185,24),new Vector2(340,46),()=>AudioRequested?.Invoke());
            MakeButton(pausePage.transform,"图鉴",new Vector2(-185,-52),new Vector2(340,46),()=>LibraryRequested?.Invoke());
            MakeButton(pausePage.transform,"返回主菜单",new Vector2(185,-52),new Vector2(340,46),RequestMenu);
            MakeButton(pausePage.transform,"新手教学 / 操作说明",new Vector2(0,-128),new Vector2(470,46),ShowHelp);
            Label(pausePage.transform,"Pause hint","返回主菜单将结束当前战役",new Vector2(0,-195),new Vector2(730,34),18);
            Navigation.Register(pausePage);
        }
        private void BuildHelpPage(Transform main)
        {
            helpPage=Rect(main,"Help page",Vector2.zero,new Vector2(848,488));
            Image(helpPage.transform,"Help background",Vector2.zero,new Vector2(848,488),RoomSiegeStyle.Overlay,true);
            GameUiChrome.Frame(helpPage.transform,material);
            helpTitle=Label(helpPage.transform,"Help title","",new Vector2(-76,207),new Vector2(610,38),28);helpTitle.alignment=TextAnchor.MiddleLeft;
            MakeButton(helpPage.transform,"关闭",new Vector2(344,207),new Vector2(96,40),()=>Navigation.Close(helpPage));
            helpIndex=Label(helpPage.transform,"Help index","",new Vector2(0,139),new Vector2(740,30),18);
            helpBody=Label(helpPage.transform,"Help body","",new Vector2(0,6),new Vector2(744,235),22);
            helpBody.alignment=TextAnchor.MiddleLeft;
            helpPrevious=MakeButton(helpPage.transform,"上一页",new Vector2(-185,-153),new Vector2(330,46),()=>ChangeHelp(-1));
            var next=MakeButton(helpPage.transform,"下一页",new Vector2(185,-153),new Vector2(330,46),()=>
            {if(helpStep==RoomSiegeGuide.Titles.Length-1)Navigation.Close(helpPage);else ChangeHelp(1);});
            helpNextLabel=next.GetComponentInChildren<Text>();
            Label(helpPage.transform,"Help footer","说明打开期间游戏暂停 · 关闭后回到刚才的界面",new Vector2(0,-214),new Vector2(760,30),18);
            Navigation.Register(helpPage);
        }
        public void ShowHelp()
        {
            helpStep=0;ChangeHelp(0);Navigation.Show(helpPage);
        }
        private void BuildTutorial(Transform top)
        {
            tutorialBanner=Rect(top,"First run tutorial",Vector2.zero,new Vector2(790,206));
            Image(tutorialBanner.transform,"Tutorial glass",Vector2.zero,new Vector2(790,206),RoomSiegeStyle.Overlay,true);
            tutorialTitle=Label(tutorialBanner.transform,"Tutorial step","",new Vector2(-85,65),new Vector2(570,38),26);
            tutorialTitle.color=RoomSiegeStyle.Friendly;
            tutorialBody=Label(tutorialBanner.transform,"Tutorial instruction","",new Vector2(0,4),new Vector2(750,84),22);
            var dismiss=MakeButton(tutorialBanner.transform,"跳过教学",new Vector2(301,65),new Vector2(162,38),()=>
            {WorldInputPointer.ConsumePagePress();Tutorial.Skip();RefreshTutorial();});
            tutorialDismissLabel=dismiss.GetComponentInChildren<Text>();
            tutorialDefer=MakeButton(tutorialBanner.transform,"稍后升级",new Vector2(260,-68),new Vector2(228,40),()=>
            {WorldInputPointer.ConsumePagePress();Tutorial.DeferUpgrade();RefreshTutorial();});
            Label(tutorialBanner.transform,"Tutorial footer","提示不暂停 · 可在主菜单重新学习",new Vector2(-124,-68),new Vector2(505,32),18);
            Tutorial.Changed+=TutorialChanged;
            Tutorial.Concluded+=TutorialConcluded;
            tutorialProgressRing=top.Find("Progress ring").gameObject;
            tutorialProgressLabel=top.Find("Progress label").gameObject;
            tutorialBanner.SetActive(false);
        }
        public void StartTutorial()
        {
            // Relearning is a menu action; it never discards an active battle.
            if(manager==null||(manager.State!=GameState.MENU&&manager.State!=GameState.SCAN))return;
            WorldInputPointer.ConsumePagePress();
            Tutorial.Begin();Navigation?.CloseCurrent();manager.SetState(GameState.SCAN);RefreshTutorial();
        }
        private void TutorialChanged()
        {
            tutorialCompletedSeconds=0;
            Debug.Log("[RoomSiege.Tutorial] Step -> "+Tutorial.Step);
            RefreshTutorial();
        }
        private void TutorialConcluded(bool skipped)
        {
            PlayerPrefs.SetInt(FirstRunTutorial.PreferenceKey,skipped?2:1);PlayerPrefs.Save();
            Debug.Log("[RoomSiege.Tutorial] "+(skipped?"Skipped":"Completed")+"; preference saved.");
        }
        public void RefreshTutorial()
        {
            if(tutorialBanner==null)return;
            bool visible=Tutorial.Visible&&manager!=null&&
                (manager.State==GameState.SCAN||manager.State==GameState.BUILD||manager.State==GameState.WAVE)&&
                (Navigation==null||!Navigation.HasOpenPage);
            tutorialBanner.SetActive(visible);
            if(visible)
            {
                tutorialTitle.text=Tutorial.Title;tutorialBody.text=Tutorial.Body;
                tutorialDismissLabel.text=Tutorial.Active?"跳过教学":"收起";
                tutorialDefer.gameObject.SetActive(Tutorial.Step==TutorialStep.Upgrade);
            }
            bool showProgress=!visible&&(Navigation==null||!Navigation.HasOpenPage)&&
                (DisplayedState==GameState.BUILD||DisplayedState==GameState.WAVE);
            if(tutorialProgressRing!=null)tutorialProgressRing.SetActive(showProgress);
            if(tutorialProgressLabel!=null)tutorialProgressLabel.SetActive(showProgress);
            if(hudGlass!=null)hudGlass.SetActive(showProgress);
            if(hudLife!=null)hudLife.SetActive(showProgress);
            if(hudGold!=null)hudGold.SetActive(showProgress);
        }
        private void BuildChoicePage(Transform main)
        {
            choicePage=Rect(main,"Reinforcement page",Vector2.zero,new Vector2(848,488));
            Image(choicePage.transform,"Reinforcement background",Vector2.zero,new Vector2(848,488),RoomSiegeStyle.Overlay,true);
            GameUiChrome.Frame(choicePage.transform,material);
            choiceTitle=Label(choicePage.transform,"Reinforcement title","选择本局强化",new Vector2(0,207),new Vector2(776,38),28);
            Label(choicePage.transform,"Reinforcement hint","按在场武器与持续道具生成 · 同类生效 · 本局有效",new Vector2(0,130),new Vector2(790,35),18);
            for(int i=0;i<3;i++)
            {
                int choice=i;
                var button=MakeButton(choicePage.transform,"强化 "+i,new Vector2((i-1)*265,-7),new Vector2(245,248),()=>choosePerk?.Invoke(choice));
                var text=button.GetComponentInChildren<Text>();RoomSiegeStyle.StyleText(text,18);text.fontSize=18;text.lineSpacing=1;choiceLabels.Add(text);
            }
            Label(choicePage.transform,"Reinforcement footer","本波已清场 · 选一项后准备15秒 · 不消耗道具、不改变武器等级",new Vector2(0,-183),new Vector2(790,50),18);
            Navigation.Register(choicePage);
        }
        public void ShowReinforcements(int completedWave,string[] names,string[] descriptions,Action<int> select)
        {
            choosePerk=select;choiceTitle.text=$"第 {completedWave} 波已守住 · 选择本局强化";
            for(int i=0;i<3;i++)choiceLabels[i].text=names[i].Replace(" · ","\n")+"\n"+descriptions[i];
            Navigation.Show(choicePage);
        }
        public void CloseReinforcements(){choosePerk=null;Navigation.Close(choicePage);}
        public void SetTacticalAction(string value,bool available)
        {if(tacticalLabel!=null)tacticalLabel.text=value;if(tacticalButton!=null)tacticalButton.interactable=available;}
        private void ChangeHelp(int delta)
        {
            helpStep=Mathf.Clamp(helpStep+delta,0,RoomSiegeGuide.Titles.Length-1);
            helpTitle.text=RoomSiegeGuide.Titles[helpStep];helpBody.text=RoomSiegeGuide.Bodies[helpStep];
            helpIndex.text=$"新手教学 · {helpStep+1} / {RoomSiegeGuide.Titles.Length}";
            helpPrevious.interactable=helpStep>0;
            helpNextLabel.text=helpStep==RoomSiegeGuide.Titles.Length-1?"我知道了 · 返回游戏":"下一页";
        }
        public void SetConstructionOpen(bool value)
        {
            WorldInputPointer.ConsumePagePress();
            ConstructionOpen=value;RefreshPhaseVisibility();
        }
        public void SetPlacementMode(bool active,string hint="")
        {
            active=active&&(DisplayedState==GameState.BUILD||DisplayedState==GameState.WAVE)&&(Navigation==null||!Navigation.HasOpenPage);
            if(PlacementMode&&!active)WorldInputPointer.ConsumePagePress();
            PlacementMode=active;
            RefreshPanelInteraction();
            if(placementHud!=null)placementHud.gameObject.SetActive(active);
            if(placementHint!=null)placementHint.text=hint;
        }
        private void RefreshPanelInteraction()
        {
            bool hidden=PlacementMode||CalibrationMode;
            if(placementGroups!=null)foreach(var group in placementGroups)
            {
                if(group==null)continue;
                group.alpha=hidden?0:1;group.interactable=!hidden;group.blocksRaycasts=!hidden;
            }
        }
        public void SetWaveAction(string label,bool available)
        {if(startWaveLabel!=null)startWaveLabel.text=label;if(startWave!=null)startWave.interactable=available;}
        public void SetBattleStatus(string value){if(battleStatus!=null)battleStatus.text=value;}
        public void SetResultSummary(string value){if(resultHint!=null)resultHint.text=value;}
        public void RequestRestart(){WorldInputPointer.ConsumePagePress();Navigation?.CloseCurrent();RestartRequested?.Invoke();}
        public void RequestMenu(){WorldInputPointer.ConsumePagePress();Navigation?.CloseCurrent();MenuRequested?.Invoke();}
        private void OpenSetup(ScanMode mode)
        {
            WorldInputPointer.ConsumePagePress();
            if(manager==null)return;
            if(PlayerPrefs.GetInt(FirstRunTutorial.PreferenceKey,0)==0)Tutorial.Begin();
            manager.SetState(GameState.SCAN);RefreshTutorial();
            if(mode==ScanMode.RealRoom)scanner?.BeginGameRoomScan();else scanner?.UseSandbox();
        }
        private void OpenPause(){Navigation.Show(pausePage);}

        public void RegisterContent(GameObject content,params GameState[] states)
        {
            if(content==null)return;
            contentStates[content]=states;
            content.SetActive((Navigation==null||!Navigation.HasOpenPage)&&Contains(states,DisplayedState));
        }
        private static bool Contains(GameState[] states,GameState state)
        {foreach(var allowed in states)if(allowed==state)return true;return false;}
        public void RefreshPhaseVisibility()
        {
            RefreshWorldPresentation();
            if(Navigation!=null&&Navigation.HasOpenPage)return;
            foreach(var content in contentStates)
                if(content.Key!=null)content.Key.SetActive(Contains(content.Value,DisplayedState));
            if(title!=null)
            {
                bool menu=DisplayedState==GameState.MENU;
                title.text=DisplayedState==GameState.SCAN?"房间部署":DisplayedState==GameState.RESULT?"战役结束":"RoomSiege";
                title.rectTransform.anchoredPosition=new Vector2(menu?-207:0,194);
                title.rectTransform.sizeDelta=new Vector2(menu?310:780,58);
                title.alignment=menu?TextAnchor.MiddleLeft:TextAnchor.MiddleCenter;title.fontSize=menu?46:32;
            }
            RefreshTutorial();
        }
        private void StateChanged(GameState value)
        {
            SetPlacementMode(false);
            WorldInputPointer.ConsumePagePress();Navigation?.CloseCurrent();
            ConstructionOpen=false;Follower.SetPaused(false);
            if(value==GameState.MENU||value==GameState.RESULT)Tutorial.Cancel();
            else if(value==GameState.BUILD&&Tutorial.Active&&manager.CurrentWave==0)Tutorial.Begin();
            RefreshPhaseVisibility();RefreshSetup();
            Debug.Log("[RoomSiege.UI] Phase -> "+value);
        }
        private void AttachManager()
        {
            if(manager!=null||GameManager.Instance==null)return;
            manager=GameManager.Instance;manager.StateChanged+=StateChanged;RefreshPhaseVisibility();
        }
        private void Update()
        {
            AttachManager();AttachScanner();RefreshSetup();
            UpdateCalibrationInput();
            RefreshWorldPresentation();
            if(PlacementMode&&((DisplayedState!=GameState.BUILD&&DisplayedState!=GameState.WAVE)||Navigation.HasOpenPage))SetPlacementMode(false);
            if(Tutorial.Step==TutorialStep.Completed&&tutorialBanner!=null&&tutorialBanner.activeInHierarchy&&!Follower.IsPaused)
            {tutorialCompletedSeconds+=Time.unscaledDeltaTime;if(tutorialCompletedSeconds>=10f)Tutorial.Cancel();}
            RefreshTutorial();
            if(library!=null)library.interactable=LibraryRequested!=null;
            if(audio!=null)audio.interactable=AudioRequested!=null;
            if(Time.unscaledTime<nextInputProbe)return;
            nextInputProbe=Time.unscaledTime+1;
            foreach(var button in FindObjectsOfType<Button>(true))
                if(button.GetComponent<WorldUiEventProbe>()==null)button.gameObject.AddComponent<WorldUiEventProbe>();
        }
        private void RefreshSetup()
        {
            if(enterBuild==null)return;
            bool gridReady=scanner!=null&&!scanner.IsScanning&&scanner.CurrentGrid!=null&&scanner.CurrentGrid.SpawnPoints.Count>0;
            bool routeReady=gridReady&&(RoomReady==null||RoomReady());
            enterBuild.interactable=CampaignReady&&routeReady;
            string roomFailure=RoomFailure?.Invoke()??"";
            if(lastRoomFailure!=roomFailure){lastRoomFailure=roomFailure;RefreshScan();}
            if(scanReal!=null){scanReal.interactable=scanner!=null&&scanner.CanRequestCapture;scanRealLabel.text=scanner!=null&&scanner.CaptureRequested?"扫描中 · 请在头显完成":"重新扫描真实房间";}
            if(calibrate!=null)
            {
                calibrate.interactable=scanner!=null&&!scanner.IsScanning&&!scanner.BattleLayoutLocked&&scanner.CurrentGrid!=null;
                calibrateLabel.text=scanner!=null&&scanner.CalibrationArmed?"取消标定 · 射线指向过道地面":"调整出怪口（可选）";
                RoomSiegeStyle.SetButtonSurface(calibrate,scanner!=null&&scanner.EntranceCalibrationPending?RoomSiegeStyle.Primary:RoomSiegeStyle.Button);
            }
            if(coreMode!=null){coreMode.interactable=scanner!=null&&!scanner.IsScanning;coreModeLabel.text=scanner!=null&&scanner.CoreFollowsPlayer?"核心：跟随玩家 · 点击固定":"核心：固定位置 · 点击跟随";}
            if(difficultyLabel==null||manager==null)return;
            string[] names={"休闲","标准","硬核"};int difficulty=Mathf.Clamp(manager.Difficulty,0,2);
            difficultyLabel.text="当前："+names[difficulty]+(StartingGold!=null?" · 初始 "+StartingGold(difficulty)+"G":"")+
                (!CampaignReady?" · 模块准备中":scanner!=null&&scanner.EntranceCalibrationPending?" · 请先标定出怪口":gridReady&&!routeReady?" · 核心不可达，请换位置或使用沙盘":"");
            for(int i=0;i<difficulties.Count;i++){
                difficulties[i].interactable=true;
                RoomSiegeStyle.NavigationButton(difficulties[i],i==difficulty);
                difficulties[i].GetComponentInChildren<Text>().color=i==difficulty?RoomSiegeStyle.Friendly:RoomSiegeStyle.Muted;
            }
        }
        private void UpdateCalibrationInput()
        {
            bool active=scanner!=null&&scanner.CalibrationArmed;
            if(active&&(manager==null||manager.State!=GameState.SCAN||Navigation.HasOpenPage))
            {scanner.CancelCalibration();active=false;}
            if(CalibrationMode!=active)
            {
                CalibrationMode=active;WorldInputPointer.ConsumePagePress();nextCalibrationPreview=0;
                RefreshPanelInteraction();
                if(calibrationHud!=null)calibrationHud.gameObject.SetActive(active);
                if(!active)roomVisuals?.SetCalibrationTarget(null,false);
            }
            if(!active)return;
            WorldInputPointer previewPointer=null;
            foreach(var pointer in FindObjectsOfType<WorldInputPointer>())
            {
                if(!pointer.Tracked||pointer.Interactor==null)continue;
                if(pointer.Interactor.TryGetCurrentUIRaycastResult(out var hit))
                {
                    if(pointer.PressedThisFrame)Debug.Log("[RoomSiege.Scan] Calibration press over UI: "+hit.gameObject.name);
                    continue;
                }
                if(previewPointer==null||pointer.LastPressFrame>previewPointer.LastPressFrame)previewPointer=pointer;
                if(!pointer.PressedThisFrame)continue;
                scanner.CalibrateEntrance(new Ray(pointer.transform.position,pointer.transform.forward));
                WorldInputPointer.ConsumePagePress();
                nextCalibrationPreview=0;
                if(!scanner.CalibrationArmed){UpdateCalibrationInput();return;}
                previewPointer=pointer;break;
            }
            if(Time.unscaledTime<nextCalibrationPreview)return;
            nextCalibrationPreview=Time.unscaledTime+.15f;
            if(previewPointer==null)
            {roomVisuals?.SetCalibrationTarget(null,false);calibrationHint.text="请将手柄 / 手势射线移向地板\n避开取消按钮";return;}
            var ray=new Ray(previewPointer.transform.position,previewPointer.transform.forward);
            bool valid=GridBuilder.TryCalibrateEntrance(scanner.CurrentRoom,scanner.CurrentGrid,ray,scanner.LayoutFailure,
                out _,out var target,out var failure);
            roomVisuals?.SetCalibrationTarget(target,valid);
            calibrationHint.text=valid?"青蓝圆环：可标定入口\n扣扳机 / 捏合确认":failure;
        }
        private void AttachScanner()
        {
            if(scanner!=null||ScanController.Instance==null)return;
            scanner=ScanController.Instance;roomVisuals=scanner.GetComponent<ScanVisualization>();scanner.ViewChanged+=RefreshScan;RefreshScan();
        }
        private void RefreshWorldPresentation()
        {
            if(roomVisuals!=null)roomVisuals.SetPresentationVisible(manager!=null&&manager.State!=GameState.MENU&&
                manager.State!=GameState.RESULT&&(Navigation==null||!Navigation.HasOpenPage));
        }
        private void RefreshScan()
        {
            if(scanner==null||scanStatus==null)return;
            var grid=scanner.CurrentGrid;
            scanStatus.text=grid==null?scanner.Status:
                $"{(grid.Mode==ScanMode.Sandbox?"模拟沙盘":"真实房间")} · {grid.PlaceableCells.Count} 个可放格 · {grid.SpawnPoints.Count} 个出怪口\n{scanner.Status}";
            if(grid!=null&&!scanner.IsScanning&&grid.SpawnPoints.Count>0&&RoomFailure!=null)
            {
                string failure=RoomFailure();
                if(!string.IsNullOrEmpty(failure)&&!scanner.CalibrationArmed)
                    scanStatus.text=$"{(grid.Mode==ScanMode.RealRoom?"真实房间":"模拟沙盘")} · {grid.PlaceableCells.Count} 个可放格\n{failure}";
            }
        }
        private Canvas Panel(string name,Vector2 metres,Vector2 pixels,Camera camera,bool background=true)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(TrackedDeviceGraphicRaycaster));
            go.layer=5;go.transform.SetParent(transform,false);go.transform.localPosition=new Vector3(metres.x,metres.y,0);go.transform.localScale=Vector3.one*.001f;
            go.GetComponent<RectTransform>().sizeDelta=pixels;
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
            WorldUiSampling.Configure(canvas);
            if(background)Image(go.transform,name+" background",Vector2.zero,pixels,RoomSiegeStyle.Glass);
            return canvas;
        }
        private Image Image(Transform parent,string name,Vector2 position,Vector2 size,Color color,bool raycast=false)
        {
            var graphic=Rect(parent,name,position,size).AddComponent<RoundedGlassImage>();
            graphic.material=material;graphic.color=color;graphic.raycastTarget=raycast;return graphic;
        }
        private void MenuKeyArt(Transform parent)
        {
            var texture=Resources.Load<Texture2D>("UI/RoomSiegeDuelKeyArt");if(texture==null)return;
            var image=Rect(parent,"RoomSiege duel key art",Vector2.zero,new Vector2(860,500)).AddComponent<RawImage>();
            image.material=material;image.texture=texture;image.raycastTarget=false;
            float imageAspect=(float)texture.width/texture.height,targetAspect=860f/500f;
            if(imageAspect>targetAspect){float width=targetAspect/imageAspect;image.uvRect=new Rect((1-width)*.5f,0,width,1);}
            else{float height=imageAspect/targetAspect;image.uvRect=new Rect(0,(1-height)*.5f,1,height);}
            RegisterContent(image.gameObject,GameState.MENU);
            var scrim=Rect(parent,"Menu readability scrim",Vector2.zero,new Vector2(860,500)).AddComponent<MenuScrimGraphic>();scrim.material=material;scrim.raycastTarget=false;RegisterContent(scrim.gameObject,GameState.MENU);
        }
        private Text Label(Transform parent,string name,string value,Vector2 position,Vector2 size,int fontSize)
        {
            var text=Rect(parent,name,position,size).AddComponent<Text>();text.font=font;text.text=value;RoomSiegeStyle.StyleText(text,fontSize);
            text.material=material;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;return text;
        }
        private Button MakeButton(Transform parent,string label,Vector2 position,Vector2 size,UnityAction action)
        {
            var image=Image(parent,label,position,size,RoomSiegeStyle.Button,true);
            var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(action);RoomSiegeStyle.StyleButton(button);
            Label(image.transform,"Label",label,Vector2.zero,size-new Vector2(24,8),23);return button;
        }
        private static GameObject Rect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);
            var rect=go.GetComponent<RectTransform>();rect.anchoredPosition=position;rect.sizeDelta=size;return go;
        }
        private void OnDestroy()
        {
            if(roomVisuals!=null)roomVisuals.SetPresentationVisible(true);
            if(scanner!=null)scanner.ViewChanged-=RefreshScan;
            if(manager!=null)manager.StateChanged-=StateChanged;
            if(Navigation!=null)Navigation.ContentRestored-=RefreshPhaseVisibility;
            Tutorial.Changed-=TutorialChanged;Tutorial.Concluded-=TutorialConcluded;
            foreach(var hidden in hiddenPanels)if(hidden!=null)hidden.SetActive(true);
            if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
            if(Instance==this)Instance=null;
        }
    }
}
