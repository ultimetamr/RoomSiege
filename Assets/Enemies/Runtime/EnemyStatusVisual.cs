using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Enemies
{
    // Fixed geometry is created once per pooled actor. No floating damage-number spam.
    [DefaultExecutionOrder(11300)]
    public sealed class EnemyStatusVisual : MonoBehaviour
    {
        private EnemyActor actor;
        private Transform billboard;
        private GameObject root;
        private LineRenderer health,healthTrack,shield,shieldTrack,breakRing;
        private TextMesh caption,roleCaption;
        private float width,previousShield,breakRemaining;
        private bool focused;
        private string previousCaption;
        public float HealthFraction {get;private set;}
        public float ShieldFraction {get;private set;}
        public string StatusCaption=>caption!=null?caption.text:"";
        public bool ShieldBreakVisible=>breakRing!=null&&breakRing.enabled;

        internal void Build(EnemyActor owner,Material material)
        {
            actor=owner;
            root=new GameObject("Enemy compact status");root.transform.SetParent(transform,false);billboard=root.transform;
            healthTrack=Bar("Health track",material,new Color(1,1,1,.16f),.014f,0);
            health=Bar("Health remaining",material,RoomSiegeStyle.Enemy,.012f,0);
            shieldTrack=Bar("Shield track",material,new Color(1,1,1,.16f),.009f,.025f);
            shield=Bar("Shield remaining",material,RoomSiegeStyle.Frost,.007f,.025f);
            var label=new GameObject("Enemy state caption");label.transform.SetParent(billboard,false);
            label.transform.localPosition=Vector3.up*.063f;
            caption=label.AddComponent<TextMesh>();caption.anchor=TextAnchor.LowerCenter;
            caption.fontSize=48;caption.characterSize=.0065f;caption.color=RoomSiegeStyle.Text;
            var role=new GameObject("Enemy role caption");role.transform.SetParent(billboard,false);
            role.transform.localPosition=Vector3.up*.098f;
            roleCaption=role.AddComponent<TextMesh>();roleCaption.anchor=TextAnchor.LowerCenter;
            roleCaption.fontSize=38;roleCaption.characterSize=.0052f;roleCaption.color=RoomSiegeStyle.Text;
            breakRing=TowerVisuals.Ring(transform,"Shield break ripple",material,RoomSiegeStyle.Gold,.2f,.008f);
            breakRing.enabled=false;root.SetActive(false);
        }

        private LineRenderer Bar(string name,Material material,Color color,float thickness,float y)
        {
            var line=TowerVisuals.Line(billboard,name,material,color,thickness);
            line.positionCount=2;line.transform.localPosition=new Vector3(0,y,0);return line;
        }

        internal void ResetForSpawn(float height,float radius)
        {
            width=Mathf.Clamp(radius*2.1f,.22f,.42f);
            billboard.localPosition=Vector3.up*(height+.075f);
            previousShield=actor.Shield;breakRemaining=0;focused=false;previousCaption=null;
            breakRing.transform.localPosition=Vector3.up*(height*.5f);
            breakRing.enabled=false;root.SetActive(false);
            Refresh(0);
        }

        public void SetFocused(bool value){focused=value;}

        private void LateUpdate()
        {
            if(actor==null||!actor.IsAlive){Hide();return;}
            var panels=WorldPanelHost.Instance;
            if(panels!=null&&panels.Navigation!=null&&panels.Navigation.HasOpenPage){root.SetActive(false);breakRing.enabled=false;return;}
            float dt=EnemyController.Instance!=null&&EnemyController.Instance.CanSimulate?Time.deltaTime:0;
            Refresh(dt);
        }

        public void Refresh(float dt)
        {
            if(actor==null||!actor.IsAlive){Hide();return;}
            if(previousShield>0&&actor.Shield<=0)breakRemaining=.9f;
            else breakRemaining=Mathf.Max(0,breakRemaining-Mathf.Max(0,dt));
            previousShield=actor.Shield;
            HealthFraction=EnemyReadability.Fraction(actor.HitPoints,actor.MaximumHitPoints);
            ShieldFraction=EnemyReadability.Fraction(actor.Shield,actor.MaximumShield);
            bool entering=!actor.CanBeTargeted;
            bool show=entering||focused||actor.HitPoints<actor.MaximumHitPoints||actor.Shield>0||actor.SlowFraction>0||actor.ArmorBreakFraction>0||breakRemaining>0||actor.EnemyId=="Boss"||actor.EnemyId=="Tank";
            root.SetActive(show);
            var head=Camera.main;
            if(head!=null)
            {
                Vector3 away=billboard.position-head.transform.position;
                if(away.sqrMagnitude>.0001f)billboard.rotation=Quaternion.LookRotation(away);
            }
            SetBar(healthTrack,1);SetBar(health,HealthFraction);
            health.startColor=health.endColor=HealthFraction<=.3f?RoomSiegeStyle.Gold:RoomSiegeStyle.Enemy;
            shieldTrack.enabled=shield.enabled=actor.Shield>0;
            SetBar(shieldTrack,1);SetBar(shield,ShieldFraction);
            string value=EnemyReadability.Status(HealthFraction,actor.Shield,actor.SlowFraction,breakRemaining>0,actor.SlowSourceCount);
            if(actor.IsFrozen)value="冰冻";
            else if(actor.ChillStacks>0)value+=" · 寒霜 "+actor.ChillStacks+"/5";
            else if(actor.FreezeProtection>0)value+=" · 冻结保护";
            if(actor.ArmorBreakFraction>0)value+=" · 破甲 "+actor.ArmorBreakFraction.ToString("P0");
            if(entering)value="出场中"+(string.IsNullOrEmpty(value)?"":" · "+value);
            if(value!=previousCaption){caption.text=value;previousCaption=value;}
            roleCaption.text=EnemyReadability.Role(actor.EnemyId);
            roleCaption.color=EnemyReadability.RoleColor(actor.EnemyId);
            roleCaption.gameObject.SetActive(focused||actor.EnemyId=="Tank"||actor.EnemyId=="Boss");
            caption.color=breakRemaining>0?RoomSiegeStyle.Gold:actor.SlowFraction>0?RoomSiegeStyle.Frost:RoomSiegeStyle.Text;
            breakRing.enabled=breakRemaining>0;
            if(breakRing.enabled)
            {
                breakRing.transform.localScale=Vector3.one*Mathf.Lerp(.8f,1.7f,1-breakRemaining/.9f);
                breakRing.startColor=breakRing.endColor=RoomSiegeStyle.Alpha(RoomSiegeStyle.Gold,breakRemaining/.9f);
            }
        }

        private void SetBar(LineRenderer bar,float fraction)
        {bar.SetPosition(0,new Vector3(-width*.5f,0,0));bar.SetPosition(1,new Vector3(width*(fraction-.5f),0,0));}
        internal void Hide(){if(root!=null)root.SetActive(false);if(breakRing!=null)breakRing.enabled=false;}
    }
}
