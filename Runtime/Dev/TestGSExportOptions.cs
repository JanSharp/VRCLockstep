using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSExportOptions : LockstepGameStateOptionsData
    {
        [System.NonSerialized] public bool shouldExport = true;
    }
}
