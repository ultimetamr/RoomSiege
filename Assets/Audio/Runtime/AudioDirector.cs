using System.Collections;
using System.Collections.Generic;
using RoomSiege.Enemies;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Waves;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RoomSiege.Audio
{
    [DefaultExecutionOrder(11200)]
    public sealed class AudioDirector : MonoBehaviour
    {
        private const string MusicPreference = "RoomSiege.Audio.MusicEnabled";
        private const int VoiceLimit = 40;
        public static AudioDirector Instance { get; private set; }
        [SerializeField, Range(0, 1)] private float effectsVolume = .7f;
        [SerializeField, Range(0, 1)] private float musicVolume = .42f;
        private const float BattlefieldBoost = 2.5f;
        private const float FrostLevel = .6f;

        private sealed class Voice
        {
            public AudioSource Source;
            public float Remaining;
            public bool Busy, Ui, Paused;
        }
        private sealed class EnemySound
        {
            public int SpawnId;
            public float StepClock, Orbit;
            public Vector3 LastPosition;
            public bool Left;
            public AudioSource Boss;
        }
        private sealed class LaserSound
        {
            public AudioSource Source;
            public TowerView View;
            public float LastFire;
        }
        private readonly List<Voice> voices = new List<Voice>();
        private readonly Dictionary<EnemyActor, EnemySound> actors = new Dictionary<EnemyActor, EnemySound>();
        private readonly Dictionary<TowerView, LaserSound> lasers = new Dictionary<TowerView, LaserSound>();
        private readonly Dictionary<TowerView, LaserSound> frostLoops = new Dictionary<TowerView, LaserSound>();
        private readonly Dictionary<int, float> impactTimes = new Dictionary<int, float>();
        private readonly Dictionary<Button, UnityAction> clicks = new Dictionary<Button, UnityAction>();
        private readonly HashSet<EnemyActor> living = new HashSet<EnemyActor>();
        private readonly List<EnemyActor> staleActors = new List<EnemyActor>();
        private readonly List<TowerView> staleLasers = new List<TowerView>();
        private readonly List<Button> staleButtons = new List<Button>();
        private readonly Stack<AudioSource> bossPool = new Stack<AudioSource>();
        private readonly Stack<AudioSource> laserPool = new Stack<AudioSource>();
        private SpatialClipBank clips;
        private EnemyController enemies;
        private WaveController waves;
        private ScanController scanner;
        private WorldPanelHost panels;
        private GameManager manager;
        private Camera head;
        private GameObject ownedContext;
        private PXR_Audio_Spatializer_AudioListener ownedListener;
        private AudioSource music;
        private AudioSettingsPanel settings;
        private AudioOutputMeter outputMeter;
        private Coroutine audition;
        private AudioSource auditionSource;
        public string AuditionStatus { get; private set; }
        private float nextUiScan;
        private float nextUiHover;
        private bool ready, musicEnabled, applicationPaused, lastPaused, musicPaused, bossAnnounced;
        public bool MusicEnabled => musicEnabled;

        public void PlaySupport(string id,Vector3 point)
        {
            if(!ready||clips==null)return;
            Play(id=="strike"||id=="mine"?clips.Trap:id=="freeze"||id=="frostsigil"?clips.Frost:id=="thunder"?clips.Tesla:id=="emp"||id=="gravity"?clips.Laser:id=="firefield"?clips.Mortar:clips.Impact,point,.25f,70,true);
        }

        public bool PlayCodexSample(string id, bool enemy)
        {
            if (!ready || clips == null) return false;
            if (enemy && id != "Grunt" && id != "Runner" && id != "Tank" && id != "Shielded" && id != "Boss") return false;
            if (!enemy && !RoomSiege.Data.WeaponRoster.Contains(id)) return false;
            AudioClip clip;
            if (enemy) clip = clips.Footstep(id);
            else if(RoomSiege.Data.WeaponRoster.IsNew(id))clip=clips.Arsenal(id);
            else if (id == "Frost") clip = clips.Frost;
            else if (id == "Laser") clip = clips.Laser;
            else if (id == "Trap") clip = clips.Trap;
            else if (id == "Tesla") clip = clips.Tesla;
            else if (id == "Mortar") clip = clips.Mortar;
            else clip = clips.Impact;
            Vector3 position = head != null ? head.transform.position + head.transform.forward * .9f : transform.position;
            Play(clip, position, .24f, 50, true);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name == "RoomSiege_Start" && FindObjectOfType<AudioDirector>() == null)
                new GameObject("RoomSiege Spatial Audio").AddComponent<AudioDirector>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            musicEnabled = PlayerPrefs.GetInt(MusicPreference, 1) != 0;
        }

        private IEnumerator Start()
        {
            while ((head = Camera.main) == null || (enemies = EnemyController.Instance) == null || !enemies.IsReady ||
                   (waves = WaveController.Instance) == null || !waves.IsReady ||
                   (scanner = ScanController.Instance) == null || (manager = GameManager.Instance) == null ||
                   (panels = WorldPanelHost.Instance) == null || panels.Panels == null || panels.Follower == null)
                yield return null;

            if (AudioSettings.GetSpatializerPluginName() != "Pico Spatial Audio")
            {
                Debug.LogError("[RoomSiege.Audio] Select Pico Spatial Audio in Project Settings > Audio. Spatial battlefield audio stays silent until the HRTF backend is enabled.");
                yield break;
            }
            // The SDK Context registers its listener synchronously in OnEnable.
            if (head.GetComponent<PXR_Audio_Spatializer_AudioListener>() == null)
                ownedListener = head.gameObject.AddComponent<PXR_Audio_Spatializer_AudioListener>();
            if (PXR_Audio_Spatializer_Context.Instance == null)
            {
                var prefab = Resources.Load<GameObject>("RoomSiegePicoAudioContext");
                if (prefab == null) { Debug.LogError("[RoomSiege.Audio] PICO HRTF context prefab missing."); yield break; }
                ownedContext = Instantiate(prefab);
            }
            outputMeter = head.gameObject.AddComponent<AudioOutputMeter>();
            clips = new SpatialClipBank();
            for (int i = 0; i < VoiceLimit; i++)
                voices.Add(new Voice { Source = SpatialAudioSourceFactory.Create(transform, "Spatial cue " + i) });
            music = SpatialAudioSourceFactory.Create(transform, "Low ambient BGM");
            music.clip = clips.Music;
            music.loop = true;
            music.priority = 200;
            music.volume = musicVolume;
            settings = gameObject.AddComponent<AudioSettingsPanel>();
            settings.Initialize(this, panels);
            TowerView.AudioFired += TowerFired;
            TowerView.AudioStopped += TowerStopped;
            WaveHorn.PlaybackRequested += Horn;
            WaveHorn.WarningRequested+=EntranceWarning;WaveHorn.FreezeRequested+=FreezePulse;
            WaveHorn.StopRequested += StopHorn;
            waves.WaveStarted += WaveStarted;
            waves.WaveFinished += WaveFinished;
            waves.CampaignFinished += CampaignFinished;
            enemies.Spawned += Spawned;
            enemies.Leaked += Leaked;
            manager.StateChanged += StateChanged;
            manager.CampaignReset += ResetCampaign;
            scanner.GridChanged += GridChanged;
            ready = true;
            UpdateMusic();
            BindButtons();
            var context = PXR_Audio_Spatializer_Context.Instance;
            Debug.Log($"[RoomSiege.Audio] Ready backend={context.spatializerApiImpl}, context={context.Initialized}, listener={head.name}, rate={AudioSettings.outputSampleRate}, musicEnabled={musicEnabled}");
            StartCoroutine(LogOutputHealth());
        }

        private IEnumerator LogOutputHealth()
        {
            var samples = new float[512];
            for (int attempt = 0; attempt < 4; attempt++)
            {
                yield return new WaitForSecondsRealtime(2);
                AudioListener.GetOutputData(samples, 0);
                float energy = 0;
                foreach (float sample in samples) energy += sample * sample;
                Debug.Log($"[RoomSiege.Audio] Output rms={Mathf.Sqrt(energy / samples.Length):F6}, peakRms={outputMeter.PeakRms:F6}, blocks={outputMeter.Blocks}, musicPlaying={music.isPlaying}, enabled={musicEnabled}, paused={Paused}, listenerVolume={AudioListener.volume:F2}");
            }
        }

        private bool Paused => applicationPaused || Time.timeScale <= .0001f ||
            (panels != null && panels.Follower != null && (panels.Follower.IsPaused || panels.Navigation.HasOpenPage));
        private bool BattleRunning => manager != null && manager.State == GameState.WAVE &&
            enemies != null && enemies.CanSimulate && scanner != null && !scanner.IsScanning;

        private void Update()
        {
            if (!ready) return;
            if (head == null) head = Camera.main;
            if (Time.unscaledTime >= nextUiScan) { nextUiScan = Time.unscaledTime + .5f; BindButtons(); }
            bool paused = Paused;
            TickVoices(paused);
            UpdateMusic();
            UpdateEnemies(BattleRunning && !paused);
            UpdateLasers(BattleRunning && !paused);
            lastPaused = paused;
        }

        private void Spawned(EnemyActor actor)
        {
            RemoveEnemy(actor);
            var state = new EnemySound
            {
                SpawnId = actor.TargetId, LastPosition = actor.Position,
                StepClock = .12f + (actor.TargetId % 5) * .07f,
                Orbit = actor.TargetId * .7f
            };
            actors.Add(actor, state);
            actor.HitReceived += Hit;
            // At spawn the actor is at the actual selected entrance (or Boss summon location).
            Play(clips.Footstep(actor.EnemyId), actor.transform.position + Vector3.up * .08f,
                actor.EnemyId == "Tank" ? .3f : .14f, 110);
            if (actor.EnemyId == "Boss")
            {
                if(!bossAnnounced&&waves.IsWaveActive)
                {
                    bossAnnounced=true;
                    Play(clips.WaveStart,actor.Position,.18f,23);
                }
                state.Boss = TakeLoop(bossPool, "Boss low orbit", clips.Boss, .2f, 65);
                state.Boss.transform.position = actor.Position + Vector3.right * .5f;
                state.Boss.Play();
            }
        }

        private void UpdateEnemies(bool simulate)
        {
            living.Clear();
            foreach (var actor in enemies.ActiveEnemies)
            {
                if (actor == null || !actor.IsAlive) continue;
                living.Add(actor);
                if (!actors.TryGetValue(actor, out var sound) || sound.SpawnId != actor.TargetId)
                { Spawned(actor); sound = actors[actor]; }
                float distance = Vector3.Distance(sound.LastPosition, actor.Position);
                sound.LastPosition = actor.Position;
                if (sound.Boss != null)
                {
                    if (simulate)
                    {
                        sound.Orbit += Time.deltaTime * 1.15f;
                        // Orbit the Boss, retaining the Boss location as the directional origin.
                        sound.Boss.transform.position = actor.Position + new Vector3(Mathf.Cos(sound.Orbit), .1f, Mathf.Sin(sound.Orbit)) * .5f;
                        if (lastPaused) sound.Boss.UnPause();
                        else if (!sound.Boss.isPlaying) sound.Boss.Play();
                    }
                    else sound.Boss.Pause();
                }
                if (!simulate || distance < .0001f) continue;
                sound.StepClock -= Time.deltaTime;
                if (sound.StepClock > 0) continue;
                float speed = Mathf.Max(.1f, actor.MoveSpeed * (1 - actor.SlowFraction));
                sound.StepClock = Mathf.Clamp(.32f / speed, .22f, 1.25f);
                Vector3 position = actor.transform.position + Vector3.up * .07f;
                if (actor.EnemyId == "Runner")
                {
                    sound.Left = !sound.Left;
                    // Alternate actual emitter positions along the listener's horizontal left/right axis.
                    Vector3 side = head != null ? Vector3.ProjectOnPlane(head.transform.right, Vector3.up).normalized : actor.transform.right;
                    position += side * (sound.Left ? -.16f : .16f);
                }
                Play(clips.Footstep(actor.EnemyId), position, actor.EnemyId == "Tank" || actor.EnemyId == "Boss" ? .3f : .13f, 120);
            }
            staleActors.Clear();
            foreach (var pair in actors) if (!living.Contains(pair.Key)) staleActors.Add(pair.Key);
            foreach (var actor in staleActors) RemoveEnemy(actor);
        }

        private void RemoveEnemy(EnemyActor actor)
        {
            if (!actors.TryGetValue(actor, out var sound)) return;
            if (actor != null) actor.HitReceived -= Hit;
            if (sound.Boss != null) { sound.Boss.Stop(); bossPool.Push(sound.Boss); }
            actors.Remove(actor);
        }

        private void Hit(TowerHit hit)
        {
            if (hit.Kind == TowerDamageKind.Laser || hit.Kind == TowerDamageKind.Trap || Paused) return;
            // Area hits may notify many victims in one frame; emit one cue per tower/80 ms.
            if (impactTimes.TryGetValue(hit.SourceTowerId, out float at) && Time.unscaledTime - at < .08f) return;
            var towers = TowerHost.Instance;
            if (towers == null || towers.Placement == null) return;
            foreach (var tower in towers.Placement.Towers)
            {
                // A consumed Trap can still be the source of a valid final hit.
                if (tower.Id != hit.SourceTowerId) continue;
                impactTimes[hit.SourceTowerId] = Time.unscaledTime;
                AudioClip clip = hit.Kind == TowerDamageKind.Frost ? clips.Frost : clips.Impact;
                Play(clip, tower.Cell.Center + Vector3.up * .16f, .25f, 60);
                break;
            }
        }

        private void TowerFired(TowerView view, Vector3 origin, bool continuous)
        {
            if (!ready || Paused || !BattleRunning || view == null || view.Tower == null) return;
            if (!continuous)
            {
                AudioClip shot = clips.Arsenal(view.Tower.Definition.id)??(view.Tower.Definition.id == "MG" ? clips.MgShot :
                    view.Tower.Definition.id == "Trap" ? clips.Trap : view.Tower.Definition.id == "Tesla" ? clips.Tesla :
                    view.Tower.Definition.id == "Mortar" ? clips.Mortar : clips.Impact);
                Play(shot, origin, .32f, 75);
                return;
            }
            if (view.Tower.Definition.id == "Frost")
            {
                if (!frostLoops.TryGetValue(view, out var frost))
                {
                    frost = new LaserSound { View = view, Source = TakeLoop(laserPool, "Frost field shimmer", clips.FrostLoop, .24f, 75) };
                    frostLoops.Add(view, frost);
                }
                frost.LastFire = Time.time;
                frost.Source.transform.position = origin;
                if (!frost.Source.isPlaying) frost.Source.Play();
                return;
            }
            if (!lasers.TryGetValue(view, out var laser))
            {
                laser = new LaserSound { View = view, Source = TakeLoop(laserPool, "Weapon directional hum", clips.Arsenal(view.Tower.Definition.id)??clips.Laser, .27f, 70) };
                lasers.Add(view, laser);
            }
            laser.LastFire = Time.time;
            laser.Source.transform.position = origin;
            if (!laser.Source.isPlaying) laser.Source.Play();
        }

        private void TowerStopped(TowerView view)
        {
            if (lasers.TryGetValue(view, out var laser)) ReleaseLaser(view, laser);
            if (frostLoops.TryGetValue(view, out var frost)) ReleaseFrost(view, frost);
        }

        private void UpdateLasers(bool simulate)
        {
            staleLasers.Clear();
            foreach (var pair in lasers)
                if (!simulate || pair.Key == null || !pair.Key.isActiveAndEnabled || pair.Key.Tower == null ||
                    pair.Key.Tower.Removed || Time.time - pair.Value.LastFire > .075f) staleLasers.Add(pair.Key);
            foreach (var view in staleLasers) ReleaseLaser(view, lasers[view]);
            staleLasers.Clear();
            foreach (var pair in frostLoops)
                if (!simulate || pair.Key == null || !pair.Key.isActiveAndEnabled || pair.Key.Tower == null ||
                    pair.Key.Tower.Removed || Time.time - pair.Value.LastFire > .075f) staleLasers.Add(pair.Key);
            foreach (var view in staleLasers) ReleaseFrost(view, frostLoops[view]);
        }

        private void ReleaseLaser(TowerView view, LaserSound laser)
        {
            laser.Source.Stop();
            laserPool.Push(laser.Source);
            lasers.Remove(view);
        }

        private void ReleaseFrost(TowerView view, LaserSound frost)
        {
            frost.Source.Stop();
            laserPool.Push(frost.Source);
            frostLoops.Remove(view);
        }

        private AudioSource TakeLoop(Stack<AudioSource> pool, string name, AudioClip clip, float volume, int priority)
        {
            var source = pool.Count > 0 ? pool.Pop() : SpatialAudioSourceFactory.Create(transform, name);
            source.clip = clip; source.loop = true; source.priority = priority; source.volume = EffectGain(clip, volume);
            return source;
        }

        private void Horn(Vector3 entrance)
        {
            if (ready && !Paused) Play(clips.Horn, entrance + Vector3.up * .6f, .42f, 25);
        }
        private void EntranceWarning(Vector3 entrance)
        {if(ready&&!Paused)Play(clips.Horn,entrance+Vector3.up*.6f,.16f,30);}
        private void FreezePulse(Vector3 core)
        {if(ready&&!Paused)Play(clips.Frost,core+Vector3.up*.2f,.40f,20);}

        private void StopHorn()
        {
            if (clips == null) return;
            foreach (var voice in voices)
                if (voice.Busy && voice.Source.clip == clips.Horn)
                { voice.Source.Stop(); voice.Busy = false; voice.Paused = false; }
        }

        private void Leaked(EnemyLeak leak)
        {
            if (!ready || scanner.CurrentGrid == null) return;
            Play(clips.Alarm, scanner.CurrentGrid.SafeZoneCenter + Vector3.up * .15f, .45f, 15);
        }

        private void Play(AudioClip clip, Vector3 position, float volume, int priority, bool ui = false)
        {
            if (!ready || clip == null || (Paused && !ui)) return;
            Voice selected = null;
            foreach (var voice in voices) if (!voice.Busy) { selected = voice; break; }
            if (selected == null)
            {
                foreach (var voice in voices)
                    if (voice.Source.priority > priority && (selected == null || voice.Source.priority > selected.Source.priority)) selected = voice;
            }
            if (selected == null) return; // Keep alarm/horn voices ahead of dense footsteps.
            selected.Source.Stop();
            selected.Source.transform.position = position;
            selected.Source.clip = clip;
            bool feedback = clip == clips.Click || clip == clips.Hover || clip == clips.Confirm || clip == clips.Error;
            SpatialAudioSourceFactory.ConfigureDistance(selected.Source, feedback);
            selected.Source.volume = EffectGain(clip, volume);
            selected.Source.priority = priority;
            selected.Source.Play();
            selected.Remaining = clip.length + .04f;
            selected.Busy = true; selected.Ui = ui; selected.Paused = false;
        }

        private void TickVoices(bool paused)
        {
            foreach (var voice in voices)
            {
                if (!voice.Busy) continue;
                bool freeze = applicationPaused || (paused && !voice.Ui);
                if (freeze) { if (!voice.Paused) voice.Source.Pause(); voice.Paused = true; continue; }
                if (voice.Paused) { voice.Source.UnPause(); voice.Paused = false; }
                voice.Remaining -= Time.unscaledDeltaTime;
                if (voice.Remaining <= 0) { voice.Source.Stop(); voice.Busy = false; }
            }
        }

        private void StateChanged(GameState state)
        {
            if (state != GameState.WAVE) StopBattleLoops();
            if(state==GameState.RESULT||state==GameState.MENU)StopCombatVoices();
        }

        private void WaveStarted(int wave)
        {
            // WaveHorn already plays from each entrance. Do not layer another start cue over it.
            bossAnnounced=false;
        }

        private void WaveFinished(WaveResult result)
        {
            if(!ready||result.Reason!=WaveEndReason.Cleared||result.WaveNumber>=waves.TotalWaves||scanner.CurrentGrid==null)return;
            StopCombatVoices();
            // A checkpoint choice can open in this frame. This foreground cue must finish through it.
            Play(clips.Confirm,scanner.CurrentGrid.SafeZoneCenter+Vector3.up*.35f,.22f,25,true);
        }

        private void CampaignFinished(CampaignOutcome outcome)
        {
            StopCombatVoices();
            if (!ready || applicationPaused || scanner == null || scanner.CurrentGrid == null ||
                (outcome!=CampaignOutcome.Victory&&outcome!=CampaignOutcome.Failure)) return;
            var center = scanner.CurrentGrid.SafeZoneCenter + Vector3.up * .35f;
            // Meta may already have opened its result page. Preserve the positioned result cue.
            Play(outcome == CampaignOutcome.Victory ? clips.Victory : clips.Defeat, center, .28f, 18,true);
        }

        private void StopCombatVoices()
        {
            foreach(var voice in voices)
                if(voice.Busy&&!voice.Ui){voice.Source.Stop();voice.Busy=voice.Paused=false;}
        }

        private void ResetCampaign()
        {
            bossAnnounced=false;StopBattleLoops();
            foreach(var voice in voices){voice.Source.Stop();voice.Busy=voice.Paused=false;}
        }

        private void GridChanged(GridModel grid) { if (grid == null) StopBattleLoops(); }

        private void StopBattleLoops()
        {
            foreach (var laser in lasers.Values) { laser.Source.Stop(); laserPool.Push(laser.Source); }
            lasers.Clear();
            foreach (var frost in frostLoops.Values) { frost.Source.Stop(); laserPool.Push(frost.Source); }
            frostLoops.Clear();
            foreach (var actor in actors.Values) if (actor.Boss != null) actor.Boss.Pause();
        }

        public void SetMusicEnabled(bool value)
        {
            musicEnabled = value;
            PlayerPrefs.SetInt(MusicPreference, value ? 1 : 0);
            PlayerPrefs.Save();
            if (ready) UpdateMusic();
            if (settings != null) settings.Refresh();
        }

        private float EffectGain(AudioClip clip, float volume)
        {
            bool feedback = clip == clips.Click || clip == clips.Hover || clip == clips.Confirm || clip == clips.Error;
            float frost = clip == clips.Frost || clip == clips.FrostLoop ? FrostLevel : 1;
            return Mathf.Clamp01(volume * effectsVolume * (feedback ? 1 : BattlefieldBoost) * frost);
        }

        public void AuditionSounds()
        {
            if (!ready || audition != null) return;
            audition = StartCoroutine(AuditionSequence());
        }

        private IEnumerator AuditionSequence()
        {
            if (auditionSource == null)
                auditionSource = SpatialAudioSourceFactory.Create(transform, "Settings audio preview");
            var samples = new[] { clips.Click, clips.MgShot, clips.FrostLoop, clips.Laser, clips.Trap, clips.Horn, clips.Music };
            var labels = new[] { "UI 点击", "MG 机枪", "Frost 冰墙", "Laser 激光", "Trap 陷阱", "波次号角", "背景音乐" };
            for (int i = 0; i < samples.Length; i++)
            {
                AuditionStatus = "正在试听：" + labels[i];
                settings.Refresh();
                auditionSource.Stop();
                auditionSource.transform.position = head.transform.position + head.transform.forward * 1.2f;
                auditionSource.clip = samples[i];
                SpatialAudioSourceFactory.ConfigureDistance(auditionSource, i == 0);
                auditionSource.volume = i == samples.Length - 1 ? musicVolume :
                    EffectGain(samples[i], .32f);
                auditionSource.priority = 10;
                auditionSource.Play();
                outputMeter.ResetPeak();
                int before = outputMeter.Blocks;
                yield return new WaitForSecondsRealtime(i == samples.Length - 1 ? 3 : Mathf.Min(samples[i].length, 1.2f) + .25f);
                Debug.Log($"[RoomSiege.Audio] Preview {labels[i]} peakRms={outputMeter.PeakRms:F6}, blocks={outputMeter.Blocks - before}, virtual={auditionSource.isVirtual}");
                auditionSource.Stop();
                yield return new WaitForSecondsRealtime(.2f);
            }
            AuditionStatus = null;
            audition = null;
            settings.Refresh();
        }

        private void UpdateMusic()
        {
            if (music == null || head == null) return;
            // A low ambience bed in front of the head; every source remains fully 3D/HRTF.
            music.transform.position = head.transform.position + head.transform.forward * 1.5f + head.transform.up * .35f;
            music.volume = musicVolume;
            if (!musicEnabled) { music.Stop(); musicPaused = false; return; }
            // Function pages pause combat, but must not silence their own BGM setting.
            if (audition != null || applicationPaused || Time.timeScale <= .0001f || panels.Follower.IsPaused)
            {
                if (music.isPlaying) { music.Pause(); musicPaused = true; }
                return;
            }
            if (musicPaused) { music.UnPause(); musicPaused = false; }
            if (!music.isPlaying) music.Play();
        }

        private void BindButtons()
        {
            staleButtons.Clear();
            foreach (var entry in clicks) if (entry.Key == null) staleButtons.Add(entry.Key);
            foreach (var button in staleButtons) clicks.Remove(button);
            foreach (var button in FindObjectsOfType<Button>(true))
            {
                if (clicks.ContainsKey(button)) continue;
                var captured = button;
                UnityAction callback = () =>
                {
                    // The action may close its page or disable itself before this callback runs.
                    if (captured != null)
                        Play(clips.Click, captured.transform.position, .32f, 45, true);
                };
                clicks.Add(button, callback);
                button.onClick.AddListener(callback);
                var cue = button.GetComponent<UiAudioFeedback>();
                if (cue == null) cue = button.gameObject.AddComponent<UiAudioFeedback>();
                cue.Initialize(this, button);
            }
        }

        internal void UiHover(Button button)
        {
            if (!ready || button == null || !button.interactable || Time.unscaledTime < nextUiHover) return;
            nextUiHover = Time.unscaledTime + .12f;
            Play(clips.Hover, button.transform.position, .18f, 100, true);
        }

        internal void UiUnavailable(Button button)
        {
            if (ready && button != null) Play(clips.Error, button.transform.position, .18f, 40, true);
        }

        private void OnApplicationPause(bool value) { applicationPaused = value; }

        private void OnDestroy()
        {
            ready = false;
            TowerView.AudioFired -= TowerFired;
            TowerView.AudioStopped -= TowerStopped;
            WaveHorn.PlaybackRequested -= Horn;
            WaveHorn.WarningRequested-=EntranceWarning;WaveHorn.FreezeRequested-=FreezePulse;
            WaveHorn.StopRequested -= StopHorn;
            if (waves != null)
            {
                waves.WaveStarted -= WaveStarted;
                waves.WaveFinished -= WaveFinished;
                waves.CampaignFinished -= CampaignFinished;
            }
            if (enemies != null) { enemies.Spawned -= Spawned; enemies.Leaked -= Leaked; }
            if (manager != null) manager.StateChanged -= StateChanged;
            if(manager!=null)manager.CampaignReset-=ResetCampaign;
            if (scanner != null) scanner.GridChanged -= GridChanged;
            foreach (var entry in clicks) if (entry.Key != null) entry.Key.onClick.RemoveListener(entry.Value);
            foreach (var actor in new List<EnemyActor>(actors.Keys)) RemoveEnemy(actor);
            StopBattleLoops();
            if (music != null) music.Stop();
            if (outputMeter != null) Destroy(outputMeter);
            foreach (var source in GetComponentsInChildren<AudioSource>()) source.Stop();
            if (ownedListener != null) Destroy(ownedListener);
            if (ownedContext != null) Destroy(ownedContext);
            clips?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
