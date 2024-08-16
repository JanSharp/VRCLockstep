using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

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
