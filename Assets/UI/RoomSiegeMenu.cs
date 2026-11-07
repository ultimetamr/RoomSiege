using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoomSiege
{
    public sealed class RoomSiegeMenu : MonoBehaviour
    {
        [SerializeField] private GameManager manager;
        [SerializeField] private Text status;
        [SerializeField] private Button startButton;

        public void Configure(GameManager gameManager, Text stateText, Button start)
        {
            manager = gameManager;
            status = stateText;
            startButton = start;
        }

        private void Start()
        {
            if (GameManager.Instance != null) manager = GameManager.Instance;
            manager.StateChanged += ShowState;
            ShowState(manager.State);
        }

        private void Update()
        {
            // Emulator/Editor shortcut uses the same button callback.
            if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
                startButton.onClick.Invoke();
        }

        private void ShowState(GameState state)
        {
            status.text = state == GameState.BUILD
                ? "状态：BUILD\n建造阶段占位 · 尚未实现玩法"
                : "状态：MENU\n选择开始进入 BUILD 占位（快捷键 B）";
        }

        private void OnDestroy()
        {
            if (manager != null) manager.StateChanged -= ShowState;
        }
    }
}
