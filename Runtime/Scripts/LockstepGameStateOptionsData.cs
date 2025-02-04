using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// TODO: docs

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class LockstepGameStateOptionsData : SerializableWannaBeClass
    {
        /// <summary>
        /// TODO: docs, note that this must perform a deep clone, not a shallow copy
        /// </summary>
        /// <returns></returns>
        public abstract LockstepGameStateOptionsData Clone();
    }
}
