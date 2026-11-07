using UnityEngine;
using RoomSiege.Visuals;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.Scan
{
    public sealed class ScanPanel : MonoBehaviour
    {
        private ScanController controller;
        private GameObject panel;
        private Text status;
        private Text summary;
        private Text overlayLabel;
        private Font font;
        public void Initialize(ScanController owner,Camera head)
        {
            controller=owner; font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panel=new GameObject("RoomSiege Scan Panel",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(TrackedDeviceGraphicRaycaster));
            panel.transform.SetParent(transform,false);
            var forward=Vector3.ProjectOnPlane(head.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<0.5f) forward=Vector3.forward;
            panel.transform.SetPositionAndRotation(head.transform.position+forward*1.8f,Quaternion.LookRotation(forward,Vector3.up));
            panel.transform.localScale=Vector3.one*0.0015f;
            panel.GetComponent<RectTransform>().sizeDelta=new Vector2(960f,350f);
            var canvas=panel.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=head;
            WorldUiSampling.Configure(canvas);
            var background=new GameObject("Panel background",typeof(RectTransform),typeof(RoundedGlassImage));
            background.transform.SetParent(panel.transform,false);
            background.GetComponent<RectTransform>().sizeDelta=new Vector2(960f,350f);
            background.GetComponent<Image>().color=RoomSiegeStyle.Glass;
            Text("Title","RoomSiege · 房间扫描 / Room Scan",new Vector2(0,135),new Vector2(920,55),34);
            summary=Text("Summary","",new Vector2(0,88),new Vector2(920,40),22);
            Button("真实扫描 [R]",new Vector2(-345,28),controller.ScanRealRoom);
            Button("模拟沙盘 [S]",new Vector2(-115,28),controller.UseSandbox);
            overlayLabel=Button("虚拟格：开 [G]",new Vector2(115,28),controller.ToggleOverlay);
            Button("标定出怪口 [C]",new Vector2(345,28),controller.BeginCalibration);
            status=Text("Status","",new Vector2(0,-44),new Vector2(915,90),22);
            Text("Legend","0.4m 部署格 · 核心圆环内可建造 · 请保留 Boss 2 格宽通路 · 出怪口 ≤ 2\nR 真实 / S 沙盘 / G 格叠加 / C 视线标定 · 仅扫描模块",new Vector2(0,-128),new Vector2(920,66),21);
            controller.ViewChanged+=Refresh;
            Refresh();
        }
        private void Refresh()
        {
            if(status==null) return;
            var grid=controller.CurrentGrid;
            summary.text=grid==null ? "真实扫描处理中…" :
                $"{(grid.Mode==ScanMode.RealRoom?"真实房间 / MR":"模拟沙盘 / Sandbox")}   |   可放格 {grid.PlaceableCells.Count}   |   出怪口 {grid.SpawnPoints.Count}";
            overlayLabel.text=controller.OverlayVisible?"虚拟格：开 [G]":"虚拟格：关 [G]";
            status.text=controller.Status;
        }
        private Text Text(string name,string value,Vector2 position,Vector2 size,int fontSize)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text)); go.transform.SetParent(panel.transform,false);
            var rect=go.GetComponent<RectTransform>(); rect.anchoredPosition=position; rect.sizeDelta=size;
            var text=go.GetComponent<Text>(); text.font=font; text.text=value; RoomSiegeStyle.StyleText(text,fontSize);
            text.alignment=TextAnchor.MiddleCenter; text.raycastTarget=false;
            return text;
        }
        private Text Button(string label,Vector2 position,UnityAction action)
        {
            var go=new GameObject(label,typeof(RectTransform),typeof(RoundedGlassImage),typeof(Button)); go.transform.SetParent(panel.transform,false);
            go.GetComponent<RectTransform>().anchoredPosition=position; go.GetComponent<RectTransform>().sizeDelta=new Vector2(212,60);
            go.GetComponent<Image>().color=RoomSiegeStyle.Button;
            var button=go.GetComponent<Button>(); button.targetGraphic=go.GetComponent<Image>(); button.onClick.AddListener(action);
            RoomSiegeStyle.StyleButton(button);
            var text=Text(label+" label",label,Vector2.zero,new Vector2(207,58),25);
            text.transform.SetParent(go.transform,false);
            return text;
        }
        private void OnDestroy() { if(controller!=null) controller.ViewChanged-=Refresh; }
    }
}
