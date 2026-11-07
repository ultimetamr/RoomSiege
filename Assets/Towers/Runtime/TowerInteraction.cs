using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.Towers
{
    public sealed class TowerShopDrag : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public TowerHost Host;
        public string TowerId;
        private bool dragged;
        private bool selecting;
        public void OnPointerDown(PointerEventData data){dragged=selecting=false;}
        public void OnPointerClick(PointerEventData data)
        { if(!dragged)Host.Choose(TowerId);Host.SuppressTap();dragged=false; }
        public void OnBeginDrag(PointerEventData data){dragged=true;selecting=Host.Choose(TowerId);if(selecting)OnDrag(data);}
        public void OnDrag(PointerEventData data)
        {if(selecting&&TryRay(data,out var ray))Host.Preview(ray);Host.SuppressTap();}
        public void OnEndDrag(PointerEventData data){OnDrag(data);Host.SuppressTap();selecting=false;}
        private void OnDisable(){dragged=selecting=false;}
        private bool TryRay(PointerEventData data,out Ray ray)
        {
            if(data is TrackedDeviceEventData tracked && tracked.rayPoints!=null && tracked.rayPoints.Count>=2)
            {ray=new Ray(tracked.rayPoints[0],tracked.rayPoints[1]-tracked.rayPoints[0]);return true;}
            if(Host.Head!=null){ray=Host.Head.ScreenPointToRay(data.position);return true;}
            ray=default;return false;
        }
    }

    public sealed class TowerGrabProxy : MonoBehaviour
    {
        private TowerHost host;
        private string id;
        private Transform home;
        private Vector3 homePosition,homeScale;
        private Quaternion homeRotation;
        private XRGrabInteractable grab;
        private Collider hitBox;
        private bool available;
        public bool Held {get;private set;}
        public void SetTowerId(string value){if(!Held)id=value;}
        public void Initialize(TowerHost host,string id)
        {
            this.host=host;this.id=id;home=transform.parent;homePosition=transform.localPosition;homeScale=transform.localScale;homeRotation=transform.localRotation;
            var box=gameObject.AddComponent<BoxCollider>();box.size=new Vector3(.095f,.095f,.095f);hitBox=box;
            var body=gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            grab=gameObject.AddComponent<XRGrabInteractable>();grab.movementType=XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach=false;grab.retainTransformParent=false;
            grab.selectEntered.AddListener(Grabbed);grab.selectExited.AddListener(Released);
        }
        public void SetAvailable(bool value)
        {
            available=value;
            // Finish an ongoing grip cleanly. The transaction still rechecks funds on placement.
            if(Held)return;
            if(grab!=null)grab.enabled=value;
            if(hitBox!=null)hitBox.enabled=value;
        }
        private void Grabbed(SelectEnterEventArgs args){Held=true;host.Choose(id);host.SuppressTap();}
        private void Released(SelectExitEventArgs args)
        {
            Held=false;
            if(home!=null){transform.SetParent(home,false);transform.localPosition=homePosition;transform.localRotation=homeRotation;transform.localScale=homeScale;}
            if(host!=null)host.SuppressTap();
            SetAvailable(available);
        }
    }
}
