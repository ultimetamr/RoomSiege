using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Waves
{
    public sealed class WaveTacticalVisual:MonoBehaviour
    {
        private Material material;
        private LineRenderer pulse;
        private float age=10;
        public void Pulse(Vector3 center)
        {
            if(material==null)material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            if(pulse==null)pulse=TowerVisuals.Ring(transform,"Emergency freeze pulse",material,RoomSiegeStyle.Frost,1,.018f);
            pulse.transform.position=center+Vector3.up*.03f;pulse.gameObject.SetActive(true);age=0;
        }
        private void Update()
        {
            if(pulse==null||!pulse.gameObject.activeSelf)return;
            var waves=WaveController.Instance;if(waves==null||waves.IsPaused)return;
            age+=Time.deltaTime;float size=Mathf.Lerp(.1f,2,Mathf.Clamp01(age/.6f));pulse.transform.localScale=Vector3.one*size;
            var color=RoomSiegeStyle.Alpha(RoomSiegeStyle.Frost,Mathf.Clamp01(1-age/1.2f));pulse.startColor=pulse.endColor=color;
            if(age>=1.2f)pulse.gameObject.SetActive(false);
        }
        public void Clear(){if(pulse!=null)pulse.gameObject.SetActive(false);age=10;}
        private void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
