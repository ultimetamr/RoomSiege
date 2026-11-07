using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Towers
{
    public static class ArsenalModelGeometry
    {
        private static void Part(FacetedMesh mesh,Vector3 p,Vector3 size,Quaternion rotation,bool glow=false)
        {mesh.Box(p,size,rotation,glow?new Color(1,1,1,0):new Color(.6f,.84f,.92f,1));}
        private static void Rod(FacetedMesh mesh,Vector3 a,Vector3 b,float width,bool glow=true)
        {Part(mesh,(a+b)*.5f,new Vector3(width,width,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a),glow);}
        private static void Orbit(FacetedMesh mesh,float radius,float y,Quaternion rotation,int count=8)
        {for(int i=0;i<count;i++){float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;Rod(mesh,rotation*new Vector3(Mathf.Cos(a)*radius,y,Mathf.Sin(a)*radius),rotation*new Vector3(Mathf.Cos(b)*radius,y,Mathf.Sin(b)*radius),.006f);}}
        public static void Build(FacetedMesh mesh,string id,int level)
        {
            Part(mesh,new Vector3(0,-.105f,0),new Vector3(.13f,.025f,.13f),Quaternion.Euler(0,45,0));
            Part(mesh,new Vector3(0,-.06f,0),new Vector3(.04f,.07f,.045f),Quaternion.identity);
            switch(id)
            {
                case "Beacon":
                    Part(mesh,Vector3.zero,new Vector3(.035f,.16f,.035f),Quaternion.identity);
                    for(int i=0;i<3+level;i++){float a=i*Mathf.PI*2/(3+level);var p=new Vector3(Mathf.Cos(a)*.07f,.045f,Mathf.Sin(a)*.07f);Rod(mesh,Vector3.down*.04f,p,.009f,false);Rod(mesh,p,p+Vector3.up*.04f,.006f);}
                    Orbit(mesh,.045f,.11f,Quaternion.identity);break;
                case "Prism":
                    Part(mesh,Vector3.up*.035f,new Vector3(.055f,.055f,.055f),Quaternion.Euler(45,45,30),true);
                    for(int i=0;i<level+2;i++){var r=Quaternion.Euler(0,i*360f/(level+2),0);Rod(mesh,r*new Vector3(.035f,-.05f,0),r*new Vector3(.09f,.07f,0),.01f,false);}
                    Orbit(mesh,.09f,.02f,Quaternion.Euler(25,0,0),6);break;
                case "Cryo":
                    for(int i=0;i<3;i++)Part(mesh,new Vector3((i-1)*.055f,i==1?.03f:0,0),new Vector3(.025f,.14f+i*.012f,.035f),Quaternion.Euler(0,45,0));
                    for(int i=0;i<level+1;i++)Orbit(mesh,.065f,-.03f+i*.035f,Quaternion.identity,6);break;
                case "Sunder":
                    Part(mesh,new Vector3(0,.015f,.03f),new Vector3(.07f,.065f,.10f),Quaternion.Euler(15,0,0));
                    for(int i=0;i<level+2;i++){var r=Quaternion.Euler(0,0,i*360f/(level+2));Rod(mesh,r*Vector3.up*.028f+Vector3.forward*.09f,r*Vector3.up*.065f+Vector3.forward*.07f,.008f);}
                    Part(mesh,Vector3.forward*.09f,new Vector3(.018f,.018f,.045f),Quaternion.identity,true);break;
                case "Disruptor":
                    Part(mesh,Vector3.up*.015f,new Vector3(.025f,.17f,.025f),Quaternion.identity);
                    for(int i=0;i<level+2;i++){var r=Quaternion.Euler(0,i*360f/(level+2),0);Part(mesh,r*new Vector3(0,.04f,.065f),new Vector3(.05f,.07f,.018f),r);Rod(mesh,Vector3.up*.02f,r*new Vector3(0,.04f,.065f),.006f);}
                    Part(mesh,Vector3.up*.12f,new Vector3(.027f,.027f,.027f),Quaternion.Euler(45,0,45),true);break;
                case "Gravity":
                    Part(mesh,Vector3.up*.02f,new Vector3(.045f,.045f,.045f),Quaternion.Euler(45,45,45),true);
                    Orbit(mesh,.085f,.02f,Quaternion.identity);
                    Orbit(mesh,.085f,0,Quaternion.Euler(70,0,0));if(level>=2)Orbit(mesh,.085f,0,Quaternion.Euler(0,0,70));break;
                case "Sniper":
                    Part(mesh,new Vector3(0,0,.065f),new Vector3(.045f,.04f,.21f),Quaternion.identity);
                    Part(mesh,new Vector3(0,.045f,.025f),new Vector3(.026f,.025f,.065f),Quaternion.identity);
                    Rod(mesh,new Vector3(-.018f,0,.08f),new Vector3(-.018f,0,.18f),.006f);
                    for(int i=0;i<level;i++)Part(mesh,new Vector3(0,-.025f,.10f+i*.025f),new Vector3(.055f,.009f,.008f),Quaternion.identity,true);break;
                case "Scatter":
                    Part(mesh,Vector3.zero,new Vector3(.09f,.06f,.07f),Quaternion.identity);
                    for(int i=-1;i<=1;i++){var r=Quaternion.Euler(0,i*17,0);Part(mesh,r*new Vector3(i*.015f,.005f,.07f),new Vector3(.025f,.025f,.11f+level*.01f),r);}
                    Orbit(mesh,.035f,0,Quaternion.Euler(90,0,0),6);break;
                case "Flame":
                    for(int i=-1;i<=1;i+=2){Part(mesh,new Vector3(i*.05f,-.01f,-.025f),new Vector3(.035f,.115f,.055f),Quaternion.identity);Part(mesh,new Vector3(i*.05f,.05f,-.025f),new Vector3(.018f,.02f,.018f),Quaternion.identity,true);}
                    Part(mesh,new Vector3(0,0,.065f),new Vector3(.055f,.038f,.12f),Quaternion.identity);
                    for(int i=0;i<level+1;i++)Part(mesh,new Vector3(0,0,.07f+i*.018f),new Vector3(.065f,.045f,.008f),Quaternion.identity,true);break;
                case "Missile":
                    for(int i=0;i<3+level;i++){int row=i/3,col=i%3;Part(mesh,new Vector3((col-1)*.042f,-.005f+row*.045f,.018f),new Vector3(.032f,.032f,.115f),Quaternion.Euler(-15,0,0));Part(mesh,new Vector3((col-1)*.042f,.01f+row*.045f,.079f),new Vector3(.018f,.018f,.012f),Quaternion.identity,true);}break;
                case "Railgun":
                    for(int i=-1;i<=1;i+=2){Part(mesh,new Vector3(i*.035f,.01f,.06f),new Vector3(.02f,.045f,.23f),Quaternion.identity);Rod(mesh,new Vector3(i*.035f,.035f,-.03f),new Vector3(i*.035f,.035f,.18f),.006f);}
                    for(int i=0;i<level+1;i++)Part(mesh,new Vector3(0,0,-.025f+i*.035f),new Vector3(.07f,.015f,.012f),Quaternion.identity);break;
                case "Blade":
                    Part(mesh,Vector3.zero,new Vector3(.035f,.09f,.035f),Quaternion.identity);
                    for(int i=0;i<level+2;i++){var r=Quaternion.Euler(0,i*360f/(level+2),0);Part(mesh,r*new Vector3(.065f,.025f,.015f),new Vector3(.12f,.012f,.026f),r);Rod(mesh,r*new Vector3(.03f,.035f,.025f),r*new Vector3(.13f,.035f,.025f),.004f);}
                    Orbit(mesh,.035f,.045f,Quaternion.identity,6);break;
            }
        }
    }
}
