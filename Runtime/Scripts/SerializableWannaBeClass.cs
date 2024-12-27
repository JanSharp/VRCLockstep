using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// TODO: docs

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class SerializableWannaBeClass : WannaBeClass
    {
        [HideInInspector] [SingletonReference] public LockstepAPI lockstep;

        public abstract bool SupportsImportExport { get; }
        public abstract uint DataVersion { get; }
        public abstract uint LowestSupportedDataVersion { get; }

        public abstract void Serialize(bool isExport);
        public abstract void Deserialize(bool isImport, uint importedDataVersion);
    }
}
