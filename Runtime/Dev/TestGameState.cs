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
        [SerializeField] [HideInInspector] private LockStep lockStep; // Set by LockStep's OnBuild handler.

        public override string GameStateInternalName => "jansharp.lock-step-test";
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

        // These 2 are set by LockStep as parameters.
        private uint lockStepPlayerId;

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
            playerData[PlayerData_DisplayName] = lockStep.GetDisplayName(lockStepPlayerId);
            playerData[PlayerData_Description] = "";

            allPlayerData.Add(lockStepPlayerId, new DataToken(playerData));
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
            lockStep.WriteSmall(playerId);
            lockStep.Write(description);
            lockStep.SendInputAction(setDescriptionNameIAId);
        }

        [SerializeField] [HideInInspector] private uint setDescriptionNameIAId;
        [LockStepInputAction(nameof(setDescriptionNameIAId))]
        public void OnSetDescriptionIA()
        {
            Debug.Log("<dlt> TestGameState  OnSetDescriptionIA");
            uint playerId = lockStep.ReadSmallUInt();
            if (!allPlayerData.TryGetValue(playerId, out DataToken playerDataToken))
                return; // Could hve left already.
            object[] playerData = (object[])playerDataToken.Reference;
            playerData[PlayerData_Description] = lockStep.ReadString();

            ui.UpdateUI();
        }

        public override void SerializeGameState(bool isExport)
        {
            Debug.Log("<dlt> TestGameState  SerializeGameState");

            int count = allPlayerData.Count;
            lockStep.WriteSmall((uint)count);
            DataList allPlayerDataValues = allPlayerData.GetValues();
            for (int i = 0; i < count; i++)
            {
                object[] playerData = (object[])allPlayerDataValues[i].Reference;
                if (!isExport)
                    lockStep.WriteSmall((uint)playerData[PlayerData_PlayerId]);
                lockStep.Write((string)playerData[PlayerData_Description]);
            }
        }

        public override string DeserializeGameState(bool isImport)
        {
            Debug.Log("<dlt> TestGameState  DeserializeGameState");

            int count = (int)lockStep.ReadSmallUInt();
            for (int j = 0; j < count; j++)
            {
                uint playerId = isImport ? (uint)Random.Range(1, 10000) : lockStep.ReadSmallUInt();
                object[] playerData = new object[PlayerData_Size];
                playerData[PlayerData_PlayerId] = playerId;
                playerData[PlayerData_DisplayName] = lockStep.GetDisplayName(playerId);
                playerData[PlayerData_Description] = lockStep.ReadString();
                allPlayerData.Add(playerId, new DataToken(playerData));
            }

            ui.UpdateUI();

            return null;
        }
    }
}
