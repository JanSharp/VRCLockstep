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
    public class LockstepInfoUI : UdonSharpBehaviour
    {
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject notificationEntryPrefab;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject clientStateEntryPrefab;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI localClientStateText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI clientCountText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject localMasterPreferenceObject;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI localMasterPreferenceText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Slider localMasterPreferenceSlider;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Toggle notificationLogTabToggle;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private CanvasGroup notificationLogCanvasGroup;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Transform notificationLogContent;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private ScrollRect notificationLogScrollRect;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Toggle clientStatesTabToggle;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private CanvasGroup clientStatesCanvasGroup;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Transform clientStatesContent;
    }
}
