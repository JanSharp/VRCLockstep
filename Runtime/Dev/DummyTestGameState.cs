using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DummyTestGameState : LockstepGameState
    {
        [SerializeField] [HideInInspector] [SingletonReference] private LockstepAPI lockstep;

        [SerializeField] private string gameStateInternalName = "jansharp.lockstep-dummy";
        [SerializeField] private string gameStateDisplayName = "Dummy Game State What?";
        [SerializeField] private bool gameStateSupportsImportExport = false;
        [SerializeField] private uint gameStateDataVersion = 0u;
        [SerializeField] private uint gameStateLowestSupportedDataVersion = 0u;
        public override LockstepGameStateOptionsUI ExportUI => null;
        public override LockstepGameStateOptionsUI ImportUI => null;

        public override string GameStateInternalName => gameStateInternalName;
        public override string GameStateDisplayName => gameStateDisplayName;
        public override bool GameStateSupportsImportExport => gameStateSupportsImportExport;
        public override uint GameStateDataVersion => gameStateDataVersion;
        public override uint GameStateLowestSupportedDataVersion => gameStateLowestSupportedDataVersion;

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {

        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
            return null;
        }
    }
}
