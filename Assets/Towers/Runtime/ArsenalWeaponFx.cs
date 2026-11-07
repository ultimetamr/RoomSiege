using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Towers
{
    public sealed class ArsenalWeaponFx:MonoBehaviour
    {
        private readonly List<ITowerTarget> targets=new List<ITowerTarget>();
        private readonly LineRenderer[] rays=new LineRenderer[8];
        private LineRenderer ring,projectile;
        private TowerInstance tower;
        private float flash,flight,totalFlight;
        private Vector3 start,end;
        private ITowerTarget follow;private int followId;
        public void Build(TowerInstance instance,Material material)
        {
            tower=instance;var color=TowerVisuals.ColorFor(tower);
            for(int i=0;i<rays.Length;i++){rays[i]=TowerVisuals.Line(transform,"Arsenal effect "+i,material,color,.006f);rays[i].useWorldSpace=true;rays[i].positionCount=2;rays[i].enabled=false;}
            ring=TowerVisuals.Ring(transform,"Arsenal pulse",material,color,1,.004f);ring.enabled=false;
            projectile=TowerVisuals.Line(transform,"Homing missile trace",material,color,.018f);projectile.useWorldSpace=true;projectile.positionCount=2;projectile.enabled=false;
        }
        public void Launch(Vector3 from,Vector3 to,float duration,float radius)
        {
            start=from;end=to;flight=totalFlight=duration;follow=null;targets.Clear();TowerTargetRegistry.AppendAliveTo(targets);
            foreach(var target in targets)if((target.Position-to).sqrMagnitude<.01f){follow=target;followId=target.TargetId;break;}
        }
        public void Fire(Vector3 from,Vector3 to,bool continuous)
        {
            flash=continuous?.09f:.22f;string id=tower.Definition.id;
            if(id=="Blade"||RoomSiege.Data.WeaponRoster.IsSupport(id)||id=="Missile")
            {
                ring.transform.position=(id=="Missile"?to:tower.Cell.Center)+Vector3.up*.04f;
                ring.transform.localScale=Vector3.one*(id=="Missile"?tower.Stats.splashRadiusMeters.value*(tower.Skin?.effect=="missileBlast"?1.2f:1):tower.Range);ring.enabled=true;return;
            }
            if(id=="Scatter"||id=="Flame")
            {
                Vector3 forward=to-from;forward.y=0;forward=forward.normalized;
                for(int i=0;i<5;i++){rays[i].SetPosition(0,from);rays[i].SetPosition(1,from+Quaternion.Euler(0,(i-2)*(id=="Scatter"?tower.Skin?.effect=="scatterCone"?20:15:17.5f),0)*forward*tower.Range);rays[i].enabled=true;}
            }
            else {rays[0].SetPosition(0,from);rays[0].SetPosition(1,to);rays[0].enabled=true;}
        }
        public void Tick(float dt)
        {
            if(flight>0)
            {
                if(follow!=null&&follow.TargetId==followId&&follow.IsAlive)end=follow.Position;
                flight=Mathf.Max(0,flight-dt);float t=1-flight/Mathf.Max(.01f,totalFlight);var p=Vector3.Lerp(start,end,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.3f;
                projectile.SetPosition(0,p);projectile.SetPosition(1,p-((end-start).normalized)*.06f);projectile.enabled=flight>0;
            }
            flash=Mathf.Max(0,flash-dt);
            if(flash<=0){ring.enabled=false;foreach(var line in rays)line.enabled=false;}
        }
    }
}
