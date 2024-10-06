using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGameState : LockstepGameState
    {
        [SerializeField] private TestGameStateUI ui;
        [SerializeField] [HideInInspector] private LockstepAPI lockstep; // Set by Lockstep's OnBuild handler.

        public override string GameStateInternalName => "jansharp.lockstep-test";
        public override string GameStateDisplayName => "Test Game State";
        public override bool GameStateSupportsImportExport => true;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;

        /// <summary>uint playerId => PlayerData</summary>
        [System.NonSerialized] public DataDictionary allPlayerData = new DataDictionary();
        private uint prevTick = 0u;

        public const int PlayerData_PlayerId = 0; // uint
        public const int PlayerData_DisplayName = 1; // string
        public const int PlayerData_Description = 2; // string
        public const int PlayerData_Size = 3;

        [LockstepEvent(LockstepEventType.OnInit)]
        public void OnInit()
        {
            Debug.Log($"<dlt> TestGameState  OnInit");
        }

        [LockstepEvent(LockstepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientBeginCatchUp - {lockstep.CatchingUpPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnPreClientJoined)]
        public void OnPreClientJoined()
        {
            Debug.Log($"<dlt> TestGameState  OnPreClientJoined - {lockstep.JoinedPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnClientJoined)]
        public void OnClientJoined()
        {
            uint joinedPlayerId = lockstep.JoinedPlayerId;
            Debug.Log($"<dlt> TestGameState  OnClientJoined - {joinedPlayerId}");

            object[] playerData = new object[PlayerData_Size];
            playerData[PlayerData_PlayerId] = joinedPlayerId;
            playerData[PlayerData_DisplayName] = lockstep.GetDisplayName(joinedPlayerId);
            playerData[PlayerData_Description] = "";

            allPlayerData.Add(joinedPlayerId, new DataToken(playerData));
            ui.UpdateUI();
        }

        [LockstepEvent(LockstepEventType.OnClientCaughtUp)]
        public void OnClientCaughtUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientCaughtUp - {lockstep.CatchingUpPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
            Debug.Log($"<dlt> TestGameState  OnClientLeft - {lockstep.LeftPlayerId}");

            allPlayerData.Remove(lockstep.LeftPlayerId);

            ui.UpdateUI();
        }

        [LockstepEvent(LockstepEventType.OnMasterClientChanged)]
        public void OnMasterClientChanged()
        {
            Debug.Log($"<dlt> TestGameState  OnMasterClientChanged - OldMasterPlayerId: {lockstep.OldMasterPlayerId}, MasterPlayerId: {lockstep.MasterPlayerId}, CurrentTick: {lockstep.CurrentTick}");
        }

        [LockstepEvent(LockstepEventType.OnLockstepTick)]
        public void OnLockstepTick()
        {
            // Debug.Log("<dlt> TestGameState  OnLockstepTick");

            if (prevTick != 0 && lockstep.CurrentTick != (prevTick + 1u))
                Debug.Log($"<dlt> Expected tick {prevTick + 1u}, got {lockstep.CurrentTick}.");
            prevTick = lockstep.CurrentTick;

            // Reactivate the code below for SendSingletonInputAction testing.
            // if (((int)lockstep.currentTick % 50) == 0)
            // {
            //     lockstep.WriteSmallUInt(lockstep.currentTick);
            //     lockstep.SendSingletonInputAction(singletonTestIAId);
            // }
        }

        [LockstepEvent(LockstepEventType.OnImportStart)]
        public void OnImportStart()
        {
            Debug.Log($"<dlt> TestGameState  OnImportStart - ImportingPlayerId: {lockstep.ImportingPlayerId}, ImportingFromWorldName: {lockstep.ImportingFromWorldName}, ImportingFromName: {lockstep.ImportingFromName ?? "<null>"}, ImportingFromDate: {lockstep.ImportingFromDate:yyyy-MM-dd HH:mm}, GameStatesWaitingForImportCount: {lockstep.GameStatesWaitingForImportCount}");
        }

        [LockstepEvent(LockstepEventType.OnImportedGameState)]
        public void OnImportedGameState()
        {
            Debug.Log($"<dlt> TestGameState  OnImportedGameState - ImportedGameState.GameStateInternalName: {lockstep.ImportedGameState.GameStateInternalName}, ImportErrorMessage: {lockstep.ImportErrorMessage ?? "<null>"}");
        }

        [LockstepEvent(LockstepEventType.OnImportFinished)]
        public void OnImportFinished()
        {
            Debug.Log($"<dlt> TestGameState  OnImportFinished - GameStatesWaitingForImportCount: {lockstep.GameStatesWaitingForImportCount}");
        }

        [SerializeField] [HideInInspector] private uint singletonTestIAId;
        [LockstepInputAction(nameof(singletonTestIAId))]
        public void OnSingletonTestIA()
        {
            Debug.Log($"<dlt> TestGameState  OnSingletonTestIA - sendTick: {lockstep.ReadSmallUInt()}, SendingPlayerId: {lockstep.SendingPlayerId}");
        }

        public void SetDescription(uint playerId, string description)
        {
            object[] playerData = (object[])allPlayerData[playerId].Reference;
            if ((string)playerData[TestGameState.PlayerData_Description] == description)
                return;
            SendSetDescriptionIA(playerId, description);
        }

        private void SendSetDescriptionIA(uint playerId, string description)
        {
            Debug.Log("<dlt> TestGameState  SendSetDescriptionIA");
            lockstep.WriteSmallUInt(playerId);
            lockstep.WriteString(description);
            lockstep.SendInputAction(setDescriptionNameIAId);
        }

        [SerializeField] [HideInInspector] private uint setDescriptionNameIAId;
        [LockstepInputAction(nameof(setDescriptionNameIAId), TrackTiming = true)]
        public void OnSetDescriptionIA()
        {
            Debug.Log($"<dlt> TestGameState  OnSetDescriptionIA - lockstep.SendingTime: {lockstep.SendingTime}, Time.realtimeSinceStartup: {Time.realtimeSinceStartup}, Time.realtimeSinceStartup - lockstep.SendingTime: {Time.realtimeSinceStartup - lockstep.SendingTime}");
            uint playerId = lockstep.ReadSmallUInt();
            if (!allPlayerData.TryGetValue(playerId, out DataToken playerDataToken))
                return; // Could hve left already.
            object[] playerData = (object[])playerDataToken.Reference;
            playerData[PlayerData_Description] = lockstep.ReadString();

            ui.UpdateUI();
        }

        public override void SerializeGameState(bool isExport)
        {
            Debug.Log("<dlt> TestGameState  SerializeGameState");

            int count = allPlayerData.Count;
            lockstep.WriteSmallUInt((uint)count);
            DataList allPlayerDataValues = allPlayerData.GetValues();
            for (int i = 0; i < count; i++)
            {
                object[] playerData = (object[])allPlayerDataValues[i].Reference;
                if (!isExport)
                    lockstep.WriteSmallUInt((uint)playerData[PlayerData_PlayerId]);
                lockstep.WriteString((string)playerData[PlayerData_Description]);
            }
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion)
        {
            Debug.Log("<dlt> TestGameState  DeserializeGameState");

            int count = (int)lockstep.ReadSmallUInt();
            for (int j = 0; j < count; j++)
            {
                uint playerId = isImport ? (uint)Random.Range(1, 10000) : lockstep.ReadSmallUInt();
                object[] playerData = new object[PlayerData_Size];
                playerData[PlayerData_PlayerId] = playerId;
                playerData[PlayerData_DisplayName] = lockstep.GetDisplayName(playerId);
                playerData[PlayerData_Description] = lockstep.ReadString();
                allPlayerData.Add(playerId, new DataToken(playerData));
            }

            ui.UpdateUI();

            return "Hi there! Just wanted to say hello!";
        }
    }
}
