using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Cyan.PlayerObjectPool;
using VRC.Udon.Common;
using VRC.SDK3.Data;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class InputActionSync : CyanPlayerObjectPoolObject
    {
        private const int MaxSyncedDataSize = 512;
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
        [UdonSynced] private string syncedData = "";
        private bool firstSync = true;
        private bool retrying = false;

        private uint[] syncedIntQueue = new uint[ArrQueue.MinCapacity];
        private int siqStartIndex = 0;
        private int siqCount = 0;

        private string[] syncedDataQueue = new string[ArrQueue.MinCapacity];
        private int sdqStartIndex = 0;
        private int sdqCount = 0;

        private uint[] uniqueIdQueue = new uint[ArrQueue.MinCapacity];
        private int uiqStartIndex = 0;
        private int uiqCount = 0;

        public int QueuedSyncsCount => siqCount;

        // receiving

        private string partialSyncedData = null;

        // This method will be called on all clients when the object is enabled and the Owner has been assigned.
        public override void _OnOwnerSet() {
            Debug.Log($"<dlt> InputActionSync  _OnOwnerSet"); }

        // This method will be called on all clients when the original owner has left and the object is about to be disabled.
        public override void _OnCleanup() {
            Debug.Log($"<dlt> InputActionSync  _OnCleanup"); }

        public uint MakeUniqueId()
        {
            Debug.Log($"<dlt> InputActionSync  MakeUniqueId");
            return shiftedPlayerId | (nextInputActionIndex++);
        }

        ///<summary>
        ///Returns the uniqueId for the send input action, or 0 in case of error.
        ///</summary>
        public uint SendInputAction(uint inputActionId, DataList inputActionData)
        {
            Debug.Log($"<dlt> InputActionSync  SendInputAction");
            uint index = (nextInputActionIndex++);
            if (!VRCJson.TrySerializeToJson(inputActionData, JsonExportType.Minify, out DataToken jsonToken))
            {
                Debug.LogError($"<dlt> Unable to serialize data for input action id {inputActionId}, index: {index}"
                    + (isLateJoinerSyncInst ? ", (late joiner sync inst)" : $", player id {Owner.playerId}")
                    + $" : {jsonToken.Error}");
                return 0u;
            }

            uint prepSyncedInt = (index << InputActionIndexShift) | SplitDataFlag | inputActionId;
            uint uniqueId = 0u;
            string jsonString = jsonToken.String;
            int length = jsonString.Length;
            for (int startIndex = 0; startIndex < length; startIndex += MaxSyncedDataSize)
            {
                int remainingLength = length - startIndex;
                string prepSyncedData = jsonString;
                if (remainingLength <= MaxSyncedDataSize)
                {
                    prepSyncedInt ^= SplitDataFlag;
                    uniqueId = shiftedPlayerId | index;
                }
                else
                    prepSyncedData = jsonString.Substring(startIndex, MaxSyncedDataSize);

                ArrQueue.Enqueue(ref syncedIntQueue, ref siqStartIndex, ref siqCount, prepSyncedInt);
                ArrQueue.Enqueue(ref syncedDataQueue, ref sdqStartIndex, ref sdqCount, prepSyncedData);
                ArrQueue.Enqueue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount, uniqueId);
            }
            CheckSyncStart();

            Debug.Log($"<dlt> InputActionSync  SendInputAction - uniqueId: 0x{uniqueId:x8}");
            return uniqueId;
        }

        public void DequeueEverything(bool doCallback)
        {
            Debug.Log($"<dlt> InputActionSync  DequeueEverything");
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
            ArrQueue.Enqueue(ref syncedDataQueue, ref sdqStartIndex, ref sdqCount, "");
            ArrQueue.Enqueue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount, 0u);
        }

        private void CheckSyncStart()
        {
            Debug.Log($"<dlt> InputActionSync  CheckSyncStart");
            if (!isLateJoinerSyncInst && Owner == null)
            {
                Debug.LogError("<dlt> Attempt to send input actions when there is no player assigned with the sync script.");
                return;
            }
            RequestSerialization();
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnPreSerialization()
        {
            Debug.Log($"<dlt> InputActionSync  OnPreSerialization");
            if (firstSync || retrying)
            {
                retrying = false;
                return;
            }

            syncedInt = ArrQueue.Dequeue(ref syncedIntQueue, ref siqStartIndex, ref siqCount);
            syncedData = ArrQueue.Dequeue(ref syncedDataQueue, ref sdqStartIndex, ref sdqCount);
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            Debug.Log($"<dlt> InputActionSync  OnPostSerialization");
            if (!result.success)
            {
                retrying = true;
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), 5f);
                return;
            }

            if (firstSync)
            {
                firstSync = false;
                SendCustomEventDelayedFrames(nameof(RequestSerializationDelayed), 1);
                return;
            }

            uint uniqueId = ArrQueue.Dequeue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount);
            if (!isLateJoinerSyncInst && uniqueId != 0u)
                lockStep.InputActionSent(uniqueId);

            if (siqCount != 0)
                SendCustomEventDelayedFrames(nameof(RequestSerializationDelayed), 1);
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            Debug.Log($"<dlt> InputActionSync  OnDeserialization");
            if (firstSync)
            {
                firstSync = false;
                return;
            }

            if (isLateJoinerSyncInst && lockStepIsMaster)
                return;

            if (syncedInt == 0u)
            {
                partialSyncedData = null;
                return;
            }

            if ((syncedInt & SplitDataFlag) != 0u)
            {
                if (partialSyncedData == null)
                    partialSyncedData = syncedData;
                else
                    partialSyncedData += syncedData;
                return;
            }

            if (partialSyncedData != null)
            {
                syncedData = partialSyncedData + syncedData;
                partialSyncedData = null;
            }

            uint id = syncedInt & InputActionIdBits;
            // Can just right shift because index uses all the highest bits;
            uint index = syncedInt >> InputActionIndexShift;

            if (!VRCJson.TryDeserializeFromJson(syncedData, out DataToken jsonToken))
            {
                // This can legitimately happen when someone joins late and starts receiving
                // data in the middle of a split input action. In that case the data should get
                // ignored anyway, so returning is correct.
                Debug.LogError($"<dlt> Unable to deserialize json for input action id {id}, index: {index}"
                    + (isLateJoinerSyncInst ? ", (late joiner sync inst)" : $", player id {Owner.playerId}")
                    + $" : {jsonToken.Error}\n\nSource json:\n{syncedData}");
                return;
            }

            lockStep.ReceivedInputAction(isLateJoinerSyncInst, id, shiftedPlayerId | index, jsonToken.DataList);
        }
    }
}
