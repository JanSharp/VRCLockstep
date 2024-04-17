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
        public LockStep lockStep;
        public bool isSinglePlayer = false; // Default value must match the one in LockStep.
        public uint currentTick;
        private uint tickInSyncedData;
        [UdonSynced] private byte[] syncedData = new byte[0];
        private int readPosition = 0;
        private float tickLoopDelay = 1f / LockStep.TickRate;
        private uint lastSyncedTick = 0u; // Default value really doesn't matter.

        private byte[] buffer = new byte[ArrList.MinCapacity];
        private int bufferSize = 0;
        private int bufferSizeToClear = 0;

        private byte[] tickBuffer = new byte[5];
        private int tickBufferSize = 0;

        public void AddInputActionToRun(uint tickToRunIn, ulong uniqueId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStepTickSync  AddInputActionToRun");
            #endif
            uint playerId = (uint)(uniqueId >> LockStep.PlayerIdKeyShift);
            uint inputActionIndex = (uint)(uniqueId & 0x00000000ffffffffuL);
            DataStream.WriteSmall(ref buffer, ref bufferSize, tickToRunIn);
            DataStream.WriteSmall(ref buffer, ref bufferSize, playerId);
            DataStream.WriteSmall(ref buffer, ref bufferSize, inputActionIndex);
        }

        public void ClearInputActionsToRun()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStepTickSync  ClearInputActionsToRun");
            #endif
            bufferSize = 0;
            bufferSizeToClear = 0;
        }

        public override void OnPreSerialization()
        {
            tickBufferSize = 0;
            DataStream.WriteSmall(ref tickBuffer, ref tickBufferSize, currentTick);
            int totalSize = tickBufferSize + bufferSize;
            if (syncedData.Length != totalSize)
                syncedData = new byte[totalSize];
            for (int i = 0; i < tickBufferSize; i++)
                syncedData[i] = tickBuffer[i];
            for (int i = 0; i < bufferSize; i++)
                syncedData[tickBufferSize + i] = buffer[i];
            tickInSyncedData = currentTick;
            bufferSizeToClear = bufferSize;
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (isSinglePlayer)
                return;

            if (!result.success)
            {
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), 1f);
                return;
            }

            bufferSize -= bufferSizeToClear;
            for (int i = 0; i < bufferSize; i++)
                buffer[i] = buffer[bufferSizeToClear + i];

            if (tickInSyncedData == lastSyncedTick) // Synced the same tick twice, slow down the frequency.
                tickLoopDelay += 0.001f;
            else if (tickInSyncedData > lastSyncedTick + 1u) // Synced 2 or more ticks at once, make it faster.
                tickLoopDelay = Mathf.Max(0.01f, tickLoopDelay - 0.001f);
            SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), tickLoopDelay);
            lastSyncedTick = tickInSyncedData;
            #if LockStepDebug
            syncCount++;
            #endif
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnDeserialization()
        {
            readPosition = 0;
            lockStep.waitTick = DataStream.ReadSmallUInt(ref syncedData, ref readPosition);
            int length = syncedData.Length;
            while (readPosition < length)
            {
                uint tickToRunIn = DataStream.ReadSmallUInt(ref syncedData, ref readPosition);
                uint playerId = DataStream.ReadSmallUInt(ref syncedData, ref readPosition);
                uint inputActionIndex = DataStream.ReadSmallUInt(ref syncedData, ref readPosition);
                lockStep.AssociateInputActionWithTick(
                    tickToRunIn,
                    ((ulong)playerId << LockStep.PlayerIdKeyShift) | (ulong)inputActionIndex
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
