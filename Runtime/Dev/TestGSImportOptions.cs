using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSImportOptions : LockstepGameStateOptionsData
    {
        public override bool SupportsImportExport => false;
        public override uint DataVersion => 0;
        public override uint LowestSupportedDataVersion => 0;

        [System.NonSerialized] public bool shouldImport = true;

        public override LockstepGameStateOptionsData Clone()
        {
            TestGSImportOptions other = WannaBeClasses.New<TestGSImportOptions>(nameof(TestGSImportOptions));
            other.shouldImport = shouldImport;
            return other;
        }

        public override void Serialize(bool isExport)
        {
            Debug.Log($"[LockstepTest] TestGSImportOptions  Serialize");
            lockstep.WriteFlags(shouldImport);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
            Debug.Log($"[LockstepTest] TestGSImportOptions  Deserialize");
            lockstep.ReadFlags(out shouldImport);
        }
    }
}
