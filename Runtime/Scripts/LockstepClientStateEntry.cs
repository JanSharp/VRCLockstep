using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace JanSharp
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
        [SerializeField] private TextMeshProUGUI clientDisplayNameText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI clientStateText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject masterPreferenceObject;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI masterPreferenceText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Slider masterPreferenceSlider;
    }
}
