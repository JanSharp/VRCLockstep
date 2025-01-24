using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSImportOptions : LockstepGameStateOptionsData
    {
        [System.NonSerialized] public bool shouldImport = true;
    }
}
