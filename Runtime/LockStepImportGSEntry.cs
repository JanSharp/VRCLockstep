using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockStepImportGSEntry : LockStepGameStateEntryBase
    {
        public TextMeshProUGUI infoText;
    }
}
