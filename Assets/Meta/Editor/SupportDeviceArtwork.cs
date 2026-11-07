using System.IO;
using RoomSiege.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoomSiege.Meta.Editor
{
    public static class SupportDeviceArtwork
    {
        [MenuItem("RoomSiege/Meta/Render support device portraits")]
        public static void RenderAll()
        {
            const string output="Assets/Meta/Runtime/Resources/SupportDevices/Portraits";
            Directory.CreateDirectory(output);
            var root=new GameObject("Support portrait studio");root.transform.position=new Vector3(0,-200,0);
            var cameraObject=new GameObject("Portrait camera");var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.04f,.065f,0);
            camera.orthographic=true;camera.orthographicSize=.215f;camera.nearClipPlane=.01f;camera.farClipPlane=3;camera.cullingMask=1<<30;
            camera.allowHDR=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.GetUniversalAdditionalCameraData().allowXRRendering=false;
            var texture=new RenderTexture(512,512,24){antiAliasing=4};texture.Create();camera.targetTexture=texture;
            try
            {
                foreach(var item in ProgressionCatalog.Load().supplies)
                {
                    var model=SupportDeviceModel.Create(root.transform,item);
                    if(model==null)throw new System.InvalidOperationException("Missing device "+item.id);
                    foreach(var child in model.GetComponentsInChildren<Transform>())child.gameObject.layer=30;
                    camera.transform.position=root.transform.position+new Vector3(.48f,.40f,-.6f);
                    camera.transform.LookAt(root.transform.position+Vector3.up*.145f);camera.Render();
                    var old=RenderTexture.active;RenderTexture.active=texture;
                    var png=new Texture2D(512,512,TextureFormat.RGBA32,false);png.ReadPixels(new Rect(0,0,512,512),0,0);png.Apply();
                    File.WriteAllBytes(output+"/"+item.id+".png",png.EncodeToPNG());Object.DestroyImmediate(png);RenderTexture.active=old;
                    Object.DestroyImmediate(model);
                }
            }
            finally{camera.targetTexture=null;texture.Release();Object.DestroyImmediate(texture);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(root);}
            AssetDatabase.Refresh();
            foreach(var item in ProgressionCatalog.Load().supplies)
            {
                var path=output+"/"+item.id+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
                importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            }
            Debug.Log("[RoomSiege] Rendered 12 authored support portraits");
        }
    }
}
