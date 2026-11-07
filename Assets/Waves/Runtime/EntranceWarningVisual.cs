using System.Collections.Generic;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.Visuals;
using RoomSiege.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Waves
{
    public sealed class EntranceWarningVisual:MonoBehaviour
    {
        private sealed class Portal{public LineRenderer Ring;public TextMesh Label;public Text Direction;public int Index;}
        private readonly List<Portal> portals=new List<Portal>();
        private GameObject root;
        private Material material;
        private GridModel displayed;
        private float phase;
        private bool wasActive;
        private float launchRemaining;
        private void Update()
        {
            var scanner=ScanController.Instance;var waves=WaveController.Instance;
            if(scanner==null||waves==null||!waves.IsReady)return;
            if(displayed!=scanner.CurrentGrid)
            {
                if(displayed==null||scanner.CurrentGrid==null||!ReferenceEquals(displayed.SpawnPoints,scanner.CurrentGrid.SpawnPoints))Rebuild(scanner.CurrentGrid);
                else displayed=scanner.CurrentGrid;
            }
            var panels=WorldPanelHost.Instance;
            bool visible=waves.IsRunning&&waves.Outcome==CampaignOutcome.None&&panels!=null&&!panels.Navigation.HasOpenPage;
            if(root!=null)root.SetActive(visible);
            foreach(var portal in portals)if(portal.Direction!=null)portal.Direction.gameObject.SetActive(visible&&!panels.Tutorial.Visible);
            if(!visible)return;
            if(waves.IsWaveActive&&!wasActive)launchRemaining=3f;
            wasActive=waves.IsWaveActive;
            if(!waves.IsPaused){phase+=Time.deltaTime;launchRemaining=Mathf.Max(0,launchRemaining-Time.deltaTime);}
            string title=waves.IsWaveActive?(launchRemaining>0?$"敌人来袭 · W{waves.CurrentWave}":$"入侵中 · W{waves.CurrentWave}"):waves.AwaitingFirstWave?"入侵方向 · 等待布防":$"入侵倒计时 {Mathf.CeilToInt(waves.PreparationRemaining)}s";
            foreach(var portal in portals)
            {
                portal.Label.gameObject.SetActive(panels.DisplayedState==GameState.WAVE&&!panels.PlacementMode);
                float pulse=(Mathf.Sin(phase*(waves.IsPreparing&&!waves.AwaitingFirstWave&&waves.PreparationRemaining<=5?9:3))+1)*.5f;
                var color=RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy,Mathf.Lerp(.35f,.95f,pulse));
                portal.Ring.startColor=portal.Ring.endColor=color;portal.Ring.widthMultiplier=Mathf.Lerp(.015f,.035f,pulse);
                string groups=!waves.IsWaveActive?WaveThreatPresentation.Forecast(waves.PreviewWave,portal.Index,Mathf.Min(2,displayed.SpawnPoints.Count)):"";
                portal.Label.text=$"入口 {portal.Index+1} · "+title+(!string.IsNullOrEmpty(groups)?"\n"+groups:"");
                var head=Camera.main;if(head!=null)portal.Label.transform.rotation=Quaternion.LookRotation(portal.Label.transform.position-head.transform.position);
                if(portal.Direction!=null&&head!=null)
                {
                    Vector3 delta=displayed.SpawnPoints[portal.Index].Position-head.transform.position;delta.y=0;
                    portal.Direction.text=$"入口 {portal.Index+1} · {WaveThreatPresentation.Direction(head.transform.forward,delta)}\n{delta.magnitude:0.0}m · "+
                        (waves.IsWaveActive?(launchRemaining>0?"敌人来袭":"入侵中"):waves.AwaitingFirstWave?"待开波":$"{Mathf.CeilToInt(waves.PreparationRemaining)}秒后入侵");
                    portal.Direction.color=waves.IsPreparing&&!waves.AwaitingFirstWave&&waves.PreparationRemaining<=5?RoomSiegeStyle.Gold:RoomSiegeStyle.Enemy;
                }
            }
        }
        private void Rebuild(GridModel grid)
        {
            displayed=grid;if(root!=null){root.SetActive(false);Destroy(root);}
            foreach(var portal in portals)if(portal.Direction!=null){portal.Direction.gameObject.SetActive(false);Destroy(portal.Direction.gameObject);}
            portals.Clear();wasActive=false;launchRemaining=0;
            if(grid==null)return;
            if(material==null)material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            root=new GameObject("Entrance tactical warnings");root.transform.SetParent(transform,false);
            int count=Mathf.Min(2,grid.SpawnPoints.Count);
            for(int index=0;index<count;index++)
            {
                var spawn=grid.SpawnPoints[index];
                var ring=TowerVisuals.Ring(root.transform,"Portal warning",material,RoomSiegeStyle.Enemy,.45f,.02f);
                ring.transform.position=spawn.Position+Vector3.up*.5f;
                ring.transform.rotation=Quaternion.LookRotation(spawn.Forward)*Quaternion.Euler(90,0,0);
                var label=new GameObject("Portal forecast");label.transform.SetParent(root.transform,false);label.transform.position=spawn.Position+Vector3.up*1.05f;
                var text=label.AddComponent<TextMesh>();text.fontSize=42;text.characterSize=.009f;text.anchor=TextAnchor.MiddleCenter;text.color=RoomSiegeStyle.Enemy;
                var panels=WorldPanelHost.Instance;Text direction=null;
                if(panels!=null&&panels.Panels!=null)
                {
                    var go=new GameObject("Entrance direction "+(index+1),typeof(RectTransform),typeof(Text));go.layer=5;
                    go.transform.SetParent(panels.Panels[3].transform,false);
                    var rect=go.GetComponent<RectTransform>();rect.anchoredPosition=new Vector2(count==1?0:index==0?-190:190,78);rect.sizeDelta=new Vector2(360,56);
                    direction=go.GetComponent<Text>();var sample=panels.Panels[3].transform.Find("Progress label").GetComponent<Text>();
                    direction.font=sample.font;direction.material=sample.material;direction.fontSize=16;direction.alignment=TextAnchor.MiddleCenter;direction.raycastTarget=false;
                    direction.gameObject.SetActive(false);
                }
                portals.Add(new Portal{Ring=ring,Label=text,Direction=direction,Index=index});
            }
        }
        private void OnDestroy(){foreach(var portal in portals)if(portal.Direction!=null)Destroy(portal.Direction.gameObject);if(material!=null)Destroy(material);}
    }
}
