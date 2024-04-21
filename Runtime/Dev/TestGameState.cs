﻿using UdonSharp;
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

        public const int PlayerData_PlayerId = 0; // uint
        public const int PlayerData_DisplayName = 1; // string
        public const int PlayerData_Description = 2; // string
        public const int PlayerData_Size = 3;

        // These 2 are set by Lockstep as parameters.
        private uint lockstepPlayerId;

        [LockstepEvent(LockstepEventType.OnInit)]
        public void OnInit()
        {
            Debug.Log($"<dlt> TestGameState  OnInit");
        }

        [LockstepEvent(LockstepEventType.OnClientJoined)]
        public void OnClientJoined()
        {
            Debug.Log($"<dlt> TestGameState  OnClientJoined - {lockstepPlayerId}");

            object[] playerData = new object[PlayerData_Size];
            playerData[PlayerData_PlayerId] = lockstepPlayerId;
            playerData[PlayerData_DisplayName] = lockstep.GetDisplayName(lockstepPlayerId);
            playerData[PlayerData_Description] = "";

            allPlayerData.Add(lockstepPlayerId, new DataToken(playerData));
            ui.UpdateUI();
        }

        [LockstepEvent(LockstepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientBeginCatchUp - {lockstepPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnClientCaughtUp)]
        public void OnClientCaughtUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientCaughtUp - {lockstepPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
            Debug.Log($"<dlt> TestGameState  OnClientLeft - {lockstepPlayerId}");

            allPlayerData.Remove(lockstepPlayerId);

            ui.UpdateUI();
        }

        [LockstepEvent(LockstepEventType.OnTick)]
        public void OnTick()
        {
            // Debug.Log("<dlt> TestGameState  OnTick");

            // Reactivate the code below for SendSingletonInputAction testing.
            // if (((int)lockstep.currentTick % 50) == 0)
            // {
            //     lockstep.WriteSmall(lockstep.currentTick);
            //     lockstep.SendSingletonInputAction(singletonTestIAId);
            // }
        }

        [LockstepEvent(LockstepEventType.OnImportStart)]
        public void OnImportStart()
        {
            Debug.Log($"<dlt> TestGameState  OnImportStart - ImportingPlayerId: {lockstep.ImportingPlayerId}, ImportingFromName: {lockstep.ImportingFromName ?? "<null>"}, ImportingFromDate: {lockstep.ImportingFromDate:yyyy-MM-dd HH:mm}, GameStatesWaitingForImportCount: {lockstep.GameStatesWaitingForImportCount}");
        }

        [LockstepEvent(LockstepEventType.OnImportedGameState)]
        public void OnImportedGameState()
        {
            Debug.Log($"<dlt> TestGameState  OnImportedGameState - ImportedGameState.GameStateInternalName: {lockstep.ImportedGameState.GameStateInternalName}");
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
            lockstep.WriteSmall(playerId);
            lockstep.Write(description);
            lockstep.SendInputAction(setDescriptionNameIAId);
        }

        [SerializeField] [HideInInspector] private uint setDescriptionNameIAId;
        [LockstepInputAction(nameof(setDescriptionNameIAId))]
        public void OnSetDescriptionIA()
        {
            Debug.Log("<dlt> TestGameState  OnSetDescriptionIA");
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
            lockstep.WriteSmall((uint)count);
            DataList allPlayerDataValues = allPlayerData.GetValues();
            for (int i = 0; i < count; i++)
            {
                object[] playerData = (object[])allPlayerDataValues[i].Reference;
                if (!isExport)
                    lockstep.WriteSmall((uint)playerData[PlayerData_PlayerId]);
                lockstep.Write((string)playerData[PlayerData_Description]);
            }
        }

        public override string DeserializeGameState(bool isImport)
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

            return null;
        }
    }
}
