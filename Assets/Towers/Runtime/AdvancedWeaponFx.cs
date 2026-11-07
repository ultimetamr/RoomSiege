using UnityEngine;
using RoomSiege.Visuals;
namespace RoomSiege.Towers
{
    // Bounded cosmetic effects: no collision or gameplay ownership.
    public sealed class AdvancedWeaponFx:MonoBehaviour
    {
        private readonly LineRenderer[] arcs=new LineRenderer[8];
        private readonly float[] ages=new float[8];
        private LineRenderer warning,shock,projectile;
        private Vector3 start,end;private float flight,elapsed,radius,shockAge=1;private int cursor;private string id;private TowerInstance tower;
        private Color Tint=>tower!=null?TowerVisuals.ColorFor(tower):RoomSiegeStyle.Gold;
        public void Build(Material material,TowerInstance instance)
        {
            tower=instance;id=tower.Definition.id;
            for(int i=0;i<arcs.Length;i++){arcs[i]=TowerVisuals.Line(transform,"Arc strand "+i,material,Tint,.009f);arcs[i].useWorldSpace=true;arcs[i].positionCount=7;arcs[i].enabled=false;ages[i]=1;}
            warning=TowerVisuals.Ring(transform,"Artillery target",material,Tint,1,.005f);warning.enabled=false;
            shock=TowerVisuals.Ring(transform,"Artillery shockwave",material,Tint,1,.018f);shock.enabled=false;
            projectile=TowerVisuals.Line(transform,"Arcing shell",material,Tint,.016f);projectile.useWorldSpace=true;projectile.positionCount=2;projectile.enabled=false;
        }
        public void Launch(Vector3 from,Vector3 to,float duration,float blastRadius)
        {start=from;end=to;flight=duration;elapsed=0;radius=blastRadius;warning.transform.position=new Vector3(to.x,transform.position.y+.025f,to.z);warning.transform.localScale=Vector3.one*radius;warning.enabled=true;projectile.enabled=true;}
        public void Impact(Vector3 from,Vector3 to)
        {
            if(id=="Mortar"){flight=0;warning.enabled=projectile.enabled=false;shockAge=0;shock.transform.position=new Vector3(to.x,transform.position.y+.035f,to.z);shock.enabled=true;return;}
            int index=cursor++%arcs.Length;ages[index]=0;var line=arcs[index];line.enabled=true;
            Vector3 side=Vector3.Cross((to-from).normalized,Vector3.up);if(side.sqrMagnitude<.01f)side=Vector3.right;
            for(int j=0;j<7;j++)line.SetPosition(j,Vector3.Lerp(from,to,j/6f)+(j==0||j==6?Vector3.zero:side*((j%2==0?1:-1)*.035f)));
        }
        private void Update()
        {
            var panels=RoomSiege.UI.WorldPanelHost.Instance;
            if(panels!=null&&(panels.Navigation.HasOpenPage||panels.Follower!=null&&panels.Follower.IsPaused))return;
            float dt=Time.deltaTime;
            for(int i=0;i<arcs.Length;i++){ages[i]+=dt;if(arcs[i]!=null){arcs[i].enabled=ages[i]<.18f;var color=Tint;color.a=Mathf.Clamp01(1-ages[i]/.18f);arcs[i].startColor=arcs[i].endColor=color;}}
            if(flight>0){elapsed+=dt;float t=Mathf.Clamp01(elapsed/flight);var point=Vector3.Lerp(start,end,t)+Vector3.up*(4*t*(1-t)*.6f);projectile.SetPosition(0,point);projectile.SetPosition(1,point-Vector3.up*.035f);}
            shockAge+=dt;if(shock!=null&&shock.enabled){shock.transform.localScale=Vector3.one*(radius*Mathf.Clamp01(shockAge/.5f));var color=Tint;color.a=Mathf.Clamp01(1-shockAge/.5f);shock.startColor=shock.endColor=color;if(shockAge>=.5f)shock.enabled=false;}
        }
    }
}
