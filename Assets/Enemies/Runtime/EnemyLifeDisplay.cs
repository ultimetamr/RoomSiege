using RoomSiege.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Enemies
{
    // Update the existing life field and result text; no additional panel or wave controls.
    public sealed class EnemyLifeDisplay : MonoBehaviour
    {
        private GameManager manager;
        private Text life;
        private Text info;
        private void Start()
        {
            manager=GameManager.Instance;
            var panels=WorldPanelHost.Instance;
            if(manager==null||panels==null||panels.Panels==null)return;
            var right=panels.Panels[2].transform;
            var lifeNode=right.Find("Info label 1");var infoNode=right.Find("Info hint");
            life=lifeNode!=null?lifeNode.GetComponent<Text>():null;
            info=infoNode!=null?infoNode.GetComponent<Text>():null;
            manager.LivesChanged+=Refresh;manager.GameFailed+=Failed;Refresh(manager.Lives);
        }
        private void Refresh(float value){if(life!=null)life.text="生命\n"+value.ToString("0.##");}
        private void Failed()
        {
            Refresh(0);if(info!=null)info.text="防线失守 · 失败";
            if(TowerHostAvailable())RoomSiege.Towers.TowerHost.Instance.SetStatus("防线失守：生命归零，已进入失败结果态");
        }
        private bool TowerHostAvailable()=>RoomSiege.Towers.TowerHost.Instance!=null;
        private void OnDestroy(){if(manager!=null){manager.LivesChanged-=Refresh;manager.GameFailed-=Failed;}}
    }
}
