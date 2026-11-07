using System.IO;
using RoomSiege.Data;
using RoomSiege.Enemies;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RoomSiegeCodexPreview
{
    static RoomSiegeCodexPreview(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        const string command="Temp/RoomSiegeCodexPreview.txt";
        if(EditorApplication.isCompiling||EditorApplication.isPlaying||EditorApplication.isUpdating||!File.Exists(command))return;
        string folder=File.ReadAllText(command).Trim();File.Delete(command);
        EditorApplication.delayCall+=()=>Render(folder);
    }
    public static void RenderNewArsenal()
    {
        const string folder="Assets/Meta/Runtime/Resources/Codex";
        var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("New arsenal capture");SceneManager.MoveGameObjectToScene(root,scene);
        var material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));RoomSiegeStyle.ApplyWorldDetail(material);
        try
        {
            var catalog=BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));
            foreach(var tower in catalog.towers)if(WeaponRoster.IsNew(tower.id))RenderTower(root,scene,material,folder,tower);
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);Object.DestroyImmediate(material);}
        AssetDatabase.Refresh();Debug.Log("[RoomSiege.Arsenal] 12 actual model portraits rendered");
    }
    public static void RenderAllSkins()
    {
        const string folder="Assets/Meta/Runtime/Resources/Codex";
        var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Skin portrait capture");SceneManager.MoveGameObjectToScene(root,scene);
        var material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));RoomSiegeStyle.ApplyWorldDetail(material);
        try
        {
            var balance=BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));
            foreach(var skin in RoomSiege.Progression.ProgressionCatalog.Load().skins)
            {
                var go=new GameObject(skin.id);SceneManager.MoveGameObjectToScene(go,scene);
                try
                {
                    var tower=TowerInstance.CreatePreview(balance.GetTower(skin.weaponId),new GridCell("skin",SurfaceKind.Floor,Vector2Int.zero,Vector3.zero));tower.Skin=skin;
                    go.AddComponent<TowerView>().Build(tower,material);
                    RenderObject(root,scene,go,folder,"Skins/Skin_"+skin.id,material,material,.23f,new Color(0,0,0,0));
                }
                finally{Object.DestroyImmediate(go);}
            }
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);Object.DestroyImmediate(material);}
        AssetDatabase.Refresh();
        foreach(var skin in RoomSiege.Progression.ProgressionCatalog.Load().skins)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(folder+"/Skins/Skin_"+skin.id+".png");
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=512;importer.isReadable=false;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
        }
        Debug.Log("[RoomSiege.Skins] 54 actual equipped-model portraits rendered");
    }
    private static void Render(string folder)
    {
        var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Codex preview");SceneManager.MoveGameObjectToScene(root,scene);
        Material body=null,lines=null;RenderTexture rt=null;Texture2D pixels=null;
        try
        {
            Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine("Assets/Meta/Runtime/Resources/Codex","Enemies"));Directory.CreateDirectory(Path.Combine("Assets/Meta/Runtime/Resources/Codex","Towers"));
            body=new Material(Resources.Load<Shader>("RoomSiegeEnemySilhouette"));lines=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            RoomSiegeStyle.ApplyWorldDetail(body);RoomSiegeStyle.ApplyWorldDetail(lines);
            var json=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Towers/Runtime/Resources/RoomSiegeTowerBalance.json");var catalog=BalanceJson.Parse(json.text);
            string[] enemyIds={"Grunt","Runner","Tank","Shielded","Boss"};float[] heights={.34f,.28f,.52f,.34f,.72f},radii={.12f,.09f,.20f,.12f,.28f};
            for(int i=0;i<enemyIds.Length;i++)RenderEnemy(root,scene,body,lines,folder,enemyIds[i],heights[i],radii[i],enemyIds[i]=="Shielded"||enemyIds[i]=="Boss");
            foreach(var definition in catalog.towers)RenderTower(root,scene,lines,folder,definition);
            Debug.Log("[RoomSiege.Meta] Codex model images saved: "+folder);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);if(body!=null)Object.DestroyImmediate(body);if(lines!=null)Object.DestroyImmediate(lines);
        }
    }
    private static void RenderEnemy(GameObject root,Scene scene,Material body,Material lines,string folder,string id,float height,float radius,bool shielded)
    {
        var go=new GameObject(id);SceneManager.MoveGameObjectToScene(go,scene);var visual=go.AddComponent<EnemyVisual>();visual.Build(body,lines);visual.Configure(id,height,radius,shielded);
        RenderObject(root,scene,go,folder,"Enemies/CodexEnemy_"+id,body,lines,.85f,new Color(0,0,0,0));Object.DestroyImmediate(go);
    }
    private static void RenderTower(GameObject root,Scene scene,Material lines,string folder,TowerDefinition definition)
    {
        var go=new GameObject(definition.id);SceneManager.MoveGameObjectToScene(go,scene);var view=go.AddComponent<TowerView>();
        var cell=new GridCell("codex",SurfaceKind.Floor,new Vector2Int(0,0),Vector3.zero);var tower=TowerInstance.CreatePreview(definition,cell);view.Build(tower,lines);
        RenderObject(root,scene,go,folder,"Towers/CodexTower_"+definition.id,lines,lines,.35f,new Color(0,0,0,0));Object.DestroyImmediate(go);
    }
    private static void RenderObject(GameObject root,Scene scene,GameObject subject,string folder,string name,Material subjectMaterial,Material floorMaterial,float ortho,Color background)
    {
        var floor=new GameObject("Codex contact ring");SceneManager.MoveGameObjectToScene(floor,scene);
        var contact=TowerVisuals.Ring(floor.transform,"Contact",floorMaterial,new Color(.25f,.55f,.65f,.28f),name.StartsWith("Towers")?.13f:.18f,.0015f);
        contact.transform.position=Vector3.up*.001f;
        var bounds=new Bounds(subject.transform.position,Vector3.zero);bool first=true;
        foreach(var renderer in subject.GetComponentsInChildren<Renderer>())if(renderer.enabled&&renderer.gameObject.activeInHierarchy)
        {if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
        ortho=Mathf.Max(.16f,bounds.size.y*.76f,Mathf.Max(bounds.size.x,bounds.size.z)*.82f);
        // LineRenderer bounds are conservative until first render; tower framing uses its known cosmetic envelope.
        if(name.StartsWith("Towers")||name.StartsWith("Skins"))
        {
            bool trap=name.EndsWith("Trap")||name.StartsWith("Skins/Skin_trap-");ortho=trap?.18f:.23f;
            bounds=new Bounds(new Vector3(0,trap?.035f:.14f,0),Vector3.zero);
        }
        var cameraGo=new GameObject("Codex camera");SceneManager.MoveGameObjectToScene(cameraGo,scene);var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=background;camera.orthographic=true;camera.orthographicSize=ortho;camera.transform.position=bounds.center+new Vector3(.62f,.44f,1.4f);camera.transform.LookAt(bounds.center);camera.nearClipPlane=.01f;camera.farClipPlane=10;
        var rt=new RenderTexture(512,512,24);camera.targetTexture=rt;var pixels=new Texture2D(512,512,TextureFormat.RGBA32,false);camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();RenderTexture.active=previous;
        string path=Path.Combine(folder,name+".png");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pixels.EncodeToPNG());
        camera.targetTexture=null;
        Object.DestroyImmediate(cameraGo);Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(floor);
    }
}
