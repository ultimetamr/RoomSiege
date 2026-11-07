using UnityEngine;
using UnityEngine.UI;
namespace RoomSiege.Meta
{
    // Resolution independent armory iconography, drawn by the world-space UI canvas.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArmoryGlyph:Image
    {
        public string Id;
        public override Texture mainTexture=>Texture2D.whiteTexture;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();float scale=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f;
            if(Id=="freeze")
            {for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Vector2 v=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(mesh,Vector2.zero,v*.85f,scale);Vector2 tip=v*.54f,side=new Vector2(-v.y,v.x);Line(mesh,tip,tip-v*.21f+side*.2f,scale);Line(mesh,tip,tip-v*.21f-side*.2f,scale);}}
            else if(Id=="repair"){Line(mesh,new Vector2(-.65f,0),new Vector2(.65f,0),scale,6);Line(mesh,new Vector2(0,-.65f),new Vector2(0,.65f),scale,6);Ring(mesh,.91f,scale);}
            else if(Id=="emp"){Ring(mesh,.84f,scale);Ring(mesh,.5f,scale);Line(mesh,new Vector2(.1f,.9f),new Vector2(-.25f,-.05f),scale);Line(mesh,new Vector2(-.25f,-.05f),new Vector2(.25f,.15f),scale);Line(mesh,new Vector2(.25f,.15f),new Vector2(-.1f,-.9f),scale);}
            else if(Id=="overload"){Line(mesh,new Vector2(.2f,.9f),new Vector2(-.5f,-.05f),scale,5);Line(mesh,new Vector2(-.5f,-.05f),new Vector2(.5f,.05f),scale,5);Line(mesh,new Vector2(.5f,.05f),new Vector2(-.2f,-.9f),scale,5);}
            else if(Id=="decoy"){Ring(mesh,.28f,scale);for(int i=0;i<3;i++){float x=.43f+i*.17f;Line(mesh,new Vector2(x,-.25f-i*.1f),new Vector2(x,.25f+i*.1f),scale);Line(mesh,new Vector2(-x,-.25f-i*.1f),new Vector2(-x,.25f+i*.1f),scale);}}
            else if(Id=="firefield"){Polygon(mesh,3,.9f,scale);Vector2[] fire={new Vector2(-.35f,-.4f),new Vector2(-.45f,.1f),new Vector2(-.1f,.65f),new Vector2(.08f,.03f),new Vector2(.3f,.35f),new Vector2(.4f,-.4f)};for(int i=0;i<fire.Length-1;i++)Line(mesh,fire[i],fire[i+1],scale);}
            else if(Id=="gravity"){Ring(mesh,.9f,scale);Ring(mesh,.4f,scale);for(int i=0;i<4;i++){float a=i*Mathf.PI/2;Vector2 v=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),s=new Vector2(-v.y,v.x);Line(mesh,v*.8f,v*.48f,scale);Line(mesh,v*.48f,v*.65f+s*.12f,scale);Line(mesh,v*.48f,v*.65f-s*.12f,scale);}}
            else if(Id=="mine"){Polygon(mesh,4,.65f,scale);Ring(mesh,.2f,scale);for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Vector2 v=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(mesh,v*.7f,v*.95f,scale);}}
            else if(Id=="thunder"){Polygon(mesh,6,.9f,scale);Line(mesh,new Vector2(.2f,.65f),new Vector2(-.25f,-.05f),scale,3);Line(mesh,new Vector2(-.25f,-.05f),new Vector2(.25f,.1f),scale,3);Line(mesh,new Vector2(.25f,.1f),new Vector2(-.15f,-.7f),scale,3);}
            else if(Id=="corrosion"){Vector2[] shield={new Vector2(-.6f,.7f),new Vector2(.6f,.7f),new Vector2(.55f,-.15f),new Vector2(0,-.8f),new Vector2(-.55f,-.15f),new Vector2(-.6f,.7f)};for(int i=0;i<shield.Length-1;i++)Line(mesh,shield[i],shield[i+1],scale);Line(mesh,new Vector2(.2f,.5f),new Vector2(-.2f,-.05f),scale);Line(mesh,new Vector2(-.2f,-.05f),new Vector2(.2f,-.15f),scale);Line(mesh,new Vector2(.2f,-.15f),new Vector2(-.15f,-.55f),scale);}
            else if(Id=="frostsigil"){Ring(mesh,.92f,scale);Polygon(mesh,3,.65f,scale);Polygon(mesh,3,.65f,scale,Mathf.PI);Ring(mesh,.17f,scale);}
            else {Ring(mesh,.68f,scale);Ring(mesh,.24f,scale);Line(mesh,new Vector2(-1,0),new Vector2(-.45f,0),scale);Line(mesh,new Vector2(.45f,0),new Vector2(1,0),scale);Line(mesh,new Vector2(0,-1),new Vector2(0,-.45f),scale);Line(mesh,new Vector2(0,.45f),new Vector2(0,1),scale);}
        }
        private void Ring(VertexHelper mesh,float radius,float scale){for(int i=0;i<40;i++){float a=i*Mathf.PI*2/40,b=(i+1)*Mathf.PI*2/40;Line(mesh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,scale);}}
        private void Polygon(VertexHelper mesh,int sides,float radius,float scale,float offset=0){for(int i=0;i<sides;i++){float a=offset+i*Mathf.PI*2/sides,b=offset+(i+1)*Mathf.PI*2/sides;Line(mesh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,scale);}}
        private void Line(VertexHelper mesh,Vector2 a,Vector2 b,float scale,float width=2)
        {a*=scale;b*=scale;Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int i=mesh.currentVertCount;mesh.AddVert(a-n,color,new Vector2(.5f,.5f));mesh.AddVert(a+n,color,new Vector2(.5f,.5f));mesh.AddVert(b+n,color,new Vector2(.5f,.5f));mesh.AddVert(b-n,color,new Vector2(.5f,.5f));mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);}
    }
}
