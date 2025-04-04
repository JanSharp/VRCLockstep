using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
    public enum LockstepMasterPreferenceEventType
    {
        /// <summary>
        /// <para>Use <see cref="LockstepMasterPreference.ChangedPlayerId"/> to get the id of the client who's
        /// master preference changed.</para>
        /// <para>Raised when the <see cref="LockstepMasterPreference.GetPreference(uint)"/> for a client
        /// changed.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnMasterPreferenceChanged,
        /// <summary>
        /// <para>Use <see cref="LockstepMasterPreference.ChangedPlayerId"/> to get the id of the client who's
        /// master preference changed.</para>
        /// <para>Raised when the
        /// <see cref="LockstepMasterPreference.GetLatencyHiddenPreference(uint)(uint)"/> for a client
        /// changed.</para>
        /// <para>Non game state safe.</para>
        /// </summary>
        OnLatencyHiddenMasterPreferenceChanged,
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class LockstepMasterPreferenceEventAttribute : CustomRaisedEventBaseAttribute
    {
        public LockstepMasterPreferenceEventAttribute(LockstepMasterPreferenceEventType eventType)
            : base((int)eventType)
        { }
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [CustomRaisedEventsDispatcher(typeof(LockstepMasterPreferenceEventAttribute), typeof(LockstepMasterPreferenceEventType))]
    [SingletonScript("2fc964357f5582524aa3501a3d87cc61")] // Runtime/Prefabs/LockstepMasterPreference.prefab
    public class LockstepMasterPreference : LockstepGameState
    {
        public override string GameStateInternalName => "jansharp.lockstep-master-preference";
        public override string GameStateDisplayName => "Lockstep Master Preference";
        public override bool GameStateSupportsImportExport => true;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;
        public override LockstepGameStateOptionsUI ExportUI => null;
        public override LockstepGameStateOptionsUI ImportUI => null;

        [HideInInspector] [SerializeField] private UdonSharpBehaviour[] onMasterPreferenceChangedListeners;
        [HideInInspector] [SerializeField] private UdonSharpBehaviour[] onLatencyHiddenMasterPreferenceChangedListeners;

        private uint localPlayerId;

        private uint playerIdWithHighestPreference;
        private int currentHighestPreference;

        private uint[] playerIds = new uint[ArrList.MinCapacity];
        private int playerIdsCount = 0;
        private int[] preferences = new int[ArrList.MinCapacity];
        private int preferencesCount = 0;
        private int[] latencyPreferences = new int[ArrList.MinCapacity];
        private int latencyPreferencesCount = 0;
        private DataDictionary[] latencyHiddenIds = new DataDictionary[ArrList.MinCapacity];
        private int latencyHiddenIdsCount = 0;

        private DataDictionary persistentPreferences = new DataDictionary();

        private void Start()
        {
            localPlayerId = (uint)Networking.LocalPlayer.playerId;
        }

        [LockstepEvent(LockstepEventType.OnInit, Order = -100)]
        public void OnInit()
        {
            AddClient(localPlayerId);
            playerIdWithHighestPreference = localPlayerId;
            currentHighestPreference = preferences[0];
        }

        [LockstepEvent(LockstepEventType.OnPreClientJoined, Order = -100)]
        public void OnPreClientJoined()
        {
            int preference = AddClient(lockstep.JoinedPlayerId);
            CheckIfIsNewHighestPreference(lockstep.JoinedPlayerId, preference);
        }

        [LockstepEvent(LockstepEventType.OnClientCaughtUp)]
        public void OnClientCaughtUp()
        {
            if (!lockstep.IsMaster || lockstep.CatchingUpPlayerId != playerIdWithHighestPreference)
                return;
            PotentiallySendMasterChangeRequestIA();
        }

        [LockstepEvent(LockstepEventType.OnMasterClientChanged)]
        public void OnMasterClientChanged()
        {
            if (!lockstep.IsMaster)
                return;
            if (preferences[BinarySearch(lockstep.MasterPlayerId)] == currentHighestPreference)
            {
                playerIdWithHighestPreference = lockstep.MasterPlayerId;
                return;
            }
            PotentiallySendMasterChangeRequestIA();
        }

        private void CheckIfIsNewHighestPreference(uint playerId, int preference)
        {
            if (preference <= currentHighestPreference)
                return;
            SetNewHighestPreference(playerId, preference);
        }

        private void CheckForNewHighestPreference()
        {
            uint highestPlayerId = 0u;
            int highest = int.MinValue;
            for (int i = 0; i < preferencesCount; i++)
            {
                int preference = preferences[i];
                if (preference > highest)
                {
                    highestPlayerId = playerIds[i];
                    highest = preference;
                }
            }
            SetNewHighestPreference(highestPlayerId, highest);
        }

        private void SetNewHighestPreference(uint playerId, int preference)
        {
            currentHighestPreference = preference;
            playerIdWithHighestPreference = playerId;
            if (!lockstep.IsMaster)
                return;
            PotentiallySendMasterChangeRequestIA();
        }

        private void PotentiallySendMasterChangeRequestIA()
        {
            if (preferences[BinarySearch(lockstep.MasterPlayerId)] == currentHighestPreference)
            {
                // The current master also has the same preference as the one that's about to be changed to,
                // so don't actually change master, just update the player id in this script.
                playerIdWithHighestPreference = lockstep.MasterPlayerId;
                return;
            }
            // Relies on SendMasterChangeRequestIA performing several checks itself.
            lockstep.SendMasterChangeRequestIA(playerIdWithHighestPreference);
        }

        private int BinarySearch(uint playerId) => ArrList.BinarySearch(ref playerIds, ref playerIdsCount, playerId);

        private int AddClient(uint playerId)
        {
            string name = lockstep.GetDisplayName(playerId);
            int preference = 0;
            if (persistentPreferences.TryGetValue(name, out DataToken preferenceToken))
                preference = preferenceToken.Int;
            else
                persistentPreferences.Add(name, preference);

            int index = ~BinarySearch(playerId);
            ArrList.Insert(ref playerIds, ref playerIdsCount, playerId, index);
            ArrList.Insert(ref preferences, ref preferencesCount, preference, index);
            ArrList.Insert(ref latencyPreferences, ref latencyPreferencesCount, preference, index);
            ArrList.Insert(ref latencyHiddenIds, ref latencyHiddenIdsCount, new DataDictionary(), index);
            return preference;
        }

        [LockstepEvent(LockstepEventType.OnClientLeft, Order = 100)]
        public void OnClientLeft()
        {
            uint playerId = lockstep.LeftPlayerId;
            int index = BinarySearch(playerId);
            ArrList.RemoveAt(ref playerIds, ref playerIdsCount, index);
            int preference = ArrList.RemoveAt(ref preferences, ref preferencesCount, index);
            ArrList.RemoveAt(ref latencyPreferences, ref latencyPreferencesCount, index);
            ArrList.RemoveAt(ref latencyHiddenIds, ref latencyHiddenIdsCount, index);
            if (preference == currentHighestPreference)
                CheckForNewHighestPreference();
        }

        private int GetHighest(int[] values, int count)
        {
            int highest = int.MinValue;
            for (int i = 0; i < count; i++)
            {
                int value = values[i];
                if (value > highest)
                    highest = value;
            }
            return highest;
        }

        private int GetLowest(int[] values, int count)
        {
            int lowest = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int value = values[i];
                if (value < lowest)
                    lowest = value;
            }
            return lowest;
        }

        /// <summary>
        /// <para>Gets the preference for the given <paramref name="playerId"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        /// <param name="playerId">Must be an id of a client that actually exists, see
        /// <see cref="LockstepAPI.ClientStateExists(uint)"/>.</param>
        /// <returns></returns>
        public int GetPreference(uint playerId) => preferences[BinarySearch(playerId)];
        /// <summary>
        /// <para>Gets the highest preference out of all clients that are in the world. Offline players
        /// excluded, even though their preferences are saved.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        /// <returns></returns>
        public int GetHighestPreference() => currentHighestPreference;
        /// <summary>
        /// <para>Gets the lowest preference out of all clients that are in the world. Offline players
        /// excluded, even though their preferences are saved.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        /// <returns></returns>
        public int GetLowestPreference() => GetLowest(preferences, preferencesCount);

        /// <summary>
        /// <para>Gets the latency hidden preference for the given <paramref name="playerId"/>.</para>
        /// <para>See <c>latency-states.md</c> in the documentation folder.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Non game state safe.</para>
        /// </summary>
        /// <param name="playerId">Must be an id of a client that actually exists, see
        /// <see cref="LockstepAPI.ClientStateExists(uint)"/>.</param>
        /// <returns></returns>
        public int GetLatencyHiddenPreference(uint playerId) => latencyPreferences[BinarySearch(playerId)];
        /// <summary>
        /// <para>Gets the highest latency hidden preference out of all clients that are in the world. Offline
        /// players excluded, even though their preferences are saved.</para>
        /// <para>See <c>latency-states.md</c> in the documentation folder.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Non game state safe.</para>
        /// </summary>
        /// <returns></returns>
        public int GetHighestLatencyHiddenPreference() => GetHighest(latencyPreferences, latencyPreferencesCount);
        /// <summary>
        /// <para>Gets the lowest latency hidden preference out of all clients that are in the world. Offline
        /// players excluded, even though their preferences are saved.</para>
        /// <para>See <c>latency-states.md</c> in the documentation folder.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Non game state safe.</para>
        /// </summary>
        /// <returns></returns>
        public int GetLowestLatencyHiddenPreference() => GetLowest(latencyPreferences, latencyPreferencesCount);

        /// <summary>
        /// <para>This function <b>must not</b> be called inside of a
        /// <see cref="LockstepMasterPreferenceEventType.OnLatencyHiddenMasterPreferenceChanged"/> handler,
        /// because that would cause recursion.</para>
        /// <para>(And we all love Udon so much that we happily choose not to use recursion. And no I'm not
        /// going to mark <see cref="SetPreference(uint, int, bool)"/> with the
        /// <see cref="RecursiveMethodAttribute"/> just for the off chance that someone wants to call it
        /// recursively, that'd just make performance worse for every non recursive call. I hope you don't
        /// mind a tasteful amount of salt occasionally.)</para>
        /// <para>Can be called inside and outside of game state safe events,
        /// <see cref="LockstepAPI.InGameStateSafeEvent"/>. Uses
        /// <see cref="LockstepAPI.SendInputAction(uint)"/> or
        /// <see cref="LockstepAPI.SendSingletonInputAction(uint)"/> depending on context.</para>
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="preference"></param>
        /// <param name="valueIsGSSafe">Set this to <see langword="true"/> if the given
        /// <paramref name="preference"/> is a game state safe value. When <see langword="true"/> and when
        /// <see cref="LockstepAPI.InGameStateSafeEvent"/> is also <see langword="true"/> then this function
        /// simply sets the preference of the given <paramref name="playerId"/>.</param>
        public void SetPreference(uint playerId, int preference, bool valueIsGSSafe = false)
        {
            if (valueIsGSSafe && lockstep.InGameStateSafeEvent)
            {
                SetPreferenceInGameState(playerId, preference, comingFromIA: false);
                return;
            }

            lockstep.WriteSmallUInt(playerId);
            lockstep.WriteSmallInt(preference);
            ulong id = lockstep.InGameStateSafeEvent
                ? lockstep.SendSingletonInputAction(onSetPreferenceIAId)
                : lockstep.SendInputAction(onSetPreferenceIAId);
            if (id == 0uL)
                return;

            int index = BinarySearch(playerId);
            int prevLatencyPreference = latencyPreferences[index];
            latencyPreferences[index] = preference;
            latencyHiddenIds[index].Add(id, true);
            if (preference != prevLatencyPreference)
                RaiseOnMasterLatencyPreferenceChanged(playerId);
        }

        [HideInInspector] [SerializeField] private uint onSetPreferenceIAId;
        [LockstepInputAction(nameof(onSetPreferenceIAId))]
        public void OnSetPreferenceIA()
        {
            uint playerId = lockstep.ReadSmallUInt();
            int preference = lockstep.ReadSmallInt();
            SetPreferenceInGameState(playerId, preference, comingFromIA: true);
        }

        private void SetPreferenceInGameState(uint playerId, int preference, bool comingFromIA)
        {
            persistentPreferences[lockstep.GetDisplayName(playerId)] = preference;
            int index = BinarySearch(playerId);
            if (index < 0)
                return;
            int prevPreference = preferences[index];
            preferences[index] = preference;

            if (preference < prevPreference)
                CheckForNewHighestPreference();
            else
                CheckIfIsNewHighestPreference(playerId, preference);

            DataDictionary hiddenIds = latencyHiddenIds[index];
            if (comingFromIA)
                hiddenIds.Remove(lockstep.SendingUniqueId);
            if (hiddenIds.Count == 0)
            {
                int prevLatencyPreference = latencyPreferences[index];
                if (preference != prevLatencyPreference)
                {
                    latencyPreferences[index] = preference;
                    RaiseOnMasterLatencyPreferenceChanged(playerId);
                }
            }

            if (preference != prevPreference)
                RaiseOnMasterPreferenceChanged(playerId);
        }

        private uint changedPlayerId;
        /// <summary>
        /// <para>The player id who's master preference has changed.</para>
        /// <para>Usable inside of <see cref="LockstepMasterPreferenceEventType.OnMasterPreferenceChanged"/>
        /// and <see cref="LockstepMasterPreferenceEventType.OnLatencyHiddenMasterPreferenceChanged"/>.</para>
        /// <para>Game state safe inside of
        /// <see cref="LockstepMasterPreferenceEventType.OnMasterPreferenceChanged"/>.</para>
        /// </summary>
        public uint ChangedPlayerId => changedPlayerId;

        private void RaiseOnMasterPreferenceChanged(uint playerId)
        {
            changedPlayerId = playerId;
            CustomRaisedEvents.Raise(ref onMasterPreferenceChangedListeners, nameof(LockstepMasterPreferenceEventType.OnMasterPreferenceChanged));
            changedPlayerId = 0u; // To prevent misuse of the API.
        }

        private void RaiseOnMasterLatencyPreferenceChanged(uint playerId)
        {
            changedPlayerId = playerId;
            CustomRaisedEvents.Raise(ref onLatencyHiddenMasterPreferenceChangedListeners, nameof(LockstepMasterPreferenceEventType.OnLatencyHiddenMasterPreferenceChanged));
            changedPlayerId = 0u; // To prevent misuse of the API.
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
            if (!isExport)
            {
                lockstep.WriteSmallUInt(playerIdWithHighestPreference);
                lockstep.WriteSmallInt(currentHighestPreference);

                lockstep.WriteSmallUInt((uint)playerIdsCount);
                for (int i = 0; i < playerIdsCount; i++)
                {
                    lockstep.WriteSmallUInt(playerIds[i]);
                    lockstep.WriteSmallInt(preferences[i]);
                }
            }

            DataList names = persistentPreferences.GetKeys();
            DataList values = persistentPreferences.GetValues();
            int count = names.Count;
            lockstep.WriteSmallUInt((uint)count);
            for (int i = 0; i < count; i++)
            {
                lockstep.WriteString(names[i].String);
                lockstep.WriteSmallInt(values[i].Int);
            }
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
            if (!isImport)
            {
                playerIdWithHighestPreference = lockstep.ReadSmallUInt();
                currentHighestPreference = lockstep.ReadSmallInt();

                playerIdsCount = (int)lockstep.ReadSmallUInt();
                preferencesCount = playerIdsCount;
                latencyPreferencesCount = playerIdsCount;
                latencyHiddenIdsCount = playerIdsCount;
                ArrList.EnsureCapacity(ref playerIds, playerIdsCount);
                ArrList.EnsureCapacity(ref preferences, preferencesCount);
                ArrList.EnsureCapacity(ref latencyPreferences, latencyPreferencesCount);
                ArrList.EnsureCapacity(ref latencyHiddenIds, latencyHiddenIdsCount);
                for (int i = 0; i < playerIdsCount; i++)
                {
                    playerIds[i] = lockstep.ReadSmallUInt();
                    int preference = lockstep.ReadSmallInt();
                    preferences[i] = preference;
                    latencyPreferences[i] = preference;
                    latencyHiddenIds[i] = new DataDictionary();
                }
            }

            int persistentCount = (int)lockstep.ReadSmallUInt();
            for (int i = 0; i < persistentCount; i++)
            {
                string name = lockstep.ReadString();
                int preference = lockstep.ReadSmallInt();
                persistentPreferences.SetValue(name, preference);
            }

            if (isImport)
                UpdatePreferencesUsingPersistentPreferences();

            return null;
        }

        private void UpdatePreferencesUsingPersistentPreferences()
        {
            for (int i = 0; i < playerIdsCount; i++)
            {
                uint playerId = playerIds[i];
                int importedPreference = persistentPreferences[lockstep.GetDisplayName(playerId)].Int;

                int prevLatencyPreference = latencyPreferences[i];
                if (importedPreference != prevLatencyPreference)
                {
                    latencyPreferences[i] = importedPreference;
                    RaiseOnMasterLatencyPreferenceChanged(playerId);
                }

                int prevPreference = preferences[i];
                if (importedPreference != prevPreference)
                {
                    preferences[i] = importedPreference;
                    RaiseOnMasterPreferenceChanged(playerId);
                }
            }
            CheckForNewHighestPreference();
        }
    }
}
