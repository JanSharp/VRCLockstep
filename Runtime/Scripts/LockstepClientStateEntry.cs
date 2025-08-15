using TMPro;
using UdonSharp;
using UnityEngine.UI;

namespace JanSharp.Internal
{
#if !LOCKSTEP_DEBUG
        [AddComponentMenu("")]
#endif
        [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
        public class LockstepClientStateEntry : UdonSharpBehaviour
        {
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public TextMeshProUGUI clientDisplayNameText;
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public TextMeshProUGUI clientStateText;
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public TextMeshProUGUI masterPreferenceText;
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public Slider masterPreferenceSlider;
#if !LOCKSTEP_DEBUG
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
