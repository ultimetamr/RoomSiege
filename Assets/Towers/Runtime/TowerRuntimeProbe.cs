#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoomSiege.Towers
{
    // Opt-in development verification. No network listener and no production command channel.
    public sealed class TowerRuntimeProbe : MonoBehaviour
    {
        private bool running;
        private float nextPoll;
        private readonly List<string> checks=new List<string>();
        private string Request=>Path.Combine(Application.persistentDataPath,"tower-acceptance.request");
        private string Result=>Path.Combine(Application.persistentDataPath,"tower-acceptance-result.txt");
        private void Update()
        {
            if(running||Time.unscaledTime<nextPoll)return;nextPoll=Time.unscaledTime+.5f;
            if(!File.Exists(Request)||TowerHost.Instance==null||TowerHost.Instance.Placement==null)return;
            File.Delete(Request);StartCoroutine(Run());
        }
        private void Check(bool valid,string name)
        {checks.Add((valid?"PASS ":"FAIL ")+name);Debug.Log("[RoomSiege.Towers.Probe] "+checks[checks.Count-1]);}
        private IEnumerator Run()
        {
            running=true;checks.Clear();var host=TowerHost.Instance;var manager=GameManager.Instance;
            if(host.Placement.Grid==null||host.Placement.Towers.Any(t=>!t.Removed))
            {Check(false,"Probe requires a scanned room and no existing purchases.");Save();yield break;}
            int original=manager.Coins;var oldState=manager.State;
            manager.ResetCampaign(original+1500,GameState.BUILD);
            try
            {
                var cell=host.Placement.Grid.PlaceableCells.OrderBy(c=>(c.Center-(host.Placement.Grid.SafeZoneCenter+Vector3.forward*1.8f)).sqrMagnitude).First();
                Ray tap=new Ray(cell.Center+Vector3.up*2,Vector3.down);
                foreach(string id in new[]{"MG","Frost","Laser","Trap"})
                {
                    int before=manager.Coins;host.Choose(id);bool bought=host.Tap(tap);
                    Check(bought&&manager.Coins==before-host.Catalog.GetTower(id).buildCost,id+" ray Tap purchase / shared GameManager debit");
                    var tower=host.Placement.Towers.LastOrDefault(t=>!t.Removed);
                    if(tower==null)continue;
                    Physics.SyncTransforms();host.Choose(id);host.Tap(tap);Check(manager.Coins==before-tower.Definition.buildCost,id+" occupied-cell inspection / no duplicate debit");
                    host.Cancel();Physics.SyncTransforms();host.Tap(tap);
                    int paid=manager.Coins;host.Upgrade(3);
                    int price=tower.Definition.levels[1].upgradeCost+tower.Definition.levels[2].upgradeCost;
                    Check(tower.Level==3&&manager.Coins==paid-price,id+" Lv3 charges both configured upgrades");
                    var panel=GameObject.Find("Tower management");
                    Check(panel!=null&&host.GetComponent<TowerPanelUi>().ManagementVisible&&!RoomSiege.UI.WorldPanelHost.Instance.Navigation.HasOpenPage,id+" inline management keeps construction UI available");
                    host.SpawnStubs();var stubs=host.GetComponentsInChildren<TowerTestTarget>();
                    float hp=stubs.Sum(t=>t.Hp);
                    yield return new WaitForSeconds(.4f);
                    Check(stubs.Sum(t=>t.Hp)<hp,id+" attacks live Unity test doubles");
                    if(id=="Frost")Check(stubs.Any(t=>t.SlowFraction>=.55f),"Frost Lv3 applies area slow");
                    if(id=="Trap")Check(tower.Consumed,"Trap consumes itself after one trigger");
                    else
                    {
                        int selling=manager.Coins;int refund=host.Placement.Refund(tower);host.Sell();
                        Check(tower.Removed&&manager.Coins==selling+refund,id+" sell credits configured 70% once");
                    }
                    host.ClearStubs();host.Cancel();yield return null;
                }
                var proxy=FindObjectsOfType<TowerGrabProxy>();Check(proxy.Length==4,"Four actual XRGrabInteractable shop proxies");
            }
            finally
            {
                foreach(var tower in host.Placement.Towers.Where(t=>!t.Removed).ToArray())host.Placement.TrySell(tower,out _);
                host.ClearStubs();host.Cancel();
                if(manager.Coins>original)manager.TrySpendCoins(manager.Coins-original);else manager.AddCoins(original-manager.Coins);
                manager.ResetCampaign(original,oldState==GameState.BUILD||oldState==GameState.SCAN?oldState:GameState.MENU);Check(manager.Coins==original,"Probe restores original shared balance");Save();
            }
        }
        private void Save(){File.WriteAllLines(Result,checks);running=false;Debug.Log("[RoomSiege.Towers.Probe] Result: "+Result);}
    }
}
#endif
