using TMPro;
using UdonSharp;
using UnityEngine.UI;

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
        public TextMeshProUGUI masterPreferenceText;
#if !LockstepDebug
        [HideInInspector]
#endif
        public Slider masterPreferenceSlider;
#if !LockstepDebug
        [HideInInspector]
#endif
        public Button makeMasterButton;

        [System.NonSerialized] public LockstepInfoUI infoUI;
        [System.NonSerialized] public uint playerId;
        private int waitingForPreferenceChangeCount = 0;
        private const float TimeToWaitForPreferenceChange = 0.3f;

        public void OnMakeMasterClick() => infoUI.OnMakeMasterClick(this);

        public void OnPreferenceSliderValueChanged() => infoUI.OnPreferenceSliderValueChanged(this);

        public void WaitBeforeApplyingPreferenceChange()
        {
            waitingForPreferenceChangeCount++;
            SendCustomEventDelayedSeconds(nameof(FinishedWaitingToApplyPreferenceChange), TimeToWaitForPreferenceChange);
        }

        public void FinishedWaitingToApplyPreferenceChange()
        {
            if ((--waitingForPreferenceChangeCount) != 0)
                return;
            infoUI.ApplyMasterPreferenceChange(this);
        }
    }
}
