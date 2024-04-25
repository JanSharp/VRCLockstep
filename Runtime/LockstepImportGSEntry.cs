using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace JanSharp.Internal
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepImportGSEntry : LockstepGameStateEntryBase
    {
        [HideInInspector] public LockstepGameStatesUI gameStatesUI;
        [HideInInspector] public LockstepGameState gameState;

        #if !LockstepDebug
        [HideInInspector]
        #endif
        public TextMeshProUGUI infoLabel;
        [System.NonSerialized] public bool canImport;
        ///<summary>LockstepImportedGS</summary>
        [System.NonSerialized] public object[] importedGS;

        public void OnToggleValueChanged()
        {
            gameStatesUI.OnImportEntryToggled();
        }
    }
}
