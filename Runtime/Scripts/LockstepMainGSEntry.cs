using TMPro;
using UdonSharp;

namespace JanSharp.Internal
{
#if !LOCKSTEP_DEBUG
    [AddComponentMenu("")]
#endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepMainGSEntry : LockstepGameStateEntryBase
    {
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        public TextMeshProUGUI autosaveText;
    }
}
