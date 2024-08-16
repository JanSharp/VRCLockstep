using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace JanSharp.Internal
{
    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepExportGSEntry : LockstepGameStateEntryBase
    {
        [SerializeField] [HideInInspector] private LockstepGameStatesUI gameStatesUI;
        [HideInInspector] public LockstepGameState gameState;

        #if !LockstepDebug
        [HideInInspector]
        #endif
        public TextMeshProUGUI infoLabel;
        [System.NonSerialized] public bool doAutosave = false;

        public void OnToggleValueChanged()
        {
            gameStatesUI.OnExportEntryToggled();
        }
    }
}
