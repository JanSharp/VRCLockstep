using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockStep : UdonSharpBehaviour
    {
        private const float TickRate = 15f;
        public const string InputActionDataField = "iaData";

        public LockStepTickSync tickSync;
        [System.NonSerialized] public uint currentTick;
        [System.NonSerialized] public uint waitTick;

        private VRCPlayerApi localPlayer;
        private InputActionSync inputActionSyncForLocalPlayer;
        private float startTime;
        private bool isMaster;
        private bool isInitialized;

        [System.NonSerialized] public DataList iaData;
        private uint clientJoinedIAId;
        private uint lateJoinerDataReceivedIAId;

        ///cSpell:ignore xxpppppp

        public const int PlayerIdKeyShift = 16;
        // uint => DataList
        // uint: unique id - pppppppp pppppppp iiiiiiii iiiiiiii (p = player id, i = input action index)
        // DataList: input action data, plus input action id appended
        private DataDictionary pendingActions;

        // uint => uint[]
        // uint: tick to run in
        // uint[]: unique id (same as for pendingActions)
        private DataDictionary queuedInputActions;

        ///cSpell:ignore iahi, iahen
        private UdonSharpBehaviour[] inputActionHandlerInstances = new UdonSharpBehaviour[ArrList.MinCapacity];
        private int iahiCount = 0;
        private string[] inputActionHandlerEventNames = new string[ArrList.MinCapacity];
        private int iahenCount = 0;

        public VRCPlayerApi[] players = new VRCPlayerApi[ArrList.MinCapacity];
        public int playersCount = 0;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
        }

        private void Update()
        {
            float timePassed = Time.time - startTime;
            uint runUntilTick = System.Math.Min(waitTick, (uint)(timePassed * TickRate));
            for (uint tick = currentTick + 1; tick <= runUntilTick; tick++)
            {
                currentTick = tick;
                RunTick();
            }

            if (isMaster)
            {
                // Synced tick is always 1 behind, that way new input actions can be run in
                // the current tick on the master without having to queue them for the next tick.
                tickSync.syncedTick = currentTick - 1u;
            }

            clientJoinedIAId = RegisterInputAction(this, nameof(OnClientJoinedIA));
            lateJoinerDataReceivedIAId = RegisterInputAction(this, nameof(OnLateJoinerDataReceivedIA));
        }

        private void RunTick()
        {
            Debug.Log($"<dlt> Running tick {currentTick}");
            if (queuedInputActions.Remove(currentTick, out DataToken uniqueIdsToken))
                foreach (uint uniqueId in (uint[])uniqueIdsToken.Reference)
                    RunInputActionForUniqueId(uniqueId);
        }

        private void RunInputActionForUniqueId(uint uniqueId)
        {
            pendingActions.Remove(uniqueId, out DataToken inputActionDataToken);
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
            uint uniqueId = inputActionSyncForLocalPlayer.SendInputAction(inputActionId, inputActionData);
            // Modify the inputActionData after sending it, otherwise bad data would be sent.
            inputActionData.Add(inputActionId);
            pendingActions.Add(uniqueId, inputActionData);
        }

        public void InputActionSent(uint uniqueId)
        {
            if (isMaster)
            {
                RunInputActionForUniqueId(uniqueId);
                tickSync.AddInputActionToRun(currentTick, uniqueId);
            }
        }

        // public override void OnPlayerJoined(VRCPlayerApi player)
        // {
        //     // CheckMasterSwitch();
        //     // if (isMaster && !player.isLocal)
        //     //     SendLateJoinderData();
        // }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            SendCustomEventDelayedFrames(nameof(CheckMasterSwitch), 1);
        }

        public void OnInputActionSyncPlayerAssigned(VRCPlayerApi player, InputActionSync inputActionSync)
        {
            if (player.isLocal)
            {
                inputActionSyncForLocalPlayer = inputActionSync;
                CheckMasterSwitch();

                if (!isMaster)
                {
                    // Inform everyone that this client officially joined the instance.
                    SendClientJoinedIA();
                }
            }

            if (isMaster && !player.isLocal)
                SendLateJoinerDataReceivedIA();
        }

        private void CheckMasterSwitch()
        {
            if (!isMaster && Networking.IsMaster)
                BecomeMaster();
        }

        private void BecomeMaster()
        {
            isMaster = true;
            waitTick = uint.MaxValue;
            Networking.SetOwner(localPlayer, tickSync.gameObject);
            if (!isInitialized)
            {
                // Becoming master when it is not initialized yet means this is a fresh instance.
                OnInit();
                isInitialized = true;
                tickSync.RequestSerialization();
            }
        }

        private void OnInit()
        {
            // NOTE: call all registered OnInit handlers
        }

        private void SendLateJoinerDataReceivedIA()
        {
            iaData = new DataList();
            // TODO: Impl.
            SendInputAction(lateJoinerDataReceivedIAId, iaData);
        }

        public void OnLateJoinerDataReceivedIA()
        {
            // TODO: Impl.
        }

        private void SendClientJoinedIA()
        {
            iaData = new DataList();
            iaData.Add(localPlayer.playerId);
            SendInputAction(clientJoinedIAId, iaData);
        }

        public void OnClientJoinedIA()
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(iaData[0].Int);
            if (player == null)
            {
                Debug.Log($"<dlt> Received ClientJoinedIA, but couldn't get the VRCPlayerApi for "
                    + $"player id {iaData[0].Int}. Assuming the player already left again, ignoring.");
                return;
            }

            if (isMaster)
            {
                // TODO: Impl.

                // TODO: Raise on player joined event.
            }
            else
            {
                // TODO: Save as pending in case the master leaves in an inopportune moment.
            }
        }

        public void ReceivedInputAction(bool isLateJoinerSync, uint inputActionId, uint uniqueId, DataList inputActionData)
        {
            if (isMaster)
            {
                RunInputAction(inputActionId, inputActionData);
                tickSync.AddInputActionToRun(currentTick, uniqueId);
            }
            else
            {
                inputActionData.Add(inputActionId);
                pendingActions.Add(uniqueId, inputActionData);
            }
        }

        public void EnqueueInputActionAtTick(uint tickToRunIn, uint uniqueId)
        {
            if (isMaster)
            {
                Debug.LogWarning("<dlt> As the master I should not be receiving "
                    + "data about running an input action at a tick...");
            }

            // This client recently joined and is still waiting on late joiner sync data, ignore any input actions
            if (!isInitialized)
                return;

            // Mark the input action to run at the given tick.
            DataToken tickToRunInToken = new DataToken(tickToRunIn);
            if (queuedInputActions.TryGetValue(tickToRunInToken, out DataToken uniqueIdsToken))
            {
                uint[] uniqueIds = (uint[])uniqueIdsToken.Reference;
                uint[] newUniqueIds = new uint[uniqueIds.Length + 1];
                uniqueIds.CopyTo(newUniqueIds, 0);
                queuedInputActions.SetValue(tickToRunInToken, new DataToken(newUniqueIds));
                return;
            }
            queuedInputActions.Add(tickToRunInToken, new DataToken(new uint[] { uniqueId }));
        }

        public uint RegisterInputAction(UdonSharpBehaviour handlerInstance, string handlerEventName)
        {
            ArrList.Add(ref inputActionHandlerInstances, ref iahiCount, handlerInstance);
            ArrList.Add(ref inputActionHandlerEventNames, ref iahenCount, handlerEventName);
            return (uint)(iahiCount - 1);
        }
    }
}
