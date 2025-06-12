using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSExportOptions : LockstepGameStateOptionsData
    {
        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0;
        public override uint LowestSupportedDataVersion => 0;

        [System.NonSerialized] public bool shouldExport = true;

        public override LockstepGameStateOptionsData Clone()
        {
            TestGSExportOptions other = WannaBeClasses.New<TestGSExportOptions>(nameof(TestGSExportOptions));
            other.shouldExport = shouldExport;
            return other;
        }

        public override void Serialize(bool isExport)
        {
            Debug.Log($"[LockstepTest] TestGSExportOptions  Serialize");
            lockstep.WriteFlags(shouldExport);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
            Debug.Log($"[LockstepTest] TestGSExportOptions  Deserialize");
            lockstep.ReadFlags(out shouldExport);
        }
    }
}
