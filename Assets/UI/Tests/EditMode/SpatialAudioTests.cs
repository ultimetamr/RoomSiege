using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PXR_Audio.Spatializer;
using RoomSiege.Audio;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace RoomSiege.UI.Tests
{
    public sealed class SpatialAudioTests
    {
        [Test]
        public void ContextUsesExplicitSdkInitialization()
        {
            var prefab = Resources.Load<GameObject>("RoomSiegePicoAudioContext");
            Assert.NotNull(prefab);
            Assert.AreEqual(SpatializerApiImpl.unity, prefab.GetComponent<PXR_Audio_Spatializer_Context>().spatializerApiImpl);
        }

        [Test]
        public void SubmittedPcmGainHonorsVolumeDistanceAndMute()
        {
            var go = new GameObject("gain test");
            var head = new GameObject("listener position");
            go.SetActive(false);
            try
            {
                var audio = go.AddComponent<AudioSource>();
                audio.maxDistance = 10;
                audio.volume = .4f;
                audio.rolloffMode = AudioRolloffMode.Custom;
                audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0, 1, 1, 0));
                var spatial = go.AddComponent<PXR_Audio_Spatializer_AudioSource>();
                spatial.SetMaxAttenuationRange(10);
                var sync = go.AddComponent<SpatialSourceGain>();
                typeof(SpatialSourceGain).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(sync, new object[] { audio, spatial, head.transform });
                Assert.AreEqual(.4f, Mathf.Pow(10, spatial.GetGainDB() / 20), .0001f);
                go.transform.position = Vector3.right * 5;
                var tick = typeof(SpatialSourceGain).GetMethod("Synchronize", BindingFlags.Instance | BindingFlags.NonPublic);
                tick.Invoke(sync, null);
                Assert.AreEqual(.2f, Mathf.Pow(10, spatial.GetGainDB() / 20), .0001f);
                audio.mute = true;
                tick.Invoke(sync, null);
                Assert.AreEqual(0, Mathf.Pow(10, spatial.GetGainDB() / 20));
                audio.mute = false;
                audio.volume = .1f;
                tick.Invoke(sync, null);
                Assert.AreEqual(.05f, Mathf.Pow(10, spatial.GetGainDB() / 20), .0001f);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(head); }
        }

        [UnityTest]
        public IEnumerator RuntimeRegistersRealHrtfSources()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");
            yield return new EnterPlayMode();
            float until = Time.realtimeSinceStartup + 20;
            var readyField = typeof(AudioDirector).GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic);
            while ((AudioDirector.Instance == null || !(bool)readyField.GetValue(AudioDirector.Instance)) && Time.realtimeSinceStartup < until)
                yield return null;
            string failure = null;
            try
            {
                Assert.NotNull(AudioDirector.Instance);
                Assert.IsTrue((bool)readyField.GetValue(AudioDirector.Instance), "Audio startup must complete");
                var context = PXR_Audio_Spatializer_Context.Instance;
                Assert.NotNull(context);
                Assert.IsTrue(context.Initialized);
                Assert.AreEqual(SpatializerApiImpl.unity, context.spatializerApiImpl);
                var sources = Object.FindObjectsOfType<PXR_Audio_Spatializer_AudioSource>();
                Assert.GreaterOrEqual(sources.Length, 41);
                var idField = typeof(PXR_Audio_Spatializer_AudioSource).GetField("sourceId", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var spatial in sources)
                {
                    Assert.GreaterOrEqual((int)idField.GetValue(spatial), 0, "SDK must allocate a real HRTF source");
                    Assert.IsFalse(spatial.GetComponent<AudioSource>().spatialize, "Do not route managed PCM through the native backend again");
                    Assert.AreEqual(1, spatial.GetComponent<AudioSource>().spatialBlend);
                }
            }
            catch (System.Exception error) { failure = error.ToString(); }
            if (failure == null)
            {
                // Source registration alone does not prove that the listener emits sound.
                var owner = AudioDirector.Instance;
                var meter = Camera.main.GetComponent<AudioOutputMeter>();
                bool oldMusic = owner.MusicEnabled;
                owner.SetMusicEnabled(false);
                var bank = typeof(AudioDirector).GetField("clips", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
                var play = typeof(AudioDirector).GetMethod("Play", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (string cue in new[] { "Click", "Hover", "MgShot", "Frost", "FrostLoop", "Laser", "Trap", "Horn", "Music" })
                {
                    yield return new WaitForSecondsRealtime(.2f);
                    meter.ResetPeak();
                    int before = meter.Blocks;
                    var clip = (AudioClip)bank.GetType().GetField(cue).GetValue(bank);
                    play.Invoke(owner, new object[] { clip, Camera.main.transform.position + Camera.main.transform.forward * 1.2f, .32f, 10, true });
                    yield return new WaitForSecondsRealtime(Mathf.Min(clip.length + .2f, 1.4f));
                    float minimumRms = cue == "Click" || cue == "Hover" ? .003f : .01f;
                    if (meter.Blocks <= before || meter.PeakRms < minimumRms)
                        failure = $"{cue} has no binaural PCM output: blocks={meter.Blocks - before}, rms={meter.PeakRms}";
                    if ((cue == "Frost" || cue == "FrostLoop") && meter.PeakRms > .04f)
                        failure = $"{cue} exceeds the softened mix level: rms={meter.PeakRms}";
                    Debug.Log($"[RoomSiege.AudioTest] {cue} peakRms={meter.PeakRms:F6}");
                    // Stop the long music sample before checking the next path.
                    foreach (var audio in owner.GetComponentsInChildren<AudioSource>()) audio.Stop();
                    if (failure != null) break;
                }
                var settings = owner.GetComponent<AudioSettingsPanel>();
                settings.GetType().GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, null);
                owner.SetMusicEnabled(false);
                yield return null;
                meter.ResetPeak();
                owner.SetMusicEnabled(true);
                yield return new WaitForSecondsRealtime(.6f);
                var bgm = (AudioSource)typeof(AudioDirector).GetField("music", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
                if (!bgm.isPlaying || meter.PeakRms < .01f)
                    failure = "Turning BGM on from its settings page must start audible playback";
                owner.SetMusicEnabled(false);
                yield return new WaitForSecondsRealtime(.3f);
                meter.ResetPeak();
                var page = (GameObject)typeof(AudioSettingsPanel).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
                var button = page.GetComponentsInChildren<UnityEngine.UI.Button>(true)[0];
                button.onClick.Invoke();
                yield return new WaitForSecondsRealtime(.25f);
                if (meter.PeakRms < .0001f) failure = "Closing a page must retain its UI click audio";
                owner.SetMusicEnabled(oldMusic);
            }
            yield return new ExitPlayMode();
            Assert.IsNull(failure, failure);
        }
    }
}
