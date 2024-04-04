using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockStepMainGSEntry : LockStepGameStateEntryBase
    {
        public TextMeshProUGUI autosaveText;
    }
}
