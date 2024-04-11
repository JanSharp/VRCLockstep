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
        [HideInInspector] public LockStepGameStatesUI gameStatesUI;
        [HideInInspector] public LockStepGameState gameState;

        public TextMeshProUGUI infoLabel;
        [System.NonSerialized] public bool canImport;
        ///<summary>LockStepImportedGS</summary>
        [System.NonSerialized] public object[] importedGS;

        public void OnToggleValueChanged()
        {
            gameStatesUI.OnImportEntryToggled();
        }
    }
}
