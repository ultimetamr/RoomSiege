using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.UI
{
    // Keep the follower's canvas roots alive; hide their content while a function page is open.
    public sealed class WorldPanelNavigation : MonoBehaviour
    {
        private readonly HashSet<GameObject> pages = new HashSet<GameObject>();
        private readonly Dictionary<GameObject, bool> hiddenContent = new Dictionary<GameObject, bool>();
        private Canvas[] panels;
        private GameObject current;
        private bool showingPage;
        private bool restoring;
        private int closeFrame;
        public bool HasOpenPage => showingPage || restoring;
        public event System.Action ContentRestored;
        public void CloseCurrent() { if (showingPage) Close(current); }

        public void Initialize(Canvas[] value) { panels = value; }

        public void Register(GameObject page)
        {
            if (page == null || !pages.Add(page)) return;
            page.SetActive(false);
        }

        public void Show(GameObject page)
        {
            if (page == null) return;
            WorldInputPointer.ConsumePagePress();
            restoring = false;
            Register(page);
            if (current != null && current != page) current.SetActive(false);
            current = page;
            showingPage = true;
            page.SetActive(true);
            HideUnderlyingContent();
            Debug.Log("[RoomSiege.UI] Page -> " + page.name);
        }

        public void Close(GameObject page)
        {
            if (!showingPage || current != page) { if (page != null) page.SetActive(false); return; }
            WorldInputPointer.ConsumePagePress();
            if (page != null) page.SetActive(false);
            current = null;
            showingPage = false;
            restoring = true;
            closeFrame = Time.frameCount;
            Debug.Log("[RoomSiege.UI] Page closed; waiting for input release");
        }

        private void LateUpdate()
        {
            if (restoring)
            {
                HideUnderlyingContent();
                // Restore only after both hands release and the closing dispatch has finished.
                if (Time.frameCount > closeFrame && !WorldInputPointer.AnyPressHeld)
                {
                    restoring = false;
                    RestoreContent();
                    Debug.Log("[RoomSiege.UI] Page -> Battlefield (input released)");
                }
                return;
            }
            if (!showingPage) return;
            if (current == null || !current.activeSelf) { Close(current); return; }
            // Modules may add controls after another module has already opened a page.
            HideUnderlyingContent();
        }

        private void HideUnderlyingContent()
        {
            if (panels == null) return;
            foreach (var panel in panels)
            {
                if (panel == null) continue;
                foreach (Transform child in panel.transform)
                {
                    var content = child.gameObject;
                    if (pages.Contains(content)) continue;
                    if (!hiddenContent.ContainsKey(content)) hiddenContent.Add(content, content.activeSelf);
                    if (content.activeSelf) content.SetActive(false);
                }
            }
        }

        private void RestoreContent()
        {
            foreach (var entry in hiddenContent)
                if (entry.Key != null) entry.Key.SetActive(entry.Value);
            hiddenContent.Clear();
            ContentRestored?.Invoke();
        }

        private void OnDestroy() { RestoreContent(); }
    }
}
