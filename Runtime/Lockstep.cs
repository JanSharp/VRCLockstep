using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp.Internal
{
    internal enum ClientState : byte
    {
        Master,
        WaitingForLateJoinerSync,
        CatchingUp,
        Normal,
    }

    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Lockstep : LockstepAPI
    {
        public const float TickRate = 10f;

        // LJ = late joiner, IA = input action
        private const uint LJCurrentTickIAId = 0;
        private const uint LJInternalGameStatesIAId = 1;
        // custom game states will have ids starting at this, ascending
        private const uint LJFirstCustomGameStateIAId = 2;

        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private InputActionSync lateJoinerInputActionSync;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private LockstepTickSync tickSync;
        [System.NonSerialized] public uint currentTick;
        [System.NonSerialized] public uint waitTick; // The system will run this tick, but not past it.
        public override uint CurrentTick => currentTick;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;
        private string localPlayerDisplayName;
        private InputActionSync inputActionSyncForLocalPlayer;
        /// <summary>
        /// <para>**Internal Game State**</para>
        /// </summary>
        private uint masterPlayerId;
        private uint startTick;
        private uint firstMutableTick; // Effectively 1 tick past the last immutable tick.
        private float tickStartTime;
        private int byteCountForLatestLJSync = -1;
        // private uint resetTickRateTick = uint.MaxValue; // At the end of this tick it gets reset to TickRate.
        // private float currentTickRate = TickRate;
        private bool isTickPaused = true;
        private bool isMaster = false;
        private bool ignoreLocalInputActions = true;
        private bool stillAllowLocalClientJoinedIA = false;
        private bool ignoreIncomingInputActions = true;
        private bool isWaitingToSendClientJoinedIA = true;
        private bool isWaitingForLateJoinerSync = false;
        private bool sendLateJoinerDataAtEndOfTick = false;
        private bool isCatchingUp = false;
        private bool isInitialCatchUp = true;
        private bool isSinglePlayer = false;
        private bool checkMasterChangeAfterProcessingLJGameStates = false;
        // Only true for the initial catch up to avoid it being true for just a few ticks seemingly randomly
        // (Caused by the master changing which requires catching up to what the previous master had already
        // processed. See IsMaster property.)
        public override bool IsCatchingUp => isCatchingUp && isInitialCatchUp;
        public override bool IsSinglePlayer => isSinglePlayer;
        public override bool CanSendInputActions => !ignoreLocalInputActions;

        private ulong unrecoverableStateDueToUniqueId = 0uL;

        ///cSpell:ignore xxpppppp

        public const int PlayerIdKeyShift = 32;
        // ulong uniqueId => objet[] { uint inputActionId, byte[] inputActionData }
        // uniqueId: pppppppp pppppppp pppppppp pppppppp iiiiiiii iiiiiiii iiiiiiii iiiiiiii
        // (p = player id, i = input action index)
        //
        // Unique ids associated with their input actions, all of which are input actions
        // which have not been run yet and are either waiting for the tick in which they will be run,
        // or waiting for tick sync to inform this client of which tick to run them in.
        private DataDictionary inputActionsByUniqueId = new DataDictionary();

        // uint => ulong[]
        // uint: tick to run in
        // ulong[]: unique ids (same as for inputActionsByUniqueId)
        private DataDictionary uniqueIdsByTick = new DataDictionary();

        ///cSpell:ignore iatrn
        ///<summary>(objet[] { uint inputActionId, byte[] inputActionData })[]</summary>
        private object[][] inputActionsToRunNextFrame = new object[ArrList.MinCapacity][];
        private int iatrnCount = 0;

        ///<summary>**Internal Game State**</summary>
        private uint nextSingletonId = 0;
        ///<summary><para>**Internal Game State**</para>
        ///<para>uint singletonId => objet[] { uint responsiblePlayerId, byte[] singletonInputActionData }</para></summary>
        private DataDictionary singletonInputActions = new DataDictionary();

        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] inputActionHandlerInstances;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private string[] inputActionHandlerEventNames;

        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onInitListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onClientJoinedListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onClientBeginCatchUpListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onClientCaughtUpListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onClientLeftListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onMasterChangedListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onTickListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onImportStartListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onImportedGameStateListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onImportFinishedListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onGameStatesToAutosaveChangedListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onAutosaveIntervalSecondsChangedListeners;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private UdonSharpBehaviour[] onIsAutosavePausedChangedListeners;

        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private LockstepGameState[] allGameStates;
        // string internalName => LockstepGameState gameState
        private DataDictionary gameStatesByInternalName = new DataDictionary();
        // string internalName => int indexInAllGameStates
        private DataDictionary gameStateIndexesByInternalName = new DataDictionary();
        public override LockstepGameState[] AllGameStates
        {
            get
            {
                int length = allGameStates.Length;
                LockstepGameState[] result = new LockstepGameState[length];
                for (int i = 0; i < length; i++)
                    result[i] = allGameStates[i];
                return result;
            }
        }
        public override int AllGameStatesCount => allGameStates.Length;

        ///<summary><para>**Internal Game State**</para>
        ///<para>uint playerId => byte ClientState</para></summary>
        private DataDictionary clientStates = null;
        ///<summary><para>**Internal Game State**</para>
        ///<para>uint playerId => string playerDisplayName</para></summary>
        private DataDictionary clientNames = null;
        /// <summary>
        /// <para>Game state safe inside of <see cref="GetDisplayName(uint)"/>.</para>
        /// </summary>
        private string leftClientName = null;
        // non game state
        private uint[] leftClients = new uint[ArrList.MinCapacity];
        private int leftClientsCount = 0;
        // This flag ultimately indicates that there is no client with the Master state in the clientStates game state
        private bool currentlyNoMaster = true;

        private int clientsJoinedInTheLastFiveMinutes = 0;
        private byte[][] unprocessedLJSerializedGameStates = new byte[ArrList.MinCapacity][];
        private int unprocessedLJSerializedGSCount = 0;
        private int nextLJGameStateToProcess = -1;
        private float nextLJGameStateToProcessTime = 0f;
        private const float LJGameStateProcessingFrequency = 0.1f;
        private bool IsProcessingLJGameStates => nextLJGameStateToProcess != -1;

        private int flagForLateJoinerSyncSentCount = 0;
        private int processLeftPlayersSentCount = 0;
        private int checkOtherMasterCandidatesSentCount = 0;
        private int someoneLeftWhileWeWereWaitingForLJSyncSentCount = 0;

        // Used by the debug UI.
        private System.Diagnostics.Stopwatch lastUpdateSW = new System.Diagnostics.Stopwatch();

        // The actions run during catch up are actions that
        // have already been run by the previous master. Therefore this must return false, otherwise this
        // IsMaster property would return true on 2 clients when running the same input action.
        // I _think_ that this covers all edge cases, since if those input actions were to use this IsMaster
        // to modify the game state, then the only way to do that is though sending another input action. If
        // the master leaves shortly after doing so, those newly sent input actions would not even get
        // associated with a tick to run in, which means that IsMaster then being true in an input action
        // twice would be fine, because some other client became master and is running still queued actions,
        // however it won't run newly sent input actions twice, because remember how the ones sent from the
        // original master before leaving didn't get associated with a tick.
        // Now if they _did_ get associated with a tick then we can safely assume that at least 1 tick has
        // passed since the moment where they got sent, That means waitTick has advanced past the original
        // input action that sent the new input actions. Technically there is an edge case where if the ticks
        // do not advance at all during this entire time and it ends up syncing the tick association without
        // advancing any ticks, and the master leaves immediately afterwards, then another client would run
        // an input action with IsMaster being true again, which would be incorrect behavior. But the only
        // way this should be possible to happen is if ticks get paused using the tick pase boolean, which is
        // currently not possible and should never be possible.
        public override bool IsMaster => isMaster && !isCatchingUp; // Even for non initial catch ups.
        public override uint MasterPlayerId => masterPlayerId;
        private uint oldMasterPlayerId;
        private uint joinedPlayerId;
        private uint leftPlayerId;
        private uint catchingUpPlayerId;
        private uint sendingPlayerId;
        public override uint OldMasterPlayerId => oldMasterPlayerId;
        public override uint JoinedPlayerId => joinedPlayerId;
        public override uint LeftPlayerId => leftPlayerId;
        public override uint CatchingUpPlayerId => catchingUpPlayerId;
        public override uint SendingPlayerId => sendingPlayerId;
        private ulong sendingUniqueId;
        public override ulong SendingUniqueId => sendingUniqueId;

        private void Start()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  Start");
            #endif
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            lateJoinerInputActionSync.lockstep = this;

            int i = 0;
            foreach (LockstepGameState gameState in allGameStates)
            {
                DataToken key = gameState.GameStateInternalName;
                gameStatesByInternalName.Add(key, gameState);
                gameStateIndexesByInternalName.Add(key, i++);
            }
        }

        private void Update()
        {
            lastUpdateSW.Reset();
            lastUpdateSW.Start();

            if (isTickPaused)
            {
                if (IsProcessingLJGameStates && Time.time >= nextLJGameStateToProcessTime)
                    ProcessNextLJSerializedGameState();
                lastUpdateSW.Stop();
                return;
            }

            if (isCatchingUp)
            {
                CatchUp();
                lastUpdateSW.Stop();
                return;
            }

            if (iatrnCount != 0)
                RunInputActionsForThisFrame();

            float timePassed = Time.time - tickStartTime;
            uint runUntilTick = System.Math.Min(waitTick, startTick + (uint)(timePassed * TickRate));
            for (uint tick = currentTick + 1; tick <= runUntilTick; tick++)
            {
                // This is the correct place for this logic because:
                // This logic must not run while catching up, so moving it into TryRunNextTick would be wrong.
                // But what about TryRunNextTick returning false? It won't be, because:
                // sendLateJoinerDataAtEndOfTick is only going to be true on the master.
                // For the master client, TryRunNextTick is guaranteed to return true - actually run the tick.
                if (sendLateJoinerDataAtEndOfTick && currentTick > firstMutableTick && !isImporting)
                {
                    // Waiting until the first mutable tick, because if the master was catching up and
                    // associating new input actions with ticks while doing so, those would be enqueued at
                    // the current first mutable tick at that time. Incoming player joined actions would also
                    // be enqueued at the end just the same. This means if late joiner sync data was to be
                    // sent before all these input actions that were added during catch up have run, then
                    // there could be a case where a client receives late joiner sync data at a tick before
                    // it knows which input actions were associated with ticks right after receiving LJ data.
                    // It would think that there are no actions there, so it would desync. This edge case
                    // requires multiple players to join while the master is still catching up.
                    sendLateJoinerDataAtEndOfTick = false;
                    SendLateJoinerData();
                }
                if (!TryRunNextTick())
                    break;
            }

            if (isMaster)
            {
                // Synced tick is always 1 behind, that way new input actions can be run in
                // the current tick on the master without having to queue them for the next tick.
                tickSync.currentTick = currentTick - 1u;
            }

            lastUpdateSW.Stop();
        }

        private void CatchUp()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CatchUp");
            #endif
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            while (currentTick != waitTick
                && TryRunNextTick()
                && stopwatch.ElapsedMilliseconds < 10L)
            { }
            stopwatch.Stop(); // I don't think this actually matters, but it's not used anymore so sure.

            // As soon as we are within 1 second of the current tick, consider it done catching up.
            // This little leeway is required, as it may not be able to reach waitTick because
            // input actions may arrive after tick sync data.
            // Run all the way to waitTick when isMaster, otherwise other clients would most likely desync.
            if (isMaster ? currentTick == waitTick : waitTick - currentTick < TickRate)
            {
                RemoveOutdatedUniqueIdsByTick();
                isCatchingUp = false;
                SendClientCaughtUpIA(); // Uses isInitialCatchUp.
                isInitialCatchUp = false;
                StartOrStopAutosave();
                startTick = currentTick;
                tickStartTime = Time.time;
                if (isMaster)
                    FinishCatchingUpOnMaster();
            }
        }

        private void RemoveOutdatedUniqueIdsByTick()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RemoveOutdatedUniqueIdsByTick");
            #endif
            DataList keys = uniqueIdsByTick.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken tickToRunToken = keys[i];
                if (tickToRunToken.UInt > currentTick)
                    continue;

                uniqueIdsByTick.Remove(tickToRunToken, out DataToken uniqueIdsToken);
                foreach (ulong uniqueId in (ulong[])uniqueIdsToken.Reference)
                    inputActionsByUniqueId.Remove(uniqueId); // Remove simply does nothing if it already doesn't exist.
            }
        }

        private bool TryRunNextTick()
        {
            uint nextTick = currentTick + 1u;
            DataToken nextTickToken = nextTick;
            ulong[] uniqueIds = null;
            if (uniqueIdsByTick.TryGetValue(nextTickToken, out DataToken uniqueIdsToken))
            {
                uniqueIds = (ulong[])uniqueIdsToken.Reference;
                foreach (ulong uniqueId in uniqueIds)
                    if (!inputActionsByUniqueId.ContainsKey(uniqueId))
                    {
                        uint playerId = (uint)(uniqueId >> PlayerIdKeyShift);
                        if (uniqueId != unrecoverableStateDueToUniqueId && !clientStates.ContainsKey(playerId))
                        {
                            // This variable is purely used as to not spam the log file every frame with this error message.
                            unrecoverableStateDueToUniqueId = uniqueId;
                            /// cSpell:ignore desync, desyncs
                            Debug.LogError($"[Lockstep] There's an input action queued to run on the tick {nextTick} "
                                + $"originating from the player {playerId}, however the input action "
                                + $"with the unique id {uniqueId} was never received and the given player id "
                                + $"is not in the instance. This is an error state the system "
                                + $"cannot recover from, as ignoring the input action would be a desync. "
                                + $"If the system does magically recover from this then the input action with "
                                + $"given unique id does get received at a later point somehow. I couldn't tell "
                                + $"you how though.");
                        }
                        return false;
                    }
                uniqueIdsByTick.Remove(nextTickToken);
            }

            RaiseOnTick(); // At the end of a tick.
            currentTick = nextTick;
            // Slowly increase the immutable tick. This prevents potential lag spikes when many input actions
            // were sent while catching up. This approach also prevents being caught catching up forever by
            // not touching waitTick.
            // Still continue increasing it even when done catching up, for the same reason: no lag spikes.
            if ((currentTick % TickRate) == 0u)
                firstMutableTick++;
            #if LockstepDebug
            // Debug.Log($"[DebugLockstep] Running tick {currentTick}");
            #endif
            if (uniqueIds != null)
                RunInputActionsForUniqueIds(uniqueIds);
            return true;
        }

        private void RunInputActionsForUniqueIds(ulong[] uniqueIds)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionsForUniqueIds");
            #endif
            foreach (ulong uniqueId in uniqueIds)
                RunInputActionForUniqueId(uniqueId);
        }

        private void RunInputActionForUniqueId(ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionForUniqueId");
            #endif
            inputActionsByUniqueId.Remove(uniqueId, out DataToken inputActionDataToken);
            object[] inputActionData = (object[])inputActionDataToken.Reference;
            RunInputAction((uint)inputActionData[0], (byte[])inputActionData[1], uniqueId);
        }

        private void RunInputActionsForThisFrame()
        {
            for (int i = 0; i < iatrnCount; i++)
            {
                object[] inputActionData = inputActionsToRunNextFrame[i];
                RunInputAction((uint)inputActionData[0], (byte[])inputActionData[1], (ulong)inputActionData[2]);
                inputActionsToRunNextFrame[i] = null;
            }
            iatrnCount = 0;
        }

        private void ForgetAboutInputActionsForThisFrame()
        {
            for (int i = 0; i < iatrnCount; i++)
                inputActionsToRunNextFrame[i] = null; // Just to ensure that memory can be freed.
            iatrnCount = 0;
        }

        private void RunInputAction(uint inputActionId, byte[] inputActionData, ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputAction");
            #endif
            ResetReadStream();
            readStream = inputActionData;
            RunInputActionWithCurrentReadStream(inputActionId, uniqueId);
        }

        private void RunInputActionWithCurrentReadStream(uint inputActionId, ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionWithCurrentReadStream");
            #endif
            UdonSharpBehaviour inst = inputActionHandlerInstances[inputActionId];
            sendingPlayerId = (uint)(uniqueId >> PlayerIdKeyShift);
            sendingUniqueId = uniqueId;
            // This provides the guarantee that input actions sent by clients which have left the instance and
            // for which the OnClientLeft event has already been raised will not be run. This is only an
            // incredibly unlikely edge case since the system waits 1 second after the player left until the
            // client left input action gets sent, however since we cannot trust and rely on timing of
            // serialized data received from VRChat's networking system, I cannot be sure that there won't be
            // some input action(s) received even after 1 second after the player left event from VRChat.
            // It sucks because this is overhead for every single input action that gets run, however I
            // couldn't think of another solution that wouldn't introduce other issues.
            // The clientJoinedIAId is of course allowed regardless, since that's how a client gets added to
            // clientStates in the first place.
            if (inputActionId != clientJoinedIAId && !clientStates.ContainsKey(sendingPlayerId))
            {
                #if LockstepDebug
                Debug.Log($"[LockStepDebug] The player id {sendingPlayerId} is not in the client states game"
                    + $"state and therefore running input actions sent by this player id is invalid. This "
                    + $"input action is ignored. This is supposed to be an incredibly rare edge case, so "
                    + $"so that it should effectively never happen.");
                #endif
                return;
            }
            inst.SendCustomEvent(inputActionHandlerEventNames[inputActionId]);
        }

        private bool IsAllowedToSendInputActionId(uint inputActionId)
        {
            return !ignoreLocalInputActions || (stillAllowLocalClientJoinedIA && inputActionId == clientJoinedIAId);
        }

        public override ulong SendInputAction(uint inputActionId)
        {
            return SendInputAction(inputActionId, forceOneFrameDelay: true);
        }

        private ulong SendInputAction(uint inputActionId, bool forceOneFrameDelay)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendInputAction - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            if (!IsAllowedToSendInputActionId(inputActionId))
            {
                ResetWriteStream();
                return 0uL;
            }

            byte[] inputActionData = new byte[writeStreamSize];
            for (int i = 0; i < writeStreamSize; i++)
                inputActionData[i] = writeStream[i];
            ResetWriteStream();

            return SendInputActionInternal(inputActionId, inputActionData, forceOneFrameDelay);
        }

        private ulong SendInputActionInternal(uint inputActionId, byte[] inputActionData, bool forceOneFrameDelay)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendInputActionInternal - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            if (isSinglePlayer) // Guaranteed to be master while in single player.
                return TryToInstantlyRunInputActionOnMaster(inputActionId, 0uL, inputActionData, forceOneFrameDelay);

            ulong uniqueId = inputActionSyncForLocalPlayer.SendInputAction(inputActionId, inputActionData, inputActionData.Length);
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendInputActionInternal (inner) - uniqueId: 0x{uniqueId:x16}");
            #endif

            if (stillAllowLocalClientJoinedIA)
            {
                if (ignoreLocalInputActions)
                {
                    #if LockstepDebug
                    Debug.Log($"[LockstepDebug] Lockstep  SendInputAction (inner) - ignoreLocalInputActions is true, returning");
                    #endif
                    return 0uL; // Do not save client joined IA because it will never be executed locally.
                }
                Debug.LogError("[Lockstep] stillAllowLocalClientJoinedIA is true while ignoreLocalInputActions is false. "
                    + "This is an invalid state, stillAllowLocalClientJoinedIA should only ever be true if "
                    + "ignoreLocalInputActions is also true. Continuing as though stillAllowLocalClientJoinedIA was false.");
            }

            inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData }));
            return uniqueId;
        }

        ///<summary><para>Unlike SendInputAction, SendSingletonInputAction must only be called from within a
        ///game state safe event.</para>
        ///<para>Returns the unique id of the input action that got sent. If OnInit or OnClientBeginCatchUp
        ///have not run yet then it will return 0uL - an invalid id - indicating that it did not get sent.
        ///It'll also return 0uL on all clients except for the responsible player, since all those clients
        ///inherently do not send any action.</para>
        ///<para>The intended purpose of this return value in combination with SendingUniqueId is making
        ///latency state implementations with latency hiding easier.</para></summary>
        public override ulong SendSingletonInputAction(uint inputActionId)
        {
            return SendSingletonInputAction(inputActionId, masterPlayerId);
        }

        ///<summary><para>Unlike SendInputAction, SendSingletonInputAction must only be called from within a
        ///game state safe event.</para>
        ///<para>Returns the unique id of the input action that got sent. If OnInit or OnClientBeginCatchUp
        ///have not run yet then it will return 0uL - an invalid id - indicating that it did not get sent.
        ///It'll also return 0uL on all clients except for the responsible player, since all those clients
        ///inherently do not send any action.</para>
        ///<para>The intended purpose of this return value in combination with SendingUniqueId is making
        ///latency state implementations with latency hiding easier.</para></summary>
        public override ulong SendSingletonInputAction(uint inputActionId, uint responsiblePlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendSingletonInputAction - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            if (!IsAllowedToSendInputActionId(inputActionId))
            {
                ResetWriteStream();
                return 0uL;
            }

            uint singletonId = nextSingletonId++;

            // Write 2 more values to the stream, then shuffle the data around when copying to the
            // singletonInputActionData such that those 2 new values come first, not last.
            int actualInputActionDataSize = writeStreamSize;
            WriteSmall(singletonId);
            WriteSmall(inputActionId);
            byte[] singletonInputActionData = new byte[writeStreamSize];
            int idsSize = writeStreamSize - actualInputActionDataSize;
            for (int i = 0; i < idsSize; i++)
                singletonInputActionData[i] = writeStream[actualInputActionDataSize + i];
            for (int i = 0; i < actualInputActionDataSize; i++)
                singletonInputActionData[idsSize + i] = writeStream[i];
            ResetWriteStream();

            singletonInputActions.Add(singletonId, new DataToken(new object[] { responsiblePlayerId, singletonInputActionData }));

            if (localPlayerId != responsiblePlayerId)
                return 0uL;

            return SendInputActionInternal(singletonInputActionIAId, singletonInputActionData, forceOneFrameDelay: true);
        }

        [SerializeField] [HideInInspector] private uint singletonInputActionIAId;
        [LockstepInputAction(nameof(singletonInputActionIAId))]
        public void OnSingletonInputActionIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnSingletonInputActionIA");
            #endif
            uint singletonId = ReadSmallUInt();
            uint inputActionId = ReadSmallUInt();
            singletonInputActions.Remove(singletonId);
            RunInputActionWithCurrentReadStream(inputActionId, SendingUniqueId);
        }

        private void CheckIfSingletonInputActionGotDropped(uint leftPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckIfSingletonInputActionGotDropped");
            #endif
            DataList values = singletonInputActions.GetValues();
            for (int i = 0; i < values.Count; i++)
            {
                object[] singletonInputAction = (object[])values[i].Reference;
                uint responsiblePlayerId = (uint)singletonInputAction[0];
                if (leftPlayerId != responsiblePlayerId)
                    continue;
                singletonInputAction[0] = masterPlayerId; // Update responsible player.
                if (isMaster)
                    SendInputActionInternal(singletonInputActionIAId, (byte[])singletonInputAction[1], forceOneFrameDelay: true);
                    // forceOneFrameDelay is true to have consistent order in relation to the OnClientLeft event.
            }
        }

        public void InputActionSent(ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  InputActionSent");
            #endif
            if (!isMaster)
                return;

            if (currentTick < firstMutableTick)
            {
                AssociateInputActionWithTick(firstMutableTick, uniqueId, allowOnMaster: true);
                if (!isSinglePlayer)
                    tickSync.AddInputActionToRun(firstMutableTick, uniqueId);
                return;
            }

            if (!isSinglePlayer)
                tickSync.AddInputActionToRun(currentTick, uniqueId);
            RunInputActionForUniqueId(uniqueId);
        }

        public void OnInputActionSyncPlayerAssigned(VRCPlayerApi player, InputActionSync inputActionSync)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnInputActionSyncPlayerAssigned");
            #endif
            if (!player.isLocal)
                return;

            localPlayerDisplayName = player.displayName;

            inputActionSyncForLocalPlayer = inputActionSync;
            SendCustomEventDelayedSeconds(nameof(OnLocalInputActionSyncPlayerAssignedDelayed), 2f);
        }

        public void OnLocalInputActionSyncPlayerAssignedDelayed()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLocalInputActionSyncPlayerAssignedDelayed");
            #endif
            if (isMaster)
            {
                Debug.Log($"[Lockstep] isMaster is already true 2 seconds after the local player's "
                    + $"InputActionSync script has been assigned... nothing to do here then.");
                return;
            }

            if (localPlayer.isMaster)
            {
                BecomeInitialMaster();
                return;
            }

            ignoreLocalInputActions = true;
            stillAllowLocalClientJoinedIA = true;
            ignoreIncomingInputActions = false;
            SendClientJoinedIA();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnPlayerLeft - player is null: {player == null}");
            #endif
            uint playerId = (uint)player.playerId;

            // inputActionSyncForLocalPlayer could still be null while this event is running,
            // but if that's the case, isMaster is false and clientStates is null,
            // so the only function that needs to handle inputActionSyncForLocalPlayer being null
            // is SomeoneLeftWhileWeWereWaitingForLJSync.

            if (isMaster)
            {
                ArrList.Add(ref leftClients, ref leftClientsCount, playerId);
                processLeftPlayersSentCount++;
                SendCustomEventDelayedSeconds(nameof(ProcessLeftPlayers), 1f);
                return;
            }

            if (clientStates == null) // Implies `isWaitingAfterJustJoining || isWaitingForLateJoinerSync`
            {
                if (!(isWaitingToSendClientJoinedIA || isWaitingForLateJoinerSync))
                    Debug.LogError("[Lockstep] clientStates should be impossible to be null when "
                        + "isWaitingAfterJustJoining and isWaitingForLateJoinerSync are both false.");
                // Still waiting for late joiner sync, so who knows,
                // maybe this client will become the new master.
                someoneLeftWhileWeWereWaitingForLJSyncSentCount++;
                SendCustomEventDelayedSeconds(nameof(SomeoneLeftWhileWeWereWaitingForLJSync), 2.5f);
                // Note that the delay should be > than the delay for the call to OnLocalInputActionSyncPlayerAssignedDelayed.
                return;
            }

            if (!clientStates.TryGetValue(playerId, out DataToken clientStateToken))
            {
                // Already removed... was it the master that got removed?
                DataList allStates = clientStates.GetValues();
                bool foundMaster = false;
                for (int i = 0; i < allStates.Count; i++)
                {
                    if ((ClientState)allStates[i].Byte == ClientState.Master)
                    {
                        foundMaster = true;
                        break;
                    }
                }
                if (!foundMaster)
                    SetMasterLeftFlag();
                return;
            }

            ArrList.Add(ref leftClients, ref leftClientsCount, playerId);
            if ((ClientState)clientStateToken.Byte == ClientState.Master)
                SetMasterLeftFlag();
        }

        public void SomeoneLeftWhileWeWereWaitingForLJSync()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SomeoneLeftWhileWeWereWaitingForLJSync");
            #endif
            if ((--someoneLeftWhileWeWereWaitingForLJSyncSentCount) != 0)
                return;

            if (clientStates == null)
            {
                if (!currentlyNoMaster)
                {
                    Debug.LogError("[Lockstep] currentlyNoMaster should be impossible to "
                        + "be false when clientStates is null. Setting it to true "
                        + "in order to resolve this error state.");
                    currentlyNoMaster = true;
                }

                if (inputActionSyncForLocalPlayer == null)
                {
                    // CheckMasterChange should happen, but the local player needs the sync script first.
                    someoneLeftWhileWeWereWaitingForLJSyncSentCount++;
                    SendCustomEventDelayedSeconds(nameof(SomeoneLeftWhileWeWereWaitingForLJSync), 1f);
                    return;
                }

                // clientStates is still null... so maybe this client should be taking charge.
                CheckMasterChange();

                // Nope, not taking charge, so some other client is not giving us late joiner data.
                if (!isMaster)
                {
                    // Tell that client that we exist.
                    SendClientJoinedIA();
                }
            }
        }

        private void SetMasterLeftFlag()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetMasterLeftFlag");
            #endif
            if (currentlyNoMaster)
                return;
            currentlyNoMaster = true;
            SendCustomEventDelayedSeconds(nameof(CheckMasterChange), 0.2f);
        }

        private void BecomeInitialMaster()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  BecomeInitialMaster");
            #endif
            isMaster = true;
            currentlyNoMaster = false;
            ignoreLocalInputActions = false;
            ignoreIncomingInputActions = false;
            isWaitingToSendClientJoinedIA = false;
            isWaitingForLateJoinerSync = false;
            isInitialCatchUp = false; // The master never raises a caught up event for itself.
            clientStates = new DataDictionary();
            clientNames = new DataDictionary();
            DataToken keyToken = localPlayerId;
            clientStates.Add(keyToken, (byte)ClientState.Master);
            clientNames.Add(keyToken, localPlayerDisplayName);
            masterPlayerId = localPlayerId;
            lateJoinerInputActionSync.lockstepIsMaster = true;
            // Just to quadruple check, setting owner on both. Trust issues with VRChat.
            Networking.SetOwner(localPlayer, lateJoinerInputActionSync.gameObject);
            Networking.SetOwner(localPlayer, tickSync.gameObject);
            tickSync.RequestSerialization();
            startTick = 0u;
            currentTick = 1u; // Start at 1 because tick sync will always be 1 behind, and ticks are unsigned.
            waitTick = uint.MaxValue;
            EnterSingePlayerMode();
            initializedEnoughForImportExport = true;
            StartOrStopAutosave();
            RaiseOnInit();
            RaiseOnClientJoined(localPlayerId);
            isTickPaused = false;
            tickStartTime = Time.time;
        }

        private bool IsAnyClientWaitingForLateJoinerSync()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  IsAnyClientWaitingForLateJoinerSync");
            #endif
            DataList allStates = clientStates.GetValues();
            for (int i = 0; i < allStates.Count; i++)
                if ((ClientState)allStates[i].Byte == ClientState.WaitingForLateJoinerSync)
                    return true;
            return false;
        }

        private bool IsAnyClientNotWaitingForLateJoinerSync()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  IsAnyClientNotWaitingForLateJoinerSync");
            #endif
            DataList allStates = clientStates.GetValues();
            for (int i = 0; i < allStates.Count; i++)
                if ((ClientState)allStates[i].Byte != ClientState.WaitingForLateJoinerSync)
                    return true;
            return false;
        }

        public void CheckOtherMasterCandidates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckOtherMasterCandidates");
            #endif
            if ((--checkOtherMasterCandidatesSentCount) != 0)
                return;

            DataList allPlayerIds = clientStates.GetKeys();
            for (int i = 0; i < allPlayerIds.Count; i++)
            {
                DataToken playerIdToken = allPlayerIds[i];
                if ((ClientState)clientStates[playerIdToken].Byte == ClientState.WaitingForLateJoinerSync)
                    continue;
                uint playerId = playerIdToken.UInt;
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById((int)playerId);
                if (player == null)
                    continue;
                Debug.Log("[Lockstep] // TODO: Ask the given client to become master. Keep in mind that "
                    + "that client isn't even running ticks right now, so it requires a special input action. "
                    + "For now this simply gets ignored and the local client becomes master instead, "
                    + "causing every other client which isn't still waiting for late joiner sync "
                    + "to pretty much just break. The behaviour is undefined. "
                    + "Note that this this is super low priority because chances of this here "
                    + "ever happening are stupidly low or even impossible.");
                // return; // once the above is implemented, uncomment the return here.
            }
            // Nope, no other play may become master, so we take it and completely reset.
            clientStates = null;
            clientNames = null;
            CheckMasterChange();
        }

        private void FactoryReset()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FactoryReset");
            #endif
            lateJoinerInputActionSync.gameObject.SetActive(true);
            lateJoinerInputActionSync.lockstepIsMaster = false;
            tickSync.ClearInputActionsToRun();
            ForgetAboutUnprocessedLJSerializedGameSates();
            ForgetAboutLeftPlayers();
            ForgetAboutInputActionsWaitingToBeSent();
            clientStates = null;
            clientNames = null;
            currentlyNoMaster = true;
            isWaitingToSendClientJoinedIA = true;
            isWaitingForLateJoinerSync = false;
            inputActionsByUniqueId.Clear();
            uniqueIdsByTick.Clear();
            isTickPaused = true;
        }

        public void CheckMasterChange()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckMasterChange");
            #endif

            if (inputActionSyncForLocalPlayer == null)
            {
                // This can happen when on ownership transferred runs on LockstepTickSync,
                // in which case nothing needs to happen here, because
                // inputActionSyncForLocalPlayer still being null is handled by OnPlayerLeft
                // which will eventually call CheckMasterChange, if needed.
                return;
            }

            if (isMaster || !currentlyNoMaster || !Networking.IsMaster || checkOtherMasterCandidatesSentCount != 0)
                return;

            if (IsProcessingLJGameStates)
            {
                checkMasterChangeAfterProcessingLJGameStates = true;
                return;
            }

            if (isWaitingToSendClientJoinedIA || isWaitingForLateJoinerSync)
            {
                if (clientStates != null && IsAnyClientNotWaitingForLateJoinerSync())
                {
                    checkOtherMasterCandidatesSentCount++;
                    SendCustomEventDelayedSeconds(nameof(CheckOtherMasterCandidates), 1f);
                    return;
                }
                // The master left before finishing sending late joiner data and we are now the new master
                // without all the data, therefore we must completely reset the system and pretend we
                // are the first client in the instance.
                // Because of this, not a single event - not even the deserialization events for game states -
                // must raised while isWaitingForLateJoinerSync is still true, otherwise deserialization of a
                // game state may happen before OnInit, which is invalid behaviour for this system.
                stillAllowLocalClientJoinedIA = false;
                isCatchingUp = false;
                FactoryReset();
                BecomeInitialMaster();
                return;
            }

            isMaster = true; // currentlyNoMaster will be set to false in OnMasterChangedIA later.
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            ignoreIncomingInputActions = false;
            isTickPaused = false;
            isCatchingUp = true; // Catch up as quickly as possible to the waitTick. Unless it was already
            // catching up, this should usually only quickly advance by 1 or 2 ticks, which is fine. The real
            // reason this is required is for the public IsMaster property to behave correctly.
            // Leave isInitialCatchUp untouched, as this may in fact still be the initial catch up, if
            // isCatchingUp was already true.

            // The immutable tick prevents any newly enqueued input actions from being enqueued too early,
            // to prevent desyncs when not in single player as wells as poor IA ordering in general.
            firstMutableTick = waitTick + 1;

            lateJoinerInputActionSync.gameObject.SetActive(true);
            lateJoinerInputActionSync.lockstepIsMaster = true;
            Networking.SetOwner(localPlayer, lateJoinerInputActionSync.gameObject);
            Networking.SetOwner(localPlayer, tickSync.gameObject);
            tickSync.RequestSerialization();

            processLeftPlayersSentCount++;
            ProcessLeftPlayers();
            if (isSinglePlayer) // In case it was already single player before CheckMasterChange ran.
                InstantlyRunInputActionsWaitingToBeSent();
        }

        private void FinishCatchingUpOnMaster()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FinishCatchingUpOnMaster");
            #endif
            waitTick = uint.MaxValue;
            SendMasterChangedIA();

            if (IsAnyClientWaitingForLateJoinerSync())
            {
                flagForLateJoinerSyncSentCount++;
                FlagForLateJoinerSync();
            }
        }

        private void InstantlyRunInputActionsWaitingToBeSent()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  InstantlyRunInputActionsWaitingToBeSent");
            #endif
            RunInputActionsForThisFrame();
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: true);
        }

        private void ForgetAboutInputActionsWaitingToBeSent()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ForgetAboutInputActionsWaitingToBeSent");
            #endif
            ForgetAboutInputActionsForThisFrame();
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: false);
        }

        public void ProcessLeftPlayers()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ProcessLeftPlayers");
            #endif
            if ((--processLeftPlayersSentCount) != 0)
                return;

            CheckSingePlayerModeChange();

            // Must be a backwards loop, because SendClientLeftIA may ultimately remove the player id from
            // the leftClients list. Specifically when only the master is left in the instance, causing input
            // actions to be run instantly, and the OnClientLeftIA handler removes the player id.
            for (int i = leftClientsCount - 1; i >= 0; i--)
                SendClientLeftIA(leftClients[i]);

            ArrList.Clear(ref leftClients, ref leftClientsCount);
        }

        private void ForgetAboutLeftPlayers()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ForgetAboutLeftPlayers");
            #endif
            ArrList.Clear(ref leftClients, ref leftClientsCount);
        }

        private void CheckSingePlayerModeChange()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckSingePlayerModeChange");
            #endif
            bool shouldBeSinglePlayer = clientStates.Count - leftClientsCount <= 1;
            if (isSinglePlayer == shouldBeSinglePlayer)
                return;
            if (shouldBeSinglePlayer)
                EnterSingePlayerMode();
            else
                ExitSinglePlayerMode();
        }

        private void EnterSingePlayerMode()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  EnterSingePlayerMode");
            #endif
            isSinglePlayer = true;
            tickSync.isSinglePlayer = true;
            lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            InstantlyRunInputActionsWaitingToBeSent();
            tickSync.ClearInputActionsToRun();
        }

        private void ExitSinglePlayerMode()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ExitSinglePlayerMode");
            #endif
            isSinglePlayer = false;
            tickSync.isSinglePlayer = false;
            // (0u, 0u) Indicate that any input actions associated with ticks
            // before this client became master should be dropped. This isn't
            // useful for the true initial master, but if a client reset the instance
            // because the master left before sending late joiner data finished,
            // then this is required to get rid of any old lingering input actions.
            // The system being MP, turning SP and then MP again is also no problem
            // for this here, as turning MP means a new client joined, so nothing
            // would actually get dropped.
            // And this must be done through the tick sync script, not using an input
            // action, in order to avoid race conditions, because those would
            // absolutely happen when using an input action to clear tick associations.
            tickSync.AddInputActionToRun(0u, 0u);
            tickSync.RequestSerialization(); // Restart the tick sync loop.
        }

        private void SendMasterChangedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendMasterChangedIA");
            #endif
            WriteSmall(localPlayerId);
            SendInputAction(masterChangedIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint masterChangedIAId;
        [LockstepInputAction(nameof(masterChangedIAId))]
        public void OnMasterChangedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnMasterChangedIA");
            #endif
            uint playerId = ReadSmallUInt();
            clientStates[playerId] = (byte)ClientState.Master;
            uint oldId = masterPlayerId;
            masterPlayerId = playerId;
            currentlyNoMaster = false;
            RaiseOnMasterChanged(oldId);
        }

        private void SendClientJoinedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientJoinedIA");
            #endif
            if (inputActionSyncForLocalPlayer == null)
            {
                Debug.LogError("[Lockstep] Impossible, the inputActionSyncForLocalPlayer is null inside of "
                    + "SendClientJoinedIA. All code paths leading to SendClientJoinedIA are supposed to "
                    + "prevent this from happening.");
                return;
            }
            WriteSmall(localPlayerId);
            Write(localPlayerDisplayName);
            isWaitingToSendClientJoinedIA = false;
            isWaitingForLateJoinerSync = true;
            clientStates = null; // To know if this client actually received all data, first to last.
            clientNames = null;
            SendInputAction(clientJoinedIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint clientJoinedIAId;
        [LockstepInputAction(nameof(clientJoinedIAId))]
        public void OnClientJoinedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientJoinedIA");
            #endif
            uint playerId = ReadSmallUInt();
            string playerName = ReadString();
            // Using set value, because the given player may already have a state,
            // because it is valid for the client joined input action to be sent
            // multiple times. And whenever it is sent, it means the client is waiting
            // for late joiner sync.
            DataToken keyToken = playerId;
            clientStates.SetValue(keyToken, (byte)ClientState.WaitingForLateJoinerSync);
            clientNames.SetValue(keyToken, playerName);

            if (isMaster)
            {
                CheckSingePlayerModeChange();
                clientsJoinedInTheLastFiveMinutes++;
                SendCustomEventDelayedSeconds(nameof(PlayerJoinedFiveMinutesAgo), 300f);
                flagForLateJoinerSyncSentCount++;
                float lateJoinerSyncDelay = Mathf.Min(30f, 3f * (float)clientsJoinedInTheLastFiveMinutes);
                SendCustomEventDelayedSeconds(nameof(FlagForLateJoinerSync), lateJoinerSyncDelay);
            }
        }

        public void PlayerJoinedFiveMinutesAgo()
        {
            clientsJoinedInTheLastFiveMinutes--;
        }

        public void FlagForLateJoinerSync()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FlagForLateJoinerSync");
            #endif
            if ((--flagForLateJoinerSyncSentCount) != 0)
                return;

            // If isMaster && isCatchingUp, this actually does _not_ need special handling, because
            // sendLateJoinerDataAtEndOfTick is only checked after catching up is done.
            if (IsAnyClientWaitingForLateJoinerSync())
                sendLateJoinerDataAtEndOfTick = true;
        }

        private int Clamp(int value, int min, int max)
        {
            return System.Math.Min(max, System.Math.Max(min, value));
        }

        private void LogBinaryData(byte[] data, int size)
        {
            string result = "";
            for (int i = 0; i < size; i++)
            {
                if ((i % 32) == 0)
                    result += "<v>\n";
                result += data[i].ToString("x2");
            }
            Debug.Log($"[LockstepDebug] Lockstep  LogBinaryData:{result}");
        }

        private void SendLateJoinerData()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendLateJoinerData");
            #endif
            if (isImporting)
            {
                Debug.LogError("[Lockstep] Attempt to SendLateJoinerData while and import is still going on "
                    + "which if it was supported would be complete waste of networking bandwidth. So it isn't "
                    + "supported, and this call to SendLateJoinerData is ignored.");
                return;
            }
            if (lateJoinerInputActionSync.QueuedBytesCount >= Clamp(byteCountForLatestLJSync / 2, 2048, 2048 * 5))
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);

            // Client states game state.
            WriteSmall((uint)clientStates.Count);
            DataList keys = clientStates.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken keyToken = keys[i];
                WriteSmall(keyToken.UInt);
                Write(clientStates[keyToken].Byte);
                Write(clientNames[keyToken].String);
            }

            // Singleton input actions game state.
            WriteSmall(nextSingletonId);
            WriteSmall((uint)singletonInputActions.Count);
            keys = singletonInputActions.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken keyToken = keys[i];
                WriteSmall(keyToken.UInt);
                object[] inputActionData = (object[])singletonInputActions[keyToken].Reference;
                WriteSmall((uint)inputActionData[0]);
                byte[] singletonInputActionData = (byte[])inputActionData[1];
                WriteSmall((uint)singletonInputActionData.Length);
                Write(singletonInputActionData);
            }

            lateJoinerInputActionSync.SendInputAction(LJInternalGameStatesIAId, writeStream, writeStreamSize);
            ResetWriteStream();

            for (int i = 0; i < allGameStates.Length; i++)
            {
                allGameStates[i].SerializeGameState(false);
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  SendLateJoinerData (inner) - writeStreamSize: {writeStreamSize}");
                LogBinaryData(writeStream, writeStreamSize);
                #endif
                lateJoinerInputActionSync.SendInputAction(LJFirstCustomGameStateIAId + (uint)i, writeStream, writeStreamSize);
                ResetWriteStream();
            }

            Write(currentTick);
            lateJoinerInputActionSync.SendInputAction(LJCurrentTickIAId, writeStream, writeStreamSize);
            ResetWriteStream();

            byteCountForLatestLJSync = lateJoinerInputActionSync.QueuedBytesCount;
        }

        private void OnLJInternalGameStatesIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLJClientStatesIA");
            #endif
            // If this client was already receiving data, but then it restarted from
            // the beginning, forget about everything that's been received so far.
            ForgetAboutUnprocessedLJSerializedGameSates();

            clientStates = new DataDictionary();
            clientNames = new DataDictionary();
            int count = (int)ReadSmallUInt();
            for (int i = 0; i < count; i++)
            {
                uint playerId = ReadSmallUInt();
                byte clientState = ReadByte();
                string clientName = ReadString();
                DataToken keyToken = playerId;
                clientStates.Add(keyToken, clientState);
                clientNames.Add(keyToken, clientName);
                if ((ClientState)clientState == ClientState.Master)
                {
                    // In order for late joiner data to be sent it must come from a master, which means this
                    // if statement is guaranteed to run (naturally exactly once. There's only 1 master.)
                    masterPlayerId = playerId;
                    currentlyNoMaster = false;
                }
            }

            singletonInputActions.Clear();
            nextSingletonId = ReadSmallUInt();
            count = (int)ReadSmallUInt();
            for (int i = 0; i < count; i++)
            {
                uint singletonId = ReadSmallUInt();
                uint inputActionId = ReadSmallUInt();
                byte[] singletonInputActionData = ReadBytes((int)ReadSmallUInt());
                singletonInputActions.Add(singletonId, new DataToken(new object[] { inputActionId, singletonInputActionData }));
            }
        }

        private void OnLJCustomGameStateIA(uint inputActionId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLJCustomGameStateIA - clientStates is null: {clientStates == null}");
            #endif
            if (clientStates == null) // This data was not meant for this client. Continue waiting.
                return;

            if (inputActionId - LJFirstCustomGameStateIAId != (uint)unprocessedLJSerializedGSCount)
            {
                Debug.LogError($"[Lockstep] Expected game state index {unprocessedLJSerializedGSCount}, "
                    + $"got {inputActionId - LJFirstCustomGameStateIAId}. Either some math "
                    + $"is wrong or the game states are somehow out of order.");
                return;
            }
            ArrList.Add(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount, readStream);
        }

        private void OnLJCurrentTickIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLJCurrentTickIA - clientStates is null: {clientStates == null}");
            #endif
            if (clientStates == null) // This data was not meant for this client. Continue waiting.
            {
                SendCustomEventDelayedSeconds(nameof(AskForLateJoinerSyncAgain), 2.5f);
                return;
            }

            currentTick = ReadUInt();

            lateJoinerInputActionSync.gameObject.SetActive(false);
            isWaitingForLateJoinerSync = false;
            TryMoveToNextLJSerializedGameState();
        }

        public void AskForLateJoinerSyncAgain()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AskForLateJoinerSyncAgain");
            #endif
            // Does not need to keep track of the amount of time this has been raised to only run for the last
            // one, because even after the first time it was sent, by the time this runs the condition below
            // should already be true. And if it isn't, no new late joiner data has been received, so it it
            // should be impossible for this event to have been sent twice. But even if that were to happen,
            // late joiner sync initialization has a 5 second delay on the master, so it doesn't really
            // matter.
            if (clientStates != null)
                return;

            Debug.LogWarning($"[Lockstep] The master has not sent another set of late joiner data for 2.5 seconds "
                + "since the last set finished, however this client is still waiting on that data. This "
                + "should be impossible because the master keeps track of joined clients, however "
                + "through mysterious means the first input action for late joiner sync may have been "
                + "lost to the ether, through means unknown to me. Therefore this client is asking the "
                + "master to send late joiner data again.");
            SendClientJoinedIA();
        }

        private void TryMoveToNextLJSerializedGameState()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  TryMoveToNextLJSerializedGameState");
            #endif
            nextLJGameStateToProcess++;
            nextLJGameStateToProcessTime = Time.time + LJGameStateProcessingFrequency;
            if (nextLJGameStateToProcess >= unprocessedLJSerializedGSCount)
                DoneProcessingLJGameStates();
        }

        private void ProcessNextLJSerializedGameState()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ProcessNextLJSerializedGameState");
            #endif
            int gameStateIndex = nextLJGameStateToProcess;
            ResetReadStream();
            readStream = unprocessedLJSerializedGameStates[gameStateIndex];
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ProcessNextLJSerializedGameState (inner) - readStream.Length: {readStream.Length}");
            LogBinaryData(readStream, readStream.Length);
            #endif
            allGameStates[gameStateIndex].DeserializeGameState(false, 0u); // TODO: Use return error message.
            TryMoveToNextLJSerializedGameState();
        }

        private void ForgetAboutUnprocessedLJSerializedGameSates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ForgetAboutUnprocessedLJSerializedGameSates");
            #endif
            nextLJGameStateToProcess = -1;
            checkMasterChangeAfterProcessingLJGameStates = false;
            ArrList.Clear(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount);
        }

        private void DoneProcessingLJGameStates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  DoneProcessingLJGameStates");
            #endif
            bool doCheckMasterChange = checkMasterChangeAfterProcessingLJGameStates;
            ForgetAboutUnprocessedLJSerializedGameSates();
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            SendClientGotLateJoinerDataIA(); // Must be before OnClientBeginCatchUp, because that can also send input actions.
            initializedEnoughForImportExport = true;
            RaiseOnClientBeginCatchUp(localPlayerId);
            isTickPaused = false;
            isCatchingUp = true;
            isInitialCatchUp = true;

            if (doCheckMasterChange)
                CheckMasterChange();
        }

        private void CheckIfLateJoinerSyncShouldStop()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckIfLateJoinerSyncShouldStop");
            #endif
            if (isMaster && !IsAnyClientWaitingForLateJoinerSync())
            {
                sendLateJoinerDataAtEndOfTick = false;
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            }
        }

        private void SendClientGotLateJoinerDataIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientGotLateJoinerDataIA");
            #endif
            WriteSmall(localPlayerId);
            SendInputAction(clientGotLateJoinerDataIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint clientGotLateJoinerDataIAId;
        [LockstepInputAction(nameof(clientGotLateJoinerDataIAId))]
        public void OnClientGotLateJoinerDataIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientGotLateJoinerDataIA");
            #endif
            uint playerId = ReadSmallUInt();
            clientStates[playerId] = (byte)ClientState.CatchingUp;
            CheckIfLateJoinerSyncShouldStop();
            RaiseOnClientJoined(playerId);
        }

        private void SendClientLeftIA(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientLeftIA");
            #endif
            WriteSmall(playerId);
            SendInputAction(clientLeftIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint clientLeftIAId;
        [LockstepInputAction(nameof(clientLeftIAId))]
        public void OnClientLeftIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientLeftIA");
            #endif
            uint playerId = ReadSmallUInt();
            DataToken keyToken = playerId;
            clientStates.Remove(keyToken);
            clientNames.Remove(keyToken, out DataToken displayNameToken);
            // leftClients may not contain playerId, and that is fine.
            ArrList.Remove(ref leftClients, ref leftClientsCount, playerId);

            CheckIfLateJoinerSyncShouldStop();
            CheckIfSingletonInputActionGotDropped(playerId);
            CheckIfImportingPlayerLeft(playerId);
            leftClientName = displayNameToken.String;
            RaiseOnClientLeft(playerId);
            leftClientName = null;
        }

        private void SendClientCaughtUpIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientCaughtUpIA");
            #endif
            WriteSmall(localPlayerId);
            Write(isInitialCatchUp ? 1 : 0); // `doRaise`.
            SendInputAction(clientCaughtUpIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint clientCaughtUpIAId;
        [LockstepInputAction(nameof(clientCaughtUpIAId))]
        public void OnClientCaughtUpIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientCaughtUpIA");
            #endif
            uint playerId = ReadSmallUInt();
            byte doRaise = ReadByte();
            clientStates[playerId] = (byte)ClientState.Normal;
            if (doRaise != 0)
                RaiseOnClientCaughtUp(playerId);
        }

        public void ReceivedInputAction(bool isLateJoinerSync, uint inputActionId, ulong uniqueId, byte[] inputActionData)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ReceivedInputAction - isLateJoinerSync: {isLateJoinerSync}, inputActionId: {inputActionId}, uniqueId: 0x{uniqueId:x16}{(isLateJoinerSync ? "" : $", event name {(inputActionId < inputActionHandlerEventNames.Length ? inputActionHandlerEventNames[inputActionId] : "<id/index out of bounds>")}")}");
            #endif
            if (isLateJoinerSync)
            {
                if (!isWaitingForLateJoinerSync)
                    return;
                ResetReadStream();
                readStream = inputActionData;
                if (inputActionId == LJInternalGameStatesIAId)
                    OnLJInternalGameStatesIA();
                else if (inputActionId == LJCurrentTickIAId)
                    OnLJCurrentTickIA();
                else
                    OnLJCustomGameStateIA(inputActionId);
                return;
            }

            if (ignoreIncomingInputActions)
                return;

            if (isMaster)
                TryToInstantlyRunInputActionOnMaster(inputActionId, uniqueId, inputActionData);
            else
                inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData }));
        }

        private ulong TryToInstantlyRunInputActionOnMaster(uint inputActionId, ulong uniqueId, byte[] inputActionData, bool forceOneFrameDelay = false)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  TryToInstantlyRunInputActionOnMaster");
            #endif
            if (currentTick < firstMutableTick) // Can't run it in the current tick. A check for isCatchingUp
            { // is not needed, because the condition above will always be true when isCatchingUp is true.
                if (uniqueId == 0uL)
                {
                    // It'll only be 0 if the local player is the one trying to instantly run it.
                    // Received data always has a unique id.
                    uniqueId = inputActionSyncForLocalPlayer.MakeUniqueId();
                }
                inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData }));
                AssociateInputActionWithTickInternal(firstMutableTick, uniqueId);
                return uniqueId;
            }

            if (!isSinglePlayer)
            {
                if (uniqueId == 0uL)
                {
                    Debug.LogError("[Lockstep] Impossible, the uniqueId when instantly running an input action "
                        + "on master cannot be 0 while not in single player, because every input action "
                        + "get sent over the network and gets a unique id assigned in the process. "
                        + "Something is very wrong in the code. Ignoring this action.");
                    return 0uL;
                }
                tickSync.AddInputActionToRun(currentTick, uniqueId);
            }
            else if (uniqueId == 0uL) // In single player, do make a unique id.
                uniqueId = inputActionSyncForLocalPlayer.MakeUniqueId();

            if (forceOneFrameDelay)
                ArrList.Add(ref inputActionsToRunNextFrame, ref iatrnCount, new object[] { inputActionId, inputActionData, uniqueId });
            else
                RunInputAction(inputActionId, inputActionData, uniqueId);
            return uniqueId;
        }

        private void ClearUniqueIdsByTick()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ClearUniqueIdsByTick");
            #endif
            // When only clearing uniqueIdsByTick we can't just clear inputActionsByUniqueId,
            // because that may contain very new input actions which have not been associated
            // with a tick yet, so they'd be associated with future ticks which means we must
            // keep them alive. The below logic just removes the ones we know for certain that
            // they can be removed, just to free up some memory.
            DataList allValues = uniqueIdsByTick.GetValues();
            for (int i = 0; i < allValues.Count; i++)
                foreach (ulong uniqueId in (ulong[])allValues[i].Reference)
                    inputActionsByUniqueId.Remove(uniqueId);
            uniqueIdsByTick.Clear();
        }

        public void AssociateInputActionWithTick(uint tickToRunIn, ulong uniqueId, bool allowOnMaster = false)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AssociateInputActionWithTick - tickToRunIn: {tickToRunIn}, uniqueId: 0x{uniqueId:x16}");
            #endif
            if (tickToRunIn == 0u && uniqueId == 0uL)
            {
                ClearUniqueIdsByTick();
                return;
            }

            if (ignoreIncomingInputActions)
                return;
            if (isMaster && !allowOnMaster)
            {
                Debug.LogWarning("[Lockstep] The master client (which is this client) should "
                    + "not be receiving data about running an input action at a tick...");
            }

            AssociateInputActionWithTickInternal(tickToRunIn, uniqueId);
        }

        private void AssociateInputActionWithTickInternal(uint tickToRunIn, ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AssociateInputActionWithTickInternal - tickToRunIn: {tickToRunIn}, uniqueId: 0x{uniqueId:x16}");
            #endif
            // Mark the input action to run at the given tick.
            DataToken tickToRunInToken = new DataToken(tickToRunIn);
            if (uniqueIdsByTick.TryGetValue(tickToRunInToken, out DataToken uniqueIdsToken))
            {
                ulong[] uniqueIds = (ulong[])uniqueIdsToken.Reference;
                int oldLength = uniqueIds.Length;
                ulong[] newUniqueIds = new ulong[oldLength + 1];
                uniqueIds.CopyTo(newUniqueIds, 0);
                newUniqueIds[oldLength] = uniqueId;
                uniqueIdsByTick.SetValue(tickToRunInToken, new DataToken(newUniqueIds));
                return;
            }
            uniqueIdsByTick.Add(tickToRunInToken, new DataToken(new ulong[] { uniqueId }));
        }

        private void RaiseOnInit()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnInit");
            #endif
            foreach (UdonSharpBehaviour listener in onInitListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnInit));
        }

        private void RaiseOnClientJoined(uint joinedPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientJoined");
            #endif
            this.joinedPlayerId = joinedPlayerId;
            foreach (UdonSharpBehaviour listener in onClientJoinedListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnClientJoined));
            this.joinedPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        private void RaiseOnClientBeginCatchUp(uint catchingUpPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientBeginCatchUp");
            #endif
            this.catchingUpPlayerId = catchingUpPlayerId;
            foreach (UdonSharpBehaviour listener in onClientBeginCatchUpListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnClientBeginCatchUp));
            this.catchingUpPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        private void RaiseOnClientCaughtUp(uint catchingUpPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientCaughtUp");
            #endif
            this.catchingUpPlayerId = catchingUpPlayerId;
            foreach (UdonSharpBehaviour listener in onClientCaughtUpListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnClientCaughtUp));
            this.catchingUpPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        private void RaiseOnClientLeft(uint leftPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientLeft");
            #endif
            this.leftPlayerId = leftPlayerId;
            foreach (UdonSharpBehaviour listener in onClientLeftListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnClientLeft));
            this.leftPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        /// <summary>
        /// <para>Make sure to set <see cref="masterPlayerId"/> before raising this event.</para>
        /// </summary>
        private void RaiseOnMasterChanged(uint oldMasterPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnMasterChanged");
            #endif
            this.oldMasterPlayerId = oldMasterPlayerId;
            foreach (UdonSharpBehaviour listener in onMasterChangedListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnMasterChanged));
            this.oldMasterPlayerId = 0; // To prevent misuse of the API which would cause desyncs.
        }

        private void RaiseOnTick()
        {
            // #if LockstepDebug
            // Debug.Log($"[LockstepDebug] Lockstep  RaiseOnTick");
            // #endif
            foreach (UdonSharpBehaviour listener in onTickListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnTick));
        }

        private void RaiseOnImportStart()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnImportStart");
            #endif
            foreach (UdonSharpBehaviour listener in onImportStartListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnImportStart));
        }

        private void RaiseOnImportedGameState()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnImportedGameState");
            #endif
            foreach (UdonSharpBehaviour listener in onImportedGameStateListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnImportedGameState));
        }

        private void RaiseOnImportFinished()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnImportFinished");
            #endif
            foreach (UdonSharpBehaviour listener in onImportFinishedListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnImportFinished));
        }

        private bool markedForOnGameStatesToAutosaveChanged;
        private void MarkForOnGameStatesToAutosaveChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  MarkForOnGameStatesToAutosaveChanged");
            #endif
            if (markedForOnGameStatesToAutosaveChanged)
                return;
            markedForOnGameStatesToAutosaveChanged = true;
            SendCustomEventDelayedFrames(nameof(RaiseOnGameStatesToAutosaveChanged), 1);
        }

        public void RaiseOnGameStatesToAutosaveChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnGameStatesToAutosaveChanged");
            #endif
            markedForOnGameStatesToAutosaveChanged = false;
            foreach (UdonSharpBehaviour listener in onGameStatesToAutosaveChangedListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnGameStatesToAutosaveChanged));
        }

        private bool markedForOnAutosaveIntervalSecondsChanged;
        private void MarkForOnAutosaveIntervalSecondsChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  MarkForOnAutosaveIntervalSecondsChanged");
            #endif
            if (markedForOnAutosaveIntervalSecondsChanged)
                return;
            markedForOnAutosaveIntervalSecondsChanged = true;
            SendCustomEventDelayedFrames(nameof(RaiseOnAutosaveIntervalSecondsChanged), 1);
        }

        public void RaiseOnAutosaveIntervalSecondsChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnAutosaveIntervalSecondsChanged");
            #endif
            markedForOnAutosaveIntervalSecondsChanged = false;
            foreach (UdonSharpBehaviour listener in onAutosaveIntervalSecondsChangedListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnAutosaveIntervalSecondsChanged));
        }

        private bool markedForOnIsAutosavePausedChanged;
        private void MarkForOnIsAutosavePausedChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  MarkForOnIsAutosavePausedChanged");
            #endif
            if (markedForOnIsAutosavePausedChanged)
                return;
            markedForOnIsAutosavePausedChanged = true;
            SendCustomEventDelayedFrames(nameof(RaiseOnIsAutosavePausedChanged), 1);
        }

        public void RaiseOnIsAutosavePausedChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnIsAutosavePausedChanged");
            #endif
            markedForOnIsAutosavePausedChanged = false;
            foreach (UdonSharpBehaviour listener in onIsAutosavePausedChangedListeners)
                listener.SendCustomEvent(nameof(LockstepEventType.OnIsAutosavePausedChanged));
        }

        public override string GetDisplayName(uint playerId)
        {
            if (clientNames.TryGetValue(playerId, out DataToken nameToken))
                return nameToken.String;
            if (leftClientName != null && playerId == leftPlayerId)
                return leftClientName; // We are inside of the OnClientLeft event.
            Debug.LogError("[Lockstep] Attempt to call GetDisplayName with a playerId which is not currently "
                + "part of the game state. This is indication of misuse of the API, make sure to fix this.");
            return null;
        }

        private byte[] writeStream = new byte[64];
        private int writeStreamSize = 0;

        public override void ResetWriteStream() => writeStreamSize = 0;
        public override void Write(sbyte value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(byte value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(short value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(ushort value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(int value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(uint value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(long value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(ulong value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(float value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(double value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(Vector2 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(Vector3 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(Vector4 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(Quaternion value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(char value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(string value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(System.DateTime value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void Write(byte[] bytes) => DataStream.Write(ref writeStream, ref writeStreamSize, bytes);
        public override void WriteSmall(short value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmall(ushort value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmall(int value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmall(uint value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmall(long value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmall(ulong value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);

        ///<summary>Arrays assigned to this variable always have the exact length of the data that is actually
        ///available to be read, and once assigned to this variable they are immutable.</summary>
        private byte[] readStream = new byte[0];
        private int readStreamPosition = 0;

        private void ResetReadStream() => readStreamPosition = 0;
        public override sbyte ReadSByte() => DataStream.ReadSByte(ref readStream, ref readStreamPosition);
        public override byte ReadByte() => DataStream.ReadByte(ref readStream, ref readStreamPosition);
        public override short ReadShort() => DataStream.ReadShort(ref readStream, ref readStreamPosition);
        public override ushort ReadUShort() => DataStream.ReadUShort(ref readStream, ref readStreamPosition);
        public override int ReadInt() => DataStream.ReadInt(ref readStream, ref readStreamPosition);
        public override uint ReadUInt() => DataStream.ReadUInt(ref readStream, ref readStreamPosition);
        public override long ReadLong() => DataStream.ReadLong(ref readStream, ref readStreamPosition);
        public override ulong ReadULong() => DataStream.ReadULong(ref readStream, ref readStreamPosition);
        public override float ReadFloat() => DataStream.ReadFloat(ref readStream, ref readStreamPosition);
        public override double ReadDouble() => DataStream.ReadDouble(ref readStream, ref readStreamPosition);
        public override Vector2 ReadVector2() => DataStream.ReadVector2(ref readStream, ref readStreamPosition);
        public override Vector3 ReadVector3() => DataStream.ReadVector3(ref readStream, ref readStreamPosition);
        public override Vector4 ReadVector4() => DataStream.ReadVector4(ref readStream, ref readStreamPosition);
        public override Quaternion ReadQuaternion() => DataStream.ReadQuaternion(ref readStream, ref readStreamPosition);
        public override char ReadChar() => DataStream.ReadChar(ref readStream, ref readStreamPosition);
        public override string ReadString() => DataStream.ReadString(ref readStream, ref readStreamPosition);
        public override System.DateTime ReadDateTime() => DataStream.ReadDateTime(ref readStream, ref readStreamPosition);
        public override byte[] ReadBytes(int byteCount) => DataStream.ReadBytes(ref readStream, ref readStreamPosition, byteCount);
        public override short ReadSmallShort() => DataStream.ReadSmallShort(ref readStream, ref readStreamPosition);
        public override ushort ReadSmallUShort() => DataStream.ReadSmallUShort(ref readStream, ref readStreamPosition);
        public override int ReadSmallInt() => DataStream.ReadSmallInt(ref readStream, ref readStreamPosition);
        public override uint ReadSmallUInt() => DataStream.ReadSmallUInt(ref readStream, ref readStreamPosition);
        public override long ReadSmallLong() => DataStream.ReadSmallLong(ref readStream, ref readStreamPosition);
        public override ulong ReadSmallULong() => DataStream.ReadSmallULong(ref readStream, ref readStreamPosition);

        private uint[] crc32LookupCache;

        public override string Export(LockstepGameState[] gameStates, string exportName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Export");
            System.Diagnostics.Stopwatch exportStopWatch = new System.Diagnostics.Stopwatch();
            exportStopWatch.Start();
            #endif
            if (!initializedEnoughForImportExport)
            {
                Debug.LogError("[Lockstep] Attempt to call Export before OnInit or OnClientBeginCatchUp, ignoring.");
                return null;
            }
            if (exportName != null && (exportName.Contains("\n") || exportName.Contains("\r")))
            {
                Debug.LogError("[Lockstep] Attempt to call Export where the given exportName contains '\\n' "
                    + "and or '\\r' (newline characters), which is forbidden. Ignoring.");
                return null;
            }
            ResetWriteStream();

            uint validCount = 0;
            foreach (LockstepGameState gameState in gameStates)
            {
                if (gameState == null)
                {
                    Debug.LogError("[Lockstep] Attempt to call Export where the gameStates array contains null, ignoring.");
                    return null;
                }
                if (gameState.GameStateSupportsImportExport)
                    validCount++;
            }
            if (validCount == 0) // No error log here, because this is the only sensible and by definition
                return null; // acceptable error case in which again by definition null is returned.

            Write(System.DateTime.UtcNow);
            Write(exportName);
            WriteSmall(validCount);

            foreach (LockstepGameState gameState in gameStates)
            {
                if (!gameState.GameStateSupportsImportExport)
                    continue;
                Write(gameState.GameStateInternalName);
                Write(gameState.GameStateDisplayName);
                WriteSmall(gameState.GameStateDataVersion);

                int sizePosition = writeStreamSize;
                writeStreamSize += 4;
                gameState.SerializeGameState(true);
                int stopPosition = writeStreamSize;
                writeStreamSize = sizePosition;
                Write(stopPosition - sizePosition - 4); // The 4 bytes got reserved prior, cannot use WriteSmall.
                writeStreamSize = stopPosition;
            }

            #if LockstepDebug
            long crcStartMs = exportStopWatch.ElapsedMilliseconds;
            #endif
            uint crc = CRC32.Compute(ref crc32LookupCache, writeStream, 0, writeStreamSize);
            #if LockstepDebug
            long crcMs = exportStopWatch.ElapsedMilliseconds - crcStartMs;
            #endif
            Write(crc);

            byte[] exportedData = new byte[writeStreamSize];
            for (int i = 0; i < writeStreamSize; i++)
                exportedData[i] = writeStream[i];
            ResetWriteStream();

            string encoded = Base64.Encode(exportedData);
            Debug.Log("[Lockstep] Export:" + (exportName != null ? $" {exportName}:\n" : "\n") + encoded);
            #if LockstepDebug
            exportStopWatch.Stop();
            Debug.Log($"[LockstepDebug] Lockstep  Export (inner) - binary size: {writeStreamSize}, crc: {crc}, crc calculation time: {crcMs}ms, total time: {exportStopWatch.ElapsedMilliseconds}ms");
            #endif
            return encoded;
        }

        public override object[][] ImportPreProcess(string exportedString, out System.DateTime exportedDate, out string exportName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ImportPreProcess");
            #endif
            exportedDate = System.DateTime.MinValue;
            exportName = null;

            if (!Base64.TryDecode(exportedString, out readStream))
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  ImportPreProcess (inner) - invalid base64 encoding:\n{exportedString}");
                #endif
                return null;
            }
            if (readStream.Length < 4)
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  ImportPreProcess (inner) - exported data too short:\n{exportedString}");
                #endif
                return null;
            }

            #if LockstepDebug
            System.Diagnostics.Stopwatch crcStopwatch = new System.Diagnostics.Stopwatch();
            crcStopwatch.Start();
            #endif
            uint gotCrc = CRC32.Compute(ref crc32LookupCache, readStream, 0, readStream.Length - 4);
            #if LockstepDebug
            crcStopwatch.Stop();
            #endif
            readStreamPosition = readStream.Length - 4;
            uint expectedCrc = ReadUInt();
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ImportPreProcess (inner) - binary size: {readStream.Length}, expected crc: {expectedCrc}, got crc: {gotCrc}, crc calculation time: {crcStopwatch.ElapsedMilliseconds}ms");
            #endif
            if (gotCrc != expectedCrc)
                return null;

            ResetReadStream();

            exportedDate = ReadDateTime();
            exportName = ReadString();
            if (exportName != null)
            {
                // While the Export function will refuse export names with newlines, it is possible to
                // fabricate an import/export string which could in turn contain newlines in the name. However
                // the whole point of Export refusing them is that the API guarantees that the name shall
                // never under any circumstances contain newlines. So they must be removed here.
                exportName = exportName.Replace('\n', ' ').Replace('\r', ' ');
            }
            int gameStatesCount = (int)ReadSmallUInt();

            object[][] importedGameStates = new object[gameStatesCount][];
            for (int i = 0; i < gameStatesCount; i++)
            {
                object[] importedGS = LockstepImportedGS.New();
                importedGameStates[i] = importedGS;
                string internalName = ReadString();
                LockstepImportedGS.SetInternalName(importedGS, internalName);
                LockstepImportedGS.SetDisplayName(importedGS, ReadString());
                uint dataVersion = ReadSmallUInt();
                LockstepImportedGS.SetDataVersion(importedGS, dataVersion);
                int dataSize = ReadInt();
                byte[] binaryData = new byte[dataSize];
                for (int j = 0; j < dataSize; j++)
                    binaryData[j] = readStream[readStreamPosition++];
                LockstepImportedGS.SetBinaryData(importedGS, binaryData);
                DataToken internalNameToken = internalName;
                if (!gameStatesByInternalName.TryGetValue(internalNameToken, out DataToken gameStateToken))
                    LockstepImportedGS.SetErrorMsg(importedGS, "not in this world");
                else
                {
                    LockstepGameState gameState = (LockstepGameState)gameStateToken.Reference;
                    LockstepImportedGS.SetGameState(importedGS, gameState);
                    LockstepImportedGS.SetGameStateIndex(importedGS, gameStateIndexesByInternalName[internalNameToken].Int);
                    if (!gameState.GameStateSupportsImportExport)
                        LockstepImportedGS.SetErrorMsg(importedGS, "no longer supports import");
                    else if (dataVersion > gameState.GameStateDataVersion)
                        LockstepImportedGS.SetErrorMsg(importedGS, "imported version too new");
                    else if (dataVersion < gameState.GameStateLowestSupportedDataVersion)
                        LockstepImportedGS.SetErrorMsg(importedGS, "imported version too old");
                }
            }

            return importedGameStates;
        }

        public override void StartImport(System.DateTime exportDate, string exportName, object[][] importedGameStates)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartImport");
            #endif
            if (!initializedEnoughForImportExport)
            {
                Debug.LogError("[Lockstep] Attempt to call StartImport before OnInit or OnClientBeginCatchUp, ignoring.");
                return;
            }
            if (isImporting)
            {
                Debug.LogError("[Lockstep] Attempt to call StartImport while IsImporting is true, ignoring.");
                return;
            }

            int count = 0;
            foreach(object[] importedGS in importedGameStates)
                if (LockstepImportedGS.GetErrorMsg(importedGS) == null)
                    count++;
            if (count == 0)
                return;
            object[][] validImportedGSs = new object[count][];
            count = 0;
            foreach(object[] importedGS in importedGameStates)
                if (LockstepImportedGS.GetErrorMsg(importedGS) == null)
                    validImportedGSs[count++] = importedGS;
            if (exportName != null)
                exportName = exportName.Replace('\n', ' ').Replace('\r', ' ');
            SendImportStartIA(exportDate, exportName, validImportedGSs);
        }

        ///<summary>LockstepImportedGS[]</summary>
        private object[][] importedGSsToSend;

        private bool initializedEnoughForImportExport = false;
        // None of this is part of an internal game state, which is fine because late joiner sync will not be
        // performed while isImporting is true.
        private bool isImporting = false;
        private void SetIsImporting(bool value)
        {
            if (isImporting == value)
                return;
            isImporting = value;
            StartOrStopAutosave();
            if (value)
                RaiseOnImportStart();
            else
            {
                RaiseOnImportFinished();
                // To make these properties game state safe.
                importingPlayerId = 0u;
                importingFromDate = new System.DateTime();
                importingFromName = null;
                gameStatesWaitingForImport.Clear(); // And to clean up.
            }
        }
        private uint importingPlayerId;
        private System.DateTime importingFromDate;
        private string importingFromName;
        private LockstepGameState importedGameState;
        private uint importedDataVersion;
        public override bool IsImporting => isImporting;
        public override uint ImportingPlayerId => importingPlayerId;

        public override System.DateTime ImportingFromDate => importingFromDate;
        public override string ImportingFromName => importingFromName;
        public override LockstepGameState ImportedGameState => importedGameState;
        public override uint ImportedDataVersion => importedDataVersion;
        public override LockstepGameState[] GameStatesWaitingForImport
        {
            get
            {
                int count = gameStatesWaitingForImport.Count;
                LockstepGameState[] result = new LockstepGameState[count];
                DataList values = gameStatesWaitingForImport.GetValues();
                for (int i = 0; i < count; i++)
                    result[i] = (LockstepGameState)values[i].Reference;
                return result;
            }
        }
        public override int GameStatesWaitingForImportCount => gameStatesWaitingForImport.Count;
        ///<summary>int gameStateIndex => LockstepGameState gameState</summary>
        private DataDictionary gameStatesWaitingForImport = new DataDictionary();

        ///<summary>LockstepImportedGS[] importedGSs</summary>
        private void SendImportStartIA(System.DateTime exportDate, string exportName, object[][] importedGSs)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendImportStartIA");
            #endif
            if (importedGSs.Length == 0)
            {
                Debug.LogError("[Lockstep] Attempt to SendImportStartIA with 0 game states to import, ignoring.");
                return;
            }
            Write(exportDate);
            Write(exportName);
            WriteSmall((uint)importedGSs.Length);
            foreach (object[] importedGS in importedGSs)
                WriteSmall((uint)LockstepImportedGS.GetGameStateIndex(importedGS));
            importedGSsToSend = importedGSs;
            SendInputAction(importStartIAId);
        }

        [SerializeField] [HideInInspector] private uint importStartIAId;
        [LockstepInputAction(nameof(importStartIAId))]
        public void OnImportStartIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnImportStartIA");
            #endif
            if (isImporting)
            {
                importedGSsToSend = null;
                return;
            }
            importingPlayerId = SendingPlayerId;
            importingFromDate = ReadDateTime();
            importingFromName = ReadString();
            int importedGSsCount = (int)ReadSmallUInt();
            for (int i = 0; i < importedGSsCount; i++)
            {
                int gameStateIndex = (int)ReadSmallUInt();
                gameStatesWaitingForImport.Add(gameStateIndex, allGameStates[gameStateIndex]);
            }
            SetIsImporting(true); // Raises an event, do it last so all the fields are populated.

            if (SendingPlayerId != localPlayerId)
                return;

            foreach (object[] importedGS in importedGSsToSend)
                SendImportGameStateIA(importedGS);
            importedGSsToSend = null;
        }

        ///<summary>LockstepImportedGS importedGS</summary>
        private void SendImportGameStateIA(object[] importedGS)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendImportGameStateIA");
            #endif
            WriteSmall((uint)LockstepImportedGS.GetGameStateIndex(importedGS));
            WriteSmall(LockstepImportedGS.GetDataVersion(importedGS));
            Write(LockstepImportedGS.GetBinaryData(importedGS));
            SendInputAction(importGameStateIAId);
        }

        [SerializeField] [HideInInspector] private uint importGameStateIAId;
        [LockstepInputAction(nameof(importGameStateIAId))]
        public void OnImportGameStateIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnImportGameStateIA");
            #endif
            int gameStateIndex = (int)ReadSmallUInt();
            LockstepGameState gameState = allGameStates[gameStateIndex];
            if (!gameStatesWaitingForImport.Remove(gameStateIndex))
            {
                Debug.LogError($"[Lockstep] Impossible: A game state received import data even though it was "
                    + $"Not marked as waiting for import. Ignoring incoming data. (Unless we received an "
                    + "input action from a player for whom we got the player left event over 1 second ago...)");
                return;
            }
            importedDataVersion = ReadSmallUInt();
            // The rest of the input action is the raw imported bytes, ready to be consumed by the function below.
            gameState.DeserializeGameState(isImport: true, importedDataVersion); // TODO: Use return error message.
            importedGameState = gameState;
            RaiseOnImportedGameState();
            importedGameState = null;
            importedDataVersion = 0u;
            if (gameStatesWaitingForImport.Count == 0)
                SetIsImporting(false);
        }

        private void CheckIfImportingPlayerLeft(uint leftPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckIfImportingPlayerLeft");
            #endif
            if (!isImporting || leftPlayerId != ImportingPlayerId)
                return;
            SetIsImporting(false);
        }

        private bool AutosaveShouldBeRunning
            => initializedEnoughForImportExport
            && !(isCatchingUp && isInitialCatchUp)
            && autosavePauseScopeCount == 0
            && !isImporting;

        private void StartOrStopAutosave()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartOrStopAutosave");
            #endif
            if (gameStatesToAutosave.Length == 0)
            {
                autosaveTimerStart = -1f;
                autosaveTimerPausedAt = float.PositiveInfinity;
                return;
            }

            bool doRun = AutosaveShouldBeRunning;
            if (autosaveTimerStart == -1f) // Timer has not been started yet, start it.
            {
                autosaveTimerStart = Time.time;
                autosaveTimerPausedAt = doRun ? -1f : 0f;
                if (doRun)
                    SendCustomEventDelayedSeconds(nameof(AutosaveLoop), autosaveIntervalSeconds);
                return;
            }

            if (doRun == (autosaveTimerPausedAt == -1f)) // Expected running state matches current state.
                return;

            if (!doRun) // Pause the timer.
            {
                autosaveTimerPausedAt = Time.time - autosaveTimerStart;
                return;
            }

            // Resume the timer.
            autosaveTimerStart = Time.time - autosaveTimerPausedAt;
            autosaveTimerPausedAt = -1f;
            SendCustomEventDelayedSeconds(nameof(AutosaveLoop), SecondsUntilNextAutosave);
        }

        private LockstepGameState[] gameStatesToAutosave = new LockstepGameState[0];
        public override LockstepGameState[] GameStatesToAutosave
        {
            get
            {
                int length = gameStatesToAutosave.Length;
                LockstepGameState[] result = new LockstepGameState[length];
                gameStatesToAutosave.CopyTo(result, 0);
                return result;
            }
            set
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  GameStatesToAutosave.set");
                #endif
                if (value == null)
                {
                    if (gameStatesToAutosave.Length == 0)
                        return;
                    gameStatesToAutosave = new LockstepGameState[0];
                    StartOrStopAutosave();
                    MarkForOnGameStatesToAutosaveChanged();
                    return;
                }

                int validCount = 0;
                bool identical = true;
                foreach (LockstepGameState gs in value)
                    if (gs != null && gs.GameStateSupportsImportExport)
                    {
                        if (validCount >= gameStatesToAutosave.Length || gs != gameStatesToAutosave[validCount])
                            identical = false;
                        validCount++;
                    }
                if (identical && validCount == gameStatesToAutosave.Length) // To only raise the event if it actually changed.
                    return;

                gameStatesToAutosave = new LockstepGameState[validCount];
                if (validCount == 0)
                {
                    StartOrStopAutosave();
                    MarkForOnGameStatesToAutosaveChanged();
                    return;
                }
                int i = 0;
                foreach (LockstepGameState gs in value)
                    if (gs != null && gs.GameStateSupportsImportExport)
                        gameStatesToAutosave[i++] = gs;
                StartOrStopAutosave();
                MarkForOnGameStatesToAutosaveChanged();
            }
        }
        public override int GameStatesToAutosaveCount => gameStatesToAutosave.Length;

        private float autosaveIntervalSeconds = 300f;
        public override float AutosaveIntervalSeconds
        {
            get => autosaveIntervalSeconds;
            set
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  AutosaveIntervalSeconds.set");
                #endif
                if (float.IsInfinity(value) || float.IsNaN(value))
                {
                    Debug.LogError($"[Lockstep] Attempt to write {value} to {nameof(AutosaveIntervalSeconds)} "
                        + $"which is invalid (+-inf and nan are invalid), ignoring. Treat this like an exception.");
                    return;
                }
                float newValue = Mathf.Max(0f, value);
                if (newValue == autosaveIntervalSeconds)
                    return;
                autosaveIntervalSeconds = newValue;
                // If it is not paused, send another loop event which should be received at the right time.
                if (autosaveTimerPausedAt == -1f)
                    SendCustomEventDelayedSeconds(nameof(AutosaveLoop), SecondsUntilNextAutosave);
                MarkForOnAutosaveIntervalSecondsChanged();
            }
        }

        /// <summary>
        /// <para><p>-1f</p> means the timer is not running.</para>
        /// </summary>
        private float autosaveTimerStart = -1f;
        /// <summary>
        /// <para><p>-1f</p> means the timer is not paused. It is only ever -1f if
        /// <see cref="autosaveTimerStart"/> is non -1f, so it's actually running.</para>
        /// </summary>
        private float autosaveTimerPausedAt = float.PositiveInfinity;

        public override float SecondsUntilNextAutosave => autosaveTimerPausedAt == -1f
            ? Mathf.Max(0f, autosaveIntervalSeconds - (Time.time - autosaveTimerStart))
            : float.IsInfinity(autosaveTimerPausedAt) ? autosaveTimerPausedAt
            : Mathf.Max(0f, autosaveIntervalSeconds - autosaveTimerPausedAt);

        private int autosavePauseScopeCount = 0;
        public override bool IsAutosavePaused => autosavePauseScopeCount != 0;

        public override void StartScopedAutosavePause()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartScopedAutosavePause");
            #endif
            autosavePauseScopeCount++;
            if (autosavePauseScopeCount == 1)
            {
                StartOrStopAutosave();
                MarkForOnIsAutosavePausedChanged();
            }
        }

        public override void StopScopedAutosavePause()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StopScopedAutosavePause");
            #endif
            if (autosavePauseScopeCount == 0)
                return;
            autosavePauseScopeCount--;
            if (autosavePauseScopeCount == 0)
            {
                StartOrStopAutosave();
                MarkForOnIsAutosavePausedChanged();
            }
        }

        private int autosaveCount = 0;

        public void AutosaveLoop()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AutosaveLoop");
            #endif
            if (autosaveTimerPausedAt != -1f) // Autosaving is paused or straight up no longer running.
                return;
            float timePassed = Time.time - autosaveTimerStart;
            if (timePassed + 1.5f < autosaveIntervalSeconds) // Accept the event 1.5 seconds early, but if it's
                return; // earlier than that, nope, too soon, ignore this call. It's caused by duplicate calls.
            string autosaveName = $"autosave {++autosaveCount} (tick: {currentTick})";
            Export(GameStatesToAutosave, autosaveName); // Export writes to the log file.
            autosaveTimerStart = Time.time;
            SendCustomEventDelayedSeconds(nameof(AutosaveLoop), autosaveIntervalSeconds);
        }
    }
}
