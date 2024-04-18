using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
    public abstract class LockstepGameState : UdonSharpBehaviour
    {
        public abstract string GameStateInternalName { get; }
        public abstract string GameStateDisplayName { get; }
        public abstract bool GameStateSupportsImportExport { get; }
        public abstract uint GameStateDataVersion { get; }
        public abstract uint GameStateLowestSupportedDataVersion { get; }
        public abstract void SerializeGameState(bool isExport);
        public abstract string DeserializeGameState(bool isImport);
    }
}
