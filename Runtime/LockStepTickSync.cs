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
        private const ulong UniqueIdBits = 0x00000000ffffffffuL;

        public LockStep lockStep;
        public bool isSinglePlayer = false; // Default value must match the one in LockStep.
        [UdonSynced] public uint syncedTick;
        [UdonSynced] private ulong[] syncedInputActionsToRun = new ulong[0];
        private bool retrying = false;
        private float tickLoopDelay = 1f / LockStep.TickRate;
        private uint lastSyncedTick = 0u; // Default value really doesn't matter.

        ///cSpell:ignore iatr

        private ulong[] inputActionsToRun = new ulong[ArrList.MinCapacity];
        private int iatrCount = 0;

        public void AddInputActionToRun(uint tickToRunIn, uint uniqueId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStepTickSync  AddInputActionToRun");
            #endif
            ArrList.Add(ref inputActionsToRun, ref iatrCount, (((ulong)tickToRunIn) << TickToRunInShift) | (ulong)uniqueId);
        }

        public void ClearInputActionsToRun()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStepTickSync  ClearInputActionsToRun");
            #endif
            ArrList.Clear(ref inputActionsToRun, ref iatrCount);
        }

        public override void OnPreSerialization()
        {
            if (retrying)
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
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), 1f);
                return;
            }

            ArrList.Clear(ref inputActionsToRun, ref iatrCount);
            if (isSinglePlayer)
                return;

            if (syncedTick == lastSyncedTick) // Synced the same tick twice, slow down the frequency.
                tickLoopDelay += 0.001f;
            else if (syncedTick > lastSyncedTick + 1u) // Synced 2 or more ticks at once, make it faster.
                tickLoopDelay = Mathf.Max(0.01f, tickLoopDelay - 0.001f);
            SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), tickLoopDelay);
            lastSyncedTick = syncedTick;
            #if LockStepDebug
            syncCount++;
            #endif
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnDeserialization()
        {
            lockStep.waitTick = syncedTick;
            foreach (ulong inputActionToRun in syncedInputActionsToRun)
            {
                lockStep.AssociateInputActionWithTick(
                    (uint)(inputActionToRun >> TickToRunInShift),
                    // Since casting in Udon isn't actually casting, it's a call to Convert.ToX
                    // I'm quite certain we first have to truncate the top bits manually, otherwise
                    // it would convert it to some wrong, large number. I believe.
                    (uint)(inputActionToRun & UniqueIdBits)
                );
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStepTickSync  OnOwnershipTransferred");
            #endif
            lockStep.SendCustomEventDelayedFrames(nameof(LockStep.CheckMasterChange), 1);
        }



        #if LockStepDebug
        private void Start()
        {
            SendCustomEventDelayedSeconds(nameof(SyncCountTestLoop), 10f);
        }

        private int syncCount = 0;
        public void SyncCountTestLoop()
        {
            Debug.Log($"[LockStepDebug] tick sync count in the last 10 seconds: {syncCount}, {((float)syncCount) / 10f}/s, current tickLoopDelay: {tickLoopDelay}");
            syncCount = 0;
            SendCustomEventDelayedSeconds(nameof(SyncCountTestLoop), 10f);
        }
        #endif
    }
}
