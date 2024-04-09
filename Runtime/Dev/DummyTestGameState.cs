using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DummyTestGameState : LockStepGameState
    {
        [SerializeField] [HideInInspector] private LockStep lockStep; // Set by LockStep's OnBuild handler.

        [SerializeField] private string gameStateInternalName = "jansharp.lock-step-dummy";
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
