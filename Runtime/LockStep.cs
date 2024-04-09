using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
    internal enum ClientState : byte
    {
        Master,
        WaitingForLateJoinerSync,
        CatchingUp,
        Normal,
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockStep : UdonSharpBehaviour
    {
        private const float TickRate = 10f;
        private const string InputActionDataField = "iaData";

        // LJ = late joiner, IA = input action
        private const uint LJCurrentTickIAId = 0;
        private const uint LJClientStatesIAId = 1;
        // custom game states will have ids starting at this, ascending
        private const uint LJFirstCustomGameStateIAId = 2;

        public InputActionSync lateJoinerInputActionSync;
        public LockStepTickSync tickSync;
        [System.NonSerialized] public uint currentTick;
        [System.NonSerialized] public uint waitTick; // The system will run this tick, but not past it.

        private VRCPlayerApi localPlayer;
        private InputActionSync inputActionSyncForLocalPlayer;
        private uint startTick;
        private uint firstMutableTick; // Effectively 1 tick past the last immutable tick.
        private float tickStartTime;
        private int syncCountForLatestLJSync = -1;
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
        private bool isSinglePlayer = false;
        private bool checkMasterChangeAfterProcessingLJGameStates = false;

        private uint unrecoverableStateDueToUniqueId = 0u;

        ///cSpell:ignore xxpppppp

        public const int PlayerIdKeyShift = 16;
        // uint uniqueId => objet[] { uint inputActionId, byte[] inputActionData }
        // uniqueId: pppppppp pppppppp iiiiiiii iiiiiiii (p = player id, i = input action index)
        //
        // Unique ids associated with their input actions, all of which are input actions
        // which have not been run yet and are either waiting for the tick in which they will be run,
        // or waiting for tick sync to inform this client of which tick to run them in.
        private DataDictionary inputActionsByUniqueId = new DataDictionary();

        // uint => uint[]
        // uint: tick to run in
        // uint[]: unique ids (same as for inputActionsByUniqueId)
        private DataDictionary uniqueIdsByTick = new DataDictionary();

        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] inputActionHandlerInstances;
        [SerializeField] [HideInInspector] private string[] inputActionHandlerEventNames;

        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onInitListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientJoinedListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientBeginCatchUpListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientCaughtUpListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientLeftListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onMasterChangedListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onTickListeners;

        [SerializeField] [HideInInspector] private LockStepGameState[] allGameStates;

        // **Internal Game State**
        // int => byte
        // int: playerId
        // byte: ClientState
        private DataDictionary clientStates = null;
        // non game state
        private int[] leftClients = new int[ArrList.MinCapacity];
        private int leftClientsCount = 0;
        // This flag ultimately indicates that there is no client with the Master state in the clientStates game state
        private bool currentlyNoMaster = true;

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

        ///<summary>
        ///<para>This is NOT part of the game state.</para>
        ///<para>Guaranteed to be true on exactly 1 client during the execution of any LockStep event or input
        ///action. Outside of those functions it is possible for this to be true for 0 clients at some point
        ///in time. This is generally useful to only send an input action once - that is from the current
        ///master client.</para>
        ///<para>However unfortunately it is possible for input actions sent by the master to get dropped if
        ///the master leaves the instance shortly after sending input actions. Therefore it may be required to
        ///handle the master changed event and validate the current state, resending input actions that got
        ///dropped in there. It is guaranteed that any input actions sent by the previous master will have
        ///run before the master changed event.</para>
        ///</summary>
        public bool IsMaster => isMaster && !isCatchingUp; // The actions run during catch up are actions that
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

        private void Start()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  Start");
            #endif
            localPlayer = Networking.LocalPlayer;
            lateJoinerInputActionSync.lockStep = this;
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

            float timePassed = Time.time - tickStartTime;
            uint runUntilTick = System.Math.Min(waitTick, startTick + (uint)(timePassed * TickRate));
            for (uint tick = currentTick + 1; tick <= runUntilTick; tick++)
            {
                if (sendLateJoinerDataAtEndOfTick && currentTick > firstMutableTick)
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
                tickSync.syncedTick = currentTick - 1u;
            }

            lastUpdateSW.Stop();
        }

        private void CatchUp()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  CatchUp");
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
                SendClientCaughtUpIA();
                startTick = currentTick;
                tickStartTime = Time.time;
                if (isMaster)
                    FinishCatchingUpOnMaster();
            }
        }

        private void RemoveOutdatedUniqueIdsByTick()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RemoveOutdatedUniqueIdsByTick");
            #endif
            DataList keys = uniqueIdsByTick.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken tickToRunToken = keys[i];
                if (tickToRunToken.UInt > currentTick)
                    continue;

                uniqueIdsByTick.Remove(tickToRunToken, out DataToken uniqueIdsToken);
                foreach (uint uniqueId in (uint[])uniqueIdsToken.Reference)
                    inputActionsByUniqueId.Remove(uniqueId); // Remove simply does nothing if it already doesn't exist.
            }
        }

        private bool TryRunNextTick()
        {
            uint nextTick = currentTick + 1u;
            DataToken nextTickToken = nextTick;
            uint[] uniqueIds = null;
            if (uniqueIdsByTick.TryGetValue(nextTickToken, out DataToken uniqueIdsToken))
            {
                uniqueIds = (uint[])uniqueIdsToken.Reference;
                foreach (uint uniqueId in uniqueIds)
                    if (!inputActionsByUniqueId.ContainsKey(uniqueId))
                    {
                        int playerId = (int)(uniqueId >> PlayerIdKeyShift);
                        if (uniqueId != unrecoverableStateDueToUniqueId && !clientStates.ContainsKey(playerId))
                        {
                            // This variable is purely used as to not spam the log file every frame with this error message.
                            unrecoverableStateDueToUniqueId = uniqueId;
                            /// cSpell:ignore desync, desyncs
                            Debug.LogError($"[LockStep] There's an input action queued to run on the tick {nextTick} "
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

            currentTick = nextTick;
            // Slowly increase the immutable tick. This prevents potential lag spikes when many input actions
            // were sent while catching up. This approach also prevents being caught catching up forever by
            // not touching waitTick.
            // Still continue increasing it even when done catching up, for the same reason: no lag spikes.
            if ((currentTick % TickRate) == 0u)
                firstMutableTick++;
            #if LockStepDebug
            // Debug.Log($"[DebugLockStep] Running tick {currentTick}");
            #endif
            if (uniqueIds != null)
                RunInputActionsForUniqueIds(uniqueIds);
            return true;
        }

        private void RunInputActionsForUniqueIds(uint[] uniqueIds)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RunInputActionsForUniqueIds");
            #endif
            foreach (uint uniqueId in uniqueIds)
                RunInputActionForUniqueId(uniqueId);
        }

        private void RunInputActionForUniqueId(uint uniqueId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RunInputActionForUniqueId");
            #endif
            inputActionsByUniqueId.Remove(uniqueId, out DataToken inputActionDataToken);
            object[] inputActionData = (object[])inputActionDataToken.Reference;
            RunInputAction((uint)inputActionData[0], (byte[])inputActionData[1]);
        }

        private void RunInputAction(uint inputActionId, byte[] inputActionData)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RunInputAction");
            #endif
            UdonSharpBehaviour inst = inputActionHandlerInstances[inputActionId];
            ResetReadStream();
            readStream = inputActionData;
            inst.SendCustomEvent(inputActionHandlerEventNames[inputActionId]);
        }

        public void SendInputAction(uint inputActionId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendInputAction - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            if (ignoreLocalInputActions && !(stillAllowLocalClientJoinedIA && inputActionId == clientJoinedIAId))
                return;

            byte[] inputActionData = new byte[writeStreamSize];
            for (int i = 0; i < writeStreamSize; i++)
                inputActionData[i] = writeStream[i];
            ResetWriteStream();

            if (isSinglePlayer) // Guaranteed to be master while in single player.
            {
                TryToInstantlyRunInputActionOnMaster(inputActionId, 0u, inputActionData);
                return;
            }

            uint uniqueId = inputActionSyncForLocalPlayer.SendInputAction(inputActionId, inputActionData, inputActionData.Length);
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendInputAction (inner) - uniqueId: 0x{uniqueId:x8}");
            #endif

            if (stillAllowLocalClientJoinedIA)
            {
                if (ignoreLocalInputActions)
                {
                    #if LockStepDebug
                    Debug.Log($"[LockStepDebug] LockStep  SendInputAction (inner) - ignoreLocalInputActions is true, returning");
                    #endif
                    return; // Do not save client joined IA because it will never be executed locally.
                }
                Debug.LogError("[LockStep] stillAllowLocalClientJoinedIA is true while ignoreLocalInputActions is false. "
                    + "This is an invalid state, stillAllowLocalClientJoinedIA should only ever be true if "
                    + "ignoreLocalInputActions is also true. Continuing as though stillAllowLocalClientJoinedIA was false.");
            }

            inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData }));
        }

        public void InputActionSent(uint uniqueId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  InputActionSent");
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnInputActionSyncPlayerAssigned");
            #endif
            if (!player.isLocal)
                return;

            // TODO: maybe save static data about players in lock step and expose them as a game state safe api. Things like the display name.

            inputActionSyncForLocalPlayer = inputActionSync;
            SendCustomEventDelayedSeconds(nameof(OnLocalInputActionSyncPlayerAssignedDelayed), 2f);
        }

        public void OnLocalInputActionSyncPlayerAssignedDelayed()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnLocalInputActionSyncPlayerAssignedDelayed");
            #endif
            if (isMaster)
            {
                Debug.Log($"[LockStep] isMaster is already true 2 seconds after the local player's "
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnPlayerLeft - player is null: {player == null}");
            #endif
            int playerId = player.playerId;

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
                    Debug.LogError("[LockStep] clientStates should be impossible to be null when "
                        + "isWaitingAfterJustJoining and isWaitingForLateJoinerSync are both false.");
                // Still waiting for late joiner sync, so who knows,
                // maybe this client will become the new master.
                someoneLeftWhileWeWereWaitingForLJSyncSentCount++;
                SendCustomEventDelayedSeconds(nameof(SomeoneLeftWhileWeWereWaitingForLJSync), 2.5f);
                // Note that the delay should be > then the delay for the call to OnLocalInputActionSyncPlayerAssignedDelayed.
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SomeoneLeftWhileWeWereWaitingForLJSync");
            #endif
            if ((--someoneLeftWhileWeWereWaitingForLJSyncSentCount) != 0)
                return;

            if (clientStates == null)
            {
                if (!currentlyNoMaster)
                {
                    Debug.LogError("[LockStep] currentlyNoMaster should be impossible to "
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SetMasterLeftFlag");
            #endif
            if (currentlyNoMaster)
                return;
            currentlyNoMaster = true;
            SendCustomEventDelayedSeconds(nameof(CheckMasterChange), 0.2f);
        }

        private void BecomeInitialMaster()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  BecomeInitialMaster");
            #endif
            isMaster = true;
            currentlyNoMaster = false;
            ignoreLocalInputActions = false;
            ignoreIncomingInputActions = false;
            isWaitingToSendClientJoinedIA = false;
            isWaitingForLateJoinerSync = false;
            clientStates = new DataDictionary();
            clientStates.Add(localPlayer.playerId, (byte)ClientState.Master);
            lateJoinerInputActionSync.lockStepIsMaster = true;
            // Just to quadruple check, setting owner on both. Trust issues with VRChat.
            Networking.SetOwner(localPlayer, lateJoinerInputActionSync.gameObject);
            Networking.SetOwner(localPlayer, tickSync.gameObject);
            tickSync.RequestSerialization();
            startTick = 0u;
            currentTick = 1u; // Start at 1 because tick sync will always be 1 behind, and ticks are unsigned.
            waitTick = uint.MaxValue;
            EnterSingePlayerMode();
            RaiseOnInit();
            RaiseOnClientJoined(localPlayer.playerId);
            isTickPaused = false;
            tickStartTime = Time.time;
        }

        private bool IsAnyClientWaitingForLateJoinerSync()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  IsAnyClientWaitingForLateJoinerSync");
            #endif
            DataList allStates = clientStates.GetValues();
            for (int i = 0; i < allStates.Count; i++)
                if ((ClientState)allStates[i].Byte == ClientState.WaitingForLateJoinerSync)
                    return true;
            return false;
        }

        private bool IsAnyClientNotWaitingForLateJoinerSync()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  IsAnyClientNotWaitingForLateJoinerSync");
            #endif
            DataList allStates = clientStates.GetValues();
            for (int i = 0; i < allStates.Count; i++)
                if ((ClientState)allStates[i].Byte != ClientState.WaitingForLateJoinerSync)
                    return true;
            return false;
        }

        public void CheckOtherMasterCandidates()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  CheckOtherMasterCandidates");
            #endif
            if ((--checkOtherMasterCandidatesSentCount) != 0)
                return;

            DataList allPlayerIds = clientStates.GetKeys();
            for (int i = 0; i < allPlayerIds.Count; i++)
            {
                DataToken playerIdToken = allPlayerIds[i];
                if ((ClientState)clientStates[playerIdToken].Byte == ClientState.WaitingForLateJoinerSync)
                    continue;
                int playerId = playerIdToken.Int;
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
                if (player == null)
                    continue;
                Debug.Log("[LockStep] // TODO: Ask the given client to become master. Keep in mind that "
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
            CheckMasterChange();
        }

        private void FactoryReset()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  FactoryReset");
            #endif
            lateJoinerInputActionSync.gameObject.SetActive(true);
            lateJoinerInputActionSync.lockStepIsMaster = false;
            tickSync.ClearInputActionsToRun();
            ForgetAboutUnprocessedLJSerializedGameSates();
            ForgetAboutLeftPlayers();
            ForgetAboutInputActionsWaitingToBeSent();
            clientStates = null;
            currentlyNoMaster = true;
            isWaitingToSendClientJoinedIA = true;
            isWaitingForLateJoinerSync = false;
            inputActionsByUniqueId.Clear();
            uniqueIdsByTick.Clear();
            isTickPaused = true;
        }

        public void CheckMasterChange()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  CheckMasterChange");
            #endif

            if (inputActionSyncForLocalPlayer == null)
            {
                // This can happen when on ownership transferred runs on LockStepTickSync,
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

            isMaster = true; // currentlyNoMaster will be set to false in SendMasterChangedIA later.
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            ignoreIncomingInputActions = false;
            isTickPaused = false;
            isCatchingUp = true; // Catch up as quickly as possible to the waitTick. Unless it was already
            // catching up, this should usually only quickly advance by 1 or 2 ticks, which is fine. The real
            // reason this is required is for the public IsMaster property to behave correctly.

            // The immutable tick prevents any newly enqueued input actions from being enqueued too early,
            // to prevent desyncs when not in single player as wells as poor IA ordering in general.
            firstMutableTick = waitTick + 1;

            lateJoinerInputActionSync.gameObject.SetActive(true);
            lateJoinerInputActionSync.lockStepIsMaster = true;
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  FinishCatchingUpOnMaster");
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  InstantlyRunInputActionsWaitingToBeSent");
            #endif
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: true);
        }

        private void ForgetAboutInputActionsWaitingToBeSent()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ForgetAboutInputActionsWaitingToBeSent");
            #endif
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: false);
        }

        public void ProcessLeftPlayers()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ProcessLeftPlayers");
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ForgetAboutLeftPlayers");
            #endif
            ArrList.Clear(ref leftClients, ref leftClientsCount);
        }

        private void CheckSingePlayerModeChange()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  CheckSingePlayerModeChange");
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  EnterSingePlayerMode");
            #endif
            isSinglePlayer = true;
            lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            InstantlyRunInputActionsWaitingToBeSent();
            tickSync.ClearInputActionsToRun();
        }

        private void ExitSinglePlayerMode()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ExitSinglePlayerMode");
            #endif
            isSinglePlayer = false;
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
        }

        private void SendMasterChangedIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendMasterChangedIA");
            #endif
            Write(localPlayer.playerId);
            SendInputAction(masterChangedIAId);
        }

        [SerializeField] [HideInInspector] private uint masterChangedIAId;
        [LockStepInputAction(nameof(masterChangedIAId))]
        public void OnMasterChangedIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnMasterChangedIA");
            #endif
            int playerId = ReadInt();
            clientStates[playerId] = (byte)ClientState.Master;
            currentlyNoMaster = false;
            RaiseOnMasterChanged(playerId);
        }

        private void SendClientJoinedIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendClientJoinedIA");
            #endif
            Write(localPlayer.playerId);
            isWaitingToSendClientJoinedIA = false;
            isWaitingForLateJoinerSync = true;
            clientStates = null; // To know if this client actually received all data, first to last.
            SendInputAction(clientJoinedIAId);
        }

        [SerializeField] [HideInInspector] private uint clientJoinedIAId;
        [LockStepInputAction(nameof(clientJoinedIAId))]
        public void OnClientJoinedIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnClientJoinedIA");
            #endif
            int playerId = ReadInt();
            // Using set value, because the given player may already have a state,
            // because it is valid for the client joined input action to be sent
            // multiple times. And whenever it is sent, it means the client is waiting
            // for late joiner sync.
            clientStates.SetValue(playerId, (byte)ClientState.WaitingForLateJoinerSync);

            if (isMaster)
            {
                CheckSingePlayerModeChange();
                flagForLateJoinerSyncSentCount++;
                SendCustomEventDelayedSeconds(nameof(FlagForLateJoinerSync), 5f);
            }
        }

        public void FlagForLateJoinerSync()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  FlagForLateJoinerSync");
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
            Debug.Log($"[LockStepDebug] LockStep  LogBinaryData:{result}");
        }

        private void SendLateJoinerData()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendLateJoinerData");
            #endif
            if (lateJoinerInputActionSync.QueuedSyncsCount >= Clamp(syncCountForLatestLJSync / 2, 5, 20))
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);

            Write(clientStates.Count);
            DataList keys = clientStates.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken keyToken = keys[i];
                Write(keyToken.Int);
                Write(clientStates[keyToken].Byte);
            }
            lateJoinerInputActionSync.SendInputAction(LJClientStatesIAId, writeStream, writeStreamSize);
            ResetWriteStream();

            for (int i = 0; i < allGameStates.Length; i++)
            {
                allGameStates[i].SerializeGameState(false);
                #if LockStepDebug
                Debug.Log($"[LockStepDebug] LockStep  SendLateJoinerData (inner) - writeStreamSize: {writeStreamSize}");
                LogBinaryData(writeStream, writeStreamSize);
                #endif
                lateJoinerInputActionSync.SendInputAction(LJFirstCustomGameStateIAId + (uint)i, writeStream, writeStreamSize);
                ResetWriteStream();
            }

            Write(currentTick);
            lateJoinerInputActionSync.SendInputAction(LJCurrentTickIAId, writeStream, writeStreamSize);
            ResetWriteStream();

            syncCountForLatestLJSync = lateJoinerInputActionSync.QueuedSyncsCount;
        }

        private void OnLJClientStatesIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnLJClientStatesIA");
            #endif
            // If this client was already receiving data, but then it restarted from
            // the beginning, forget about everything that's been received so far.
            ForgetAboutUnprocessedLJSerializedGameSates();

            clientStates = new DataDictionary();
            int stopBeforeIndex = 1 + 2 * ReadInt();
            for (int i = 1; i < stopBeforeIndex; i += 2)
            {
                // Can't just reuse the tokens from iaData, because they're doubles, because of the json round trip.
                int playerId = ReadInt();
                byte clientState = ReadByte();
                clientStates.Add(playerId, clientState);
                if ((ClientState)clientState == ClientState.Master)
                    currentlyNoMaster = false;
            }
        }

        private void OnLJCustomGameStateIA(uint inputActionId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnLJCustomGameStateIA - clientStates is null: {clientStates == null}");
            #endif
            if (clientStates == null) // This data was not meant for this client. Continue waiting.
                return;

            if (inputActionId - LJFirstCustomGameStateIAId != (uint)unprocessedLJSerializedGSCount)
            {
                Debug.LogError($"[LockStep] Expected game state index {unprocessedLJSerializedGSCount}, "
                    + $"got {inputActionId - LJFirstCustomGameStateIAId}. Either some math "
                    + $"is wrong or the game states are somehow out of order.");
                return;
            }
            ArrList.Add(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount, readStream);
        }

        private void OnLJCurrentTickIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnLJCurrentTickIA - clientStates is null: {clientStates == null}");
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
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  AskForLateJoinerSyncAgain");
            #endif
            // Does not need to keep track of the amount of time this has been raised to only run for the last
            // one, because even after the first time it was sent, by the time this runs the condition below
            // should already be true. And if it isn't, no new late joiner data has been received, so it it
            // should be impossible for this event to have been sent twice. But even if that were to happen,
            // late joiner sync initialization has a 5 second delay on the master, so it doesn't really
            // matter.
            if (clientStates != null)
                return;

            Debug.LogWarning($"[LockStep] The master has not sent another set of late joiner data for 2.5 seconds "
                + "since the last set finished, however this client is still waiting on that data. This "
                + "should be impossible because the master keeps track of joined clients, however "
                + "through mysterious means the first input action for late joiner sync may have been "
                + "lost to the ether, through means unknown to me. Therefore this client is asking the "
                + "master to send late joiner data again.");
            SendClientJoinedIA();
        }

        private void TryMoveToNextLJSerializedGameState()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  TryMoveToNextLJSerializedGameState");
            #endif
            nextLJGameStateToProcess++;
            nextLJGameStateToProcessTime = Time.time + LJGameStateProcessingFrequency;
            if (nextLJGameStateToProcess >= unprocessedLJSerializedGSCount)
                DoneProcessingLJGameStates();
        }

        private void ProcessNextLJSerializedGameState()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ProcessNextLJSerializedGameState");
            #endif
            int gameStateIndex = nextLJGameStateToProcess;
            ResetReadStream();
            readStream = unprocessedLJSerializedGameStates[gameStateIndex];
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ProcessNextLJSerializedGameState (inner) - readStream.Length: {readStream.Length}");
            LogBinaryData(readStream, readStream.Length);
            #endif
            allGameStates[gameStateIndex].DeserializeGameState(false); // TODO: Use return error message.
            TryMoveToNextLJSerializedGameState();
        }

        private void ForgetAboutUnprocessedLJSerializedGameSates()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ForgetAboutUnprocessedLJSerializedGameSates");
            #endif
            nextLJGameStateToProcess = -1;
            checkMasterChangeAfterProcessingLJGameStates = false;
            ArrList.Clear(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount);
        }

        private void DoneProcessingLJGameStates()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  DoneProcessingLJGameStates");
            #endif
            bool doCheckMasterChange = checkMasterChangeAfterProcessingLJGameStates;
            ForgetAboutUnprocessedLJSerializedGameSates();
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            SendClientGotLateJoinerDataIA(); // Must be before OnClientBeginCatchUp, because that can also send input actions.
            RaiseOnClientBeginCatchUp(localPlayer.playerId);
            isTickPaused = false;
            isCatchingUp = true;

            if (doCheckMasterChange)
                CheckMasterChange();
        }

        private void CheckIfLateJoinerSyncShouldStop()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  CheckIfLateJoinerSyncShouldStop");
            #endif
            if (isMaster && !IsAnyClientWaitingForLateJoinerSync())
            {
                sendLateJoinerDataAtEndOfTick = false;
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            }
        }

        private void SendClientGotLateJoinerDataIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendClientGotLateJoinerDataIA");
            #endif
            Write(localPlayer.playerId);
            SendInputAction(clientGotLateJoinerDataIAId);
        }

        [SerializeField] [HideInInspector] private uint clientGotLateJoinerDataIAId;
        [LockStepInputAction(nameof(clientGotLateJoinerDataIAId))]
        public void OnClientGotLateJoinerDataIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnClientGotLateJoinerDataIA");
            #endif
            int playerId = ReadInt();
            clientStates[playerId] = (byte)ClientState.CatchingUp;
            CheckIfLateJoinerSyncShouldStop();
            RaiseOnClientJoined(playerId);
        }

        private void SendClientLeftIA(int playerId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendClientLeftIA");
            #endif
            Write(playerId);
            SendInputAction(clientLeftIAId);
        }

        [SerializeField] [HideInInspector] private uint clientLeftIAId;
        [LockStepInputAction(nameof(clientLeftIAId))]
        public void OnClientLeftIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnClientLeftIA");
            #endif
            int playerId = ReadInt();
            clientStates.Remove(playerId);
            // leftClients may not contain playerId, and that is fine.
            ArrList.Remove(ref leftClients, ref leftClientsCount, playerId);

            CheckIfLateJoinerSyncShouldStop();
            RaiseOnClientLeft(playerId);
        }

        private void SendClientCaughtUpIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  SendClientCaughtUpIA");
            #endif
            Write(localPlayer.playerId);
            SendInputAction(clientCaughtUpIAId);
        }

        [SerializeField] [HideInInspector] private uint clientCaughtUpIAId;
        [LockStepInputAction(nameof(clientCaughtUpIAId))]
        public void OnClientCaughtUpIA()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  OnClientCaughtUpIA");
            #endif
            int playerId = ReadInt();
            clientStates[playerId] = (byte)ClientState.Normal;
            RaiseOnClientCaughtUp(playerId);
        }

        public void ReceivedInputAction(bool isLateJoinerSync, uint inputActionId, uint uniqueId, byte[] inputActionData)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ReceivedInputAction - isLateJoinerSync: {isLateJoinerSync}, inputActionId: {inputActionId}, uniqueId: 0x{uniqueId:x8}{(isLateJoinerSync ? "" : $", event name {inputActionHandlerEventNames[inputActionId]}")}");
            #endif
            if (isLateJoinerSync)
            {
                if (!isWaitingForLateJoinerSync)
                    return;
                ResetReadStream();
                readStream = inputActionData;
                if (inputActionId == LJClientStatesIAId)
                    OnLJClientStatesIA();
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

        private void TryToInstantlyRunInputActionOnMaster(uint inputActionId, uint uniqueId, byte[] inputActionData)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  TryToInstantlyRunInputActionOnMaster");
            #endif
            if (currentTick < firstMutableTick) // Can't run it in the current tick. A check for isCatchingUp
            { // is not needed, because the condition above will always be true when isCatchingUp is true.
                if (uniqueId == 0u)
                {
                    // It'll only be 0 if the local player is the one trying to instantly run it.
                    // Received data always has a unique id.
                    uniqueId = inputActionSyncForLocalPlayer.MakeUniqueId();
                }
                inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData }));
                AssociateInputActionWithTick(firstMutableTick, uniqueId, allowOnMaster: true);
                return;
            }

            if (!isSinglePlayer)
            {
                if (uniqueId == 0u)
                {
                    Debug.LogError("[LockStep] Impossible, the uniqueId when instantly running an input action "
                        + "on master cannot be 0 while not in single player, because every input action "
                        + "get sent over the network and gets a unique id assigned in the process. "
                        + "Something is very wrong in the code. Ignoring this action.");
                    return;
                }
                tickSync.AddInputActionToRun(currentTick, uniqueId);
            }
            RunInputAction(inputActionId, inputActionData);
        }

        private void ClearUniqueIdsByTick()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  ClearUniqueIdsByTick");
            #endif
            // When only clearing uniqueIdsByTick we can't just clear inputActionsByUniqueId,
            // because that may contain very new input actions which have not been associated
            // with a tick yet, so they'd be associated with future ticks which means we must
            // keep them alive. The below logic just removes the ones we know for certain that
            // they can be removed, just to free up some memory.
            DataList allValues = uniqueIdsByTick.GetValues();
            for (int i = 0; i < allValues.Count; i++)
                foreach (uint uniqueId in (uint[])allValues[i].Reference)
                    inputActionsByUniqueId.Remove(uniqueId);
            uniqueIdsByTick.Clear();
        }

        public void AssociateInputActionWithTick(uint tickToRunIn, uint uniqueId, bool allowOnMaster = false)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  AssociateInputActionWithTick - tickToRunIn: {tickToRunIn}, uniqueId: 0x{uniqueId:x8}");
            #endif
            if (tickToRunIn == 0u && uniqueId == 0u)
            {
                ClearUniqueIdsByTick();
                return;
            }

            if (ignoreIncomingInputActions)
                return;
            if (isMaster && !allowOnMaster)
            {
                Debug.LogWarning("[LockStep] The master client (which is this client) should "
                    + "not be receiving data about running an input action at a tick...");
            }

            // Mark the input action to run at the given tick.
            DataToken tickToRunInToken = new DataToken(tickToRunIn);
            if (uniqueIdsByTick.TryGetValue(tickToRunInToken, out DataToken uniqueIdsToken))
            {
                uint[] uniqueIds = (uint[])uniqueIdsToken.Reference;
                int oldLength = uniqueIds.Length;
                uint[] newUniqueIds = new uint[oldLength + 1];
                uniqueIds.CopyTo(newUniqueIds, 0);
                newUniqueIds[oldLength] = uniqueId;
                uniqueIdsByTick.SetValue(tickToRunInToken, new DataToken(newUniqueIds));
                return;
            }
            uniqueIdsByTick.Add(tickToRunInToken, new DataToken(new uint[] { uniqueId }));
        }

        private void RaiseOnInit()
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RaiseOnInit");
            #endif
            foreach (UdonSharpBehaviour listener in onInitListeners)
                listener.SendCustomEvent(nameof(LockStepEventType.OnInit));
        }

        private void RaiseOnClientJoined(int playerId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RaiseOnClientJoined");
            #endif
            foreach (UdonSharpBehaviour listener in onClientJoinedListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientJoined));
            }
        }

        private void RaiseOnClientBeginCatchUp(int playerId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RaiseOnClientBeginCatchUp");
            #endif
            foreach (UdonSharpBehaviour listener in onClientBeginCatchUpListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientBeginCatchUp));
            }
        }

        private void RaiseOnClientCaughtUp(int playerId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RaiseOnClientCaughtUp");
            #endif
            foreach (UdonSharpBehaviour listener in onClientCaughtUpListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientCaughtUp));
            }
        }

        private void RaiseOnClientLeft(int playerId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RaiseOnClientLeft");
            #endif
            foreach (UdonSharpBehaviour listener in onClientLeftListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientLeft));
            }
        }

        private void RaiseOnMasterChanged(int newMasterPlayerId)
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RaiseOnMasterChanged");
            #endif
            foreach (UdonSharpBehaviour listener in onMasterChangedListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", newMasterPlayerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnMasterChanged));
            }
        }

        private void RaiseOnTick() // TODO: actually raise this somewhere
        {
            #if LockStepDebug
            Debug.Log($"[LockStepDebug] LockStep  RaiseOnTick");
            #endif
            foreach (UdonSharpBehaviour listener in onTickListeners)
                listener.SendCustomEvent(nameof(LockStepEventType.OnTick));
        }

        private byte[] writeStream = new byte[64];
        private int writeStreamSize = 0;

        public void ResetWriteStream() => writeStreamSize = 0;
        public void Write(sbyte value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(byte value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(short value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(ushort value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(int value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(uint value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(long value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(ulong value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(float value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(double value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(Vector2 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(Vector3 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(Vector4 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(Quaternion value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(char value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(string value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public void Write(System.DateTime value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);

        ///<summary>Arrays assigned to this variable always have the exact length of the data that is actually
        ///available to be read, and once assigned to this variable they are immutable.</summary>
        private byte[] readStream = new byte[0];
        private int readStreamPosition = 0;

        public void ResetReadStream() => readStreamPosition = 0;
        public sbyte ReadSByte() => DataStream.ReadSByte(ref readStream, ref readStreamPosition);
        public byte ReadByte() => DataStream.ReadByte(ref readStream, ref readStreamPosition);
        public short ReadShort() => DataStream.ReadShort(ref readStream, ref readStreamPosition);
        public ushort ReadUShort() => DataStream.ReadUShort(ref readStream, ref readStreamPosition);
        public int ReadInt() => DataStream.ReadInt(ref readStream, ref readStreamPosition);
        public uint ReadUInt() => DataStream.ReadUInt(ref readStream, ref readStreamPosition);
        public long ReadLong() => DataStream.ReadLong(ref readStream, ref readStreamPosition);
        public ulong ReadULong() => DataStream.ReadULong(ref readStream, ref readStreamPosition);
        public float ReadFloat() => DataStream.ReadFloat(ref readStream, ref readStreamPosition);
        public double ReadDouble() => DataStream.ReadDouble(ref readStream, ref readStreamPosition);
        public Vector2 ReadVector2() => DataStream.ReadVector2(ref readStream, ref readStreamPosition);
        public Vector3 ReadVector3() => DataStream.ReadVector3(ref readStream, ref readStreamPosition);
        public Vector4 ReadVector4() => DataStream.ReadVector4(ref readStream, ref readStreamPosition);
        public Quaternion ReadQuaternion() => DataStream.ReadQuaternion(ref readStream, ref readStreamPosition);
        public char ReadChar() => DataStream.ReadChar(ref readStream, ref readStreamPosition);
        public string ReadString() => DataStream.ReadString(ref readStream, ref readStreamPosition);
        public System.DateTime ReadDateTime() => DataStream.ReadDateTime(ref readStream, ref readStreamPosition);

        private uint[] crc32LookupCache;

        public string Export(LockStepGameState[] gameStates)
        {
            ResetWriteStream();

            Write(System.DateTime.UtcNow);
            Write(gameStates.Length);

            foreach (LockStepGameState gameState in gameStates)
            {
                Write(gameState.GameStateInternalName);
                Write(gameState.GameStateDataVersion);
            }

            foreach (LockStepGameState gameState in gameStates)
            {
                int sizePosition = writeStreamSize;
                writeStreamSize += 4;
                gameState.SerializeGameState(true);
                int stopPosition = writeStreamSize;
                writeStreamSize = sizePosition;
                Write(stopPosition - sizePosition - 4);
                writeStreamSize = stopPosition;
            }

            Write(CRC32.Compute(ref crc32LookupCache, writeStream, length: writeStreamSize));

            byte[] package = new byte[writeStreamSize];
            for (int i = 0; i < writeStreamSize; i++)
                package[i] = writeStream[i];

            return Base64.Encode(package);
        }
    }
}
