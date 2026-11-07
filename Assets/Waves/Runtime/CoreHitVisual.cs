using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Waves
{
    // A single soft ground outline at the displayed core; never a fullscreen red flash.
    public sealed class CoreHitVisual : MonoBehaviour
    {
        private LineRenderer ring;
        private Material material;
        private float remaining;
        private ScanVisualization scanVisual;
        public bool Visible=>ring!=null&&ring.enabled;

        public void Pulse()
        {
            var scanner=ScanController.Instance;
            if(scanner!=null)scanner.GetComponentInChildren<GuardCoreVisual>(true)?.PulseDamage();
            if(ring==null)
            {
                material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
                ring=TowerVisuals.Ring(transform,"Core hit outline",material,RoomSiegeStyle.Enemy,GridModel.SafeRadius,.025f);
            }
            remaining=1.1f;ring.enabled=true;
        }
        private void LateUpdate()
        {
            if(ring==null)return;
            var waves=WaveController.Instance;var scanner=ScanController.Instance;var panels=RoomSiege.UI.WorldPanelHost.Instance;
            bool hidden=panels!=null&&panels.Navigation!=null&&panels.Navigation.HasOpenPage;
            if(waves==null||scanner==null||scanner.CurrentGrid==null||remaining<=0||hidden){ring.enabled=false;return;}
            if(scanVisual==null)scanVisual=scanner.GetComponent<ScanVisualization>();
            Vector3 center=scanVisual!=null?scanVisual.VisualCoreCenter:scanner.CurrentGrid.SafeZoneCenter;
            ring.transform.position=center+Vector3.up*.028f;
            if(!waves.IsPaused)remaining=Mathf.Max(0,remaining-Time.deltaTime);
            ring.enabled=remaining>0;
            ring.startColor=ring.endColor=RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy,.65f*Mathf.Clamp01(remaining/.45f));
        }
        public void Clear(){remaining=0;if(ring!=null)ring.enabled=false;}
        private void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
