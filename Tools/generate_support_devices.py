"""Author twelve original beveled support-device meshes for Unity (no third-party assets)."""
from pathlib import Path
import math, json

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets/Meta/Runtime/Resources/SupportDevices'
OUT.mkdir(parents=True,exist_ok=True)
TAU=math.tau

class Mesh:
    def __init__(self,name): self.name=name; self.v=[]; self.f=[]
    def face(self,pts,mat):
        clean=[]
        for p in pts:
            if not clean or sum((p[i]-clean[-1][i])**2 for i in range(3))>1e-14:clean.append(p)
        if len(clean)>1 and sum((clean[0][i]-clean[-1][i])**2 for i in range(3))<1e-14:clean.pop()
        if len(clean)<3:return
        start=len(self.v)+1; self.v.extend(clean); self.f.append((mat,list(range(start,start+len(clean)))))
    def lathe(self,profile,mat='Metal',n=40,c=(0,0,0)):
        for k in range(len(profile)-1):
            r0,h0=profile[k]; r1,h1=profile[k+1]
            for i in range(n):
                a=i*TAU/n; b=(i+1)*TAU/n
                self.face([(c[0]+r0*math.cos(a),c[1]+h0,c[2]+r0*math.sin(a)),
                           (c[0]+r0*math.cos(b),c[1]+h0,c[2]+r0*math.sin(b)),
                           (c[0]+r1*math.cos(b),c[1]+h1,c[2]+r1*math.sin(b)),
                           (c[0]+r1*math.cos(a),c[1]+h1,c[2]+r1*math.sin(a))][::-1],mat)
    def box(self,c,size,mat='Metal',yaw=0,bevel=.008):
        x,y,z=size; x/=2; z/=2; b=min(bevel,x*.35,z*.35,size[1]*.3)
        p=[(-x+b,-z),(x-b,-z),(x,-z+b),(x,z-b),(x-b,z),(-x+b,z),(-x,z-b),(-x,-z+b)]
        rings=[(size[1]*-.5,b),(size[1]*-.5+b,0),(size[1]*.5-b,0),(size[1]*.5,b)]
        def pt(a,h,shrink):
            px=a[0]-math.copysign(shrink,a[0]); pz=a[1]-math.copysign(shrink,a[1]); ang=math.radians(yaw)
            return (c[0]+px*math.cos(ang)+pz*math.sin(ang),c[1]+h,c[2]-px*math.sin(ang)+pz*math.cos(ang))
        for k in range(3):
            h0,s0=rings[k];h1,s1=rings[k+1]
            for i in range(8):self.face([pt(p[i],h0,s0),pt(p[(i+1)%8],h0,s0),pt(p[(i+1)%8],h1,s1),pt(p[i],h1,s1)][::-1],mat)
        self.face([pt(a,rings[0][0],b) for a in p],mat)
        self.face([pt(a,rings[-1][0],b) for a in reversed(p)],mat)
    def tube(self,points,r=.007,mat='Trim',sides=8):
        # Shared cross-sections at bends prevent open seams between arm/coil segments.
        rings=[]
        for k,p in enumerate(points):
            a=points[max(0,k-1)];b=points[min(len(points)-1,k+1)]
            d=[b[i]-a[i] for i in range(3)];l=math.sqrt(sum(x*x for x in d))
            if l<1e-7:return
            d=[x/l for x in d]; ref=(0,1,0) if abs(d[1])<.95 else (1,0,0)
            u=[d[1]*ref[2]-d[2]*ref[1],d[2]*ref[0]-d[0]*ref[2],d[0]*ref[1]-d[1]*ref[0]]
            q=math.sqrt(sum(x*x for x in u));u=[x/q for x in u]
            v=[d[1]*u[2]-d[2]*u[1],d[2]*u[0]-d[0]*u[2],d[0]*u[1]-d[1]*u[0]]
            rings.append([tuple(p[j]+r*(math.cos(i*TAU/sides)*u[j]+math.sin(i*TAU/sides)*v[j]) for j in range(3)) for i in range(sides)])
        for k in range(len(rings)-1):
            for i in range(sides):
                j=(i+1)%sides;self.face([rings[k][i],rings[k][j],rings[k+1][j],rings[k+1][i]],mat)
        self.face(rings[0][::-1],mat);self.face(rings[-1],mat)
    def torus(self,c,R,r,mat='Trim',axis='y',n=48,m=8,tilt=0):
        def p(a,b):
            x=(R+r*math.cos(b))*math.cos(a);y=r*math.sin(b);z=(R+r*math.cos(b))*math.sin(a)
            if axis=='x':x,y=y,x
            if axis=='z':z,y=y,z
            an=math.radians(tilt);x,y=x*math.cos(an)-y*math.sin(an),x*math.sin(an)+y*math.cos(an)
            return (c[0]+x,c[1]+y,c[2]+z)
        for i in range(n):
            for j in range(m):
                pts=[p(i*TAU/n,j*TAU/m),p((i+1)*TAU/n,j*TAU/m),p((i+1)*TAU/n,(j+1)*TAU/m),p(i*TAU/n,(j+1)*TAU/m)]
                self.face(pts[::-1] if axis=='y' else pts,mat)
    def crystal(self,c,r=.045,h=.13,mat='Glass',n=6):
        self.lathe([(0,-h*.3),(r,-h*.15),(r*.85,h*.30),(0,h*.7)],mat,n,c)
    def radial(self,count,r,y,size,mat='Ceramic',phase=0):
        for i in range(count):
            a=i*TAU/count+phase;self.box((math.sin(a)*r,y,math.cos(a)*r),size,mat,math.degrees(a))
    def base(self,kind=0):
        self.lathe([(0,0),(.12,0),(.143,.012),(.147,.029),(.133,.046),(.12,.051),(0,.051)],'Metal',48)
        self.lathe([(.142,.016),(.146,.020),(.146,.024),(.142,.027)],'Glow',48)
        self.radial(8,.122,.052,(.031,.014,.019),'Trim')
        self.radial(12,.133,.033,(.018,.009,.006),'Inset',.1)
    def write(self):
        text=['# RoomSiege original authored support device','mtllib device-materials.mtl','o '+self.name]
        text += ['v %.6f %.6f %.6f'%p for p in self.v]
        text += ['s off'];last=None;tri=0
        for mat,indices in self.f:
            if mat!=last:text.append('usemtl '+mat);last=mat
            text.append('f '+' '.join(map(str,indices)));tri+=len(indices)-2
        (OUT/(self.name+'.obj')).write_text('\n'.join(text)+'\n',encoding='utf-8')
        return {'id':self.name,'vertices':len(self.v),'triangles':tri,'materials':sorted(set(m for m,_ in self.f)),'origin':'Original mesh authoring; no AI generation service output'}

def device(id):
    m=Mesh(id);m.base()
    if id=='freeze':
        m.lathe([(.09,.05),(.105,.075),(.085,.09),(.072,.18),(.055,.20),(0,.20)],'Ceramic',12)
        m.radial(12,.087,.13,(.012,.075,.034),'Trim')
        m.torus((0,.19,0),.066,.009,'Glow');m.crystal((0,.235,0),.043,.14)
        for i in range(6):
            a=i*TAU/6;m.crystal((.062*math.cos(a),.193,.062*math.sin(a)),.016,.06,n=5)
    elif id=='emp':
        m.lathe([(.065,.05),(.077,.075),(.064,.19),(.047,.225),(0,.225)],'Inset')
        for y in [.09,.12,.15,.18,.21]:m.torus((0,y,0),.078,.008,'Trim')
        m.radial(3,.10,.15,(.025,.18,.026),'Ceramic')
        m.torus((0,.23,0),.064,.011,'Glow');m.crystal((0,.22,0),.034,.075)
        pts=[(.09*math.cos(t*.2),.07+t*.0014,.09*math.sin(t*.2)) for t in range(106)]
        m.tube(pts,.003,'Glow',6)
    elif id=='overload':
        m.box((0,.145,0),(.17,.18,.125),'Ceramic',bevel=.018)
        m.box((0,.145,-.065),(.09,.115,.009),'Glow',bevel=.006)
        for x in [-.087,.087]:m.box((x,.15,0),(.016,.14,.10),'Trim')
        for y in [.09,.115,.14,.165,.19]:m.box((0,y,.066),(.13,.008,.01),'Inset')
        for x in [-.045,.045]:m.lathe([(0,.235),(.018,.235),(.018,.258),(.012,.268),(0,.268)],'Trim',12,(x,0,0))
        m.torus((0,.145,-.074),.038,.006,'Trim','z',32)
    elif id=='decoy':
        m.lathe([(.10,.05),(.105,.074),(.067,.087),(.05,.11),(0,.11)],'Ceramic',6)
        m.lathe([(.042,.10),(.038,.19),(.05,.204),(.045,.221),(0,.221)],'Trim',16)
        m.radial(3,.08,.14,(.022,.16,.036),'Ceramic')
        m.torus((0,.22,0),.047,.006,'Glow');m.crystal((0,.25,0),.031,.07)
        for y,r in [(.28,.06),(.295,.045)]:m.torus((0,y,0),r,.0025,'Glow',n=32,m=6)
    elif id=='repair':
        m.box((0,.09,0),(.18,.075,.15),'Ceramic',bevel=.013)
        m.lathe([(.047,.12),(.049,.145),(.044,.20),(.035,.215),(0,.215)],'Glass')
        m.torus((0,.205,0),.043,.006,'Glow')
        for sign in [-1,1]:
            m.tube([(sign*.082,.08,0),(sign*.13,.16,0),(sign*.105,.245,0),(sign*.056,.26,0)],.013,'Trim')
            m.box((sign*.052,.254,0),(.035,.025,.04),'Glow')
        m.box((0,.1,-.079),(.023,.046,.005),'Glow');m.box((0,.1,-.080),(.048,.02,.005),'Glow')
    elif id=='strike':
        m.lathe([(.07,.05),(.09,.07),(.08,.085),(.045,.1),(0,.1)],'Ceramic')
        for a in [0,TAU/3,TAU*2/3]:
            x,z=math.sin(a)*.08,math.cos(a)*.08
            m.tube([(x,.05,z),(x*.65,.2,z*.65),(0,.235,0)],.012,'Trim')
        m.torus((0,.24,0),.085,.014,'Ceramic','z');m.torus((0,.24,0),.063,.005,'Glow','z')
        m.lathe([(.032,.09),(.027,.23),(.035,.27),(0,.27)],'Inset',24)
        m.box((0,.24,-.012),(.095,.006,.015),'Glow');m.box((0,.24,-.012),(.006,.095,.015),'Glow')
    elif id=='firefield':
        m.lathe([(.09,.05),(.07,.075),(.05,.1),(.065,.13),(.10,.18),(.105,.20),(.083,.21),(.045,.15),(0,.15)],'Ceramic',32)
        for i in range(8):
            a=i*TAU/8;m.tube([(.055*math.cos(a),.11,.055*math.sin(a)),(.10*math.cos(a),.22,.10*math.sin(a)),(.085*math.cos(a),.26,.085*math.sin(a))],.009,'Trim')
        m.crystal((0,.2,0),.042,.1,'Glow',8)
        m.torus((0,.14,0),.064,.006,'Glow');m.radial(12,.07,.105,(.015,.035,.012),'Inset')
    elif id=='gravity':
        m.lathe([(.08,.05),(.09,.08),(.045,.11),(0,.11)],'Ceramic',12)
        m.torus((0,.215,0),.10,.013,'Trim','x');m.torus((0,.215,0),.081,.009,'Ceramic','z',tilt=35)
        m.torus((0,.215,0),.06,.006,'Glow','y')
        m.crystal((0,.215,0),.037,.08,'Glass',10)
        m.radial(3,.09,.115,(.02,.15,.03),'Metal')
    elif id=='mine':
        m.lathe([(.10,.05),(.125,.065),(.12,.088),(.09,.10),(.055,.11),(0,.11)],'Ceramic',16)
        m.torus((0,.095,0),.092,.004,'Glow')
        m.radial(8,.086,.091,(.025,.016,.04),'Trim')
        m.lathe([(.033,.105),(.035,.12),(.023,.135),(0,.135)],'Glass',24)
        for a in [0,TAU/3,TAU*2/3]:m.box((.054*math.sin(a),.113,.054*math.cos(a)),(.013,.007,.023),'Glow',math.degrees(a))
    elif id=='thunder':
        m.lathe([(.08,.05),(.064,.07),(.028,.12),(.027,.24),(.038,.25),(0,.25)],'Ceramic',16)
        pts=[(.052*math.cos(i*TAU/20),.10+i*.0014,.052*math.sin(i*TAU/20)) for i in range(106)]
        m.tube(pts,.007,'Trim');m.torus((0,.26,0),.054,.012,'Glow')
        m.radial(3,.085,.15,(.016,.18,.018),'Metal')
        m.crystal((0,.29,0),.019,.08,'Glass',6)
    elif id=='corrosion':
        m.lathe([(.075,.05),(.075,.07),(.06,.09),(.06,.21),(.07,.23),(.045,.25),(0,.25)],'Trim',32)
        m.lathe([(.061,.10),(.061,.195)],'Glass',32)
        m.radial(4,.067,.15,(.015,.135,.017),'Ceramic')
        m.torus((0,.098,0),.063,.007,'Glow');m.torus((0,.205,0),.063,.006,'Glow')
        for x in [-.09,.09]:m.tube([(x*.5,.23,0),(x,.23,0),(x,.10,0),(x*.75,.075,0)],.006,'Metal')
    elif id=='frostsigil':
        m.lathe([(.09,.05),(.075,.08),(.05,.11),(0,.11)],'Ceramic',12)
        m.crystal((0,.17,0),.04,.11,'Glass',6)
        for i in range(6):
            a=i*TAU/6;m.box((.075*math.sin(a),.13,.075*math.cos(a)),(.023,.028,.115),'Trim',math.degrees(a))
            m.crystal((.114*math.sin(a),.155,.114*math.cos(a)),.022,.075,'Glass',5)
            m.tube([(.035*math.sin(a),.15,.035*math.cos(a)),(.107*math.sin(a),.15,.107*math.cos(a))],.003,'Glow')
        m.torus((0,.1,0),.107,.003,'Glow')
    return m

ids=['freeze','emp','overload','decoy','repair','strike','firefield','gravity','mine','thunder','corrosion','frostsigil']
materials={'Metal':(.18,.25,.32),'Ceramic':(.53,.65,.71),'Trim':(.67,.75,.80),'Inset':(.04,.07,.11),'Glow':(.25,.85,1),'Glass':(.28,.67,.8)}
mtl=[]
for name,color in materials.items():mtl.extend(['newmtl '+name,'Kd %.3f %.3f %.3f'%color,'Ks 0.6 0.6 0.6','Ns 90',''])
(OUT/'device-materials.mtl').write_text('\n'.join(mtl),encoding='utf-8')
manifest=[device(id).write() for id in ids]
(OUT/'support-mesh-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps(manifest))
