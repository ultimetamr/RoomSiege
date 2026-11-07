using UnityEngine;
using System.Collections.Generic;
using RoomSiege.Visuals;

namespace RoomSiege.Towers
{
    public static class TowerVisuals
    {
        public static bool GoldSkinEnabled { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSkin() => GoldSkinEnabled = false;
        public static readonly Color Blue = RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly, .85f);
        public static readonly Color Red = RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy, .85f);
        public static void SetGoldSkin(bool enabled)
        {
            GoldSkinEnabled = enabled;
            foreach(var view in Object.FindObjectsOfType<TowerView>(true)) if(view!=null) view.Refresh();
        }
        public static Color ColorFor(string id) => GoldSkinEnabled ? RoomSiegeStyle.Alpha(RoomSiegeStyle.Gold,.95f) :
            RoomSiege.Data.WeaponRoster.Tint(id);
        public static Color ColorFor(TowerInstance tower)=>tower.Skin!=null&&ColorUtility.TryParseHtmlString(tower.Skin.color,out var color)?color:ColorFor(tower.Definition.id);
        public static LineRenderer Line(Transform parent, string name, Material material, Color color, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
            line.startColor = line.endColor = color; line.startWidth = line.endWidth = width;
            line.useWorldSpace = false; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; return line;
        }
        public static LineRenderer Ring(Transform parent, string name, Material material, Color color, float radius, float width = 0.007f)
        {
            var line = Line(parent, name, material, color, width); line.loop = true; line.positionCount = 48;
            for (int i=0;i<48;i++) { float a=i*Mathf.PI*2/48; line.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius)); }
            return line;
        }
    }

    public sealed class TowerView : MonoBehaviour
    {
        public static event System.Action<TowerView,Vector3,bool> AudioFired;
        public static event System.Action<TowerView> AudioStopped;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAudioEvents(){AudioFired=null;AudioStopped=null;}
        public TowerInstance Tower { get; private set; }
        private Transform turret,skinSignature;
        private LineRenderer shot;
        private readonly List<LineRenderer> details = new List<LineRenderer>();
        private LineRenderer[] levels;
        private float shotUntil,lastAdvancedAudio=-10;
        private bool beam;
        private MachineGunModel machineGun;
        private HoloTowerModel holoModel;
        private TrapExplosionVisuals trapExplosions;
        private AdvancedWeaponFx advancedFx;
        private ArsenalWeaponFx arsenalFx;
        private LineRenderer skinCrown,powerRing;
        public void Build(TowerInstance tower, Material material)
        {
            Tower = tower; transform.position = tower.Cell.Center;
            trapExplosions=GetComponentInParent<TrapExplosionVisuals>();
            Color color = TowerVisuals.ColorFor(tower);
            levels = new LineRenderer[3];
            for (int i=0;i<3;i++)
            {
                levels[i]=TowerVisuals.Ring(transform,"Level "+(i+1),material,color,0.11f-i*0.018f);
                levels[i].transform.localPosition=Vector3.up*(0.015f+i*0.028f);
            }
            var top=new GameObject("Turret"); top.transform.SetParent(transform,false); turret=top.transform;
            turret.localPosition=Vector3.up*(tower.Definition.singleUse?0.035f:0.16f);
            BuildWireframe(tower.Definition.id,material,color);
            var collider=gameObject.AddComponent<BoxCollider>(); collider.center=new Vector3(0,.12f,0); collider.size=new Vector3(.28f,.26f,.28f);
            shot=TowerVisuals.Line(transform,"Holographic fire",material,color,.006f); shot.useWorldSpace=true; shot.positionCount=2; shot.enabled=false;
            if(tower.Skin!=null)
            {
                skinCrown=TowerVisuals.Ring(transform,"Skin circuit "+tower.Skin.rarity,material,color,.145f,.007f);
                skinCrown.positionCount=tower.Skin.rarity=="传说"?12:tower.Skin.rarity=="史诗"?6:4;
                for(int i=0;i<skinCrown.positionCount;i++){float a=Mathf.PI*2*i/skinCrown.positionCount;skinCrown.SetPosition(i,new Vector3(Mathf.Cos(a)*.145f,.012f,Mathf.Sin(a)*.145f));}
                BuildSkinSignature(material,color);
            }
            powerRing=TowerVisuals.Ring(transform,"Enchantment and overload",material,TowerVisuals.Blue,.16f,.006f);powerRing.enabled=false;
            if(tower.Definition.id=="Tesla"||tower.Definition.id=="Mortar"){advancedFx=gameObject.AddComponent<AdvancedWeaponFx>();advancedFx.Build(material,tower);}
            if(RoomSiege.Data.WeaponRoster.IsNew(tower.Definition.id)){arsenalFx=gameObject.AddComponent<ArsenalWeaponFx>();arsenalFx.Build(tower,material);}
            Refresh();
        }
        public void Launch(Vector3 start,Vector3 end,float duration,float radius){advancedFx?.Launch(start,end,duration,radius);arsenalFx?.Launch(start,end,duration,radius);}
        public void Refresh()
        {
            if (Tower == null) return;
            transform.position=Tower.Cell.Center;
            Color color=TowerVisuals.ColorFor(Tower);
            foreach(var level in levels) { level.startColor=color; level.endColor=color; }
            foreach(var detail in details) { detail.startColor=color; detail.endColor=color; }
            if(shot!=null) { shot.startColor=color; shot.endColor=color; }
            for(int i=0;i<3;i++) levels[i].enabled=i<Tower.Level;
            if(machineGun!=null)machineGun.SetAppearance(Tower.Level,color);
            if(holoModel!=null)holoModel.SetAppearance(Tower.Level,color);
        }

        private LineRenderer Path(string name,Material material,Color color,Vector3[] points,bool loop=false)
        {
            var line=TowerVisuals.Line(turret,name,material,color,.009f);
            line.positionCount=points.Length;line.SetPositions(points);line.loop=loop;
            details.Add(line);return line;
        }

        private void BuildSkinSignature(Material material,Color color)
        {
            var go=new GameObject("Skin signature "+Tower.Skin.rarity);go.transform.SetParent(transform,false);skinSignature=go.transform;
            int rank=Tower.Skin.rarity=="传说"?3:Tower.Skin.rarity=="史诗"?2:1;
            int count=rank==3?6:rank==2?4:2;
            float height=Tower.Definition.singleUse?.045f:.13f;
            for(int i=0;i<count;i++)
            {
                float a=Mathf.PI*2*i/count;var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var line=TowerVisuals.Line(skinSignature,"Skin fin "+i,material,color,.0035f);
                line.positionCount=4;line.SetPositions(new[]{radial*.11f+Vector3.up*.025f,radial*.145f+Vector3.up*height,radial*.125f+Vector3.up*(height+.025f),radial*.11f+Vector3.up*.025f});details.Add(line);
            }
            if(rank>=2)
            {
                var orbit=TowerVisuals.Ring(skinSignature,"Skin orbit",material,color,.15f,.003f);orbit.transform.localPosition=Vector3.up*(height*.6f);details.Add(orbit);
                if(rank==3){var halo=TowerVisuals.Ring(skinSignature,"Legend halo",material,color,.11f,.0035f);halo.transform.localPosition=Vector3.up*(height+.025f);halo.transform.localRotation=Quaternion.Euler(18,0,0);details.Add(halo);}
            }
        }

        private void BuildWireframe(string id,Material material,Color color)
        {
            if(id=="MG")
            {
                machineGun=turret.gameObject.AddComponent<MachineGunModel>();machineGun.Build(transform,material);return;
            }
            holoModel=turret.gameObject.AddComponent<HoloTowerModel>();holoModel.Build(id,material);
            // Sparse emissive edges reinforce each silhouette without hiding the real room.
            if(RoomSiege.Data.WeaponRoster.IsNew(id))return;
            if(id=="Frost")
            {
                const int sides=6;
                var bottom=new Vector3[sides];var top=new Vector3[sides];
                for(int i=0;i<sides;i++)
                {
                    float a=i*Mathf.PI*2/sides;
                    bottom[i]=new Vector3(Mathf.Cos(a)*.072f,-.105f,Mathf.Sin(a)*.072f);
                    top[i]=new Vector3(Mathf.Cos(a)*.047f,.085f,Mathf.Sin(a)*.047f);
                    Path("Ice prism edge "+i,material,color,new[]{bottom[i],top[i]});
                }
                Path("Ice prism foot",material,color,bottom,true);
                Path("Ice prism crown",material,color,top,true);
            }
            else if(id=="Laser")
            {
                var waist=new[]{new Vector3(-.065f,0,0),new Vector3(0,0,.065f),new Vector3(.065f,0,0),new Vector3(0,0,-.065f)};
                Path("Laser crystal waist",material,color,waist,true);
                for(int i=0;i<waist.Length;i++)
                    Path("Laser crystal facet "+i,material,color,new[]{Vector3.up*.085f,waist[i],Vector3.down*.09f});
                var lens=TowerVisuals.Ring(turret,"Laser aperture",material,color,.038f,.008f);
                lens.transform.localPosition=Vector3.forward*.12f;lens.transform.localRotation=Quaternion.Euler(90,0,0);
                details.Add(lens);
                Path("Laser optical rail",material,color,new[]{Vector3.zero,Vector3.forward*.14f});
            }
            else if(id=="Tesla")
            {
                for(int i=0;i<6;i++){float a=i*Mathf.PI*2/6;var p=new Vector3(Mathf.Cos(a)*.10f,0,Mathf.Sin(a)*.10f);Path("Tesla arc "+i,material,color,new[]{Vector3.up*.1f,p});}
                Path("Tesla crown",material,color,new[]{Vector3.forward*.07f,Vector3.right*.07f,Vector3.back*.07f,Vector3.left*.07f},true);
            }
            else if(id=="Mortar")
            {
                Path("Mortar frame",material,color,new[]{new Vector3(-.09f,-.09f,-.07f),new Vector3(.09f,-.09f,-.07f),new Vector3(.09f,.08f,.02f),new Vector3(-.09f,.08f,.02f),new Vector3(-.09f,-.09f,-.07f)});
                Path("Mortar barrel",material,color,new[]{Vector3.up*.02f,Vector3.up*.12f+Vector3.forward*.1f});
            }
            else if(id=="Trap")
            {
                Path("Trap diamond",material,color,new[]{new Vector3(-.105f,0,0),new Vector3(0,0,.105f),new Vector3(.105f,0,0),new Vector3(0,0,-.105f)},true);
                Path("Trap cross A",material,color,new[]{new Vector3(-.07f,.01f,-.07f),new Vector3(.07f,.01f,.07f)});
                Path("Trap cross B",material,color,new[]{new Vector3(-.07f,.01f,.07f),new Vector3(.07f,.01f,-.07f)});
                var trigger=TowerVisuals.Ring(turret,"Trap trigger",material,color,.037f,.008f);details.Add(trigger);
            }
            else
            {
                Vector3 a=new Vector3(-.055f,-.035f,-.045f),b=new Vector3(.055f,-.035f,-.045f),
                    c=new Vector3(.055f,-.035f,.065f),d=new Vector3(-.055f,-.035f,.065f);
                Vector3 lift=Vector3.up*.07f;
                Path("MG receiver",material,color,new[]{a,b,c,d,a,a+lift,b+lift,b,b+lift,c+lift,c,c+lift,d+lift,d,d+lift,a+lift});
                for(int side=-1;side<=1;side+=2)
                    Path("MG barrel rail "+side,material,color,new[]{new Vector3(side*.025f,0,.035f),new Vector3(side*.025f,0,.145f)});
                Path("MG support",material,color,new[]{new Vector3(-.07f,-.11f,0),new Vector3(0,-.025f,0),new Vector3(.07f,-.11f,0)});
            }
        }
        public void Aim(ITowerTarget target, float dt)
        {
            if(target==null || !target.IsAlive)return;
            Vector3 direction=target.Position-turret.position;
            if(direction.sqrMagnitude>.00001f)turret.rotation=Quaternion.RotateTowards(turret.rotation,Quaternion.LookRotation(direction),360*dt);
        }
        public void Fire(Vector3 start,Vector3 end,bool continuous)
        {
            if(arsenalFx!=null){arsenalFx.Fire(start,end,continuous);if(continuous||Time.time-lastAdvancedAudio>.08f){lastAdvancedAudio=Time.time;AudioFired?.Invoke(this,Tower.Cell.Center+Vector3.up*.16f,continuous);}return;}
            if(advancedFx!=null)
            {advancedFx.Impact(start,end);if(Time.time-lastAdvancedAudio>.05f){lastAdvancedAudio=Time.time;AudioFired?.Invoke(this,Tower.Definition.id=="Mortar"?end:Tower.Cell.Center+Vector3.up*.16f,false);}return;}
            if(Tower!=null&&Tower.Definition.id=="Trap")
            {
                if(trapExplosions!=null)trapExplosions.Emit(Tower.Cell.Center,Tower.Level);
                AudioFired?.Invoke(this,Tower.Cell.Center+Vector3.up*.035f,false);
                return;
            }
            if(machineGun!=null){machineGun.Fire();start=machineGun.MuzzlePosition;}
            bool drawBeam=Tower==null||Tower.Definition.id!="Frost";
            if(drawBeam){shot.SetPosition(0,start);shot.SetPosition(1,end);shot.enabled=true;}
            beam=continuous&&drawBeam;shotUntil=Time.time+(continuous?.06f:.1f); AudioFired?.Invoke(this,start,continuous);
        }
        public void StopFire() { if(shot!=null && beam) shot.enabled=false; AudioStopped?.Invoke(this); }
        private void Update()
        {
            var panels=RoomSiege.UI.WorldPanelHost.Instance;
            bool paused=panels!=null&&(panels.Follower!=null&&panels.Follower.IsPaused||panels.Navigation.HasOpenPage);
            if(Tower!=null&&powerRing!=null)
            {
                powerRing.enabled=Tower.FrostEnchanted||Tower.OverloadRemaining>0||Tower.AuraRateBonus>0||Tower.AuraRangeBonus>0;
                Color glow=Tower.FrostEnchanted?new Color(.48f,.84f,1):RoomSiegeStyle.Gold;glow.a=.65f+.2f*Mathf.Sin(Time.unscaledTime*4);
                powerRing.startColor=powerRing.endColor=glow;
                if(shot!=null&&Tower.FrostEnchanted)shot.startColor=shot.endColor=glow;
            }
            if(skinCrown!=null)skinCrown.transform.localRotation=Quaternion.Euler(0,Time.unscaledTime*12,0);
            if(skinSignature!=null)skinSignature.localRotation=Quaternion.Euler(0,-Time.unscaledTime*8,0);
            if(paused)return;
            if(machineGun!=null)machineGun.Tick(Time.deltaTime);
            arsenalFx?.Tick(Time.deltaTime);
            if(shot!=null && Time.time>shotUntil)shot.enabled=false;
        }
    }
}
