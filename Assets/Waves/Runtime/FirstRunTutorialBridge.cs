using RoomSiege.Towers;
using RoomSiege.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomSiege.Waves
{
    // The composition layer can read towers and waves without a UI -> gameplay dependency.
    [DefaultExecutionOrder(11200)]
    public sealed class FirstRunTutorialBridge : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name == "RoomSiege_Start" && FindObjectOfType<FirstRunTutorialBridge>() == null)
                new GameObject("RoomSiege First Run Tutorial").AddComponent<FirstRunTutorialBridge>();
        }

        private void LateUpdate()
        {
            var panels = WorldPanelHost.Instance;
            var waves = WaveController.Instance;
            var towers = TowerHost.Instance;
            var manager = GameManager.Instance;
            if (panels == null || !panels.Tutorial.Active || waves == null || !waves.IsReady ||
                towers == null || towers.Placement == null || manager == null) return;
            // Function pages own input; the inline lesson never pauses or opens a page.
            if (panels.Navigation.HasOpenPage || panels.Follower.IsPaused) return;
            bool hasTowers = false, upgraded = false;
            foreach (var tower in towers.Placement.Towers)
            {
                if (tower.Removed) continue;
                hasTowers = true;
                upgraded |= tower.Level > 1;
            }
            var selected = towers.ManagedTower;
            panels.Tutorial.Observe(new TutorialObservation
            {
                State = manager.State,
                RoomReady = waves.HasPlayableGrid,
                HasPendingTower = towers.PendingTowerId != null,
                HasTowers = hasTowers,
                HasUpgradedTower = upgraded,
                HasManagedTower = selected != null,
                UpgradeAffordable = selected != null && towers.Placement.CanUpgrade(selected, 2, out _, out _),
                WaveActive = waves.IsWaveActive
            });
            panels.RefreshTutorial();
        }
    }
}
