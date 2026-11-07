using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.UI
{
    public sealed class RoomSiegeRayInteractor : XRRayInteractor
    {
        public override void UpdateUIModel(ref TrackedDeviceModel model)
        {
            base.UpdateUIModel(ref model);
            // XRI's built-in click-on-down path only applies to ordinary Selectables.
            // Buttons activate on the deliberate pinch/trigger edge, before release moves the aim.
            // Drag handles and shop cards retain the normal press/drag/release lifecycle.
            model.clickOnDown = true;
        }
    }
}
