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
        private const float TickRate = 16f;
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
        private uint immutableUntilTick;
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

        [System.NonSerialized] public DataList iaData;
        private uint clientJoinedIAId;
        private uint clientGotLateJoinerDataIAId;
        private uint clientCaughtUpIAId;
        private uint clientLeftIAId;
        private uint masterChangedIAId;

        private uint unrecoverableStateDueToUniqueId = 0u;

        ///cSpell:ignore xxpppppp

        public const int PlayerIdKeyShift = 16;
        // uint => DataList
        // uint: unique id - pppppppp pppppppp iiiiiiii iiiiiiii (p = player id, i = input action index)
        // DataList: input action data, plus input action id appended
        //
        // Unique ids associated with their input actions, all of which are input actions
        // which have not been run yet and are either waiting for the tick in which they will be run,
        // or waiting for tick sync to inform this client of which tick to run them in.
        private DataDictionary inputActionsByUniqueId = new DataDictionary();

        // uint => uint[]
        // uint: tick to run in
        // uint[]: unique ids (same as for inputActionsByUniqueId)
        private DataDictionary uniqueIdsByTick = new DataDictionary();

        ///cSpell:ignore iahi, iahen
        private UdonSharpBehaviour[] inputActionHandlerInstances = new UdonSharpBehaviour[ArrList.MinCapacity];
        private int iahiCount = 0;
        private string[] inputActionHandlerEventNames = new string[ArrList.MinCapacity];
        private int iahenCount = 0;

        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onInitListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientJoinedListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientBeginCatchUpListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientCaughtUpListeners;
        [SerializeField] [HideInInspector] private UdonSharpBehaviour[] onClientLeftListeners;
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

        private DataList[] unprocessedLJSerializedGameStates = new DataList[ArrList.MinCapacity];
        private int unprocessedLJSerializedGSCount = 0;
        private int nextLJGameStateToProcess = -1;
        private float nextLJGameStateToProcessTime = 0f;
        private const float LJGameStateProcessingFrequency = 0.1f;
        private bool IsProcessingLJGameStates => nextLJGameStateToProcess != -1;

        private int initiateLateJoinerSyncSentCount = 0;
        private int processLeftPlayersSentCount = 0;
        private int checkOtherMasterCandidatesSentCount = 0;
        private int someoneLeftWhileWeWereWaitingForLJSyncSentCount = 0;

        // Used by the debug UI.
        private System.Diagnostics.Stopwatch lastUpdateSW = new System.Diagnostics.Stopwatch();

        private void Start()
        {
            Debug.Log($"<dlt> LockStep  Start");
            localPlayer = Networking.LocalPlayer;
            lateJoinerInputActionSync.lockStep = this;

            // TODO: this should probably happen somewhere else, not sure how and where at this moment though
            clientJoinedIAId = RegisterInputAction(this, nameof(OnClientJoinedIA));
            clientGotLateJoinerDataIAId = RegisterInputAction(this, nameof(OnClientGotLateJoinerDataIA));
            clientCaughtUpIAId = RegisterInputAction(this, nameof(OnClientCaughtUpIA));
            clientLeftIAId = RegisterInputAction(this, nameof(OnClientLeftIA));
            masterChangedIAId = RegisterInputAction(this, nameof(OnMasterChangedIA));
        }

        private void Update()
        {
            lastUpdateSW.Reset();
            lastUpdateSW.Start();

            if (isTickPaused)
            {
                if (nextLJGameStateToProcess != -1 && Time.time >= nextLJGameStateToProcessTime)
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
                if (sendLateJoinerDataAtEndOfTick)
                {
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
            Debug.Log($"<dlt> LockStep  CatchUp");
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
            if (waitTick - currentTick < TickRate)
            {
                if (isMaster)
                {
                    waitTick = uint.MaxValue;
                }
                RemoveOutdatedUniqueIdsByTick();
                isCatchingUp = false;
                SendClientCaughtUpIA();
                startTick = currentTick;
                tickStartTime = Time.time;
            }
        }

        private void RemoveOutdatedUniqueIdsByTick()
        {
            Debug.Log($"<dlt> LockStep  RemoveOutdatedUniqueIdsByTick");
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
                            /// cSpell:ignore desync
                            Debug.LogError($"<dlt> There's an input action queued to run on the tick {nextTick} "
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
            }
            uniqueIdsByTick.Remove(nextTickToken);

            currentTick = nextTick;
            // Debug.Log($"<dlt> Running tick {currentTick}");
            if (uniqueIds != null)
                RunInputActionsForUniqueIds(uniqueIds);
            return true;
        }

        private void RunInputActionsForUniqueIds(uint[] uniqueIds)
        {
            Debug.Log($"<dlt> LockStep  RunInputActionsForUniqueIds");
            foreach (uint uniqueId in uniqueIds)
                RunInputActionForUniqueId(uniqueId);
        }

        private void RunInputActionForUniqueId(uint uniqueId)
        {
            Debug.Log($"<dlt> LockStep  RunInputActionForUniqueId");
            inputActionsByUniqueId.Remove(uniqueId, out DataToken inputActionDataToken);
            DataList inputActionData = inputActionDataToken.DataList;
            int lastIndex = inputActionData.Count - 1;
            uint inputActionId = inputActionData[lastIndex].UInt;
            inputActionData.RemoveAt(lastIndex);
            RunInputAction(inputActionId, inputActionData);
        }

        private void RunInputAction(uint inputActionId, DataList inputActionData)
        {
            Debug.Log($"<dlt> LockStep  RunInputAction");
            UdonSharpBehaviour inst = inputActionHandlerInstances[inputActionId];
            inst.SetProgramVariable(InputActionDataField, inputActionData);
            inst.SendCustomEvent(inputActionHandlerEventNames[inputActionId]);
        }

        public void SendInputAction(uint inputActionId, DataList inputActionData)
        {
            Debug.Log($"<dlt> LockStep  SendInputAction - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            if (ignoreLocalInputActions && !(stillAllowLocalClientJoinedIA && inputActionId == clientJoinedIAId))
                return;

            if (isSinglePlayer)
            {
                TryToInstantlyRunInputActionOnMaster(inputActionId, 0u, inputActionData);
                return;
            }

            uint uniqueId = inputActionSyncForLocalPlayer.SendInputAction(inputActionId, inputActionData);
            Debug.Log($"<dlt> LockStep  SendInputAction (inner) - uniqueId: 0x{uniqueId:x8}");

            if (stillAllowLocalClientJoinedIA)
            {
                if (ignoreLocalInputActions)
                {
                    Debug.Log($"<dlt> LockStep  SendInputAction (inner) - ignoreLocalInputActions is true, returning");
                    return; // Do not save client joined IA because it will never be executed locally.
                }
                Debug.LogError("<dlt> stillAllowLocalClientJoinedIA is true while ignoreLocalInputActions is false. "
                    + "This is an invalid state, stillAllowLocalClientJoinedIA should only ever be true if "
                    + "ignoreLocalInputActions is also true. Continuing as though stillAllowLocalClientJoinedIA was false."
                );
            }

            // Modify the inputActionData after sending it, otherwise bad data would be sent.
            inputActionData.Add(inputActionId);
            inputActionsByUniqueId.Add(uniqueId, inputActionData);
        }

        public void InputActionSent(uint uniqueId)
        {
            Debug.Log($"<dlt> LockStep  InputActionSent");
            if (isMaster)
            {
                if (!isSinglePlayer)
                {
                    if (currentTick <= immutableUntilTick)
                    {
                        immutableUntilTick++;
                        AssociateInputActionWithTick(immutableUntilTick, uniqueId, allowOnMaster: true);
                        tickSync.AddInputActionToRun(immutableUntilTick, uniqueId);
                        return;
                    }
                    tickSync.AddInputActionToRun(currentTick, uniqueId);
                }
                RunInputActionForUniqueId(uniqueId);
            }
        }

        public void OnInputActionSyncPlayerAssigned(VRCPlayerApi player, InputActionSync inputActionSync)
        {
            Debug.Log($"<dlt> LockStep  OnInputActionSyncPlayerAssigned");
            if (!player.isLocal)
                return;

            // TODO: maybe save static data about players in lock step and expose them as a game state safe api. Things like the display name.

            inputActionSyncForLocalPlayer = inputActionSync;
            SendCustomEventDelayedSeconds(nameof(OnLocalInputActionSyncPlayerAssignedDelayed), 2f);
        }

        public void OnLocalInputActionSyncPlayerAssignedDelayed()
        {
            Debug.Log($"<dlt> LockStep  OnLocalInputActionSyncPlayerAssignedDelayed");
            if (isMaster)
            {
                Debug.Log($"<dlt> isMaster is already true 2 seconds after the local player's "
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
            Debug.Log($"<dlt> LockStep  OnPlayerLeft - player is null: {player == null}");
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
                    Debug.LogError("<dlt> clientStates should be impossible to be null when "
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
            Debug.Log($"<dlt> LockStep  SomeoneLeftWhileWeWereWaitingForLJSync");
            if ((--someoneLeftWhileWeWereWaitingForLJSyncSentCount) != 0)
                return;

            if (clientStates == null)
            {
                if (!currentlyNoMaster)
                {
                    Debug.LogError("<dlt> currentlyNoMaster should be impossible to "
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
            Debug.Log($"<dlt> LockStep  SetMasterLeftFlag");
            if (currentlyNoMaster)
                return;
            currentlyNoMaster = true;
            SendCustomEventDelayedSeconds(nameof(CheckMasterChange), 0.2f);
        }

        private void BecomeInitialMaster()
        {
            Debug.Log($"<dlt> LockStep  BecomeInitialMaster");
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
            Debug.Log($"<dlt> LockStep  IsAnyClientWaitingForLateJoinerSync");
            DataList allStates = clientStates.GetValues();
            for (int i = 0; i < allStates.Count; i++)
                if ((ClientState)allStates[i].Byte == ClientState.WaitingForLateJoinerSync)
                    return true;
            return false;
        }

        private bool IsAnyClientNotWaitingForLateJoinerSync()
        {
            Debug.Log($"<dlt> LockStep  IsAnyClientNotWaitingForLateJoinerSync");
            DataList allStates = clientStates.GetValues();
            for (int i = 0; i < allStates.Count; i++)
                if ((ClientState)allStates[i].Byte != ClientState.WaitingForLateJoinerSync)
                    return true;
            return false;
        }

        public void CheckOtherMasterCandidates()
        {
            Debug.Log($"<dlt> LockStep  CheckOtherMasterCandidates");
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
                Debug.Log("<dlt> // TODO: Ask the given client to become master. Keep in mind that "
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
            Debug.Log($"<dlt> LockStep  FactoryReset");
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
            Debug.Log($"<dlt> LockStep  CheckMasterChange");

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

            isMaster = true;
            currentlyNoMaster = false;
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            ignoreIncomingInputActions = false;
            isTickPaused = false;

            if (isCatchingUp)
                // If it is currently catching up, it will continue catching up while already being master.
                // Any new input actions must enqueued _after_ the wait tick, as that tick may already
                // have been executed on a different client.
                waitTick++;
            else
            {
                immutableUntilTick = waitTick;
                waitTick = uint.MaxValue;
            }

            lateJoinerInputActionSync.gameObject.SetActive(true);
            lateJoinerInputActionSync.lockStepIsMaster = true;
            Networking.SetOwner(localPlayer, lateJoinerInputActionSync.gameObject);
            Networking.SetOwner(localPlayer, tickSync.gameObject);
            tickSync.RequestSerialization();

            // ProcessLeftPlayers also checks this, but doing it before SendMasterChangedIA is cleaner.
            CheckSingePlayerModeChange();
            SendMasterChangedIA();
            processLeftPlayersSentCount++;
            ProcessLeftPlayers();
            if (isSinglePlayer)
                InstantlyRunInputActionsWaitingToBeSent();

            if (IsAnyClientWaitingForLateJoinerSync())
            {
                initiateLateJoinerSyncSentCount++;
                FlagForLateJoinerSync();
            }
        }

        private void InstantlyRunInputActionsWaitingToBeSent()
        {
            Debug.Log($"<dlt> LockStep  InstantlyRunInputActionsWaitingToBeSent");
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: true);
        }

        private void ForgetAboutInputActionsWaitingToBeSent()
        {
            Debug.Log($"<dlt> LockStep  ForgetAboutInputActionsWaitingToBeSent");
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: false);
        }

        public void ProcessLeftPlayers()
        {
            Debug.Log($"<dlt> LockStep  ProcessLeftPlayers");
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
            Debug.Log($"<dlt> LockStep  ForgetAboutLeftPlayers");
            ArrList.Clear(ref leftClients, ref leftClientsCount);
        }

        private void CheckSingePlayerModeChange()
        {
            Debug.Log($"<dlt> LockStep  CheckSingePlayerModeChange");
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
            Debug.Log($"<dlt> LockStep  EnterSingePlayerMode");
            isSinglePlayer = true;
            lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            InstantlyRunInputActionsWaitingToBeSent();
            tickSync.ClearInputActionsToRun();
        }

        private void ExitSinglePlayerMode()
        {
            Debug.Log($"<dlt> LockStep  ExitSinglePlayerMode");
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
            Debug.Log($"<dlt> LockStep  SendMasterChangedIA");
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            SendInputAction(masterChangedIAId, iaData);
        }

        public void OnMasterChangedIA()
        {
            Debug.Log($"<dlt> LockStep  OnMasterChangedIA");
            int playerId = (int)iaData[0].Double;
            clientStates[playerId] = (byte)ClientState.Master;
            currentlyNoMaster = false;
        }

        private void SendClientJoinedIA()
        {
            Debug.Log($"<dlt> LockStep  SendClientJoinedIA");
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            isWaitingToSendClientJoinedIA = false;
            isWaitingForLateJoinerSync = true;
            clientStates = null; // To know if this client actually received all data, first to last.
            SendInputAction(clientJoinedIAId, iaData);
        }

        public void OnClientJoinedIA()
        {
            Debug.Log($"<dlt> LockStep  OnClientJoinedIA");
            int playerId = (int)iaData[0].Double;
            // Using set value, because the given player may already have a state,
            // because it is valid for the client joined input action to be sent
            // multiple times. And whenever it is sent, it means the client is waiting
            // for late joiner sync.
            clientStates.SetValue(playerId, (byte)ClientState.WaitingForLateJoinerSync);

            if (isMaster)
            {
                CheckSingePlayerModeChange();
                initiateLateJoinerSyncSentCount++;
                SendCustomEventDelayedSeconds(nameof(FlagForLateJoinerSync), 5f);
            }
        }

        public void FlagForLateJoinerSync()
        {
            Debug.Log($"<dlt> LockStep  FlagForLateJoinerSync");
            if ((--initiateLateJoinerSyncSentCount) != 0)
                return;

            if (IsAnyClientWaitingForLateJoinerSync())
                sendLateJoinerDataAtEndOfTick = true;
        }

        private int Clamp(int value, int min, int max)
        {
            return System.Math.Min(max, System.Math.Max(min, value));
        }

        private void SendLateJoinerData()
        {
            Debug.Log($"<dlt> LockStep  SendLateJoinerData");
            if (lateJoinerInputActionSync.QueuedSyncsCount >= Clamp(syncCountForLatestLJSync / 2, 5, 20))
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);

            iaData = new DataList();
            iaData.Add((double)clientStates.Count);
            DataList keys = clientStates.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken keyToken = keys[i];
                iaData.Add((double)keyToken.Int);
                iaData.Add((double)clientStates[keyToken].Byte);
            }
            lateJoinerInputActionSync.SendInputAction(LJClientStatesIAId, iaData);

            for (int i = 0; i < allGameStates.Length; i++)
            {
                iaData = allGameStates[i].SerializeGameState();
                lateJoinerInputActionSync.SendInputAction(LJFirstCustomGameStateIAId + (uint)i, iaData);
            }

            iaData = new DataList();
            iaData.Add(currentTick);
            lateJoinerInputActionSync.SendInputAction(LJCurrentTickIAId, iaData);

            syncCountForLatestLJSync = lateJoinerInputActionSync.QueuedSyncsCount;
        }

        private void OnLJClientStatesIA()
        {
            Debug.Log($"<dlt> LockStep  OnLJClientStatesIA");
            // If this client was already receiving data, but then it restarted from
            // the beginning, forget about everything that's been received so far.
            ForgetAboutUnprocessedLJSerializedGameSates();

            clientStates = new DataDictionary();
            int stopBeforeIndex = 1 + 2 * (int)iaData[0].Double;
            for (int i = 1; i < stopBeforeIndex; i += 2)
            {
                // Can't just reuse the tokens from iaData, because they're doubles, because of the json round trip.
                int playerId = (int)iaData[i].Double;
                byte clientState = (byte)iaData[i + 1].Double;
                clientStates.Add(playerId, clientState);
                if ((ClientState)clientState == ClientState.Master)
                    currentlyNoMaster = false;
            }
        }

        private void OnLJCustomGameStateIA(uint inputActionId)
        {
            Debug.Log($"<dlt> LockStep  OnLJCustomGameStateIA - clientStates is null: {clientStates == null}");
            if (clientStates == null) // This data was not meant for this client. Continue waiting.
                return;

            if (inputActionId - LJFirstCustomGameStateIAId != (uint)unprocessedLJSerializedGSCount)
            {
                Debug.LogError($"<dlt> Expected game state index {unprocessedLJSerializedGSCount}, "
                    + $"got {inputActionId - LJFirstCustomGameStateIAId}. Either some math "
                    + $"is wrong or the game states are somehow out of order."
                );
                return;
            }
            ArrList.Add(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount, iaData);
        }

        private void OnLJCurrentTickIA()
        {
            Debug.Log($"<dlt> LockStep  OnLJCurrentTickIA - clientStates is null: {clientStates == null}");
            if (clientStates == null) // This data was not meant for this client. Continue waiting.
            {
                SendCustomEventDelayedSeconds(nameof(AskForLateJoinerSyncAgain), 2.5f);
                return;
            }

            currentTick = (uint)iaData[0].Double;

            lateJoinerInputActionSync.gameObject.SetActive(false);
            isWaitingForLateJoinerSync = false;
            TryMoveToNextLJSerializedGameState();
        }

        public void AskForLateJoinerSyncAgain()
        {
            Debug.Log($"<dlt> LockStep  AskForLateJoinerSyncAgain");
            // Does not need to keep track of the amount of time this has been raised to only run for the last
            // one, because even after the first time it was sent, by the time this runs the condition below
            // should already be true. And if it isn't, no new late joiner data has been received, so it it
            // should be impossible for this event to have been sent twice. But even if that were to happen,
            // late joiner sync initialization has a 5 second delay on the master, so it doesn't really
            // matter.
            if (clientStates != null)
                return;

            Debug.LogWarning($"<dlt> The master has not sent another set of late joiner data for 2.5 seconds "
                + "since the last set finished, however this client is still waiting on that data. This "
                + "should be impossible because the master keeps track of joined clients, however "
                + "through mysterious means the first input action for late joiner sync may have been "
                + "lost to the ether, through means unknown to me. Therefore this client is asking the "
                + "master to send late joiner data again.");
            SendClientJoinedIA();
        }

        private void TryMoveToNextLJSerializedGameState()
        {
            Debug.Log($"<dlt> LockStep  TryMoveToNextLJSerializedGameState");
            nextLJGameStateToProcess++;
            nextLJGameStateToProcessTime = Time.time + LJGameStateProcessingFrequency;
            if (nextLJGameStateToProcess >= unprocessedLJSerializedGSCount)
                DoneProcessingLJGameStates();
        }

        private void ProcessNextLJSerializedGameState()
        {
            Debug.Log($"<dlt> LockStep  ProcessNextLJSerializedGameState");
            int gameStateIndex = nextLJGameStateToProcess;
            iaData = unprocessedLJSerializedGameStates[gameStateIndex];
            allGameStates[gameStateIndex].DeserializeGameState(iaData);
            TryMoveToNextLJSerializedGameState();
        }

        private void ForgetAboutUnprocessedLJSerializedGameSates()
        {
            Debug.Log($"<dlt> LockStep  ForgetAboutUnprocessedLJSerializedGameSates");
            nextLJGameStateToProcess = -1;
            checkMasterChangeAfterProcessingLJGameStates = false;
            ArrList.Clear(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount);
        }

        private void DoneProcessingLJGameStates()
        {
            Debug.Log($"<dlt> LockStep  DoneProcessingLJGameStates");
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
            Debug.Log($"<dlt> LockStep  CheckIfLateJoinerSyncShouldStop");
            if (isMaster && !IsAnyClientWaitingForLateJoinerSync())
            {
                sendLateJoinerDataAtEndOfTick = false;
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            }
        }

        private void SendClientGotLateJoinerDataIA()
        {
            Debug.Log($"<dlt> LockStep  SendClientGotLateJoinerDataIA");
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            SendInputAction(clientGotLateJoinerDataIAId, iaData);
        }

        public void OnClientGotLateJoinerDataIA()
        {
            Debug.Log($"<dlt> LockStep  OnClientGotLateJoinerDataIA");
            int playerId = (int)iaData[0].Double;
            clientStates[playerId] = (byte)ClientState.CatchingUp;
            CheckIfLateJoinerSyncShouldStop();
            RaiseOnClientJoined(playerId);
        }

        private void SendClientLeftIA(int playerId)
        {
            Debug.Log($"<dlt> LockStep  SendClientLeftIA");
            iaData = new DataList();
            iaData.Add((double)playerId);
            SendInputAction(clientLeftIAId, iaData);
        }

        public void OnClientLeftIA()
        {
            Debug.Log($"<dlt> LockStep  OnClientLeftIA");
            int playerId = (int)iaData[0].Double;
            clientStates.Remove(playerId);
            // leftClients may not contain playerId, and that is fine.
            ArrList.Remove(ref leftClients, ref leftClientsCount, playerId);

            CheckIfLateJoinerSyncShouldStop();
            RaiseOnClientLeft(playerId);
        }

        private void SendClientCaughtUpIA()
        {
            Debug.Log($"<dlt> LockStep  SendClientCaughtUpIA");
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            SendInputAction(clientCaughtUpIAId, iaData);
        }

        public void OnClientCaughtUpIA()
        {
            Debug.Log($"<dlt> LockStep  OnClientCaughtUpIA");
            int playerId = (int)iaData[0].Double;
            clientStates[playerId] = (byte)ClientState.Normal;
            RaiseOnClientCaughtUp(playerId);
        }

        public void ReceivedInputAction(bool isLateJoinerSync, uint inputActionId, uint uniqueId, DataList inputActionData)
        {
            Debug.Log($"<dlt> LockStep  ReceivedInputAction - isLateJoinerSync: {isLateJoinerSync}, inputActionId: {inputActionId}, uniqueId: 0x{uniqueId:x8}{(isLateJoinerSync ? "" : $", event name {inputActionHandlerEventNames[inputActionId]}")}");
            if (isLateJoinerSync)
            {
                if (!isWaitingForLateJoinerSync)
                    return;
                iaData = inputActionData;
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
            {
                inputActionData.Add(inputActionId);
                inputActionsByUniqueId.Add(uniqueId, inputActionData);
            }
        }

        private void TryToInstantlyRunInputActionOnMaster(uint inputActionId, uint uniqueId, DataList inputActionData)
        {
            Debug.Log($"<dlt> LockStep  TryToInstantlyRunInputActionOnMaster");
            if (isCatchingUp) // Can't instantly run it while still catching up, enqueue it after all input actions.
            {
                if (uniqueId == 0u)
                {
                    // It'll only be 0 if the local player is the one trying to instantly run it.
                    // Received data always has a unique id.
                    uniqueId = inputActionSyncForLocalPlayer.MakeUniqueId();
                }
                inputActionsByUniqueId.Add(uniqueId, inputActionData);
                AssociateInputActionWithTick(waitTick, uniqueId);
                waitTick++;
                return;
            }

            if (!isSinglePlayer)
            {
                if (uniqueId == 0u)
                {
                    Debug.LogError("<dlt> Impossible, the uniqueId when instantly running an input action "
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
            Debug.Log($"<dlt> LockStep  ClearUniqueIdsByTick");
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
            Debug.Log($"<dlt> LockStep  AssociateInputActionWithTick - tickToRunIn: {tickToRunIn}, uniqueId: 0x{uniqueId:x8}");
            if (tickToRunIn == 0u && uniqueId == 0u)
            {
                ClearUniqueIdsByTick();
                return;
            }

            if (ignoreIncomingInputActions)
                return;
            if (isMaster && !isCatchingUp && !allowOnMaster) // If it is catching up, it's still enqueueing actions for later.
            {
                Debug.LogWarning("<dlt> The master client (which is this client) should "
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
            Debug.Log($"<dlt> LockStep  RaiseOnInit");
            foreach (UdonSharpBehaviour listener in onInitListeners)
                listener.SendCustomEvent(nameof(LockStepEventType.OnInit));
        }

        private void RaiseOnClientJoined(int playerId)
        {
            Debug.Log($"<dlt> LockStep  RaiseOnClientJoined");
            foreach (UdonSharpBehaviour listener in onClientJoinedListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientJoined));
            }
        }

        private void RaiseOnClientBeginCatchUp(int playerId)
        {
            Debug.Log($"<dlt> LockStep  RaiseOnClientBeginCatchUp");
            foreach (UdonSharpBehaviour listener in onClientBeginCatchUpListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientBeginCatchUp));
            }
        }

        private void RaiseOnClientCaughtUp(int playerId)
        {
            Debug.Log($"<dlt> LockStep  RaiseOnClientCaughtUp");
            foreach (UdonSharpBehaviour listener in onClientCaughtUpListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientCaughtUp));
            }
        }

        private void RaiseOnClientLeft(int playerId)
        {
            Debug.Log($"<dlt> LockStep  RaiseOnClientLeft");
            foreach (UdonSharpBehaviour listener in onClientLeftListeners)
            {
                listener.SetProgramVariable("lockStepPlayerId", playerId);
                listener.SendCustomEvent(nameof(LockStepEventType.OnClientLeft));
            }
        }

        private void RaiseOnTick() // TODO: actually raise this somewhere
        {
            Debug.Log($"<dlt> LockStep  RaiseOnTick");
            foreach (UdonSharpBehaviour listener in onTickListeners)
                listener.SendCustomEvent(nameof(LockStepEventType.OnTick));
        }

        public uint RegisterInputAction(UdonSharpBehaviour handlerInstance, string handlerEventName)
        {
            Debug.Log($"<dlt> LockStep  RegisterInputAction");
            ArrList.Add(ref inputActionHandlerInstances, ref iahiCount, handlerInstance);
            ArrList.Add(ref inputActionHandlerEventNames, ref iahenCount, handlerEventName);
            return (uint)(iahiCount - 1);
        }
    }
}
