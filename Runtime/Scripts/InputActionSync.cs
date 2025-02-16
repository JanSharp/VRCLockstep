using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Cyan.PlayerObjectPool;
using VRC.Udon.Common;

namespace JanSharp.Internal
{
    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class InputActionSync : CyanPlayerObjectPoolObject
    {
        private const int MaxSyncedDataSize = 4096;
        private const int PlayerIdShift = 32;
        ///<summary>WriteSmall((uint)playerId) never writes 0xfe as the first byte, making this distinguishable.</summary>
        private const byte SplitDataMarker = 0xfe;
        ///<summary>WriteSmall((uint)playerId) never writes 0xff as the first byte, making this distinguishable.</summary>
        private const byte ClearedDataMarker = 0xff;
        ///<summary>WriteSmall((uint)index) never writes 0xff as the first byte, making this distinguishable.</summary>
        private const byte IgnoreRestOfDataMarker = 0xff;

        [System.NonSerialized] public Lockstep lockstep;
        /// <summary>Guaranteed to be <see langword="false"/> on the lockstep master client.</summary>
        [System.NonSerialized] public bool lockstepIsWaitingForLateJoinerSync;
        [System.NonSerialized] public ulong shiftedPlayerId;
        [System.NonSerialized] public uint ownerPlayerId;

        // Who is the current owner of this object. Null if object is not currently in use.
        // [System.NonSerialized] public VRCPlayerApi Owner; // In the base class.

        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private bool isLateJoinerSyncInst = false;

        // First one is 1, making 0 an indication of an invalid index.
        // Since the input action index 0 is invalid, the unique id 0 is also invalid.
        // Unique id is the shifted player index plus the input action index.
        private uint nextInputActionIndex = 1u;

        /// <summary>
        /// <para>The latest input action index either sent or received by this script.</para>
        /// </summary>
        public uint latestInputActionIndex;

        // sending

        private bool retrying = false;
        private float retryBackoff = 1f;

        [UdonSynced] private byte[] syncedData = new byte[1] { ClearedDataMarker }; // Initial value for first sync, which gets ignored.
        private int sendingUniqueIdsCount = 0;
        private bool syncedDataRequiresTimeTracking = false;
        private int syncedDataTimeTrackingIndex = 0;

        private byte[][] dataQueue = new byte[ArrQueue.MinCapacity][];
        private int dqStartIndex = 0;
        private int dqCount = 0;

        ///cSpell:ignore uicq
        private int[] uniqueIdsCountQueue = new int[ArrQueue.MinCapacity];
        private int uicqStartIndex = 0;
        private int uicqCount = 0;

        private int[] timeTrackingIndexQueue = new int[ArrQueue.MinCapacity];
        private int ttiStartIndex = 0;
        private int ttiCount = 0;

        private byte[] stage = null;
        private int stageSize = 0;
        private int stagedUniqueIdCount = 0;
        private bool stageRequiresTimeTracking = false;
        private int stageTimeTrackingIndex = 0;

        /// <summary>
        /// <para>3 * small uint + 2 * float.</para>
        /// </summary>
        private const int MaxHeaderSize = 3 * 5 + 2 * 4;
        private const int PotentialStageSizeOverflowThreshold = MaxSyncedDataSize - MaxHeaderSize;

        /// <summary>
        /// <para>Does not contain 0uL uniqueIds.</para>
        /// </summary>
        private ulong[] uniqueIdQueue = new ulong[ArrQueue.MinCapacity];
        private int uiqStartIndex = 0;
        private int uiqCount = 0;

        public int QueuedBytesCount => dqCount * MaxSyncedDataSize + stageSize;

        // receiving

        private uint sendingPlayerId = 0u; // Initial value does not matter, so long as they match.
        private ulong shiftedSendingPlayerId = 0uL; // Initial value does not matter, so long as they match.

        private bool hasPartialInputAction = false;
        private uint receivedInputActionId;
        private uint receivedInputActionIndex;
        private ulong receivedUniqueId;
        private float receivedSendTime;
        private byte[] receivedData;
        private int partialContinueIndex;
        private int partialMissingSize;
        private bool partialRequiresTimeTracking;
        private float partialSendTime;

        // This method will be called on all clients when the object is enabled and the Owner has been assigned.
        public override void _OnOwnerSet()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  _OnOwnerSet");
            #endif
        }

        // This method will be called on all clients when the original owner has left and the object is about to be disabled.
        public override void _OnCleanup()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  _OnCleanup");
            #endif
        }

        public ulong MakeUniqueId()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  MakeUniqueId");
            #endif
            return shiftedPlayerId | (ulong)(nextInputActionIndex++);
        }

        /// <summary>
        /// <para>Ignores the current <see cref="stageSize"/>, treats it as though it was always
        /// <see cref="MaxSyncedDataSize"/>.</para>
        /// </summary>
        private void MoveStageToQueue()
        {
            byte[] stageCopy = new byte[MaxSyncedDataSize];
            stage.CopyTo(stageCopy, 0);
            ArrQueue.Enqueue(ref dataQueue, ref dqStartIndex, ref dqCount, stageCopy);
            if (stageRequiresTimeTracking)
            {
                stagedUniqueIdCount = -stagedUniqueIdCount; // Almost like using the sign bit as a boolean.
                // Only enqueueing when it's actually needed because Udon is slow, so this is just an optimization.
                ArrQueue.Enqueue(ref timeTrackingIndexQueue, ref ttiStartIndex, ref ttiCount, stageTimeTrackingIndex);
            }
            ArrQueue.Enqueue(ref uniqueIdsCountQueue, ref uicqStartIndex, ref uicqCount, stagedUniqueIdCount);
            stageSize = 0;
            stagedUniqueIdCount = 0;
            stageRequiresTimeTracking = false;
            stageTimeTrackingIndex = 0;
        }

        /// <summary>
        /// <para>Uses <see cref="Lockstep.currentInputActionSendTime"/> when <paramref name="inputActionId"/>
        /// is a timed input action according to
        /// <see cref="Lockstep.inputActionHandlersRequireTimeTracking"/>.</para>
        /// </summary>
        /// <returns>The uniqueId for the sent input action.</returns>
        public ulong SendInputAction(uint inputActionId, byte[] inputActionData, int inputActionDataSize)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  SendInputAction - inputActionId: {inputActionId}, inputActionDataSize: {inputActionDataSize}");
            #endif
            if (stage == null)
                stage = new byte[MaxSyncedDataSize];

            if (stageSize > PotentialStageSizeOverflowThreshold)
            {
                if (stageSize < MaxSyncedDataSize)
                    DataStream.Write(ref stage, ref stageSize, IgnoreRestOfDataMarker);
                // Move the whole MaxSyncedDataSize regardless of if there are a few bytes at the end that are
                // unused, to reduce the amount of times a new array has to be allocated when deserializing
                // due to differing syncedData lengths when syncing lots of data in quick succession.
                MoveStageToQueue();
            }

            // Always send the player id to prevent race conditions around players joining and leaving, because
            // I cannot trust VRChat and the player object pool to ensure that the owner (assigned by the
            // player object pool) to be the same for every client at the time of deserialization.
            if (stageSize == 0)
                DataStream.WriteSmall(ref stage, ref stageSize, ownerPlayerId);

            uint index = (nextInputActionIndex++);
            ulong uniqueId = shiftedPlayerId | index;
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  SendInputAction (inner) - uniqueId: 0x{uniqueId:x16}");
            #endif

            // Write IA header. This is what MaxHeaderSize correlates to.
            DataStream.WriteSmall(ref stage, ref stageSize, index);
            DataStream.WriteSmall(ref stage, ref stageSize, inputActionId);
            float sendTime = lockstep.currentInputActionSendTime; // Fetch into local as an optimization.
            if (lockstep.inputActionHandlersRequireTimeTracking[inputActionId])
            {
                if (!stageRequiresTimeTracking)
                {
                    stageRequiresTimeTracking = true;
                    stageTimeTrackingIndex = stageSize;
                    stageSize += 4; // Reserve 4 bytes, which will be written to right before sending.
                }
                DataStream.Write(ref stage, ref stageSize, sendTime);
            }
            DataStream.WriteSmall(ref stage, ref stageSize, (uint)inputActionDataSize);

            int baseIndex = 0;
            int remainingLength = inputActionDataSize;
            int freeSpace = MaxSyncedDataSize - stageSize;
            while (remainingLength > freeSpace)
            {
                // #if LockstepDebug
                // Debug.Log($"[LockstepDebug] {this.name}  SendInputAction (inner) - stageSize: {stageSize}, baseIndex: {baseIndex}, remainingLength: {remainingLength}, freeSpace: {freeSpace}");
                // #endif
                System.Array.Copy(inputActionData, baseIndex, stage, stageSize, freeSpace);
                MoveStageToQueue();
                // Instead of starting with WriteSmall((uint)ownerPlayerId), write SplitDataMarker.
                DataStream.Write(ref stage, ref stageSize, SplitDataMarker);
                baseIndex += freeSpace;
                remainingLength -= freeSpace;
                freeSpace = MaxSyncedDataSize - stageSize;
            }
            // #if LockstepDebug
            // Debug.Log($"[LockstepDebug] {this.name}  SendInputAction (inner) - stageSize: {stageSize}, baseIndex: {baseIndex}, remainingLength: {remainingLength}, freeSpace: {freeSpace}");
            // #endif
            System.Array.Copy(inputActionData, baseIndex, stage, stageSize, remainingLength);
            stageSize += remainingLength;

            ArrQueue.Enqueue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount, uniqueId);
            stagedUniqueIdCount++;

            CheckSyncStart();
            return uniqueId;
        }

        public void AddUniqueIdsWaitingToBeSentToHashSet(VRC.SDK3.Data.DataDictionary lut)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  AddUniqueIdsWaitingToBeSentToHashSet - uiqCount: {uiqCount}");
            #endif
            int length = uniqueIdQueue.Length;
            for (int i = 0; i < uiqCount; i++)
                lut.Add(uniqueIdQueue[(uiqStartIndex + i) % length], true);
        }

        public void DequeueEverything(bool doCallback)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  DequeueEverything");
            #endif
            if (uiqCount == 0) // Absolutely nothing is being sent out right now, nothing to do.
                return;

            if (doCallback && !isLateJoinerSyncInst && uiqCount != 0)
            {
                int length = uniqueIdQueue.Length;
                ulong uniqueId = 0uL;
                for (int i = 0; i < uiqCount; i++)
                {
                    uniqueId = uniqueIdQueue[(uiqStartIndex + i) % length];
                    lockstep.InputActionSent(uniqueId);
                }
                latestInputActionIndex = (uint)(uniqueId & Lockstep.InputActionIndexBits);
            }
            ArrQueue.Clear(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount);

            stageSize = 0;
            stagedUniqueIdCount = 0;
            stageRequiresTimeTracking = false;
            stageTimeTrackingIndex = 0;
            ArrQueue.Clear(ref dataQueue, ref dqStartIndex, ref dqCount);
            ArrQueue.Clear(ref uniqueIdsCountQueue, ref uicqStartIndex, ref uicqCount);
            ArrQueue.Clear(ref timeTrackingIndexQueue, ref ttiStartIndex, ref ttiCount);

            // Since there was something already in the process of sending, potentially a split input action
            // do still send one set of data indicating that syncing has been aborted, in case any other
            // client is still receiving data from this script.
            // In most cases, if not all, the DequeueEverything function will be called when there aren't
            // any player's receiving data anymore, that's kind of the purpose of this function, however
            // just in case something weird happens with VRChat, this here exists.
            // This is using 0xfe as a special value
            syncedData = new byte[1] { ClearedDataMarker };
            sendingUniqueIdsCount = 0;
            syncedDataRequiresTimeTracking = false;
            syncedDataTimeTrackingIndex = 0;
            // Abuse retrying because it causes serialization to just send whatever is currently set in the
            // syncedData variable without touching the stage or queue or anything.
            // And by setting sendingUniqueIdCount it also doesn't touch the unique id queue.
            retrying = true;
        }

        private void CheckSyncStart()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  CheckSyncStart");
            #endif
            if (!isLateJoinerSyncInst && Owner == null)
            {
                Debug.LogError("[Lockstep] Attempt to send input actions when there is no player assigned with the sync script.");
                return;
            }
            RequestSerialization();
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnPreSerialization()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  OnPreSerialization");
            #endif
            if (retrying)
            {
                retrying = false;
                if (syncedDataRequiresTimeTracking)
                {
                    int temp = syncedDataTimeTrackingIndex; // Do not modify syncedDataTimeTrackingIndex.
                    DataStream.Write(ref syncedData, ref temp, Time.realtimeSinceStartup); // Fetch time as late as possible.
                }
                return;
            }

            if (stage == null) // No input actions have been sent yet.
            {
                syncedData = new byte[1] { ClearedDataMarker };
                syncedDataRequiresTimeTracking = false;
                syncedDataTimeTrackingIndex = 0;
                return;
            }

            if (dqCount != 0)
            {
                // Take from the queue.
                syncedData = ArrQueue.Dequeue(ref dataQueue, ref dqStartIndex, ref dqCount);
                sendingUniqueIdsCount = ArrQueue.Dequeue(ref uniqueIdsCountQueue, ref uicqStartIndex, ref uicqCount);
                syncedDataRequiresTimeTracking = sendingUniqueIdsCount < 0;
                if (syncedDataRequiresTimeTracking)
                {
                    sendingUniqueIdsCount = -sendingUniqueIdsCount;
                    syncedDataTimeTrackingIndex = ArrQueue.Dequeue(ref timeTrackingIndexQueue, ref ttiStartIndex, ref ttiCount);
                    int temp = syncedDataTimeTrackingIndex; // Do not modify syncedDataTimeTrackingIndex.
                    DataStream.Write(ref syncedData, ref temp, Time.realtimeSinceStartup); // Fetch time as late as possible.
                }
                return;
            }

            // Take the current stage and then clear the stage.
            syncedData = new byte[stageSize];
            syncedDataRequiresTimeTracking = stageRequiresTimeTracking;
            syncedDataTimeTrackingIndex = stageTimeTrackingIndex;
            System.Array.Copy(stage, syncedData, stageSize);
            sendingUniqueIdsCount = stagedUniqueIdCount;
            stageSize = 0;
            stagedUniqueIdCount = 0;
            stageRequiresTimeTracking = false;
            stageTimeTrackingIndex = 0;
            if (syncedDataRequiresTimeTracking)
            {
                int temp = syncedDataTimeTrackingIndex; // Do not modify syncedDataTimeTrackingIndex.
                DataStream.Write(ref syncedData, ref temp, Time.realtimeSinceStartup); // Fetch time as late as possible.
            }
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            #if LockstepDebug
            // syncedData should be impossible to be null, but well these debug messages are there for when the unexpected happens.
            Debug.Log($"[LockstepDebug] {this.name}  OnPostSerialization - success: {result.success}, byteCount: {result.byteCount}, syncedData.Length: {(syncedData == null ? "null" : syncedData.Length.ToString())}");
            #endif
            if (!result.success)
            {
                retrying = true;
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), retryBackoff);
                retryBackoff = Mathf.Min(16f, retryBackoff * 2f);
                return;
            }
            retryBackoff = 1f;

            for (int i = 0; i < sendingUniqueIdsCount; i++)
            {
                ulong uniqueId = ArrQueue.Dequeue(ref uniqueIdQueue, ref uiqStartIndex, ref uiqCount);
                if (!isLateJoinerSyncInst)
                {
                    latestInputActionIndex = (uint)(uniqueId & Lockstep.InputActionIndexBits);
                    lockstep.InputActionSent(uniqueId);
                }
            }

            if (uiqCount != 0)
                SendCustomEventDelayedFrames(nameof(RequestSerializationDelayed), 1);
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            #if LockstepDebug
            // syncedData should be impossible to be null, but well these debug messages are there for when the unexpected happens.
            Debug.Log($"[LockstepDebug] {this.name}  OnDeserialization - syncedData.Length: {(syncedData == null ? "null" : syncedData.Length.ToString())}");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            #endif
            if ((isLateJoinerSyncInst && !lockstepIsWaitingForLateJoinerSync) || lockstep == null)
            {
                // When lockstep is still null, this can safely ignore any incoming data,
                // because this script can handle broken partial actions and Lockstep
                // doesn't except to get input actions instantly after joining.
                return;
            }

            byte firstByte = syncedData[0];
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] {this.name}  OnDeserialization (inner) - firstByte: 0x{firstByte:x2} or {firstByte}");
            #endif
            if (firstByte == ClearedDataMarker)
            {
                hasPartialInputAction = false;
                receivedData = null;
                return;
            }

            int i = 0;
            int syncedDataLength = syncedData.Length;
            if (firstByte == SplitDataMarker)
            {
                if (!hasPartialInputAction)
                    return; // We just joined, this data is not for us.
                i++;
                int bytesToRead = System.Math.Min(syncedDataLength - i, partialMissingSize);
                System.Array.Copy(syncedData, i, receivedData, partialContinueIndex, bytesToRead);
                i += bytesToRead;
                partialContinueIndex += bytesToRead;
                partialMissingSize -= bytesToRead;
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] {this.name}  OnDeserialization (inner) - partialContinueIndex: {partialContinueIndex}, partialMissingSize: {partialMissingSize}");
                #endif
                if (partialMissingSize == 0)
                {
                    hasPartialInputAction = false;
                    latestInputActionIndex = receivedInputActionIndex;
                    lockstep.ReceivedInputAction(isLateJoinerSyncInst, receivedInputActionId, receivedUniqueId, receivedSendTime, receivedData);
                }
            }
            else
            {
                if (hasPartialInputAction)
                {
                    Debug.LogError("[Lockstep] Expected continuation of split up partial input action data, "
                        + "but didn't receive as such. This is very most likely an unrecoverable state for "
                        + "the system, but just in case someone just tried sending data through malicious "
                        + "means this data gets ignored.");
                    return;
                }
                uint playerId = DataStream.ReadSmallUInt(ref syncedData, ref i);
                if (playerId != sendingPlayerId)
                {
                    sendingPlayerId = playerId;
                    shiftedSendingPlayerId = (ulong)playerId << PlayerIdShift;
                    hasPartialInputAction = false;
                    receivedData = null;
                }
            }

            bool didReadSerializationTime = false;
            float serializationTime = float.NaN;

            while (i < syncedDataLength && syncedData[i] != IgnoreRestOfDataMarker)
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] {this.name}  OnDeserialization (inner) - bytes left (syncedDataLength - i): {syncedDataLength - i}");
                #endif
                // Read IA header.
                receivedInputActionIndex = DataStream.ReadSmallUInt(ref syncedData, ref i);
                receivedUniqueId = shiftedSendingPlayerId | (ulong)receivedInputActionIndex;
                receivedInputActionId = DataStream.ReadSmallUInt(ref syncedData, ref i);
                if (!lockstep.inputActionHandlersRequireTimeTracking[receivedInputActionId])
                    receivedSendTime = Lockstep.SendTimeForNonTimedIAs;
                else
                {
                    if (!didReadSerializationTime)
                    {
                        didReadSerializationTime = true;
                        serializationTime = DataStream.ReadFloat(ref syncedData, ref i);
                    }
                    float sendTime = DataStream.ReadFloat(ref syncedData, ref i);
                    receivedSendTime = result.sendTime - (serializationTime - sendTime);
                    #if LockstepDebug
                    Debug.Log($"[LockstepDebug] {this.name}  OnDeserialization (inner) - receivedSendTime: {receivedSendTime}, sendTime: {sendTime}, serializationTime: {serializationTime}, result.sendTime: {result.sendTime}, result.receiveTime: {result.receiveTime}, Time.realtimeSinceStartup: {Time.realtimeSinceStartup}");
                    #endif
                }
                int dataLength = (int)DataStream.ReadSmallUInt(ref syncedData, ref i);
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] {this.name}  OnDeserialization (inner) - receivedUniqueId: 0x{receivedUniqueId:x16}, receivedInputActionId: {receivedInputActionId}, dataLength: {dataLength}");
                #endif
                receivedData = new byte[dataLength];
                if (i + dataLength > syncedDataLength)
                {
                    hasPartialInputAction = true;
                    int rest = syncedDataLength - i;
                    System.Array.Copy(syncedData, i, receivedData, 0, rest);
                    partialContinueIndex = rest;
                    partialMissingSize = dataLength - rest;
                    #if LockstepDebug
                    Debug.Log($"[LockstepDebug] {this.name}  OnDeserialization (inner) - partialContinueIndex: {partialContinueIndex}, partialMissingSize: {partialMissingSize}");
                    #endif
                    break;
                }
                System.Array.Copy(syncedData, i, receivedData, 0, dataLength);
                i += dataLength;
                latestInputActionIndex = receivedInputActionIndex;
                lockstep.ReceivedInputAction(isLateJoinerSyncInst, receivedInputActionId, receivedUniqueId, receivedSendTime, receivedData);
            }
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] [sw] {this.name}  OnDeserialization (inner) - ms: {sw.Elapsed.TotalMilliseconds}");
            #endif
        }
    }
}
