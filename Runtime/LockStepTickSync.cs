using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LockStepTickSync : UdonSharpBehaviour
    {
        private const int TickToRunInShift = 32;
        private const uint UniqueIdBits = 0xffffffffu;

        public LockStep lockStep;
        [UdonSynced] public uint syncedTick;
        [UdonSynced] private ulong[] syncedInputActionsToRun = new ulong[0];
        private bool firstSync = true;
        private bool retrying = false;

        ///cSpell:ignore iatr

        private ulong[] inputActionsToRun = new ulong[ArrList.MinCapacity];
        private int iatrCount = 0;

        public void AddInputActionToRun(uint tickToRunIn, uint uniqueId)
        {
            ArrList.Add(ref inputActionsToRun, ref iatrCount, (tickToRunIn << TickToRunInShift) | uniqueId);
        }

        public override void OnPreSerialization()
        {
            if (firstSync || retrying)
            {
                retrying = false;
                return;
            }

            if (syncedInputActionsToRun.Length != iatrCount)
                syncedInputActionsToRun = new ulong[iatrCount];
            for (int i = 0; i < iatrCount; i++)
                syncedInputActionsToRun[i] = inputActionsToRun[i];
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (!result.success)
            {
                retrying = true;
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), 2f);
                return;
            }

            if (firstSync)
            {
                firstSync = false;
                SendCustomEventDelayedFrames(nameof(RequestSerializationDelayed), 1);
                return;
            }

            SendCustomEventDelayedFrames(nameof(RequestSerializationDelayed), 1);
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnDeserialization()
        {
            if (firstSync)
            {
                firstSync = false;
                return;
            }

            lockStep.waitTick = syncedTick;
            foreach (ulong inputActionToRun in syncedInputActionsToRun)
            {
                lockStep.EnqueueInputActionAtTick(
                    (uint)(inputActionToRun >> TickToRunInShift),
                    // Since casting in Udon isn't actually casting, it's a call to Convert.ToX
                    // I'm quite certain we first have to truncate the top bits manually, otherwise
                    // it would convert it to some wrong, large number. I believe.
                    (uint)(inputActionToRun & UniqueIdBits)
                );
            }
        }
    }
}
