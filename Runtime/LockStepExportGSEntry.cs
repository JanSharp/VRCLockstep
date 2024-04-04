using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockStepExportGSEntry : LockStepGameStateEntryBase
    {
        [SerializeField] private LockStepGameStatesUI gameStatesUI;

        public Toggle mainToggle;
        public TextMeshProUGUI autosaveText;
        [System.NonSerialized] public bool doAutosave = false;

        public void OnToggleValueChanged()
        {

        }
    }
}
