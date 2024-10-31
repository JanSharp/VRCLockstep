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
        /// </summary>i
        OnMasterPreferenceChanged,
        /// <summary>
        /// <para>Use <see cref="LockstepMasterPreference.ChangedPlayerId"/> to get the id of the client who's
        /// master preference changed.</para>
        /// <para>Raised when the
        /// <see cref="LockstepMasterPreference.GetLatencyHiddenPreference(uint)(uint)"/> for a client
        /// changed.</para>
        /// <para>Non game state safe.</para>
        /// </summary>i
        OnLatencyHiddenMasterPreferenceChanged,
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    sealed class LockstepMasterPreferenceEventAttribute : CustomRaisedEventBaseAttribute
    {
        public LockstepMasterPreferenceEventAttribute(LockstepMasterPreferenceEventType eventType)
            : base((int)eventType)
        { }
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [CustomRaisedEventsDispatcher(typeof(LockstepMasterPreferenceEventAttribute), typeof(LockstepMasterPreferenceEventType))]
    public class LockstepMasterPreference : LockstepGameState
    {
        [HideInInspector] [SerializeField] private LockstepAPI lockstep;

        public override string GameStateInternalName => "jansharp.lockstep-master-preference";
        public override string GameStateDisplayName => "Lockstep Master Preference";
        public override bool GameStateSupportsImportExport => false;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;

        [HideInInspector] [SerializeField] private UdonSharpBehaviour[] onMasterPreferenceChangedListeners;
        [HideInInspector] [SerializeField] private UdonSharpBehaviour[] onLatencyHiddenMasterPreferenceChangedListeners;

        private uint localPlayerId;

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
        public void OnInit() => AddClient(localPlayerId);

        [LockstepEvent(LockstepEventType.OnPreClientJoined, Order = -100)]
        public void OnPreClientJoined() => AddClient(lockstep.JoinedPlayerId);

        private int BinarySearch(uint playerId) => ArrList.BinarySearch(ref playerIds, ref playerIdsCount, playerId);

        private void AddClient(uint playerId)
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
        }

        [LockstepEvent(LockstepEventType.OnClientLeft, Order = 100)]
        public void OnClientLeft()
        {
            uint playerId = lockstep.LeftPlayerId;
            int index = BinarySearch(playerId);
            ArrList.RemoveAt(ref playerIds, ref playerIdsCount, index);
            ArrList.RemoveAt(ref preferences, ref preferencesCount, index);
            ArrList.RemoveAt(ref latencyPreferences, ref latencyPreferencesCount, index);
            ArrList.RemoveAt(ref latencyHiddenIds, ref latencyHiddenIdsCount, index);
        }

        public int GetPreference(uint playerId) => preferences[BinarySearch(playerId)];

        public int GetLatencyHiddenPreference(uint playerId) => latencyPreferences[BinarySearch(playerId)];

        /// <summary>
        /// <para>This function <b>must not</b> be called inside of a
        /// <see cref="LockstepMasterPreferenceEventType.OnLatencyHiddenMasterPreferenceChanged"/> handler,
        /// because that would cause recursion.</para>
        /// <para>(And we all love Udon so much that we happily choose not to use recursion. And no I'm not
        /// going to mark <see cref="SetPreference(uint, int)"/>) with the
        /// <see cref="RecursiveMethodAttribute"/> just for the off chance that someone wants to call it
        /// recursively, that'd just make performance worse for every non recursive call. I hope you don't
        /// mind a tasteful amount of salt occasionally.)</para>
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="preference"></param>
        public void SetPreference(uint playerId, int preference)
        {
            lockstep.WriteSmallUInt(playerId);
            lockstep.WriteSmallInt(preference);
            ulong id = lockstep.SendInputAction(onSetPreferenceIAId);
            int index = BinarySearch(playerId);
            int prevLatencyPreference = latencyPreferences[index];
            latencyPreferences[index] = preference;
            latencyHiddenIds[index].Add(id, true);
            if (preference != prevLatencyPreference)
            {
                changedPlayerId = playerId;
                RaiseOnMasterLatencyPreferenceChanged();
            }
        }

        [HideInInspector] [SerializeField] private uint onSetPreferenceIAId;
        [LockstepInputAction(nameof(onSetPreferenceIAId))]
        public void OnSetPreferenceIA()
        {
            changedPlayerId = lockstep.ReadSmallUInt();
            int preference = lockstep.ReadSmallInt();
            int index = BinarySearch(changedPlayerId);
            int prevPreference = preferences[index];
            preferences[index] = preference;
            persistentPreferences[lockstep.GetDisplayName(changedPlayerId)] = preference;

            DataDictionary hiddenIds = latencyHiddenIds[index];
            hiddenIds.Remove(lockstep.SendingUniqueId);
            if (hiddenIds.Count == 0)
            {
                int prevLatencyPreference = latencyPreferences[index];
                latencyPreferences[index] = preference;
                if (preference != prevLatencyPreference)
                    RaiseOnMasterLatencyPreferenceChanged();
            }

            if (preference != prevPreference)
                RaiseOnMasterPreferenceChanged();
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

        private void RaiseOnMasterPreferenceChanged()
        {
            CustomRaisedEvents.Raise(ref onMasterPreferenceChangedListeners, nameof(LockstepMasterPreferenceEventType.OnMasterPreferenceChanged));
        }

        private void RaiseOnMasterLatencyPreferenceChanged()
        {
            CustomRaisedEvents.Raise(ref onLatencyHiddenMasterPreferenceChangedListeners, nameof(LockstepMasterPreferenceEventType.OnLatencyHiddenMasterPreferenceChanged));
        }

        public override void SerializeGameState(bool isExport)
        {
            lockstep.WriteSmallUInt((uint)playerIdsCount);
            for (int i = 0; i < playerIdsCount; i++)
            {
                lockstep.WriteSmallUInt(playerIds[i]);
                lockstep.WriteSmallInt(preferences[i]);
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

        public override string DeserializeGameState(bool isImport, uint importedDataVersion)
        {
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

            int persistentCount = (int)lockstep.ReadSmallUInt();
            for (int i = 0; i < persistentCount; i++)
            {
                string name = lockstep.ReadString();
                int preference = lockstep.ReadSmallInt();
                persistentPreferences.Add(name, preference);
            }

            return null;
        }
    }
}
