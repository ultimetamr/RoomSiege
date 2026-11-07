using System.IO;
using RoomSiege.Enemies;
using RoomSiege.Towers;
using RoomSiege.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Offline preview of the same runtime meshes/materials; never modifies the launch scene.</summary>
[InitializeOnLoad]
public static class RoomSiegeModelPreview
{
    static RoomSiegeModelPreview(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        const string enemies="Temp/RoomSiegeEnemyPreview.txt";
        if(!EditorApplication.isCompiling&&!EditorApplication.isPlaying&&!EditorApplication.isUpdating&&File.Exists(enemies))
        {
            string destination=File.ReadAllText(enemies).Trim();File.Delete(enemies);
            EditorApplication.delayCall+=()=>RenderEnemies(destination);return;
        }
        const string command="Temp/RoomSiegeModelPreview.txt";
        if(EditorApplication.isCompiling||EditorApplication.isPlaying||EditorApplication.isUpdating||!File.Exists(command))return;
        string folder=File.ReadAllText(command).Trim();File.Delete(command);
        EditorApplication.delayCall+=()=>Render(folder);
    }
    private static void RenderEnemies(string folder)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("All enemy contact preview");SceneManager.MoveGameObjectToScene(root,scene);
        Material body=null,lines=null;RenderTexture rt=null;Texture2D pixels=null;
        try
        {
            Directory.CreateDirectory(folder);
            body=new Material(Resources.Load<Shader>("RoomSiegeEnemySilhouette"));
            lines=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            RoomSiegeStyle.ApplyWorldDetail(body);RoomSiegeStyle.ApplyWorldDetail(lines);
            string[] ids={"Grunt","Runner","Tank","Shielded","Boss"};
            float[] heights={.34f,.28f,.52f,.34f,.72f},radii={.12f,.09f,.20f,.12f,.28f};
            var visuals=new EnemyVisual[5];
            for(int i=0;i<ids.Length;i++)
            {
                var actor=new GameObject(ids[i]);actor.transform.SetParent(root.transform,false);
                actor.transform.localPosition=new Vector3((i-2)*.68f,0,0);actor.transform.localRotation=Quaternion.Euler(0,-15,0);
                visuals[i]=actor.AddComponent<EnemyVisual>();visuals[i].Build(body,lines);
                visuals[i].Configure(ids[i],heights[i],radii[i],ids[i]=="Shielded");
                var contact=TowerVisuals.Ring(actor.transform,"Floor contact",lines,new Color(.3f,.6f,.8f,.5f),radii[i],.002f);
                contact.transform.localPosition=Vector3.up*.001f;
            }
            for(int i=-9;i<=9;i++)
            {
                var go=new GameObject("Floor grid");go.transform.SetParent(root.transform,false);
                var line=go.AddComponent<LineRenderer>();line.sharedMaterial=lines;line.positionCount=2;line.startWidth=line.endWidth=.001f;
                line.startColor=line.endColor=new Color(.18f,.35f,.45f,.3f);
                line.SetPosition(0,new Vector3(i*.2f,0,-.4f));line.SetPosition(1,new Vector3(i*.2f,0,.4f));
            }
            var cameraGo=new GameObject("Preview camera");cameraGo.transform.SetParent(root.transform,false);
            var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.025f,.035f,.055f,1);camera.orthographic=true;camera.orthographicSize=.70f;
            camera.transform.position=new Vector3(0,1.02f,4);camera.transform.LookAt(new Vector3(0,.28f,0));
            camera.nearClipPlane=.01f;camera.farClipPlane=10;
            rt=new RenderTexture(1920,800,24);camera.targetTexture=rt;pixels=new Texture2D(1920,800,TextureFormat.RGB24,false);
            for(int frame=0;frame<48;frame++)
            {
                for(int i=0;i<visuals.Length;i++)if(frame>0)visuals[i].Tick(1/16f,ids[i]=="Shielded",0,.018f);
                camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,1920,800),0,0);pixels.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(folder,"frame-"+frame.ToString("D2")+".png"),pixels.EncodeToPNG());
            }
            Debug.Log("[RoomSiege.Visuals] Enemy contact preview saved: "+folder);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);
            if(body!=null)Object.DestroyImmediate(body);if(lines!=null)Object.DestroyImmediate(lines);
        }
    }
    private static void Render(string folder)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("MG Grunt preview");SceneManager.MoveGameObjectToScene(root,scene);
        Material body=null,lines=null;RenderTexture rt=null;Texture2D pixels=null;
        try
        {
            Directory.CreateDirectory(folder);
            body=new Material(Resources.Load<Shader>("RoomSiegeEnemySilhouette"));
            lines=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            RoomSiegeStyle.ApplyWorldDetail(body);RoomSiegeStyle.ApplyWorldDetail(lines);
            var baseGo=new GameObject("MG base");baseGo.transform.SetParent(root.transform,false);baseGo.transform.localPosition=new Vector3(-.20f,0,0);
            var turret=new GameObject("MG swivel");turret.transform.SetParent(baseGo.transform,false);turret.transform.localPosition=Vector3.up*.16f;
            var gun=turret.AddComponent<MachineGunModel>();gun.Build(baseGo.transform,lines);gun.SetAppearance(3,TowerVisuals.Blue);
            for(int j=0;j<3;j++){var ring=TowerVisuals.Ring(baseGo.transform,"Level "+j,lines,TowerVisuals.Blue,.11f-j*.018f);ring.transform.localPosition=Vector3.up*(.015f+j*.028f);}
            var gruntGo=new GameObject("Grunt");gruntGo.transform.SetParent(root.transform,false);gruntGo.transform.localPosition=new Vector3(.20f,0,0);
            var grunt=gruntGo.AddComponent<GruntModel>();grunt.Build(body);grunt.SetTint(new Color(.58f,.42f,.43f,.55f),0);
            var death=root.AddComponent<GruntDeathVisuals>();death.Build(body);
            var cameraGo=new GameObject("Preview camera");cameraGo.transform.SetParent(root.transform,false);
            var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.025f,.035f,.055f,1);camera.orthographic=true;camera.orthographicSize=.265f;
            camera.transform.position=new Vector3(.65f,.48f,1.1f);camera.transform.LookAt(new Vector3(0,.15f,0));
            camera.nearClipPlane=.01f;camera.farClipPlane=10;
            rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;
            pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            for(int frame=0;frame<48;frame++)
            {
                float time=frame/16f;
                if(frame==0||frame==13||frame==26){gun.Fire();if(frame==26)grunt.Highlight();}
                gun.Tick(1/16f);
                if(frame<31){grunt.Tick(1/16f,.5f/16);grunt.SetTint(new Color(.58f,.42f,.43f,.55f),frame==26?1:0);}
                if(frame==31){death.Emit(grunt);gruntGo.SetActive(false);}
                death.Tick(1/16f,false);camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(folder,"frame-"+frame.ToString("D2")+".png"),pixels.EncodeToPNG());
            }
            Debug.Log("[RoomSiege.Visuals] Model preview saved: "+folder);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);
            if(body!=null)Object.DestroyImmediate(body);if(lines!=null)Object.DestroyImmediate(lines);
        }
    }
}
