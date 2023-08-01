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
        // private uint resetTickRateTick = uint.MaxValue; // At the end of this tick it gets reset to TickRate.
        // private float currentTickRate = TickRate;
        private bool isTickPaused = true;
        private bool isMaster = false;
        private bool ignoreLocalInputActions = true;
        private bool stillAllowLocalClientJoinedIA = false;
        private bool ignoreIncomingInputActions = true;
        private bool isWaitingForLateJoinerSync = false;
        private bool sendLateJoinerDataAtEndOfTick = false;
        private bool isCatchingUp = false;
        private bool isSinglePlayer = false;

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

        private int initiateLateJoinerSyncSentCount = 0;
        private int processLeftPlayersSentCount = 0;

        // Used by the debug UI.
        private float lastUpdateTime;

        private void Start()
        {
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
            float startTime = Time.realtimeSinceStartup;

            if (isTickPaused)
            {
                lastUpdateTime = Time.realtimeSinceStartup - startTime;
                return;
            }

            if (isCatchingUp)
            {
                CatchUp();
                lastUpdateTime = Time.realtimeSinceStartup - startTime;
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

            lastUpdateTime = Time.realtimeSinceStartup - startTime;
        }

        private void CatchUp()
        {
            float startTime = Time.realtimeSinceStartup;
            while (true)
            {
                if (currentTick == waitTick || !TryRunNextTick())
                    break;
                float realtimePassed = Time.realtimeSinceStartup - startTime;
                // If secondsPassed == 0f then this platform is reporting the same realtimeSinceStartup during a frame,
                // in which case there's no way to know how long we've been processing ticks already, so to be on the save
                // side we only process 1 tick per frame.
                if (realtimePassed == 0f || realtimePassed >= 0.01f) // 10ms.
                    break;
            }

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
            foreach (uint uniqueId in uniqueIds)
                RunInputActionForUniqueId(uniqueId);
        }

        private void RunInputActionForUniqueId(uint uniqueId)
        {
            inputActionsByUniqueId.Remove(uniqueId, out DataToken inputActionDataToken);
            DataList inputActionData = inputActionDataToken.DataList;
            int lastIndex = inputActionData.Count - 1;
            uint inputActionId = inputActionData[lastIndex].UInt;
            inputActionData.RemoveAt(lastIndex);
            RunInputAction(inputActionId, inputActionData);
        }

        private void RunInputAction(uint inputActionId, DataList inputActionData)
        {
            UdonSharpBehaviour inst = inputActionHandlerInstances[inputActionId];
            inst.SetProgramVariable(InputActionDataField, inputActionData);
            inst.SendCustomEvent(inputActionHandlerEventNames[inputActionId]);
        }

        public void SendInputAction(uint inputActionId, DataList inputActionData)
        {
            if (ignoreLocalInputActions && !(stillAllowLocalClientJoinedIA && inputActionId == clientJoinedIAId))
                return;

            if (isSinglePlayer)
            {
                TryToInstantlyRunInputActionOnMaster(inputActionId, 0u, inputActionData);
                return;
            }

            uint uniqueId = inputActionSyncForLocalPlayer.SendInputAction(inputActionId, inputActionData);

            if (stillAllowLocalClientJoinedIA)
            {
                if (ignoreLocalInputActions)
                    return; // Do not save client joined IA because it will never be executed locally.
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
            if (!player.isLocal)
                return;

            inputActionSyncForLocalPlayer = inputActionSync;
            SendCustomEventDelayedSeconds(nameof(OnLocalInputActionSyncPlayerAssignedDelayed), 2f);
        }

        public void OnLocalInputActionSyncPlayerAssignedDelayed()
        {
            if (localPlayer.isMaster)
            {
                BecomeInitialMaster();
                return;
            }

            ignoreLocalInputActions = true;
            stillAllowLocalClientJoinedIA = true;
            ignoreIncomingInputActions = false;
            isWaitingForLateJoinerSync = true;
            SendClientJoinedIA();
        }

        public void OnInputActionSyncPlayerUnassigned(VRCPlayerApi player)
        {
            int playerId = player.playerId;

            if (isMaster)
            {
                ArrList.Add(ref leftClients, ref leftClientsCount, playerId);
                processLeftPlayersSentCount++;
                SendCustomEventDelayedSeconds(nameof(ProcessLeftPlayers), 1f);
                return;
            }

            // Already removed... was it the master that got removed?
            if (!clientStates.TryGetValue(playerId, out DataToken clientStateToken))
            {
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

        private void SetMasterLeftFlag()
        {
            if (currentlyNoMaster)
                return;
            currentlyNoMaster = true;
            SendCustomEventDelayedFrames(nameof(CheckMasterChange), 1);
        }

        private void BecomeInitialMaster()
        {
            isMaster = true;
            currentlyNoMaster = false;
            ignoreLocalInputActions = false;
            ignoreIncomingInputActions = false;
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
            // TODO: Raise OnInit();
            // TODO: Raise OnClientJoined(int playerId);
            isTickPaused = false;
            tickStartTime = Time.time;
        }

        private bool IsAnyClientWaitingForLateJoinerSync()
        {
            DataList allStates = clientStates.GetValues();
            for (int i = 0; i < allStates.Count; i++)
                if ((ClientState)allStates[i].Byte == ClientState.WaitingForLateJoinerSync)
                    return true;
            return false;
        }

        public void CheckMasterChange()
        {
            if (isMaster || !currentlyNoMaster || !Networking.IsMaster)
                return;

            currentlyNoMaster = false;
            isMaster = true;
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            ignoreIncomingInputActions = false;
            isWaitingForLateJoinerSync = false;
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
            inputActionSyncForLocalPlayer.DequeueEverything();
        }

        public void ProcessLeftPlayers()
        {
            if ((--processLeftPlayersSentCount) != 0)
                return;

            CheckSingePlayerModeChange();

            for (int i = 0; i < leftClientsCount; i++)
                SendClientLeftIA(leftClients[i]);

            ArrList.Clear(ref leftClients, ref leftClientsCount);
        }

        private void CheckSingePlayerModeChange()
        {
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
            isSinglePlayer = true;
            lateJoinerInputActionSync.DequeueEverything();
            InstantlyRunInputActionsWaitingToBeSent();
            tickSync.ClearInputActionsToRun();
        }

        private void ExitSinglePlayerMode()
        {
            isSinglePlayer = false;
        }

        private void SendMasterChangedIA()
        {
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            SendInputAction(masterChangedIAId, iaData);
        }

        public void OnMasterChangedIA()
        {
            int playerId = (int)iaData[0].Double;
            clientStates[playerId] = (byte)ClientState.Master;
            currentlyNoMaster = false;
        }

        private void SendClientJoinedIA()
        {
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            SendInputAction(clientJoinedIAId, iaData);
        }

        public void OnClientJoinedIA()
        {
            int playerId = (int)iaData[0].Double;
            clientStates.Add(playerId, (byte)ClientState.WaitingForLateJoinerSync);

            if (isMaster)
            {
                CheckSingePlayerModeChange();
                initiateLateJoinerSyncSentCount++;
                SendCustomEventDelayedSeconds(nameof(FlagForLateJoinerSync), 5f);
            }
        }

        public void FlagForLateJoinerSync()
        {
            if ((--initiateLateJoinerSyncSentCount) != 0)
                return;

            if (IsAnyClientWaitingForLateJoinerSync())
                sendLateJoinerDataAtEndOfTick = true;
        }

        private void SendLateJoinerData()
        {
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

            // TODO: send custom game states

            iaData = new DataList();
            iaData.Add(currentTick);
            lateJoinerInputActionSync.SendInputAction(LJCurrentTickIAId, iaData);
        }

        private void OnLJClientStatesIA()
        {
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
            // TODO: impl
        }

        private void OnLJCurrentTickIA()
        {
            currentTick = (uint)iaData[0].Double;

            lateJoinerInputActionSync.gameObject.SetActive(false);
            isWaitingForLateJoinerSync = false;
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            SendClientGotLateJoinerDataIA(); // Must be before OnClientBeginCatchUp, because that can also send input actions.
            // TODO: Raise OnClientBeginCatchUp(int playerId);
            isTickPaused = false;
            isCatchingUp = true;
        }

        private void SendClientGotLateJoinerDataIA()
        {
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            SendInputAction(clientGotLateJoinerDataIAId, iaData);
        }

        private void CheckIfLateJoinerSyncShouldStop()
        {
            if (isMaster && !IsAnyClientWaitingForLateJoinerSync())
            {
                sendLateJoinerDataAtEndOfTick = false;
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            }
        }

        public void OnClientGotLateJoinerDataIA()
        {
            int playerId = (int)iaData[0].Double;
            clientStates[playerId] = (byte)ClientState.Normal;
            CheckIfLateJoinerSyncShouldStop();
            // TODO: Raise OnClientJoined(int playerId);
        }

        private void SendClientLeftIA(int playerId)
        {
            iaData = new DataList();
            iaData.Add((double)playerId);
            SendInputAction(clientLeftIAId, iaData);
        }

        public void OnClientLeftIA()
        {
            int playerId = (int)iaData[0].Double;
            clientStates.Remove(playerId);

            int index = ArrList.IndexOf(ref leftClients, ref leftClientsCount, playerId);
            if (index != -1)
                ArrList.RemoveAt(ref leftClients, ref leftClientsCount, index);

            CheckIfLateJoinerSyncShouldStop();
            // TODO: Raise OnClientLeft(int playerId);
        }

        private void SendClientCaughtUpIA()
        {
            iaData = new DataList();
            iaData.Add((double)localPlayer.playerId);
            SendInputAction(clientCaughtUpIAId, iaData);
        }

        public void OnClientCaughtUpIA()
        {
            // TODO: Raise OnClientCaughtUp(int playerId);
        }

        public void ReceivedInputAction(bool isLateJoinerSync, uint inputActionId, uint uniqueId, DataList inputActionData)
        {
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

        public void AssociateInputActionWithTick(uint tickToRunIn, uint uniqueId, bool allowOnMaster = false)
        {
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

        public uint RegisterInputAction(UdonSharpBehaviour handlerInstance, string handlerEventName)
        {
            ArrList.Add(ref inputActionHandlerInstances, ref iahiCount, handlerInstance);
            ArrList.Add(ref inputActionHandlerEventNames, ref iahenCount, handlerEventName);
            return (uint)(iahiCount - 1);
        }
    }
}
