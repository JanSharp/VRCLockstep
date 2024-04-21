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
        [SerializeField] [HideInInspector] private LockstepAPI lockstep; // Set by Lockstep's OnBuild handler.

        [SerializeField] private string gameStateInternalName = "jansharp.lockstep-dummy";
        [SerializeField] private string gameStateDisplayName = "Dummy Game State What?";
        [SerializeField] private bool gameStateSupportsImportExport = false;
        [SerializeField] private uint gameStateDataVersion = 0u;
        [SerializeField] private uint gameStateLowestSupportedDataVersion = 0u;

        public override string GameStateInternalName => gameStateInternalName;
        public override string GameStateDisplayName => gameStateDisplayName;
        public override bool GameStateSupportsImportExport => gameStateSupportsImportExport;
        public override uint GameStateDataVersion => gameStateDataVersion;
        public override uint GameStateLowestSupportedDataVersion => gameStateLowestSupportedDataVersion;

        public override void SerializeGameState(bool isExport)
        {

        }

        public override string DeserializeGameState(bool isImport)
        {
            return null;
        }
    }
}
