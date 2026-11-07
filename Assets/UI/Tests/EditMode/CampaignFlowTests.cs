using NUnit.Framework;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    public sealed class CampaignFlowTests
    {
        [Test]
        public void FailureAndVictoryCanBothRestartWithFreshResources()
        {
            var go=new GameObject("Campaign reset test");
            try
            {
                var game=go.AddComponent<GameManager>();
                if(GameManager.Instance!=game)typeof(GameManager).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(game,null);
                game.InitializeEconomy(150);game.TrySpendCoins(90);game.SetCurrentWave(7);
                game.ApplyLeakDamage(100);
                Assert.IsTrue(game.IsFailed);
                int resets=0;
                game.CampaignReset+=()=>{resets++;Assert.AreEqual(10,game.Lives);Assert.AreEqual(0,game.CurrentWave);Assert.IsFalse(game.IsFailed);};
                game.ResetCampaign(150,GameState.BUILD);
                Assert.AreEqual(150,game.Coins);Assert.AreEqual(GameState.BUILD,game.State);
                game.CompleteVictory();Assert.IsTrue(game.IsVictorious);
                game.ResetCampaign(150,GameState.BUILD);
                Assert.IsFalse(game.IsVictorious);Assert.AreEqual(2,resets);
                game.SetState(GameState.WAVE);Assert.AreEqual(GameState.WAVE,game.State);
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void MenuSetupBuildBattleAndResultExposeOnlyTheirOwnContent()
        {
            var gameObject=new GameObject("Flow test manager");
            var cameraObject=new GameObject("Flow test camera",typeof(Camera));
            var hostObject=new GameObject("Flow test host");
            try
            {
                var game=gameObject.AddComponent<GameManager>();
                if(GameManager.Instance!=game)typeof(GameManager).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(game,null);
                cameraObject.transform.position=new Vector3(0,1.6f,0);
                var host=hostObject.AddComponent<WorldPanelHost>();host.Build(cameraObject.GetComponent<Camera>());
                var main=host.Panels[0].transform;
                Assert.IsTrue(main.Find("Main menu page").gameObject.activeSelf);
                Assert.IsFalse(main.Find("Room setup page").gameObject.activeSelf);
                game.SetState(GameState.SCAN);
                Assert.IsFalse(main.Find("Main menu page").gameObject.activeSelf);
                Assert.IsTrue(main.Find("Room setup page").gameObject.activeSelf);
                game.ResetCampaign(150,GameState.BUILD);
                Assert.IsTrue(main.Find("Build controls").gameObject.activeSelf);
                Assert.IsTrue(host.Panels[1].transform.Find("Tower slot 0").gameObject.activeSelf);
                Assert.IsFalse(main.Find("Main Battlefield background").gameObject.activeSelf);
                host.SetPlacementMode(true,"MG · 50G");Assert.IsTrue(host.PlacementMode);
                host.Follower.Tick(1/60f);
                Assert.AreEqual(0,host.Panels[0].GetComponent<CanvasGroup>().alpha,"Follower cannot make hidden UI visible");
                var card=host.Panels[1].transform.Find("Tower slot 0").GetComponent<UnityEngine.UI.Image>();
                Assert.IsFalse(card.Raycast(Vector2.zero,cameraObject.GetComponent<Camera>()),"Hidden graphics reject rays");
                game.SetState(GameState.WAVE);
                Assert.IsFalse(host.PlacementMode);Assert.AreEqual(1,host.Panels[0].GetComponent<CanvasGroup>().alpha);
                Assert.IsTrue(main.Find("Battle controls").gameObject.activeSelf);
                Assert.IsFalse(host.Panels[1].transform.Find("Tower slot 0").gameObject.activeSelf);
                host.SetConstructionOpen(true);
                Assert.IsTrue(main.Find("Build controls").gameObject.activeSelf);
                Assert.AreEqual(GameState.WAVE,game.State); // UI does not suspend/rewind a live wave.
                game.ApplyLeakDamage(100);
                Assert.IsTrue(main.Find("Result actions page").gameObject.activeSelf);
                Assert.IsFalse(main.Find("Build controls").gameObject.activeSelf);
                game.ResetCampaign(150,GameState.MENU);
                Assert.IsTrue(main.Find("Main menu page").gameObject.activeSelf);
                Assert.IsFalse(main.Find("Result actions page").gameObject.activeSelf);
                game.ResetCampaign(150,GameState.BUILD);host.SetPlacementMode(true);host.ShowHelp();
                typeof(WorldPanelHost).GetMethod("Update",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(host,null);
                Assert.IsFalse(host.PlacementMode);Assert.AreEqual(1,host.Panels[0].GetComponent<CanvasGroup>().alpha,"Help must remain visible");
            }
            finally{Object.DestroyImmediate(hostObject);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(gameObject);}
        }
    }
}
