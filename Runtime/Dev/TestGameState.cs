using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("390fe6d092732ae17bddcb5e4175d7df")] // Runtime/Dev/TestGameState.prefab
    public class TestGameState : LockstepGameState
    {
        [SerializeField] private TestGameStateUI ui;
        [SerializeField] private TestGSExportUI exportUI;
        [SerializeField] private TestGSImportUI importUI;

        public override string GameStateInternalName => "jansharp.lockstep-test";
        public override string GameStateDisplayName => "Test Game State";
        public override bool GameStateSupportsImportExport => true;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;
        public override LockstepGameStateOptionsUI ExportUI => exportUI;
        public override LockstepGameStateOptionsUI ImportUI => importUI;

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
            Debug.Log($"[LockstepTest] TestGameState  OnInit");
            lockstep.WriteSmallUInt(1u);
            lockstep.SendEventDelayedTicks(delayedInputActionId, 50);
        }

        [SerializeField][HideInInspector] private uint delayedInputActionId;
        [LockstepInputAction(nameof(delayedInputActionId))]
        public void OnDelayedInputAction()
        {
            uint counter = lockstep.ReadSmallUInt();
            Debug.Log($"[LockstepTest] TestGameState  OnDelayedInputAction - lockstep.CurrentTick: {lockstep.CurrentTick}, lockstep.ReadSmallUInt(): {counter}");
            lockstep.WriteSmallUInt(counter + 1u);
            lockstep.SendEventDelayedTicks(delayedInputActionId, 50);
        }

        [LockstepOnNthTick(75)]
        public void On75thTick()
        {
            Debug.Log($"[LockstepTest] TestGameState  On75thTick - lockstep.CurrentTick: {lockstep.CurrentTick}");
        }

        [LockstepOnNthTick(100, Order = 0)]
        public void On100thTick()
        {
            Debug.Log($"[LockstepTest] TestGameState  On100thTick - lockstep.CurrentTick: {lockstep.CurrentTick}");
        }

        [LockstepOnNthTick(100, Order = 1)]
        public void On100thTickAgain()
        {
            Debug.Log($"[LockstepTest] TestGameState  On100thTickAgain - lockstep.CurrentTick: {lockstep.CurrentTick}");
        }

        [LockstepEvent(LockstepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnClientBeginCatchUp - {lockstep.CatchingUpPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnPreClientJoined)]
        public void OnPreClientJoined()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnPreClientJoined - {lockstep.JoinedPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnClientJoined)]
        public void OnClientJoined()
        {
            uint joinedPlayerId = lockstep.JoinedPlayerId;
            Debug.Log($"[LockstepTest] TestGameState  OnClientJoined - {joinedPlayerId}");

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
            Debug.Log($"[LockstepTest] TestGameState  OnClientCaughtUp - {lockstep.CatchingUpPlayerId}");
        }

        [LockstepEvent(LockstepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnClientLeft - {lockstep.LeftPlayerId}");

            allPlayerData.Remove(lockstep.LeftPlayerId);

            ui.UpdateUI();
        }

        [LockstepEvent(LockstepEventType.OnMasterClientChanged)]
        public void OnMasterClientChanged()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnMasterClientChanged - OldMasterPlayerId: {lockstep.OldMasterPlayerId}, MasterPlayerId: {lockstep.MasterPlayerId}, CurrentTick: {lockstep.CurrentTick}");
        }

        [LockstepEvent(LockstepEventType.OnLockstepTick)]
        public void OnLockstepTick()
        {
            // Debug.Log("[LockstepTest] TestGameState  OnLockstepTick");

            if (prevTick != 0 && lockstep.CurrentTick != (prevTick + 1u))
                Debug.Log($"[LockstepTest] Expected tick {prevTick + 1u}, got {lockstep.CurrentTick}.");
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
            Debug.Log($"[LockstepTest] TestGameState  OnImportStart - ImportingPlayerId: {lockstep.ImportingPlayerId}, ImportingFromWorldName: {lockstep.ImportingFromWorldName}, ImportingFromName: {lockstep.ImportingFromName ?? "<null>"}, ImportingFromDate: {lockstep.ImportingFromDate:yyyy-MM-dd HH:mm}, GameStatesWaitingForImportCount: {lockstep.GameStatesBeingImportedCount}");
        }

        [LockstepEvent(LockstepEventType.OnImportOptionsDeserialized)]
        public void OnImportOptionsDeserialized()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnImportOptionsDeserialized - ImportingPlayerId: {lockstep.ImportingPlayerId}, ImportingFromWorldName: {lockstep.ImportingFromWorldName}, ImportingFromName: {lockstep.ImportingFromName ?? "<null>"}, ImportingFromDate: {lockstep.ImportingFromDate:yyyy-MM-dd HH:mm}, GameStatesWaitingForImportCount: {lockstep.GameStatesBeingImportedCount}");
        }

        [LockstepEvent(LockstepEventType.OnImportedGameState)]
        public void OnImportedGameState()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnImportedGameState - ImportedGameState.GameStateInternalName: {lockstep.ImportedGameState.GameStateInternalName}, ImportErrorMessage: {lockstep.ImportErrorMessage ?? "<null>"}");
        }

        [LockstepEvent(LockstepEventType.OnImportFinished)]
        public void OnImportFinished()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnImportFinished - GameStatesWaitingForImportCount: {lockstep.GameStatesBeingImportedCount}");
        }

        [SerializeField][HideInInspector] private uint singletonTestIAId;
        [LockstepInputAction(nameof(singletonTestIAId))]
        public void OnSingletonTestIA()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnSingletonTestIA - sendTick: {lockstep.ReadSmallUInt()}, SendingPlayerId: {lockstep.SendingPlayerId}");
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
            Debug.Log("[LockstepTest] TestGameState  SendSetDescriptionIA");
            lockstep.WriteSmallUInt(playerId);
            lockstep.WriteString(description);
            lockstep.SendInputAction(setDescriptionNameIAId);
        }

        [SerializeField][HideInInspector] private uint setDescriptionNameIAId;
        [LockstepInputAction(nameof(setDescriptionNameIAId), TrackTiming = true)]
        public void OnSetDescriptionIA()
        {
            Debug.Log($"[LockstepTest] TestGameState  OnSetDescriptionIA - lockstep.SendingTime: {lockstep.SendingTime}, Time.realtimeSinceStartup: {Time.realtimeSinceStartup}, Time.realtimeSinceStartup - lockstep.SendingTime: {Time.realtimeSinceStartup - lockstep.SendingTime}");
            uint playerId = lockstep.ReadSmallUInt();
            if (!allPlayerData.TryGetValue(playerId, out DataToken playerDataToken))
                return; // Could hve left already.
            object[] playerData = (object[])playerDataToken.Reference;
            playerData[PlayerData_Description] = lockstep.ReadString();

            ui.UpdateUI();
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
            Debug.Log("[LockstepTest] TestGameState  SerializeGameState");
            TestGSExportOptions options = (TestGSExportOptions)exportOptions;
            if (isExport)
            {
                lockstep.WriteFlags(options.shouldExport);
                if (!options.shouldExport)
                    return;
            }

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

        public bool HasImportData()
        {
            Debug.Log("[LockstepTest] TestGameState  HasImportData");
            lockstep.ReadFlags(out bool didExport);
            return didExport;
        }

        private int deserializeStage = 0;
        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
            Debug.Log($"[LockstepTest] TestGameState  DeserializeGameState - CurrentTick: {lockstep.CurrentTick}, lastRunnableTick: {(uint)lockstep.GetProgramVariable("lastRunnableTick")}, RealtimeAtTick(CurrentTick): {lockstep.RealtimeAtTick(lockstep.CurrentTick)}, Time.realtimeSinceStartup: {Time.realtimeSinceStartup}");
            if (deserializeStage == 0)
            {
                lockstep.FlagToContinueNextFrame();
                deserializeStage++;
                return null;
            }
            if (deserializeStage == 1)
            {
                lockstep.FlagToContinueNextFrame();
                deserializeStage++;
                return null;
            }
            if (deserializeStage == 2)
            {
                lockstep.FlagToContinueNextFrame();
                deserializeStage++;
                return null;
            }

            TestGSImportOptions options = (TestGSImportOptions)importOptions;
            if (isImport && (!options.shouldImport || !HasImportData()))
                return null;

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

            deserializeStage = 0;
            ui.UpdateUI();

            return "Hi there! Just wanted to say hello!";
        }
    }
}
