using UnityEngine;

namespace RoomSiege
{
    public enum GameState { MENU, SCAN, BUILD, WAVE, RESULT }

    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        [SerializeField] private int coins = 100;
        [SerializeField] private float lives = 10;
        [SerializeField] private int currentWave;
        [SerializeField] private int difficulty = 1;
        [SerializeField] private GameState state = GameState.MENU;

        public int Coins => coins;
        public float Lives => lives;
        public float InitialLives { get; private set; }
        public bool IsFailed { get; private set; }
        public bool IsVictorious { get; private set; }
        public int CurrentWave => currentWave;
        public int Difficulty => difficulty;
        public GameState State => state;
        public event System.Action<GameState> StateChanged;
        public event System.Action<int> CoinsChanged;
        public event System.Action<float> LivesChanged;
        public event System.Action GameFailed;
        public event System.Action CampaignReset;
        public bool EconomyInitialized { get; private set; }
        public bool TookDamage {get;private set;}
        public event System.Action<float> CoreDamaged;

        public void SelectDifficulty(int value)
        {
            if (value < 0 || value > 2) throw new System.ArgumentOutOfRangeException(nameof(value));
            if (state == GameState.MENU || state == GameState.SCAN) difficulty = value;
        }

        // All session owners reset before the new phase is published. Local unlocks stay intact.
        public void ResetCampaign(int initialGold, GameState nextState)
        {
            if (initialGold < 0) throw new System.ArgumentOutOfRangeException(nameof(initialGold));
            if (nextState != GameState.BUILD && nextState != GameState.MENU && nextState != GameState.SCAN)
                throw new System.ArgumentOutOfRangeException(nameof(nextState));
            coins = initialGold;
            lives = InitialLives;
            currentWave = 0;
            IsFailed = IsVictorious = false;
            TookDamage=false;
            EconomyInitialized = true;
            state = nextState;
            CampaignReset?.Invoke();
            CoinsChanged?.Invoke(coins);
            LivesChanged?.Invoke(lives);
            Debug.Log($"[RoomSiege] Campaign reset -> {state}; gold={coins}; lives={lives}; difficulty={difficulty}");
            StateChanged?.Invoke(state);
        }

        public void InitializeEconomy(int initialGold)
        {
            if (EconomyInitialized) return;
            if (initialGold < 0) throw new System.ArgumentOutOfRangeException(nameof(initialGold));
            coins = initialGold;
            EconomyInitialized = true;
            CoinsChanged?.Invoke(coins);
        }

        public bool TrySpendCoins(int amount)
        {
            if (amount < 0 || amount > coins) return false;
            coins -= amount;
            CoinsChanged?.Invoke(coins);
            return true;
        }

        public void AddCoins(int amount)
        {
            if (amount < 0 || amount > int.MaxValue - coins)
                throw new System.ArgumentOutOfRangeException(nameof(amount));
            coins += amount;
            CoinsChanged?.Invoke(coins);
        }

        // Keep fractional difficulty damage (for example Casual Grunt = 0.5 life).
        public void ApplyLeakDamage(float amount)
        {
            if (IsFailed || IsVictorious || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            TookDamage=true;CoreDamaged?.Invoke(amount);
            lives = Mathf.Max(0, lives - amount);
            if (lives <= 0)
            {
                IsFailed = true;
                SetState(GameState.RESULT);
                LivesChanged?.Invoke(lives);
                GameFailed?.Invoke();
            }
            else LivesChanged?.Invoke(lives);
        }

        public bool Heal(float amount)
        {
            if(IsFailed||IsVictorious||state!=GameState.WAVE||float.IsNaN(amount)||float.IsInfinity(amount)||amount<=0||lives>=InitialLives)return false;
            lives=Mathf.Min(InitialLives,lives+amount);LivesChanged?.Invoke(lives);return true;
        }

        public void SetCurrentWave(int number)
        {
            if (number < 0) throw new System.ArgumentOutOfRangeException(nameof(number));
            currentWave = number;
        }

        public void CompleteVictory()
        {
            if (IsFailed || IsVictorious || lives <= 0) return;
            IsVictorious = true;
            SetState(GameState.RESULT);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            InitialLives = lives;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetState(GameState nextState)
        {
            if ((IsFailed || IsVictorious) && nextState != GameState.RESULT) return;
            if (state == nextState) return;
            state = nextState;
            Debug.Log($"[RoomSiege] State -> {state}");
            StateChanged?.Invoke(state);
        }
        public void EnterBuildPlaceholder()
        {
            if (state != GameState.WAVE && state != GameState.RESULT) SetState(GameState.BUILD);
        }
        public void EnterSettingsPlaceholder() => SetState(GameState.MENU);
    }
}
