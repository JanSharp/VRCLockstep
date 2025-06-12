using TMPro;
using UdonSharp;

namespace JanSharp.Internal
{
#if !LockstepDebug
    [AddComponentMenu("")]
#endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepMainGSEntry : LockstepGameStateEntryBase
    {
#if !LockstepDebug
        [HideInInspector]
#endif
        public TextMeshProUGUI autosaveText;
    }
}
