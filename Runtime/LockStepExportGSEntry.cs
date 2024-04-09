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
        [SerializeField] [HideInInspector] private LockStepGameStatesUI gameStatesUI;
        [HideInInspector] public LockStepGameState gameState;

        public TextMeshProUGUI infoLabel;
        [System.NonSerialized] public bool doAutosave = false;

        public void OnToggleValueChanged()
        {
            gameStatesUI.OnExportEntryToggled();
        }
    }
}
