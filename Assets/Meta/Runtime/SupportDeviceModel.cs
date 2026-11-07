using System.Collections.Generic;
using RoomSiege.Progression;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomSiege.Meta
{
    // Authored meshes are shared by the world devices and their shop portraits.
    // No colliders: a support marker must never obstruct building or enemy routes.
    public sealed class SupportDeviceModel:MonoBehaviour
    {
        private static readonly Dictionary<string,Material> Materials=new Dictionary<string,Material>();
        public static GameObject Create(Transform parent,SupplyDefinition item)
        {
            var asset=Resources.Load<GameObject>("SupportDevices/"+item.id);
            if(asset==null){Debug.LogError("[RoomSiege] Missing support device: "+item.id);return null;}
            var root=new GameObject("Support device "+item.id);root.transform.SetParent(parent,false);
            var model=Instantiate(asset,root.transform,false);model.name="Authored mesh";
            // OBJ import scale and handedness are normalized once, with its base on the ground.
            var renderers=model.GetComponentsInChildren<MeshRenderer>();
            if(renderers.Length==0){Destroy(root);return null;}
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float scale=.294f/Mathf.Max(.001f,Mathf.Max(bounds.size.x,bounds.size.z));
            model.transform.localScale*=scale;
            var center=root.transform.InverseTransformPoint(bounds.center);
            model.transform.localPosition=new Vector3(-center.x,-(center.y-bounds.extents.y),-center.z)*scale;
            Color tint=SupportFieldVisual.ColorFor(item);
            foreach(var r in renderers)
            {
                var source=r.sharedMaterials;var replacement=new Material[source.Length];
                for(int i=0;i<source.Length;i++)replacement[i]=Surface(item.id,source[i]!=null?source[i].name:"Metal",tint);
                r.sharedMaterials=replacement;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
                r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            }
            root.AddComponent<SupportDeviceModel>();return root;
        }
        private static Material Surface(string id,string group,Color tint)
        {
            string key=id+"/"+group;if(Materials.TryGetValue(key,out var existing)&&existing!=null)return existing;
            var shader=Resources.Load<Shader>("RoomSiegeDeviceSurface");
            var material=new Material(shader){name="Support "+key};
            bool glow=group.Contains("Glow"),glass=group.Contains("Glass"),trim=group.Contains("Trim"),ceramic=group.Contains("Ceramic"),inset=group.Contains("Inset");
            Color color=glow?Color.Lerp(tint,Color.white,.2f):glass?Color.Lerp(tint,Color.white,.06f):
                trim?new Color(.58f,.69f,.77f):ceramic?new Color(.30f,.40f,.49f):inset?new Color(.025f,.04f,.065f):new Color(.09f,.14f,.20f);
            material.SetColor("_Color",color);material.SetColor("_Emission",glow?tint*.7f:glass?tint*.3f:Color.black);
            material.SetFloat("_Metallic",trim?.85f:glass?.35f:ceramic?.3f:.65f);
            Materials[key]=material;return material;
        }
    }
}
