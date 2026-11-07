using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Visuals
{
    // Shared visual framing only; page navigation and world following stay with their owners.
    public static class GameUiChrome
    {
        public static void Frame(Transform parent, Material material, float headerRule = 174)
        {
            var chrome = Node(parent, "Game UI chrome", Vector2.zero, new Vector2(848, 488));
            Surface(chrome, "Header band", new Vector2(0, 208), new Vector2(844, 66), new Color(.04f,.075f,.105f,.88f), material);
            Surface(chrome, "Status band", new Vector2(0,-225), new Vector2(844,34), new Color(.035f,.065f,.09f,.78f), material);
            RoomSiegeStyle.AddRule(chrome,material,new Vector2(0,243),new Vector2(800,2),RoomSiegeStyle.Alpha(RoomSiegeStyle.Friendly,.55f));
            RoomSiegeStyle.AddRule(chrome,material,new Vector2(-394,207),new Vector2(3,28),RoomSiegeStyle.Friendly);
            RoomSiegeStyle.AddRule(chrome,material,new Vector2(0,headerRule),new Vector2(784,1),RoomSiegeStyle.Rule);
            RoomSiegeStyle.AddRule(chrome,material,new Vector2(0,-207),new Vector2(784,1),RoomSiegeStyle.Rule);
        }

        public static Image Surface(Transform parent,string name,Vector2 position,Vector2 size,Color color,Material material)
        {
            var image = Node(parent,name,position,size).gameObject.AddComponent<RoundedGlassImage>();
            image.color=color;image.material=material;image.raycastTarget=false;
            return image;
        }

        private static RectTransform Node(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);
            var rect=go.GetComponent<RectTransform>();rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
        }
    }
}
