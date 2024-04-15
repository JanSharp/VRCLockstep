using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Cyan.PlayerObjectPool;
using VRC.Udon.Common;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class InputActionSync : CyanPlayerObjectPoolObject
    {
        private const int MaxSyncedDataSize = 4096;
        private const int InputActionIndexShift = 16;
        private const uint SplitDataFlag = 0x8000u;
        private const uint InputActionIdBits = 0x7fff;

        [System.NonSerialized] public LockStep lockStep;
        [System.NonSerialized] public bool lockStepIsMaster;
        [System.NonSerialized] public uint shiftedPlayerId;

        // Who is the current owner of this object. Null if object is not currently in use.
        // [System.NonSerialized] public VRCPlayerApi Owner; // In the base class.

        public bool isLateJoinerSyncInst = false;

        // It is actually a ushort, but to reduce the amount of casts the variable is uint.
        // First one is 1, making 0 an indication of an invalid index.
        // Since the input action index 0 is invalid, the unique id 0 is also invalid.
        // Unique id is the shifted player index plus the input action index.
        private uint nextInputActionIndex = 1u;

        // sending

        [UdonSynced] private uint syncedInt = 0u; // Initial values for first sync, which gets ignored.
        [UdonSynced] private byte[] syncedData = new byte[0];
        private bool retrying = false;
        private float retryBackoff = 1f;

        private uint[] syncedIntQueue = new uint[ArrQueue.MinCapacity];
        private int siqStartIndex = 0;
        private int siqCount = 0;

        private byte[][] syncedDataQueue = new byte[ArrQueue.MinCapacity][];
        private int sdqStartIndex = 0;
        private int sdqCount = 0;

        private uint[] uniqueIdQueue = new uint[ArrQueue.MinCapacity];
        private int uiqStartIndex = 0;
        private int uiqCount = 0;

        public int QueuedSyncsCount => siqCount;

        // receiving

        private byte[] syncedDataBuffer = null;

        // This method will be called on all clients when the object is enabled and the Owner has been assigned.
        public override void _OnOwnerSet()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  _OnOwnerSet");
            #endif
        }

        // This method will be called on all clients when the original owner has left and the object is about to be disabled.
        public override void _OnCleanup()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  _OnCleanup");
            #endif
        }

        public uint MakeUniqueId()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  MakeUniqueId");
            #endif
            return shiftedPlayerId | (nextInputActionIndex++);
        }

        ///<summary>
        ///Returns the uniqueId for the send input action, or 0 in case of error.
        ///</summary>
        public uint SendInputAction(uint inputActionId, byte[] inputActionData, int inputActionDataSize)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  SendInputAction");
            #endif
            uint index = (nextInputActionIndex++);

            uint prepSyncedInt = (index << InputActionIndexShift) | SplitDataFlag | inputActionId;
            uint uniqueId = 0u;
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  SendInputAction (inner) - inputActionDataSize: {inputActionDataSize}");
            #endif
            for (int startIndex = 0;
                startIndex < inputActionDataSize || (inputActionDataSize == 0 && startIndex == 0);
                startIndex += MaxSyncedDataSize)
            {
                int remainingLength = inputActionDataSize - startIndex;
                int prepSize;
                if (remainingLength > MaxSyncedDataSize)
                    prepSize = MaxSyncedDataSize;
                else
                {
                    prepSize = remainingLength;
                    prepSyncedInt ^= SplitDataFlag;
                    uniqueId = shiftedPlayerId | index;
                }

                byte[] prepSyncedData = new byte[prepSize];
                for (int i = 0; i < prepSize; i++) // I can see the Udon performance crumble in front of my eyes.
                    prepSyncedData[i] = inputActionData[startIndex + i];

                #if LockStepDebug
                Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  SendInputAction (inner) - enqueueing "
                    + $"syncedInt: 0x{prepSyncedInt:X8}, syncedData length: {prepSize}, uniqueId: 0x{uniqueId:X8}");
                #endif
                ArrQueue.Enqueue(ref syncedIntQueue, ref siqStartIndex, ref siqCount, prepSyncedInt);
                ArrQueue.Enqueue(ref syncedDataQueue, ref sdqStartIndex, ref sdqCount, prepSyncedData);
                ArrQueue.Enqueue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount, uniqueId);
            }
            CheckSyncStart();

            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  SendInputAction - uniqueId: 0x{uniqueId:x8}");
            #endif
            return uniqueId;
        }

        public void DequeueEverything(bool doCallback)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  DequeueEverything");
            #endif
            if (siqCount == 0)
                return;

            ArrQueue.Clear(ref syncedIntQueue, ref siqStartIndex, ref siqCount);
            ArrQueue.Clear(ref syncedDataQueue, ref sdqStartIndex, ref sdqCount);

            if (!doCallback || isLateJoinerSyncInst)
                ArrQueue.Clear(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount);
            else
            {
                while (uiqCount != 0)
                {
                    uint uniqueId = ArrQueue.Dequeue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount);
                    if (uniqueId != 0u)
                        lockStep.InputActionSent(uniqueId);
                }
            }

            // Since there was something already in the process of sending, potentially a split input action
            // do still send one set of data indicating that syncing has been aborted, in case any other
            // client is still receiving data from this script.
            // In most cases, if not all, the DequeueEverything function will be called when there aren't
            // any player's receiving data anymore, that's kind of the purpose of this function, however
            // just in case something weird happens with VRChat, this here exists.
            // Not only does it prevent this weird case from causing issues, it also prevents this script
            // here from erroring when PreSerialization does eventually run, because that function
            // expects at least 1 element to be in the queue.
            ArrQueue.Enqueue(ref syncedIntQueue, ref siqStartIndex, ref siqCount, 0u);
            ArrQueue.Enqueue(ref syncedDataQueue, ref sdqStartIndex, ref sdqCount, new byte[0]);
            ArrQueue.Enqueue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount, 0u);
        }

        private void CheckSyncStart()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  CheckSyncStart");
            #endif
            if (!isLateJoinerSyncInst && Owner == null)
            {
                Debug.LogError("[LockStep] Attempt to send input actions when there is no player assigned with the sync script.");
                return;
            }
            RequestSerialization();
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnPreSerialization()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  OnPreSerialization");
            #endif
            if (retrying)
            {
                retrying = false;
                return;
            }

            syncedInt = ArrQueue.Dequeue(ref syncedIntQueue, ref siqStartIndex, ref siqCount);
            syncedData = ArrQueue.Dequeue(ref syncedDataQueue, ref sdqStartIndex, ref sdqCount);
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  OnPostSerialization - success: {result.success}, byteCount: {result.byteCount}");
            #endif
            if (!result.success)
            {
                retrying = true;
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), retryBackoff);
                retryBackoff = Mathf.Min(16f, retryBackoff * 2f);
                return;
            }
            retryBackoff = 1f;

            uint uniqueId = ArrQueue.Dequeue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount);
            if (!isLateJoinerSyncInst && uniqueId != 0u)
                lockStep.InputActionSent(uniqueId);

            if (siqCount != 0)
                SendCustomEventDelayedFrames(nameof(RequestSerializationDelayed), 1);
        }

        private void AppendToSyncedDataBuffer()
        {
            if (syncedDataBuffer == null)
            {
                // syncedData gets modified by Udon during the next deserialization if the data size is the same.
                syncedDataBuffer = new byte[syncedData.Length];
                syncedData.CopyTo(syncedDataBuffer, 0);
                return;
            }
            int oldLength = syncedDataBuffer.Length;
            byte[] biggerBuffer = new byte[oldLength + syncedData.Length];
            syncedDataBuffer.CopyTo(biggerBuffer , 0);
            syncedData.CopyTo(biggerBuffer, oldLength);
            syncedDataBuffer = biggerBuffer;
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] InputActionSync  {this.name}  OnDeserialization");
            #endif
            if ((isLateJoinerSyncInst && lockStepIsMaster) || lockStep == null)
            {
                // When lockStep is still null, this can safely ignore any incoming data,
                // because this script can handle broken partial actions and LockStep
                // doesn't except to get input actions instantly after joining.
                return;
            }

            if (syncedInt == 0u)
            {
                syncedDataBuffer = null;
                return;
            }

            AppendToSyncedDataBuffer();
            if ((syncedInt & SplitDataFlag) != 0u)
                return;

            uint id = syncedInt & InputActionIdBits;
            // Can just right shift because index uses all the highest bits;
            uint index = syncedInt >> InputActionIndexShift;
            lockStep.ReceivedInputAction(isLateJoinerSyncInst, id, shiftedPlayerId | index, syncedDataBuffer);
            syncedDataBuffer = null;
        }
    }
}
