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

        public override void Serialize(bool isExport)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportOptions  Serialize");
            #endif
            lockstep.WriteByte((byte)(shouldImport ? 1 : 0));
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportOptions  Deserialize");
            #endif
            shouldImport = lockstep.ReadByte() != 0;
        }
    }
}
