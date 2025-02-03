using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSExportOptions : LockstepGameStateOptionsData
    {
        public override bool SupportsImportExport => false;
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
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportOptions  Serialize");
            #endif
            lockstep.WriteFlags(shouldExport);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportOptions  Deserialize");
            #endif
            lockstep.ReadFlags(out shouldExport);
        }
    }
}
