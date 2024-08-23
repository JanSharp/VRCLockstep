using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace JanSharp.Internal
{
    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepClientStateEntry : UdonSharpBehaviour
    {
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public TextMeshProUGUI clientDisplayNameText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public TextMeshProUGUI clientStateText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public GameObject masterPreferenceObject;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public TextMeshProUGUI masterPreferenceText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public Slider masterPreferenceSlider;

        [System.NonSerialized] public uint clientId;
    }
}
