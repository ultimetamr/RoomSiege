using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_PICO_OPENXR_SDK
using ByteDance.PICO.OpenXR;
#endif

namespace RoomSiege.Scan
{
    internal static class RoomTrackingReady
    {
        private static readonly List<XRDisplaySubsystem> displays=new List<XRDisplaySubsystem>();
        private static bool? sessionFocused;
        public static int Revision { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Initialize()
        {
            sessionFocused=null;Revision=0;
#if ENABLE_PICO_OPENXR_SDK
            PICOFeature.onAppFocusedAction-=FocusChanged;
            PICOFeature.onAppFocusedAction+=FocusChanged;
#endif
        }

        private static void FocusChanged(bool focused)
        {
            if(sessionFocused!=focused)Revision++;
            sessionFocused=focused;
        }

        private static bool Ready()
        {
            if(!Application.isFocused||sessionFocused==false)return false;
            SubsystemManager.GetSubsystems(displays);
            bool running=false;
            foreach(var display in displays)if(display.running){running=true;break;}
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return running&&head.isValid&&head.TryGetFeatureValue(CommonUsages.isTracked,out var tracked)&&tracked;
        }

        public static Task Wait(CancellationToken token)
        {
            int observed=Revision;
            return RoomAnchorRecovery.WaitUntilStable(()=>
            {
                // Even a loss/regain occurring between polls invalidates the
                // stabilization window and any provider started before it.
                if(observed!=Revision){observed=Revision;return false;}
                return Ready();
            },()=>Time.realtimeSinceStartupAsDouble,t=>Task.Delay(100,t),token);
        }
    }
}
