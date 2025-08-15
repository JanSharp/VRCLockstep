using TMPro;
using UdonSharp;
#if !LOCKSTEP_DEBUG
using UnityEngine;
#endif

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
