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

        /// <summary>int playerId => PlayerData</summary>
        [System.NonSerialized] public DataDictionary allPlayerData = new DataDictionary();

        public const int PlayerData_PlayerId = 0; // int
        public const int PlayerData_DisplayName = 1; // string
        public const int PlayerData_Description = 2; // string
        public const int PlayerData_Size = 3;

        // These 2 are set by LockStep as parameters.
        private int lockStepPlayerId;

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
            lockStep.Write(playerId);
            lockStep.Write(displayName);
            lockStep.SendInputAction(setDisplayNameIAId);
        }

        [SerializeField] [HideInInspector] private uint setDisplayNameIAId;
        [LockStepInputAction(nameof(setDisplayNameIAId))]
        public void OnSetDisplayNameIA()
        {
            Debug.Log("<dlt> TestGameState  OnSetDisplayNameIA");
            int playerId = lockStep.ReadInt();
            if (!allPlayerData.TryGetValue(playerId, out DataToken playerDataToken))
                return; // Could hve left already.
            object[] playerData = (object[])playerDataToken.Reference;
            playerData[PlayerData_DisplayName] = lockStep.ReadString();

            ui.UpdateUI();
        }

        public void SetDescription(int playerId, string description)
        {
            object[] playerData = (object[])allPlayerData[playerId].Reference;
            if ((string)playerData[TestGameState.PlayerData_Description] == description)
                return;
            SendSetDescriptionIA(playerId, description);
        }

        private void SendSetDescriptionIA(int playerId, string description)
        {
            Debug.Log("<dlt> TestGameState  SendSetDescriptionIA");
            lockStep.Write(playerId);
            lockStep.Write(description);
            lockStep.SendInputAction(setDescriptionNameIAId);
        }

        [SerializeField] [HideInInspector] private uint setDescriptionNameIAId;
        [LockStepInputAction(nameof(setDescriptionNameIAId))]
        public void OnSetDescriptionIA()
        {
            Debug.Log("<dlt> TestGameState  OnSetDescriptionIA");
            int playerId = lockStep.ReadInt();
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
            lockStep.Write(count);
            DataList allPlayerDataValues = allPlayerData.GetValues();
            for (int i = 0; i < count; i++)
            {
                object[] playerData = (object[])allPlayerDataValues[i].Reference;
                if (!isExport)
                    lockStep.Write((int)playerData[PlayerData_PlayerId]);
                lockStep.Write((string)playerData[PlayerData_DisplayName]);
                lockStep.Write((string)playerData[PlayerData_Description]);
            }
        }

        public override string DeserializeGameState(bool isImport)
        {
            Debug.Log("<dlt> TestGameState  DeserializeGameState");
            // TODO: impl import.

            int count = lockStep.ReadInt();
            for (int j = 0; j < count; j++)
            {
                int playerId = lockStep.ReadInt();
                object[] playerData = new object[PlayerData_Size];
                playerData[PlayerData_PlayerId] = playerId;
                playerData[PlayerData_DisplayName] = lockStep.ReadString();
                playerData[PlayerData_Description] = lockStep.ReadString();
                allPlayerData.Add(playerId, new DataToken(playerData));
            }

            ui.UpdateUI();

            return null;
        }
    }
}
