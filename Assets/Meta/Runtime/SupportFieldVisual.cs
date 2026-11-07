using RoomSiege.Progression;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.Meta
{
    // Thin ground glyphs and a brief bolt preserve the MR view. Driven by the gameplay clock.
    public sealed class SupportFieldVisual:MonoBehaviour
    {
        private LineRenderer inner,rune,bolt;
        private TextMesh label;
        private SupplyDefinition item;
        private Color tint;
        private float flash;
        public static Color ColorFor(SupplyDefinition definition)=>definition!=null&&ColorUtility.TryParseHtmlString(definition.color,out var c)?c:TowerVisuals.Blue;
        public void Build(SupplyDefinition definition,Material material,Vector3 point)
        {
            item=definition;tint=ColorFor(item);transform.position=point+Vector3.up*.038f;
            SupportDeviceModel.Create(transform,item);
            inner=TowerVisuals.Ring(transform,"Inner ward",material,tint,item.id=="mine"?item.triggerRadius:item.radius*.72f,.007f);
            rune=TowerVisuals.Line(transform,"Field sigil",material,tint,.009f);
            int sides=item.id=="mine"?4:item.id=="firefield"?3:item.id=="gravity"?8:6;rune.positionCount=sides;rune.loop=true;
            for(int i=0;i<sides;i++){float angle=i*Mathf.PI*2/sides;rune.SetPosition(i,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*item.radius*.53f);}
            bolt=TowerVisuals.Line(transform,"Support pulse",material,tint,.016f);bolt.useWorldSpace=true;bolt.positionCount=5;bolt.enabled=false;
            var go=new GameObject("Field lifetime");go.transform.SetParent(transform,false);go.transform.localPosition=Vector3.up*.40f;
            label=go.AddComponent<TextMesh>();label.anchor=TextAnchor.MiddleCenter;label.characterSize=.004f;label.fontSize=44;label.color=tint;
        }
        public void Pulse(Vector3 point)
        {
            flash=.22f;bolt.enabled=true;
            for(int i=0;i<5;i++)bolt.SetPosition(i,point+Vector3.up*(.6f-i*.15f)+Vector3.right*(i==0||i==4?0:(i%2==0?.07f:-.07f)));
        }
        public void Draw(SupportField field,float dt)
        {
            flash=Mathf.Max(0,flash-dt);bolt.enabled=flash>0;
            float pulse=.55f+Mathf.Sin(field.Elapsed*4)*.2f;
            float coverage=item.radius>0?field.Radius/item.radius:1;inner.transform.localScale=rune.transform.localScale=Vector3.one*coverage;
            inner.startColor=inner.endColor=new Color(tint.r,tint.g,tint.b,pulse);
            rune.transform.localRotation=Quaternion.Euler(0,field.Elapsed*(item.id=="gravity"?-25:10),0);
            label.gameObject.SetActive(!field.Finished);label.text=item.name+" · "+(item.id=="mine"&&!field.Armed?"布防中":Mathf.CeilToInt(field.Remaining)+"s");
            var camera=Camera.main;if(camera!=null){var away=label.transform.position-camera.transform.position;if(away.sqrMagnitude>.001f)label.transform.rotation=Quaternion.LookRotation(away);}
        }
    }
}
