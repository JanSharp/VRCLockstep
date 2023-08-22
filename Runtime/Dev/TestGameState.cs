using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGameState : LockStepGameState
    {
        [SerializeField] private TestGameStateUI ui;
        [SerializeField] private LockStep lockStep; // TODO: Automatically set references to LockStep using editor scripting.

        public override string GameStateDisplayName => "Test Game State";

        /// <summary>int playerId => PlayerData</summary>
        public DataDictionary allPlayerData = new DataDictionary();

        public const int PlayerData_PlayerId = 0; // int
        public const int PlayerData_DisplayName = 1; // string
        public const int PlayerData_Description = 2; // string
        public const int PlayerData_Size = 3;

        private int lockStepPlayerId;

        private DataList iaData;
        private uint setDisplayNameIAId;
        private uint setDescriptionNameIAId;

        private void Start()
        {
            setDisplayNameIAId = lockStep.RegisterInputAction(this, nameof(OnSetDisplayNameIA));
            setDescriptionNameIAId = lockStep.RegisterInputAction(this, nameof(OnSetDescriptionIA));

            ui.UpdateUI();
        }

        [LockStepEvent(LockStepEventType.OnInit)]
        public void OnInit()
        {
            Debug.Log($"<dlt> TestGameState  OnInit");
        }

        [LockStepEvent(LockStepEventType.OnClientJoined)]
        public void OnClientJoined()
        {
            Debug.Log($"<dlt> TestGameState  OnClientJoined - {lockStepPlayerId}");

            object[] playerData = new object[PlayerData_Size];
            playerData[PlayerData_PlayerId] = lockStepPlayerId;
            playerData[PlayerData_DisplayName] = "???";
            playerData[PlayerData_Description] = "";

            allPlayerData.Add(lockStepPlayerId, new DataToken(playerData));

            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(lockStepPlayerId);
            if (player != null && player.isLocal)
                SendSetDisplayNameIA(lockStepPlayerId, player.displayName);

            ui.UpdateUI();
        }

        [LockStepEvent(LockStepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientBeginCatchUp - {lockStepPlayerId}");
        }

        [LockStepEvent(LockStepEventType.OnClientCaughtUp)]
        public void OnClientCaughtUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientCaughtUp - {lockStepPlayerId}");
        }

        [LockStepEvent(LockStepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
            Debug.Log($"<dlt> TestGameState  OnClientLeft - {lockStepPlayerId}");

            allPlayerData.Remove(lockStepPlayerId);

            ui.UpdateUI();
        }

        [LockStepEvent(LockStepEventType.OnTick)]
        public void OnTick()
        {
            // Debug.Log("<dlt> TestGameState  OnTick");
        }

        private void SendSetDisplayNameIA(int playerId, string displayName)
        {
            Debug.Log("<dlt> TestGameState  SendSetDisplayNameIA");
            iaData = new DataList();
            iaData.Add((double)playerId);
            iaData.Add(displayName);
            lockStep.SendInputAction(setDisplayNameIAId, iaData);
        }

        public void OnSetDisplayNameIA()
        {
            Debug.Log("<dlt> TestGameState  OnSetDisplayNameIA");
            int playerId = (int)iaData[0].Double;
            if (!allPlayerData.TryGetValue(playerId, out DataToken playerDataToken))
                return; // Could hve left already.
            object[] playerData = (object[])playerDataToken.Reference;
            playerData[PlayerData_DisplayName] = iaData[1].String;

            ui.UpdateUI();
        }

        public void SendSetDescriptionIA(int playerId, string description)
        {
            Debug.Log("<dlt> TestGameState  SendSetDescriptionIA");
            iaData = new DataList();
            iaData.Add((double)playerId);
            iaData.Add(description);
            lockStep.SendInputAction(setDescriptionNameIAId, iaData);
        }

        public void OnSetDescriptionIA()
        {
            Debug.Log("<dlt> TestGameState  OnSetDescriptionIA");
            int playerId = (int)iaData[0].Double;
            if (!allPlayerData.TryGetValue(playerId, out DataToken playerDataToken))
                return; // Could hve left already.
            object[] playerData = (object[])playerDataToken.Reference;
            playerData[PlayerData_Description] = iaData[1].String;

            ui.UpdateUI();
        }

        public override DataList SerializeGameState()
        {
            Debug.Log("<dlt> TestGameState  SerializeGameState");
            DataList stream = new DataList();

            int count = allPlayerData.Count;
            stream.Add((double)count);
            DataList allPlayerDataValues = allPlayerData.GetValues();
            for (int i = 0; i < count; i++)
            {
                object[] playerData = (object[])allPlayerDataValues[i].Reference;
                stream.Add((double)(int)playerData[PlayerData_PlayerId]);
                stream.Add((string)playerData[PlayerData_DisplayName]);
                stream.Add((string)playerData[PlayerData_Description]);
            }

            return stream;
        }

        public override string DeserializeGameState(DataList stream)
        {
            Debug.Log("<dlt> TestGameState  DeserializeGameState");
            int i = 0;

            int count = (int)stream[i++].Double;
            for (int j = 0; j < count; j++)
            {
                int playerId = (int)stream[i++].Double;
                object[] playerData = new object[PlayerData_Size];
                playerData[PlayerData_PlayerId] = playerId;
                playerData[PlayerData_DisplayName] = stream[i++].String;
                playerData[PlayerData_Description] = stream[i++].String;
                allPlayerData.Add(playerId, new DataToken(playerData));
            }

            ui.UpdateUI();

            return null;
        }
    }
}
