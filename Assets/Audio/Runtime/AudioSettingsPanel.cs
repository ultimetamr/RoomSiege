using RoomSiege.UI;
using RoomSiege.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Audio
{
    // Shares exclusive function-page navigation and the world canvas's follow/fade behavior.
    public sealed class AudioSettingsPanel : MonoBehaviour
    {
        private AudioDirector director;
        private WorldPanelNavigation navigation;
        private WorldPanelHost panels;
        private Text musicLabel;
        private Image musicTrack,musicThumb;
        private Text coreLabel;
        private Text auditionLabel;
        private Button auditionButton;
        private GameObject settings;
        private GameObject toggle;
        private GameObject oldHint;
        private bool hintWasActive;

        public void Initialize(AudioDirector owner, WorldPanelHost panels)
        {
            director = owner;
            this.panels = panels;
            navigation = panels.Navigation;
            var main = panels.Panels[0].transform;
            var sample = main.Find("Title").GetComponent<Text>();
            panels.AudioRequested += Open;
            settings = Rect(main, "Audio settings", Vector2.zero, new Vector2(848, 488));
            var background = settings.AddComponent<RoundedGlassImage>();
            background.material = sample.material;
            background.color = RoomSiegeStyle.Overlay;
            background.raycastTarget = true;
            GameUiChrome.Frame(settings.transform,sample.material);
            var title = Rect(settings.transform, "Settings title", new Vector2(-226,207), new Vector2(310,38)).AddComponent<Text>();
            title.font = sample.font; title.material = sample.material; title.text = "声音与布局";
            RoomSiegeStyle.StyleText(title, 27); title.alignment = TextAnchor.MiddleLeft; title.raycastTarget = false;
            RoomSiegeStyle.AddRule(settings.transform,sample.material,new Vector2(0,164),new Vector2(730,1),RoomSiegeStyle.Rule);
            var close = CreateButton(settings.transform, "关闭", new Vector2(344,207), new Vector2(96,40), sample);
            close.onClick.AddListener(() => navigation.Close(settings));
            SettingLabel("背景音乐",113,sample);SettingLabel("面板位置",47,sample);SettingLabel("部署格叠加",-19,sample);SettingLabel("核心位置",-85,sample);
            var music = CreateButton(settings.transform, "", new Vector2(245,113), new Vector2(210, 42), sample);
            musicLabel = music.GetComponentInChildren<Text>();
            musicLabel.rectTransform.anchoredPosition=new Vector2(-28,0);musicLabel.rectTransform.sizeDelta=new Vector2(124,36);
            musicTrack=SwitchImage(music.transform,"Music switch",new Vector2(65,0),new Vector2(54,24),sample);
            musicThumb=SwitchImage(music.transform,"Music switch thumb",new Vector2(75,0),new Vector2(18,18),sample);musicThumb.color=RoomSiegeStyle.Text;
            music.onClick.AddListener(() => director.SetMusicEnabled(!director.MusicEnabled));
            var reset=CreateButton(settings.transform,"回正",new Vector2(245,47),new Vector2(210,42),sample);
            reset.onClick.AddListener(panels.Follower.ResetToDefault);
            var overlay=CreateButton(settings.transform,"切换显示",new Vector2(245,-19),new Vector2(210,42),sample);
            overlay.onClick.AddListener(()=>RoomSiege.Scan.ScanController.Instance?.ToggleOverlay());
            var core=CreateButton(settings.transform,"核心：固定位置",new Vector2(245,-85),new Vector2(210,42),sample);
            coreLabel=core.GetComponentInChildren<Text>();
            core.onClick.AddListener(()=>{RoomSiege.Scan.ScanController.Instance?.ToggleCoreMode();Refresh();});
            auditionButton=CreateButton(settings.transform,"试听：点击 / 四塔 / 号角 / 音乐",new Vector2(0,-151),new Vector2(700,38),sample);
            RoomSiegeStyle.PrimaryButton(auditionButton);
            auditionLabel=auditionButton.GetComponentInChildren<Text>();
            auditionButton.onClick.AddListener(director.AuditionSounds);
            var coreHint=Rect(settings.transform,"Core hint",new Vector2(0,-225),new Vector2(776,26)).AddComponent<Text>();
            coreHint.font=sample.font;coreHint.material=sample.material;RoomSiegeStyle.StyleText(coreHint,17);
            coreHint.fontSize=16;coreHint.alignment=TextAnchor.MiddleCenter;coreHint.raycastTarget=false;
            coreHint.color=RoomSiegeStyle.Muted;
            coreHint.text="跟随保留已有武器 · 圈内禁建 · 无效地板或断路时保留最后有效位置";
            navigation.Register(settings);
            Refresh();
        }

        public void Refresh()
        {
            if(musicLabel!=null)musicLabel.text=director.MusicEnabled?"开启":"关闭";
            if(musicTrack!=null)musicTrack.color=director.MusicEnabled?RoomSiegeStyle.Selected:RoomSiegeStyle.Rule;
            if(musicThumb!=null)musicThumb.rectTransform.anchoredPosition=new Vector2(director.MusicEnabled?75:55,0);
            if(auditionLabel!=null)auditionLabel.text=director.AuditionStatus??"试听：点击 / 四塔 / 号角 / 音乐";
            if(auditionButton!=null)auditionButton.interactable=director.AuditionStatus==null;
            if(coreLabel!=null)coreLabel.text=RoomSiege.Scan.ScanController.Instance!=null&&RoomSiege.Scan.ScanController.Instance.CoreFollowsPlayer?"跟随玩家":"固定位置";
        }
        private void SettingLabel(string value,float y,Text sample)
        {
            GameUiChrome.Surface(settings.transform,value+" row",new Vector2(0,y),new Vector2(740,56),RoomSiegeStyle.Card,sample.material);
            var text=Rect(settings.transform,value,new Vector2(-160,y),new Vector2(380,42)).AddComponent<Text>();
            text.font=sample.font;text.material=sample.material;text.text=value;text.alignment=TextAnchor.MiddleLeft;text.raycastTarget=false;
            RoomSiegeStyle.StyleText(text,22);
            RoomSiegeStyle.AddRule(settings.transform,sample.material,new Vector2(0,y-32),new Vector2(700,1),RoomSiegeStyle.Rule);
        }
        private static Image SwitchImage(Transform parent,string name,Vector2 position,Vector2 size,Text sample)
        {
            var image=Rect(parent,name,position,size).AddComponent<RoundedGlassImage>();
            image.material=sample.material;image.raycastTarget=false;return image;
        }
        private void Open() { Refresh();navigation.Show(settings); }

        private void OnDestroy()
        {
            if (navigation != null) navigation.Close(settings);
            if (panels != null) panels.AudioRequested -= Open;
            if (oldHint != null) oldHint.SetActive(hintWasActive);
            if (settings != null) Destroy(settings);
            if (toggle != null) Destroy(toggle);
        }

        private static Button CreateButton(Transform parent, string label, Vector2 pos, Vector2 size, Text sample)
        {
            var go = Rect(parent, label, pos, size);
            var image = go.AddComponent<RoundedGlassImage>();
            image.material = sample.material; image.color = RoomSiegeStyle.Button; image.raycastTarget = true;
            var button = go.AddComponent<Button>(); button.targetGraphic = image;
            RoomSiegeStyle.StyleButton(button);
            var text = Rect(go.transform, "Label", Vector2.zero, size - Vector2.one * 4).AddComponent<Text>();
            text.font = sample.font; text.material = sample.material; RoomSiegeStyle.StyleText(text, 19);
            text.text = label; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            return button;
        }

        private static GameObject Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5; go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size;
            return go;
        }
    }
}
