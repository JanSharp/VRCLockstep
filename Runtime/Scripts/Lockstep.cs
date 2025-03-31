using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;
using TMPro;

namespace JanSharp.Internal
{
    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [CustomRaisedEventsDispatcher(typeof(LockstepEventAttribute), typeof(LockstepEventType))]
    public class Lockstep : LockstepAPI
    {
        // LJ = late joiner, IA = input action
        private const uint LJCurrentTickIAId = 0;
        private const uint LJInternalGameStatesIAId = 1;
        // custom game states will have ids starting at this, ascending
        private const uint LJFirstCustomGameStateIAId = 2;
        public const float SendTimeForNonTimedIAs = float.NaN;

        [Tooltip("This uses the scene name, not the World Name set in the VRChat Control Panel. Sorry, but "
            + "it's at least better than not having any default.")]
        public bool useSceneNameAsWorldName = true; // public just to prevent the "unused field" warning in unity.
        [Tooltip("This is exposed in the Lockstep API as 'WorldName', and is also automatically included in "
            + "exported game state data/strings. It's intended to be usable in UI, so it should be human "
            + "readable.\nMust be a single line, non empty, without leading or trailing space. (These "
            + "requirements are enforced at runtime. If it ends up being empty, 'Unnamed' is used.)")]
        [SerializeField] private string worldName;
        private string cachedWorldName = null;
        private string SanitizeWorldName(string name)
        {
            name = (name ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return name == "" ? "Unnamed" : name;
        }
        public override string WorldName
        {
            get
            {
                if (cachedWorldName == null) // Cannot use ??= with the current version of UdonSharp.
                    cachedWorldName = SanitizeWorldName(worldName);
                return cachedWorldName;
            }
        }

        [HideInInspector] [SerializeField] [SingletonReference] private WannaBeClassesManager wannaBeClassesManager;
        [SerializeField] private InputActionSync lateJoinerInputActionSync;
        [SerializeField] private LockstepTickSync tickSync;
        /// <summary>
        /// <para>On the master this is the currently running tick, as in new input actions are getting
        /// associated and run in this tick.</para>
        /// <para>On non master this is the tick input actions will get run in. Once input actions have been
        /// run for this tick it is advanced immediately to the next tick, so you can think of it like input
        /// actions getting run at the end of a tick (as well as on nth tick and on tick events).</para>
        /// <para>Ultimately this means that no matter what client we are on, when
        /// <see cref="TryRunCurrentTick"/> runs, the very last thing that happens is raising on tick and
        /// incrementing currentTick. I hope this makes master changes (which happen at the start of a tick)
        /// easier to think about.</para>
        /// </summary>
        private uint currentTick;
        /// <summary>
        /// <para>The system will run this tick, but not any later ticks.</para>
        /// <para>This means that <see cref="currentTick"/> can be 1 higher than <see cref="lastRunnableTick"/>,
        /// since in order for a tick to fully run it must increment <see cref="currentTick"/> at the end.</para>
        /// </summary>
        private uint lastRunnableTick;
        public override uint CurrentTick => currentTick;
        public override float RealtimeAtTick(uint tick) => tickStartTime + (float)tick / TickRate;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;
        private string localPlayerDisplayName;
        private InputActionSync inputActionSyncForLocalPlayer;
        /// <summary>
        /// <para>**Internal Game State**</para>
        /// <para>When <see cref="allClientStates"/> is not <see langword="null"/> then this is guaranteed to be
        /// the client id which has the <see cref="ClientState.Master"/> state.</para>
        /// </summary>
        private uint masterPlayerId;
        /// <summary>
        /// <para>So long as <see cref="currentTick"/> is less than <i>or equal to</i> the
        /// <see cref="firstMutableTick"/> there can be input actions associated with ticks even on the
        /// master.</para>
        /// <para>Once <see cref="currentTick"/> is greater than the <see cref="firstMutableTick"/> it is
        /// guaranteed that there are no more input actions associated with ticks on the master.</para>
        /// </summary>
        private uint firstMutableTick = 0u; // Effectively 1 tick past the last immutable tick.
        /// <summary>
        /// <para>This is only relevant to the master. It expands the meaning of <see cref="firstMutableTick"/>
        /// because if <see cref="currentTick"/> is equal to <see cref="firstMutableTick"/> then input actions
        /// still get associated with ticks on the master rather than either instantly running or running one
        /// frame later.</para>
        /// <para>However once <see cref="TryRunCurrentTick"/> is running input actions which were associated
        /// with the <see cref="currentTick"/> then it becomes invalid to associate more input actions with
        /// the <see cref="currentTick"/>, however it's not been advanced to the next tick yet.</para>
        /// <para>In this scenario <see cref="disallowAssociatingWithCurrentTick"/> gets set to
        /// <see langword="true"/>, preventing exactly those kinds of tick associations and instead running
        /// input actions in the next frame.</para>
        /// </summary>
        private bool disallowAssociatingWithCurrentTick = false;
        /// <summary>
        /// <para>The <see cref="Time.realtimeSinceStartup"/> for "tick 0". Tick 0 is invalid, but for
        /// calculations this is just easier than having it start at 1.</para>
        /// </summary>
        private float tickStartTime;
        private void SetTickStartTime() => tickStartTime = Time.realtimeSinceStartup - (float)currentTick / TickRate;
        /// <summary>
        /// <para>While <see cref="isCatchingUp"/> <see cref="tickStartTime"/> should be dilated for more
        /// accurate time calculations. If something happened 10 ticks ago, with a tick rate of 10, then
        /// getting the time for the current tick when it is behind the last runnable tick by 10 ticks should
        /// be 1 second in the past as well, even though it is the current tick.</para>
        /// </summary>
        private void SetDilatedTickStartTime() => tickStartTime = Time.realtimeSinceStartup - (float)lastRunnableTick / TickRate;
        private float tickStartTimeShift;
        private const float MaxTickStartTimeShift = (1f / TickRate) * 0.05f; // At most 5% faster or slower.
        /// <summary>Bit shorter interval to make the system try to run ticks slightly sooner.</summary>
        private const float PredictedTimeUntilNextNetworkTick = (1f / NetworkTickRate) * 0.925f;
        private const long MaxMSWhileCatchingUp = 10L;
        private int byteCountForLatestLJSync = -1;
        // private uint resetTickRateTick = uint.MaxValue; // At the end of this tick it gets reset to TickRate.
        // private float currentTickRate = TickRate;
        private bool isTickPaused = true;
        private bool isMaster = false;
        private bool ignoreLocalInputActions = true;
        private bool stillAllowLocalClientJoinedIA = false;
        private bool ignoreIncomingInputActions = true;
        private bool isWaitingToSendClientJoinedIA = true;
        private bool isWaitingForLateJoinerSync = false;
        private bool sendLateJoinerDataAtStartOfTick = false;
        private bool isCatchingUp = false;
        private bool isInitialCatchUp = true;
        private bool isSinglePlayer = false;
        private bool checkMasterChangeAfterProcessingLJGameStates = false;
        private bool lockstepIsInitialized = false;
        // Only true for the initial catch up to avoid it being true for just a few ticks seemingly randomly
        // (Caused by the master changing which requires catching up to what the previous master had already
        // processed. See IsMaster property.)
        public override bool IsCatchingUp => isCatchingUp && isInitialCatchUp;
        public override bool IsSinglePlayer => isSinglePlayer;
        public override bool IsInitialized => lockstepIsInitialized;

        private bool inGameStateSafeEvent = false;
        public override bool InGameStateSafeEvent => inGameStateSafeEvent;

        public override void FlagToContinueNextFrame()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FlagToContinueNextFrame");
            #endif
            flaggedToContinueNextFrame = true;
        }
        private bool isContinuationFromPrevFrame = false;
        public override bool FlaggedToContinueNextFrame => flaggedToContinueNextFrame;
        public override bool IsContinuationFromPrevFrame => isContinuationFromPrevFrame;
        private bool flaggedToContinueNextFrame = false;
        private bool flaggedToContinueInsideOfGSImport = false;
        private uint suspendedInputActionId;
        private uint suspendedSingletonInputActionId;
        private bool suspendedInInputActionsToRunNextFrame = false;
        private DataList suspendedDelayedEventsList = null;
        private ulong[] suspendedUniqueIds = null;
        private bool suspendedInStandaloneIA = false;
        private bool suspendedInLJSerialization = false;
        private bool suspendedInExportPreparation = false;
        private bool suspendedInExport = false;
        private bool suspendedInImportOptionsDeserialization = false;
        private int suspendedExportGSSizePosition;
        private byte[] suspendedWriteStream = null;
        private int suspendedWriteStreamSize = 0;
        /// <summary>
        /// <para>Must always be reset to 0 once done using it.</para>
        /// </summary>
        private int suspendedIndexInArray = 0;
        private int suspendedGSIndexInExport = 0;
        private int currentIncomingGSDataIndex = 0;
        /// <summary>
        /// <para>(object[] {int optionsByteCount, byte[] optionsBytes, byte[] gsBytes})[]</para>
        /// </summary>
        private object[][] incomingGameStateData = null;

        /// <summary>
        /// <para><see langword="true"/> on a single client, the asking client.</para>
        /// </summary>
        private bool isAskingForMasterCandidates = false;
        /// <summary>
        /// <para><see langword="true"/> on every other client, not the asking client.</para>
        /// <para>Clients which just joined may also have this set to <see langword="false"/>.</para>
        /// </summary>
        private bool someoneIsAskingForMasterCandidates = false;
        private uint clientIdAskingForCandidates;
        /// <summary>
        /// <para>uint playerId => bool true</para>
        /// <para>Basically just a hash set.</para>
        /// </summary>
        private DataDictionary notYetRespondedCandidates;
        /// <summary>
        /// <para>This is only used and "maintained" on and by the asking client. All other clients do not
        /// keep track of which clients have accepted to be candidates, since on other clients they may
        /// receive the response from one client before the initial request, making it impossible to clear the
        /// list, and making it impossible to guarantee only the right clients are in this list.</para>
        /// </summary>
        private uint[] acceptingCandidates = new uint[ArrList.MinCapacity];
        private int acceptingCandidatesCount = 0;
        private bool acceptForcedCandidate = false;
        private uint acceptForcedCandidateFromPlayerId;

        private ulong unrecoverableStateDueToUniqueId = 0uL;

        public const ulong InputActionIndexBits = 0x00000000ffffffffuL;
        public const int PlayerIdKeyShift = 32;
        // ulong uniqueId => objet[] { uint inputActionId, byte[] inputActionData, float sendTime }
        // uniqueId: pppppppp pppppppp pppppppp pppppppp iiiiiiii iiiiiiii iiiiiiii iiiiiiii
        // (p = player id, i = input action index)
        //
        // Unique ids associated with their input actions, all of which are input actions
        // which have not been run yet and are either waiting for the tick in which they will be run,
        // or waiting for tick sync to inform this client of which tick to run them in.
        //
        // On the master, as soon as the first mutable tick has been reached, this is guaranteed to be empty.
        // As soon as ticks are running, be it for the first master or catching up on new clients, this dict
        // is guaranteed to only contain input actions that are going to be run. Any input actions received
        // before catching up that got run on the other clients already get removed.
        private DataDictionary inputActionsByUniqueId = new DataDictionary();

        // uint => ulong[]
        // uint: tick to run in
        // ulong[]: unique ids (same as for inputActionsByUniqueId)
        private DataDictionary uniqueIdsByTick = new DataDictionary();

        /// <summary>
        /// <para>uint tick => DataList(object[] { uint inputActionId, byte[] inputActionData })</para>
        /// </summary>
        private DataDictionary delayedEventsByTick = new DataDictionary();

        ///cSpell:ignore iatrn
        ///<summary>(objet[] { uint inputActionId, byte[] inputActionData, ulong uniqueId, float sendTime })[]</summary>
        private object[][] inputActionsToRunNextFrame = new object[ArrList.MinCapacity][];
        private int iatrnCount = 0;

        ///<summary>**Internal Game State**</summary>
        private uint nextSingletonId = 0;
        ///<summary>
        ///<para><b>Internal Game State</b></para>
        ///<para>localSendTime is <b>not</b> part of the game state.</para>
        ///<para>uint singletonId => objet[] { uint responsiblePlayerId, byte[] singletonInputActionData, bool requiresTimeTracking, uint sendTick, float localSendTime }</para>
        ///</summary>
        private DataDictionary singletonInputActions = new DataDictionary();

        [SerializeField] private UdonSharpBehaviour[] inputActionHandlerInstances;
        [SerializeField] private string[] inputActionHandlerEventNames;
        public bool[] inputActionHandlersRequireTimeTracking;
        [System.NonSerialized] public float currentInputActionSendTime;

        [SerializeField] private UdonSharpBehaviour[] onNthTickHandlerInstances;
        [SerializeField] private string[] onNthTickHandlerEventNames;
        [SerializeField] private int onNthTickGroupsCount;
        [SerializeField] private int[] onNthTickHandlerGroupSizes;
        [SerializeField] private uint[] onNthTickIntervals;

        [SerializeField] private UdonSharpBehaviour[] onInitListeners;
        [SerializeField] private UdonSharpBehaviour[] onClientBeginCatchUpListeners;
        [SerializeField] private UdonSharpBehaviour[] onClientJoinedListeners;
        [SerializeField] private UdonSharpBehaviour[] onPreClientJoinedListeners;
        [SerializeField] private UdonSharpBehaviour[] onClientCaughtUpListeners;
        [SerializeField] private UdonSharpBehaviour[] onClientLeftListeners;
        [SerializeField] private UdonSharpBehaviour[] onMasterClientChangedListeners;
        [SerializeField] private UdonSharpBehaviour[] onLockstepTickListeners;
        [SerializeField] private UdonSharpBehaviour[] onExportStartListeners;
        [SerializeField] private UdonSharpBehaviour[] onExportFinishedListeners;
        [SerializeField] private UdonSharpBehaviour[] onImportStartListeners;
        [SerializeField] private UdonSharpBehaviour[] onImportOptionsDeserializedListeners;
        [SerializeField] private UdonSharpBehaviour[] onImportedGameStateListeners;
        [SerializeField] private UdonSharpBehaviour[] onImportFinishedListeners;
        [SerializeField] private UdonSharpBehaviour[] onExportOptionsForAutosaveChangedListeners;
        [SerializeField] private UdonSharpBehaviour[] onAutosaveIntervalSecondsChangedListeners;
        [SerializeField] private UdonSharpBehaviour[] onIsAutosavePausedChangedListeners;
        [SerializeField] private UdonSharpBehaviour[] onLockstepNotificationListeners;

        [SerializeField] private LockstepGameState[] allGameStates;
        [SerializeField] private int allGameStatesCount;
        [SerializeField] private LockstepGameState[] gameStatesSupportingImportExport;
        [SerializeField] private int gameStatesSupportingImportExportCount;
        // string internalName => LockstepGameState gameState
        private DataDictionary gameStatesByInternalName = new DataDictionary();
        // string internalName => int indexInAllGameStates
        private DataDictionary gameStateIndexesByInternalName = new DataDictionary();
        public override int AllGameStatesCount => allGameStatesCount;
        public override LockstepGameState[] AllGameStates
        {
            get
            {
                LockstepGameState[] result = new LockstepGameState[allGameStatesCount];
                allGameStates.CopyTo(result, 0);
                return result;
            }
        }
        public override int GameStatesSupportingImportExportCount => gameStatesSupportingImportExportCount;
        public override LockstepGameState[] GameStatesSupportingImportExport
        {
            get
            {
                LockstepGameState[] result = new LockstepGameState[gameStatesSupportingImportExportCount];
                gameStatesSupportingImportExport.CopyTo(result, 0);
                return result;
            }
        }

        private bool PlayerIdHasClientState(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  PlayerIdHasClientState - playerId: {playerId}");
            #endif
            return ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId) >= 0;
        }

        private ClientState GetClientStateUnsafe(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetClientStateUnsafe - playerId: {playerId}");
            #endif
            return allClientStates[ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId)];
        }

        public override bool ClientStateExists(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ClientStateExists - playerId: {playerId}");
            #endif
            if (allClientStates == null)
                return false;
            int index = ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId);
            return index >= 0;
        }

        public override ClientState GetClientState(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetClientState - playerId: {playerId}");
            #endif
            if (allClientStates == null)
                return ClientState.None;
            int index = ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId);
            return index >= 0 ? allClientStates[index] : ClientState.None;
        }

        public override bool TryGetClientState(uint playerId, out ClientState clientState)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  TryGetClientState - playerId: {playerId}");
            #endif
            clientState = GetClientState(playerId);
            return clientState != ClientState.None;
        }

        public override int ClientStatesCount => allClientStates == null ? 0 : allClientStatesCount;

        public override uint[] AllClientPlayerIds
        {
            get
            {
                if (allClientStates == null)
                    return null;
                uint[] playerIds = new uint[allClientIdsCount];
                System.Array.Copy(allClientIds, playerIds, allClientIdsCount);
                return playerIds;
            }
        }

        public override ClientState[] AllClientStates
        {
            get
            {
                if (allClientStates == null)
                    return null;
                ClientState[] clientStates = new ClientState[allClientStatesCount];
                System.Array.Copy(allClientStates, clientStates, allClientStatesCount);
                return clientStates;
            }
        }

        public override string[] AllClientDisplayNames
        {
            get
            {
                if (allClientStates == null)
                    return null;
                string[] clientNames = new string[allClientNamesCount];
                System.Array.Copy(allClientNames, clientNames, allClientNamesCount);
                return clientNames;
            }
        }

        private string[] clientStateNameLut = new string[]
        {
            nameof(ClientState.Master),
            nameof(ClientState.WaitingForLateJoinerSync),
            nameof(ClientState.CatchingUp),
            nameof(ClientState.Normal),
            nameof(ClientState.None),
        };
        public override string ClientStateToString(ClientState clientState)
        {
            // No debug log message because the debug UI calls this every frame.

            // Just a cast to int or a cast to byte then int does not emit an actual conversion and as such
            // causes the error that a byte variable is attempted to be used as an int. My guess is that
            // UdonSharp assumes that enums are ints in this case or something.
            int index = System.Convert.ToInt32(clientState);
            return 0 <= index && index < clientStateNameLut.Length
                ? clientStateNameLut[index]
                : index.ToString();
        }

        private void SetClientStatesToEmpty()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetClientStatesToEmpty");
            #endif
            allClientIds = new uint[ArrList.MinCapacity];
            allClientIdsCount = 0;
            allClientStates = new ClientState[ArrList.MinCapacity];
            allClientStatesCount = 0;
            allClientNames = new string[ArrList.MinCapacity];
            allClientNamesCount = 0;
        }

        private void SetClientStatesToNull()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetClientStatesToNull");
            #endif
            allClientIds = null;
            allClientIdsCount = 0;
            allClientStates = null;
            allClientStatesCount = 0;
            allClientNames = null;
            allClientNamesCount = 0;
        }

        private void AddClientState(uint playerId, ClientState clientState, string playerName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AddClientState - playerId: {playerId}, clientState: {clientState}, playerName: {playerName}");
            #endif
            // Note the ~, not -. Read C# docs about BinarySearch, that's just how it is.
            int index = ~ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId);
            #if LockstepDebug
            if (index < 0)
            {
                Debug.LogError($"[LockstepDebug] Attempt to add client state with already existing playerId {playerId}.");
                playerId = allClientIds[index]; // Intentionally crash the script.
                return;
            }
            #endif
            ArrList.Insert(ref allClientIds, ref allClientIdsCount, playerId, index);
            ArrList.Insert(ref allClientStates, ref allClientStatesCount, clientState, index);
            ArrList.Insert(ref allClientNames, ref allClientNamesCount, playerName, index);
        }

        private void SetClientState(uint playerId, ClientState clientState)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetClientState - playerId: {playerId}, clientState: {clientState}");
            #endif
            int index = ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId);
            #if LockstepDebug
            if (index < 0)
            {
                Debug.LogError($"[LockstepDebug] Attempt to set client state of non existent playerId {playerId}.");
                playerId = allClientIds[index]; // Intentionally crash the script.
                return;
            }
            #endif
            allClientStates[index] = clientState;
        }

        private void RemoveClientState(uint playerId, out ClientState clientState, out string playerName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RemoveClientState - playerId: {playerId}");
            #endif
            int index = ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId);
            #if LockstepDebug
            if (index < 0)
            {
                Debug.LogError($"[LockstepDebug] Attempt to remove client state of non existent playerId {playerId}.");
                playerId = allClientIds[index]; // Intentionally crash the script.
                clientState = ClientState.None;
                playerName = null;
                return;
            }
            #endif
            ArrList.RemoveAt(ref allClientIds, ref allClientIdsCount, index);
            clientState = ArrList.RemoveAt(ref allClientStates, ref allClientStatesCount, index);
            playerName = ArrList.RemoveAt(ref allClientNames, ref allClientNamesCount, index);
        }

        /// <summary>
        /// <para>uint playerId => InputActionSync inst</para>
        /// </summary>
        private DataDictionary inputActionSyncByPlayerId = new DataDictionary();
        /// <summary>
        /// <para>uint playerId => uint latestInputActionIndex</para>
        /// </summary>
        private DataDictionary latestInputActionIndexByPlayerId = new DataDictionary();
        /// <summary>
        /// <para>uint playerId => uint latestInputActionIndex</para>
        /// </summary>
        private DataDictionary latestInputActionIndexByPlayerIdForLJ;

        /// <summary>
        /// <para>**Internal Game State**</para>
        /// <para>Guaranteed to to be sorted ascending.</para>
        /// </summary>
        private uint[] allClientIds = null;
        private int allClientIdsCount = 0;
        /// <summary>
        /// <para>**Internal Game State**</para>
        /// <para>Indexes are associated with playerIds in <see cref="allClientIds"/>.</para>
        /// <para>Guaranteed to always contain exactly 1 client with the <see cref="ClientState.Master"/> state.</para>
        /// </summary>
        private ClientState[] allClientStates = null;
        private int allClientStatesCount = 0;
        /// <summary>
        /// <para>**Internal Game State**</para>
        /// <para>Indexes are associated with playerIds in <see cref="allClientIds"/>.</para>
        /// </summary>
        private string[] allClientNames = null;
        private int allClientNamesCount = 0;
        /// <summary>
        /// <para>Game state safe inside of <see cref="GetDisplayName(uint)"/>.</para>
        /// </summary>
        private string leftClientName = null;
        // non game state
        private uint[] leftClients = new uint[ArrList.MinCapacity];
        private int leftClientsCount = 0;
        /// <summary>
        /// <para>Must be <see langword="true"/> when <see cref="allClientStates"/> is <see langword="null"/>.</para>
        /// <para>Otherwise is true when the <see cref="masterPlayerId"/> has left, aka when said id is in
        /// <see cref="leftClients"/>.</para>
        /// <para>Hilariously it is possible for <see cref="isMaster"/> to be <see langword="true"/> while
        /// this is still <see langword="false"/>. Incredibly rare, but possible.</para>
        /// </summary>
        private bool currentlyNoMaster = true;

        private int clientsJoinedInTheLastFiveMinutes = 0;
        private byte[][] unprocessedLJSerializedGameStates = new byte[ArrList.MinCapacity][];
        private int unprocessedLJSerializedGSCount = 0;
        private int nextLJGameStateToProcess = -1;
        private float nextLJGameStateToProcessTime = 0f;
        private const float LJGameStateProcessingFrequency = 0.1f;
        private bool IsProcessingLJGameStates => nextLJGameStateToProcess != -1;

        private int flagForLateJoinerSyncSentCount = 0;
        private int processLeftPlayersSentCount = 0;
        private int someoneLeftWhileWeWereWaitingForLJSyncSentCount = 0;
        private bool waitingForCandidatesLoopRunning = false;

        // ALso used by the debug UI.
        private System.Diagnostics.Stopwatch lastUpdateSW = new System.Diagnostics.Stopwatch();

        // The actions run during catch up are actions that
        // have already been run by the previous master. Therefore this must return false, otherwise this
        // IsMaster property would return true on 2 clients when running the same input action.
        // I _think_ that this covers all edge cases, since if those input actions were to use this IsMaster
        // to modify the game state, then the only way to do that is though sending another input action. If
        // the master leaves shortly after doing so, those newly sent input actions would not even get
        // associated with a tick to run in, which means that IsMaster then being true in an input action
        // twice would be fine, because some other client became master and is running still queued actions,
        // however it won't run newly sent input actions twice, because remember how the ones sent from the
        // original master before leaving didn't get associated with a tick.
        // Now if they _did_ get associated with a tick then we can safely assume that at least 1 tick has
        // passed since the moment where they got sent, That means waitTick has advanced past the original
        // input action that sent the new input actions. Technically there is an edge case where if the ticks
        // do not advance at all during this entire time and it ends up syncing the tick association without
        // advancing any ticks, and the master leaves immediately afterwards, then another client would run
        // an input action with IsMaster being true again, which would be incorrect behavior. But the only
        // way this should be possible to happen is if ticks get paused using the tick pase boolean, which is
        // currently not possible and should never be possible.
        public override bool IsMaster => isMaster && !isCatchingUp; // Even for non initial catch ups.
        public override uint MasterPlayerId => masterPlayerId;
        private uint oldMasterPlayerId;
        private uint joinedPlayerId;
        private uint leftPlayerId;
        private uint catchingUpPlayerId;
        private uint sendingPlayerId;
        public override uint OldMasterPlayerId => oldMasterPlayerId;
        public override uint JoinedPlayerId => joinedPlayerId;
        public override uint LeftPlayerId => leftPlayerId;
        public override uint CatchingUpPlayerId => catchingUpPlayerId;
        public override uint SendingPlayerId => sendingPlayerId;
        private ulong sendingUniqueId;
        public override ulong SendingUniqueId => sendingUniqueId;
        private float sendingTime;
        public override float SendingTime => sendingTime;
        public override float RealtimeSinceSending => Time.realtimeSinceStartup - sendingTime;
        private string notificationMessage;
        public override string NotificationMessage => notificationMessage;

        // 4 of these are part of the game state, however not part of LJ data because LJ data won't be sent
        // while masterChangeRequestInProcess is true.
        private bool masterChangeRequestInProgress = false;
        private uint masterRequestManagingMasterId = 0u;
        private uint requestedMasterClientId = 0u;
        /// <summary>
        /// Not part of the game state.
        /// </summary>
        private bool sendMasterChangeConfirmationInFirstMutableTick = false;
        private bool finishMasterChangeProcessAtStartOfTick = false;

        private void Start()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  Start");
            #endif
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            lateJoinerInputActionSync.lockstep = this;

            int i = 0;
            foreach (LockstepGameState gameState in allGameStates)
            {
                DataToken key = gameState.GameStateInternalName;
                gameStatesByInternalName.Add(key, gameState);
                gameStateIndexesByInternalName.Add(key, i++);
            }
        }

        private void Update()
        {
            lastUpdateSW.Reset();
            lastUpdateSW.Start();

            if (isTickPaused)
            {
                if (IsProcessingLJGameStates && Time.realtimeSinceStartup >= nextLJGameStateToProcessTime)
                    ProcessNextLJSerializedGameState();
                lastUpdateSW.Stop();
                return;
            }

            if (flaggedToContinueNextFrame)
            {
                bool relevant = true;
                if (suspendedInStandaloneIA)
                {
                    suspendedInStandaloneIA = false;
                    RunInputActionSuspendedPrevFrame();
                    if (flaggedToContinueNextFrame)
                        suspendedInStandaloneIA = true;
                }
                else if (suspendedInLJSerialization)
                    SendLateJoinerData();
                else if (suspendedInExport)
                    ExportInternal();
                else
                    relevant = false;

                if (relevant && flaggedToContinueNextFrame)
                {
                    lastUpdateSW.Stop();
                    return;
                }
            }

            if (isCatchingUp)
            {
                CatchUp();
                lastUpdateSW.Stop();
                return;
            }

            if (iatrnCount != 0 && (!flaggedToContinueNextFrame || suspendedInInputActionsToRunNextFrame))
            {
                RunInputActionsForThisFrame();
                if (flaggedToContinueNextFrame)
                {
                    lastUpdateSW.Stop();
                    return;
                }
            }

            float timePassed = Time.realtimeSinceStartup - tickStartTime;
            uint runUntilTick = System.Math.Min(lastRunnableTick, (uint)(timePassed * TickRate));
            while (currentTick <= runUntilTick)
            {
                if (!TryRunCurrentTick())
                    break;
                tickStartTime += tickStartTimeShift;
                if (lastUpdateSW.ElapsedMilliseconds >= MaxMSWhileCatchingUp)
                    break;
            }

            if (isMaster)
            {
                // Synced tick is always 1 behind, that way new input actions can be run in
                // the current tick on the master without having to queue them for the next tick.
                tickSync.lastRunnableTick = currentTick - 1u;
            }

            lastUpdateSW.Stop();
        }

        public void SetLastRunnableTick(uint lastRunnableTick)
        {
            this.lastRunnableTick = lastRunnableTick;
            float timeAtNextNetworkTick = Time.realtimeSinceStartup + PredictedTimeUntilNextNetworkTick;
            float timePassed = timeAtNextNetworkTick - tickStartTime;
            uint runUntilTick = (uint)(timePassed * TickRate);
            tickStartTimeShift = runUntilTick.CompareTo(lastRunnableTick) * MaxTickStartTimeShift;
        }

        private void CatchUp()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CatchUp");
            #endif
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            SetDilatedTickStartTime();
            while (currentTick <= lastRunnableTick
                && TryRunCurrentTick()
                && stopwatch.ElapsedMilliseconds < MaxMSWhileCatchingUp)
            { }
            stopwatch.Stop(); // I don't think this actually matters, but it's not used anymore so sure.

            // As soon as we are within 1 second of the current tick, consider it done catching up.
            // This little leeway is required, as it may not be able to reach waitTick because
            // input actions may arrive after tick sync data.
            // Run all the way to waitTick when isMaster, otherwise other clients would most likely desync.
            if (isMaster
                ? currentTick > lastRunnableTick // Yes it's duplicated but this is more readable.
                : currentTick > lastRunnableTick || lastRunnableTick - currentTick < TickRate)
            {
                CleanUpOldTickAssociations();
                isCatchingUp = false;
                SendClientCaughtUpIA(); // Uses isInitialCatchUp.
                isInitialCatchUp = false;
                StartOrStopAutosave();
                SetTickStartTime();
                if (isMaster)
                    FinishCatchingUpOnMaster();
            }
        }

        private void CleanUpOldTickAssociations()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CleanUpOldTickAssociations");
            #endif
            DataList keys = uniqueIdsByTick.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken tickToRunToken = keys[i];
                if (tickToRunToken.UInt >= currentTick)
                    continue;

                uniqueIdsByTick.Remove(tickToRunToken, out DataToken uniqueIdsToken);
                // inputActionsByUniqueId has already been cleaned up by CleanUpOldInputActions.
            }
        }

        private bool TryRunCurrentTick()
        {
            DataToken currentTickToken = currentTick;
            ulong[] uniqueIds = suspendedUniqueIds;
            if (!flaggedToContinueNextFrame && uniqueIdsByTick.TryGetValue(currentTickToken, out DataToken uniqueIdsToken))
            {
                uniqueIds = (ulong[])uniqueIdsToken.Reference;
                foreach (ulong uniqueId in uniqueIds)
                    if (!inputActionsByUniqueId.ContainsKey(uniqueId))
                    {
                        uint playerId = (uint)(uniqueId >> PlayerIdKeyShift);
                        if (uniqueId != unrecoverableStateDueToUniqueId && !PlayerIdHasClientState(playerId))
                        {
                            // This variable is purely used as to not spam the log file every frame with this error message.
                            unrecoverableStateDueToUniqueId = uniqueId;
                            /// cSpell:ignore desync, desyncs
                            Debug.LogError($"[Lockstep] There's an input action queued to run on the tick {currentTick} "
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
                uniqueIdsByTick.Remove(currentTickToken);
            }

            #if LockstepDebug
            if (isMaster && currentTick > firstMutableTick && uniqueIds != null)
                Debug.LogError($"[LockstepDebug] When on master the first mutable tick is the only tick that "
                    + $"is still allowed to have input actions associated with it. Past that there must be "
                    + $"zero input actions associated with ticks, since they should just get run as soon as "
                    + $"they get received (or once they got sent if the local client was issuing them).");
            #endif

            disallowAssociatingWithCurrentTick = true;

            if (uniqueIds != null)
            {
                RunInputActionsForUniqueIds(uniqueIds);
                if (flaggedToContinueNextFrame)
                    return false; // Do not unset disallowAssociatingWithCurrentTick since lockstep is now suspended in this tick.
            }

            if (flaggedToContinueNextFrame)
            {
                RunDelayedEventsForCurrentTick(suspendedDelayedEventsList);
                if (flaggedToContinueNextFrame)
                    return false; // Do not unset disallowAssociatingWithCurrentTick since lockstep is now suspended in this tick.
            }
            if (delayedEventsByTick.Remove(currentTickToken, out DataToken eventDataListToken))
            {
                RunDelayedEventsForCurrentTick(eventDataListToken.DataList);
                if (flaggedToContinueNextFrame)
                    return false; // Do not unset disallowAssociatingWithCurrentTick since lockstep is now suspended in this tick.
            }

            RaiseOnNthTicks();
            RaiseOnTick(); // End of tick.

            disallowAssociatingWithCurrentTick = false;

            currentTick++;

            // Slowly increase the immutable tick. This prevents potential lag spikes when many input actions
            // were sent while catching up. This approach also prevents being stuck catching up forever by
            // not touching waitTick.
            // Still continue increasing it even when done catching up, for the same reason: no lag spikes.
            if ((currentTick % TickRate) == 0u)
                firstMutableTick++;
            #if LockstepDebug
            // Debug.Log($"[LockstepDebug] Running tick {currentTick}");
            #endif

            StartOfTick();

            if (isMaster && !StartOfTickChecksOnMaster())
                return false;

            return true;
        }

        private void StartOfTick()
        {
            if (!isMaster && finishMasterChangeProcessAtStartOfTick)
            {
                finishMasterChangeProcessAtStartOfTick = false;
                if (masterRequestManagingMasterId == localPlayerId)
                {
                    Debug.LogError($"[Lockstep] Impossible because the master cannot change during a master "
                        + $"change request through another change request. Therefore the only way for the "
                        + $"master to change during a request is by the previous master leaving. If this "
                        + $"happens then another clint becomes master, and said client may put the "
                        + $"confirmation input action into the 'catchup queue' so to speak, which is the "
                        + $"only way for the master client to actually reach this point in the code. However "
                        + $"if that happens then the masterRequestManagingMasterId cannot equal the local "
                        + $"client id. And, side note, this is unrecoverable.");
                }
                SetMaster(requestedMasterClientId);
            }
        }

        private bool StartOfTickChecksOnMaster()
        {
            // Simply put, none of the checks make sense while still catching up, and while I can't explain it
            // right now, I'm quite certain that StopBeingMaster would break if called while still catching up
            // and the others may not behave properly either.
            if (isCatchingUp)
                return true;

            if (finishMasterChangeProcessAtStartOfTick)
            {
                finishMasterChangeProcessAtStartOfTick = false;
                SetMaster(requestedMasterClientId);
                return false; // To break out of the tick running loop.
            }

            if (sendLateJoinerDataAtStartOfTick && currentTick > firstMutableTick && !isImporting && !masterChangeRequestInProgress)
            {
                // Waiting until the first mutable tick, because if the master was catching up and
                // associating new input actions with ticks while doing so, those would be enqueued at
                // the current first mutable tick at that time. Incoming player joined actions would also
                // be enqueued at the end just the same. This means if late joiner sync data was to be
                // sent before all these input actions that were added during catch up have run, then
                // there could be a case where a client receives late joiner sync data at a tick before
                // it knows which input actions were associated with ticks right after receiving LJ data.
                // It would think that there are no actions there, so it would desync. This edge case
                // requires multiple players to join while the master is still catching up.
                sendLateJoinerDataAtStartOfTick = false;
                SendLateJoinerData();
            }

            if (sendMasterChangeConfirmationInFirstMutableTick && currentTick > firstMutableTick)
            {
                sendMasterChangeConfirmationInFirstMutableTick = false;
                SendConfirmedMasterChangeIA();
            }

            return true;
        }

        private void RunDelayedEventsForCurrentTick(DataList eventDataList)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunDelayedEventsForCurrentTick");
            #endif
            int count = eventDataList.Count;
            for (int i = suspendedIndexInArray; i < count; i++)
            {
                if (flaggedToContinueNextFrame)
                {
                    suspendedDelayedEventsList = null;
                    suspendedIndexInArray = 0;
                    RunInputActionSuspendedPrevFrame();
                }
                else
                {
                    object[] eventData = (object[])eventDataList[i].Reference;
                    uint inputActionId = (uint)eventData[0];
                    byte[] inputActionData = (byte[])eventData[1];
                    RunInputAction(inputActionId, inputActionData, 0uL, 0f, bypassValidityCheck: true);
                }

                if (flaggedToContinueNextFrame)
                {
                    suspendedDelayedEventsList = eventDataList;
                    suspendedIndexInArray = i;
                    return;
                }
            }
        }

        private void RaiseOnNthTicks()
        {
            inGameStateSafeEvent = true;
            int handlerIndex = 0;
            for (int i = 0; i < onNthTickGroupsCount; i++)
            {
                int groupSize = onNthTickHandlerGroupSizes[i];
                uint interval = onNthTickIntervals[i];
                // Udon only exposes int modulo. No uint, long, etc. Hilariously terribly sad.
                if (currentTick - ((currentTick / interval) * interval) != 0u)
                {
                    handlerIndex += groupSize;
                    continue;
                }
                int startIndex = handlerIndex;
                handlerIndex += groupSize;
                for (int j = startIndex; j < handlerIndex; j++)
                    onNthTickHandlerInstances[j].SendCustomEvent(onNthTickHandlerEventNames[j]);
            }
            inGameStateSafeEvent = false;
        }

        private void RunInputActionsForUniqueIds(ulong[] uniqueIds)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionsForUniqueIds");
            #endif
            int length = uniqueIds.Length;
            for (int i = suspendedIndexInArray; i < length; i++)
            {
                if (flaggedToContinueNextFrame)
                {
                    suspendedUniqueIds = null;
                    suspendedIndexInArray = 0;
                    RunInputActionSuspendedPrevFrame();
                }
                else
                    RunInputActionForUniqueId(uniqueIds[i]);

                if (flaggedToContinueNextFrame)
                {
                    suspendedUniqueIds = uniqueIds;
                    suspendedIndexInArray = i;
                    return;
                }
            }
        }

        private void RunInputActionForUniqueId(ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionForUniqueId");
            #endif
            inputActionsByUniqueId.Remove(uniqueId, out DataToken inputActionDataToken);
            object[] inputActionData = (object[])inputActionDataToken.Reference;
            RunInputAction((uint)inputActionData[0], (byte[])inputActionData[1], uniqueId, (float)inputActionData[2]);
        }

        private void RunInputActionForUniqueIdNextFrame(ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionForUniqueIdNextFrame");
            #endif
            inputActionsByUniqueId.Remove(uniqueId, out DataToken inputActionDataToken);
            object[] inputActionData = (object[])inputActionDataToken.Reference;
            ArrList.Add(ref inputActionsToRunNextFrame, ref iatrnCount, new object[] { (uint)inputActionData[0], (byte[])inputActionData[1], uniqueId, (float)inputActionData[2] });
        }

        private void RunInputActionsForThisFrame()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionsForThisFrame");
            #endif
            for (int i = suspendedIndexInArray; i < iatrnCount; i++)
            {
                if (flaggedToContinueNextFrame)
                {
                    suspendedInInputActionsToRunNextFrame = false;
                    suspendedIndexInArray = 0;
                    RunInputActionSuspendedPrevFrame();
                }
                else
                {
                    object[] inputActionData = inputActionsToRunNextFrame[i];
                    inputActionsToRunNextFrame[i] = null;
                    RunInputAction((uint)inputActionData[0], (byte[])inputActionData[1], (ulong)inputActionData[2], (float)inputActionData[3]);
                }

                if (flaggedToContinueNextFrame)
                {
                    suspendedInInputActionsToRunNextFrame = true;
                    suspendedIndexInArray = i;
                    return;
                }
            }
            iatrnCount = 0;
        }

        private void ForgetAboutInputActionsForThisFrame()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ForgetAboutInputActionsForThisFrame");
            #endif
            for (int i = 0; i < iatrnCount; i++)
                inputActionsToRunNextFrame[i] = null; // Just to ensure that memory can be freed.
            iatrnCount = 0;
        }

        private void RunInputAction(uint inputActionId, byte[] inputActionData, ulong uniqueId, float sendTime, bool bypassValidityCheck = false)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputAction - event name: {inputActionHandlerEventNames[inputActionId]}");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            #endif
            ResetReadStream();
            readStream = inputActionData;
            UdonSharpBehaviour inst = inputActionHandlerInstances[inputActionId];
            sendingPlayerId = (uint)(uniqueId >> PlayerIdKeyShift);
            sendingUniqueId = uniqueId;
            sendingTime = sendTime;
            // This provides the guarantee that input actions sent by clients which have left the instance and
            // for which the OnClientLeft event has already been raised will not be run. This is only an
            // incredibly unlikely edge case since the system waits 1 second after the player left until the
            // client left input action gets sent, however since we cannot trust and rely on timing of
            // serialized data received from VRChat's networking system, I cannot be sure that there won't be
            // some input action(s) received even after 1 second after the player left event from VRChat.
            // It sucks because this is overhead for every single input action that gets run, however I
            // couldn't think of another solution that wouldn't introduce other issues.
            // The clientJoinedIAId is of course allowed regardless, since that's how a client gets added to
            // clientStates in the first place.
            if (!bypassValidityCheck && inputActionId != clientJoinedIAId && !PlayerIdHasClientState(sendingPlayerId))
            {
                #if LockstepDebug
                Debug.Log($"[LockStepDebug] The player id {sendingPlayerId} is not in the client states game"
                    + $"state and therefore running input actions sent by this player id is invalid. This "
                    + $"input action is ignored. This is supposed to be an incredibly rare edge case, such "
                    + $"that it should effectively never happen.");
                #endif
                return;
            }
            inGameStateSafeEvent = true;
            inst.SendCustomEvent(inputActionHandlerEventNames[inputActionId]);
            inGameStateSafeEvent = false;
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] [sw] Lockstep  RunInputActionWithCurrentReadStream (inner) - ms: {sw.Elapsed.TotalMilliseconds}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            if (flaggedToContinueNextFrame)
                suspendedInputActionId = inputActionId;
        }

        private void RunInputActionSuspendedPrevFrame()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RunInputActionSuspendedPrevFrame - event name: {inputActionHandlerEventNames[suspendedInputActionId]}");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            #endif
            flaggedToContinueNextFrame = false;
            UdonSharpBehaviour inst = inputActionHandlerInstances[suspendedInputActionId];
            // sendingPlayerId, sendingUniqueId, sendingTime are all still set.
            isContinuationFromPrevFrame = true;
            inGameStateSafeEvent = true;
            inst.SendCustomEvent(inputActionHandlerEventNames[suspendedInputActionId]);
            inGameStateSafeEvent = false;
            isContinuationFromPrevFrame = false;
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] [sw] Lockstep  RunInputActionSuspendedPrevFrame (inner) - ms: {sw.Elapsed.TotalMilliseconds}, event name: {inputActionHandlerEventNames[suspendedInputActionId]}");
            #endif
        }

        private bool IsAllowedToSendInputActionId(uint inputActionId)
        {
            return !ignoreLocalInputActions || (stillAllowLocalClientJoinedIA && inputActionId == clientJoinedIAId);
        }

        private void SendInstantAction(uint inputActionId, bool doRunLocally = false)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendInstantAction - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}, doRunLocally: {doRunLocally}");
            #endif
            byte[] inputActionData = new byte[writeStreamSize];
            System.Buffer.BlockCopy(writeStream, 0, inputActionData, 0, writeStreamSize);
            ResetWriteStream();
            ulong uniqueId = inputActionSyncForLocalPlayer.SendInputAction(inputActionId, inputActionData, inputActionData.Length);
            if (!doRunLocally)
                return;
            RunInputAction(inputActionId, inputActionData, uniqueId, SendTimeForNonTimedIAs, bypassValidityCheck: true);
        }

        public override ulong SendInputAction(uint inputActionId)
        {
            currentInputActionSendTime = Time.realtimeSinceStartup; // As early as possible.
            if (!lockstepIsInitialized)
            {
                ResetWriteStream();
                return 0uL;
            }
            return SendInputAction(inputActionId, forceOneFrameDelay: true);
        }

        private ulong SendInputAction(uint inputActionId, bool forceOneFrameDelay)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendInputAction - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            // currentInputActionSendTime is not set here because none of the internal Lockstep IAs require time tracking,
            // so the if statement further below is always going to set it to SendTimeForNonTimedIAs;
            if (!IsAllowedToSendInputActionId(inputActionId))
            {
                ResetWriteStream();
                return 0uL;
            }
            if (!inputActionHandlersRequireTimeTracking[inputActionId])
                currentInputActionSendTime = SendTimeForNonTimedIAs;

            byte[] inputActionData = new byte[writeStreamSize];
            System.Buffer.BlockCopy(writeStream, 0, inputActionData, 0, writeStreamSize);
            ResetWriteStream();

            return SendInputActionInternal(inputActionId, inputActionData, forceOneFrameDelay);
        }

        /// <summary>
        /// <para>Expects <see cref="currentInputActionSendTime"/> to have an updated/valid/appropriate value.</para>
        /// </summary>
        private ulong SendInputActionInternal(uint inputActionId, byte[] inputActionData, bool forceOneFrameDelay)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendInputActionInternal - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            if (isSinglePlayer) // Guaranteed to be master while in single player.
                return TryToInstantlyRunInputActionOnMaster(inputActionId, 0uL, currentInputActionSendTime, inputActionData, forceOneFrameDelay);

            ulong uniqueId = inputActionSyncForLocalPlayer.SendInputAction(inputActionId, inputActionData, inputActionData.Length);
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendInputActionInternal (inner) - uniqueId: 0x{uniqueId:x16}");
            #endif

            if (stillAllowLocalClientJoinedIA)
            {
                if (ignoreLocalInputActions)
                {
                    #if LockstepDebug
                    Debug.Log($"[LockstepDebug] Lockstep  SendInputAction (inner) - ignoreLocalInputActions is true, returning");
                    #endif
                    return 0uL; // Do not save client joined IA because it will never be executed locally.
                }
                Debug.LogError("[Lockstep] stillAllowLocalClientJoinedIA is true while ignoreLocalInputActions is false. "
                    + "This is an invalid state, stillAllowLocalClientJoinedIA should only ever be true if "
                    + "ignoreLocalInputActions is also true. Continuing as though stillAllowLocalClientJoinedIA was false.");
            }

            inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData, currentInputActionSendTime }));
            return uniqueId;
        }

        ///<summary><para>Unlike SendInputAction, SendSingletonInputAction must only be called from within a
        ///game state safe event.</para>
        ///<para>Returns the unique id of the input action that got sent. If OnInit or OnClientBeginCatchUp
        ///have not run yet then it will return 0uL - an invalid id - indicating that it did not get sent.
        ///It'll also return 0uL on all clients except for the responsible player, since all those clients
        ///inherently do not send any action.</para>
        ///<para>The intended purpose of this return value in combination with SendingUniqueId is making
        ///latency state implementations with latency hiding easier.</para></summary>
        public override ulong SendSingletonInputAction(uint inputActionId)
        {
            return SendSingletonInputAction(inputActionId, masterPlayerId);
        }

        ///<summary><para>Unlike SendInputAction, SendSingletonInputAction must only be called from within a
        ///game state safe event.</para>
        ///<para>Returns the unique id of the input action that got sent. If OnInit or OnClientBeginCatchUp
        ///have not run yet then it will return 0uL - an invalid id - indicating that it did not get sent.
        ///It'll also return 0uL on all clients except for the responsible player, since all those clients
        ///inherently do not send any action.</para>
        ///<para>The intended purpose of this return value in combination with SendingUniqueId is making
        ///latency state implementations with latency hiding easier.</para></summary>
        public override ulong SendSingletonInputAction(uint inputActionId, uint responsiblePlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendSingletonInputAction - inputActionId: {inputActionId}, event name: {inputActionHandlerEventNames[inputActionId]}");
            #endif
            currentInputActionSendTime = Time.realtimeSinceStartup; // As early as possible.

            if (!inGameStateSafeEvent)
            {
                Debug.LogError("[Lockstep] Attempt to call SendSingletonInputAction outside of game state safe events.");
                ResetWriteStream();
                return 0uL;
            }
            // No need to check if IsAllowedToSendInputActionId, because inside of game state safe events
            // sending input actions is guaranteed to be allowed.
            // Similarly no need to check lockstepIsInitialized.

            uint singletonId = nextSingletonId++;

            // Write 2 more values to the stream, then shuffle the data around when copying to the
            // singletonInputActionData such that those 2 new values come first, not last.
            int actualInputActionDataSize = writeStreamSize;
            WriteSmallUInt(singletonId);
            WriteSmallUInt(inputActionId);
            byte[] singletonInputActionData = new byte[writeStreamSize];
            int idsSize = writeStreamSize - actualInputActionDataSize;
            System.Buffer.BlockCopy(writeStream, actualInputActionDataSize, singletonInputActionData, 0, idsSize);
            System.Buffer.BlockCopy(writeStream, 0, singletonInputActionData, idsSize, actualInputActionDataSize);
            ResetWriteStream();

            bool requiresTimeTracking = inputActionHandlersRequireTimeTracking[inputActionId];
            singletonInputActions.Add(singletonId, new DataToken(new object[]
            {
                responsiblePlayerId,
                singletonInputActionData,
                requiresTimeTracking,
                currentTick,
                currentInputActionSendTime,
            }));

            if (localPlayerId != responsiblePlayerId)
                return 0uL;

            uint singletonIAId = requiresTimeTracking ? timedSingletonInputActionIAId : singletonInputActionIAId;
            return SendInputActionInternal(singletonIAId, singletonInputActionData, forceOneFrameDelay: true);
        }

        [SerializeField] [HideInInspector] private uint singletonInputActionIAId;
        [LockstepInputAction(nameof(singletonInputActionIAId))]
        public void OnSingletonInputActionIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnSingletonInputActionIA - sendingTime: {sendingTime}");
            #endif
            if (!isContinuationFromPrevFrame)
            {
                uint singletonId = ReadSmallUInt();
                suspendedSingletonInputActionId = ReadSmallUInt();
                singletonInputActions.Remove(singletonId);
            }
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnSingletonInputActionIA (inner) - event name: {inputActionHandlerEventNames[suspendedSingletonInputActionId]}");
            #endif
            UdonSharpBehaviour inst = inputActionHandlerInstances[suspendedSingletonInputActionId];
            inst.SendCustomEvent(inputActionHandlerEventNames[suspendedSingletonInputActionId]);
        }

        [SerializeField] [HideInInspector] private uint timedSingletonInputActionIAId;
        [LockstepInputAction(nameof(timedSingletonInputActionIAId), TrackTiming = true)]
        public void OnTimedSingletonInputActionIA() => OnSingletonInputActionIA(); // Exact same handler.

        private void CheckIfSingletonInputActionGotDropped(uint leftPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckIfSingletonInputActionGotDropped");
            #endif
            DataList values = singletonInputActions.GetValues();
            for (int i = 0; i < values.Count; i++)
            {
                object[] singletonInputAction = (object[])values[i].Reference;
                uint responsiblePlayerId = (uint)singletonInputAction[0];
                if (leftPlayerId != responsiblePlayerId)
                    continue;
                singletonInputAction[0] = masterPlayerId; // Update responsible player.
                if (isMaster)
                {
                    currentInputActionSendTime = (float)singletonInputAction[4];
                    if (float.IsNaN(currentInputActionSendTime)) // NaN means this entry is from late joiner syncing.
                        currentInputActionSendTime = RealtimeAtTick((uint)singletonInputAction[3]);
                    bool requiresTimeTracking = (bool)singletonInputAction[2];
                    uint singletonIAId = requiresTimeTracking ? timedSingletonInputActionIAId : singletonInputActionIAId;
                    SendInputActionInternal(singletonIAId, (byte[])singletonInputAction[1], forceOneFrameDelay: true);
                    // forceOneFrameDelay is true to have consistent order in relation to the OnClientLeft event.
                }
            }
        }

        public override void SendEventDelayedTicks(uint inputActionId, uint tickDelay)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendEventDelayedTicks");
            #endif
            if (!inGameStateSafeEvent)
            {
                Debug.LogError("[Lockstep] Attempt to call SendEventDelayedTicks outside of game state safe events.");
                ResetWriteStream();
                return;
            }
            // No need to check if ignoreLocalInputActions, because inside of game state safe events
            // sending input actions is guaranteed to be allowed.

            byte[] inputActionData = new byte[writeStreamSize];
            System.Buffer.BlockCopy(writeStream, 0, inputActionData, 0, writeStreamSize);
            ResetWriteStream();

            if (tickDelay == 0u)
            {
                Debug.LogError($"[Lockstep] Attempt to SendEventDelayedTicks with a tickDelay of 0. This is "
                    + $"invalid as it can cause input actions to be run inside of other input actions, which "
                    + $"is not only recursion which Udon will throw a fit about, it also goes against "
                    + $"Lockstep's design and infrastructure. For example {nameof(FlagToContinueNextFrame)} "
                    + $"inside of an inner input action would cause the outer input action to be invoked "
                    + $"next frame expecting it to handle continuation of the inner input action. Which is "
                    + $"simply a mess.");
                ResetWriteStream();
                return;
            }

            uint tick = currentTick + tickDelay;
            DataList eventDataList;
            if (delayedEventsByTick.TryGetValue(tick, out DataToken eventDataListToken))
                eventDataList = eventDataListToken.DataList;
            else
            {
                eventDataList = new DataList();
                delayedEventsByTick.Add(tick, eventDataList);
            }
            eventDataList.Add(new DataToken(new object[] { inputActionId, inputActionData }));
        }

        public void InputActionSent(ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  InputActionSent");
            #endif
            if (!isMaster) // When instant actions are sent, isMaster is still going to be false.
                return;

            // Must use <= in order to ensure correct order of input actions. There may already be input
            // actions associated with the firstMutableTick, so if currentTick == firstMutableTick then it
            // must still associate them in order for these new input actions to run after the already
            // associated ones.
            if (currentTick <= firstMutableTick)
            {
                if (currentTick == firstMutableTick && !disallowAssociatingWithCurrentTick)
                {
                    if (!isSinglePlayer)
                        tickSync.AddInputActionToRun(currentTick, uniqueId);
                    RunInputActionForUniqueIdNextFrame(uniqueId);
                }
                else
                    AssociateInputActionWithTickOnMaster(firstMutableTick, uniqueId);
                return;
            }

            if (iatrnCount != 0 // Must enqueue if there's already a queue to ensure proper order.
                || flaggedToContinueNextFrame) // An input action is currently suspended, cannot run another one.
            {
                if (!isSinglePlayer)
                    tickSync.AddInputActionToRun(currentTick, uniqueId);
                RunInputActionForUniqueIdNextFrame(uniqueId);
                return;
            }

            if (!isSinglePlayer)
                tickSync.AddInputActionToRun(currentTick, uniqueId);
            RunInputActionForUniqueId(uniqueId);
            if (flaggedToContinueNextFrame)
                suspendedInStandaloneIA = true;
        }

        public void OnInputActionSyncPlayerAssigned(VRCPlayerApi player, InputActionSync inputActionSync)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnInputActionSyncPlayerAssigned");
            #endif
            inputActionSyncByPlayerId.Add((uint)player.playerId, inputActionSync);
            if (!player.isLocal)
                return;

            localPlayerDisplayName = player.displayName;

            inputActionSyncForLocalPlayer = inputActionSync;
            SendCustomEventDelayedSeconds(nameof(OnLocalInputActionSyncPlayerAssignedDelayed), 2f);
        }

        public void OnInputActionSyncPlayerUnassigned(VRCPlayerApi player, InputActionSync inputActionSync)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnInputActionSyncPlayerUnassigned");
            #endif
            DataToken playerIdToken = (uint)player.playerId;
            if (!inputActionSyncByPlayerId.Remove(playerIdToken))
                return; // Could already be removed due to client left input action.
            latestInputActionIndexByPlayerId.Add(playerIdToken, inputActionSync.latestInputActionIndex);
        }

        public void OnLocalInputActionSyncPlayerAssignedDelayed()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLocalInputActionSyncPlayerAssignedDelayed");
            #endif
            if (isMaster)
            {
                Debug.Log($"[Lockstep] isMaster is already true 2 seconds after the local player's "
                    + $"InputActionSync script has been assigned... nothing to do here then.");
                return;
            }

            if (Networking.IsMaster)
            {
                // No longer guaranteed to be the first and single client in the instance, therefore run
                // through the usual CheckMasterChange process.
                CheckMasterChange();
                if (isMaster) // If we didn't become master immediately, tell any potential existing master that we exist.
                    return;
            }

            ignoreLocalInputActions = true;
            stillAllowLocalClientJoinedIA = true;
            ignoreIncomingInputActions = false;
            SendClientJoinedIA();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnPlayerLeft - player is null: {player == null}");
            #endif
            uint playerId = (uint)player.playerId;
            if (playerId == localPlayerId)
            {
                isTickPaused = true; // Just sit there, do nothing and wait for the player to finish leaving
                // the world. It is invalid for Lockstep to have 0 clients in its client states game state, so
                // we must ignore the local player leaving.
                return;
            }

            // inputActionSyncForLocalPlayer could still be null while this event is running,
            // but if that's the case, isMaster is false and clientStates is null,
            // so the only function that needs to handle inputActionSyncForLocalPlayer being null
            // is SomeoneLeftWhileWeWereWaitingForLJSync.

            if (isMaster)
            {
                if (masterChangeRequestInProgress && playerId == requestedMasterClientId)
                {
                    SendCancelledMasterChangeIA();
                    sendMasterChangeConfirmationInFirstMutableTick = false; // Prevent sending the confirmation.
                }
                AddToLeftClients(playerId);
                processLeftPlayersSentCount++;
                SendCustomEventDelayedSeconds(nameof(ProcessLeftPlayers), 1f);
                return;
            }

            if (allClientStates == null) // Implies `isWaitingToSendClientJoinedIA || isWaitingForLateJoinerSync`
            {
                if (!isWaitingToSendClientJoinedIA && !isWaitingForLateJoinerSync)
                    Debug.LogError("[Lockstep] clientStates should be impossible to be null when "
                        + "isWaitingToSendClientJoinedIA and isWaitingForLateJoinerSync are both false.");
                // Still waiting for late joiner sync, so who knows,
                // maybe this client will become the new master.
                someoneLeftWhileWeWereWaitingForLJSyncSentCount++;
                SendCustomEventDelayedSeconds(nameof(SomeoneLeftWhileWeWereWaitingForLJSync), 2.5f);
                // Note that the delay should be > than the delay for the call to OnLocalInputActionSyncPlayerAssignedDelayed.
                if (!ArrList.Contains(ref leftClients, ref leftClientsCount, playerId)) // Ignore duplicate left events.
                    ArrList.Add(ref leftClients, ref leftClientsCount, playerId); // Remember everything until we actually get clientStates.
                return;
            }

            AddToLeftClients(playerId);
        }

        private void AddToLeftClients(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AddToLeftClients");
            #endif
            if (!PlayerIdHasClientState(playerId) // If they're not in clientStates then we simply don't care.
                || ArrList.Contains(ref leftClients, ref leftClientsCount, playerId))
                return; // ^ Detect and ignore player left events (aka major trust issues).
            ArrList.Add(ref leftClients, ref leftClientsCount, playerId);
            if (playerId == masterPlayerId)
                SetMasterLeftFlag();
        }

        public void SomeoneLeftWhileWeWereWaitingForLJSync()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SomeoneLeftWhileWeWereWaitingForLJSync");
            #endif
            if ((--someoneLeftWhileWeWereWaitingForLJSyncSentCount) != 0)
                return;

            if (allClientStates == null)
            {
                if (!currentlyNoMaster)
                {
                    Debug.LogError("[Lockstep] currentlyNoMaster should be impossible to "
                        + "be false when clientStates is null. Setting it to true "
                        + "in order to resolve this error state.");
                    currentlyNoMaster = true;
                }

                if (inputActionSyncForLocalPlayer == null)
                {
                    // CheckMasterChange should happen, but the local player needs the sync script first.
                    someoneLeftWhileWeWereWaitingForLJSyncSentCount++;
                    SendCustomEventDelayedSeconds(nameof(SomeoneLeftWhileWeWereWaitingForLJSync), 1f);
                    return;
                }

                // clientStates is still null... so maybe this client should be taking charge.
                CheckMasterChange();

                // Nope, did not take charge, so some other client is not giving us late joiner data.
                if (!isMaster)
                {
                    // Tell that client that we exist.
                    SendClientJoinedIA();
                }
            }
        }

        private void SetMasterLeftFlag()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetMasterLeftFlag");
            #endif
            if (currentlyNoMaster)
                return;
            currentlyNoMaster = true;
            SendCustomEventDelayedSeconds(nameof(CheckMasterChange), 0.2f);
        }

        private void BecomeInitialMaster()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  BecomeInitialMaster");
            #endif
            isMaster = true;
            currentlyNoMaster = false;
            ignoreLocalInputActions = false;
            ignoreIncomingInputActions = false;
            isWaitingToSendClientJoinedIA = false;
            isWaitingForLateJoinerSync = false;
            lateJoinerInputActionSync.lockstepIsWaitingForLateJoinerSync = false;
            isInitialCatchUp = false; // The master never raises a caught up event for itself.
            SetClientStatesToEmpty();
            AddClientState(localPlayerId, ClientState.Master, localPlayerDisplayName);
            ArrList.Clear(ref leftClients, ref leftClientsCount);
            masterPlayerId = localPlayerId;
            // Just to quadruple check, setting owner on both. Trust issues with VRChat.
            Networking.SetOwner(localPlayer, lateJoinerInputActionSync.gameObject);
            Networking.SetOwner(localPlayer, tickSync.gameObject);
            tickSync.RequestSerialization();
            currentTick = 1u; // Start at 1 because tick sync will always be 1 behind, and ticks are unsigned.
            lastRunnableTick = uint.MaxValue;
            EnterSingePlayerMode();
            lockstepIsInitialized = true;
            StartOrStopAutosave();
            RaiseOnInit();
            RaiseOnClientJoined(localPlayerId);
            isTickPaused = false;
            InitAllImportExportOptionsWidgetData();
            tickStartTimeShift = 0f;
            SetTickStartTime();
        }

        private void InitAllImportExportOptionsWidgetData()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  InitAllImportExportOptionsWidgetData");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
            {
                if (gameState.ExportUI != null)
                    gameState.ExportUI.InitWidgetDataInternal();
                if (gameState.ImportUI != null)
                    gameState.ImportUI.InitWidgetDataInternal();
            }
        }

        /// <summary>
        /// <para>Can and is supposed to be called on all clients.</para>
        /// </summary>
        private void SetMaster(uint newMasterId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetMaster - newMasterId: {newMasterId}");
            #endif

            if (masterPlayerId == newMasterId)
            {
                Debug.LogError("[Lockstep] SetMaster being called with the same player id as the current "
                    + "master indicates that something went wrong somewhere.");
                return;
            }

            if (localPlayerId == masterPlayerId)
            {
                StopBeingMaster();
                UpdateClientStatesForNewMaster(newMasterId);
            }
            else if (localPlayerId == newMasterId)
                BecomeNewMaster(); // Calls UpdateClientStatesForNewMaster in the middle.
            else
                UpdateClientStatesForNewMaster(newMasterId);
        }

        private void StopBeingMaster()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StopBeingMaster");
            #endif
            masterChangeRequestInProgress = false;
            isMaster = false;
            // This runs at the start of a tick. That tick should not actually get run though, so stop right there.
            lastRunnableTick = currentTick - 1u;
            // Actually end the last tick this master ran and allow other clients to run said tick.
            tickSync.lastRunnableTick = lastRunnableTick;
            tickSync.stopAfterThisSync = true;
            // tickStartTime will be a bit off (too high) since ticks won't run for a short bit.
        }

        private void UpdateClientStatesForNewMaster(uint newMasterId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  UpdateClientStatesForNewMaster");
            #endif

            bool oldMasterHasAState =  PlayerIdHasClientState(masterPlayerId);
            if (!oldMasterHasAState)
            {
                Debug.LogError($"[Lockstep] The system is supposed to guarantee that there is always 1 client "
                    + "in client states which has the master state. However UpdateClientStatesForNewMaster "
                    + "noticed that the previous master id is no longer in the client states.");
            }

            uint oldMasterPlayerId = masterPlayerId;
            SetMasterPlayerId(newMasterId);

            // If a master change happened through any means, abort master change request if it is in progress.
            masterChangeRequestInProgress = false;
            masterRequestManagingMasterId = 0u; // Reset just to be clean, not actually required.
            requestedMasterClientId = 0u; // Reset just to be clean, not actually required.
            sendMasterChangeConfirmationInFirstMutableTick = false;
            finishMasterChangeProcessAtStartOfTick = false;

            someoneIsAskingForMasterCandidates = false;
            acceptForcedCandidate = false;
            StopWaitingForCandidates();

            if (oldMasterHasAState)
                SetClientState(oldMasterPlayerId, ClientState.Normal);
            SetClientState(masterPlayerId, ClientState.Master);
            RaiseOnMasterClientChanged(oldMasterPlayerId);
        }

        private void SetMasterPlayerId(uint newMasterId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetMasterPlayerId");
            #endif
            masterPlayerId = newMasterId;
            if (ArrList.Contains(ref leftClients, ref leftClientsCount, masterPlayerId))
                SetMasterLeftFlag();
            else
                currentlyNoMaster = false;
        }

        private void CheckIfRequestedMasterClientLeft(uint leftClientId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckIfRequestedMasterClientLeft");
            #endif
            if (!masterChangeRequestInProgress || leftClientId != requestedMasterClientId)
                return;
            CompletelyCancelMasterChangeRequest();
        }

        public override bool RequestLocalClientToBecomeMaster()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RequestToBecomeMaster");
            #endif
            return SendMasterChangeRequestIA(localPlayerId);
        }

        public override bool SendMasterChangeRequestIA(uint newMasterClientId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendMasterChangeRequestIA - newMasterClientId: {newMasterClientId}");
            #endif
            if (!lockstepIsInitialized || newMasterClientId == masterPlayerId || masterChangeRequestInProgress)
                return false;
            if (!TryGetClientState(newMasterClientId, out ClientState clientState))
            {
                Debug.LogError("[Lockstep] Attempt to send master request with a client id that is not in "
                    + "the client states game state.");
                return false;
            }
            if (clientState != ClientState.Normal)
                return false;

            WriteSmallUInt(newMasterClientId);
            SendInputAction(masterChangeRequestIAId);
            return true;
        }

        [SerializeField] [HideInInspector] private uint masterChangeRequestIAId;
        [LockstepInputAction(nameof(masterChangeRequestIAId))]
        public void OnMasterChangeRequestIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnMasterChangeRequestIA");
            #endif
            if (masterChangeRequestInProgress) // Already in process, ignore another request.
                return;
            requestedMasterClientId = ReadSmallUInt();
            if (!PlayerIdHasClientState(requestedMasterClientId)) // The client left already, ignore it.
            { // Cannot use leftClients for a check here since that is not part of the game state.
                requestedMasterClientId = 0u; // Just to be clean.
                return;
            }
            if (requestedMasterClientId == masterPlayerId) // Multiple requests were sent by the same player
                return; // in quick succession, ignore the duplicates.
            masterChangeRequestInProgress = true;
            masterRequestManagingMasterId = masterPlayerId;

            if (!isMaster)
                return;
            if (ArrList.Contains(ref leftClients, ref leftClientsCount, requestedMasterClientId))
            {
                SendCancelledMasterChangeIA();
                return;
            }
            sendMasterChangeConfirmationInFirstMutableTick = true;
        }

        private void SendConfirmedMasterChangeIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendConfirmedMasterChangeIA");
            #endif
            SendInputAction(confirmedMasterChangeIAId);
        }

        [SerializeField] [HideInInspector] private uint confirmedMasterChangeIAId;
        [LockstepInputAction(nameof(confirmedMasterChangeIAId))]
        public void OnConfirmedMasterChangeIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnConfirmedMasterChangeIA");
            #endif
            if (!PlayerIdHasClientState(requestedMasterClientId)) // The client left already, cannot change master.
            { // Cannot use leftClients for a check here since that is not part of the game state.
                CompletelyCancelMasterChangeRequest();
                return;
            }
            finishMasterChangeProcessAtStartOfTick = true;
        }

        private void SendCancelledMasterChangeIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendCancelledMasterChangeIA");
            #endif
            SendInputAction(cancelledMasterChangeIAId);
        }

        [SerializeField] [HideInInspector] private uint cancelledMasterChangeIAId;
        [LockstepInputAction(nameof(cancelledMasterChangeIAId))]
        public void OnCancelledMasterChangeIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnCancelledMasterChangeIA");
            #endif
            CompletelyCancelMasterChangeRequest();
        }

        /// <summary>
        /// <para>Modifies the game state.</para>
        /// <para>Also resets <see cref="sendMasterChangeConfirmationInFirstMutableTick"/> even though that is
        /// not part of the game state as it'll only matter on the master.</para>
        /// </summary>
        private void CompletelyCancelMasterChangeRequest()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CompletelyCancelMasterChangeRequest");
            #endif
            masterChangeRequestInProgress = false;
            masterRequestManagingMasterId = 0u;
            requestedMasterClientId = 0u;
            finishMasterChangeProcessAtStartOfTick = false;
            sendMasterChangeConfirmationInFirstMutableTick = false; // Only matters if this is the master client.
        }

        private DataDictionary GetLeftClientsLut()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetLeftClientsLut");
            #endif
            DataDictionary lut = new DataDictionary();
            for (int i = 0; i < leftClientsCount; i++)
                lut.Add(leftClients[i], true);
            return lut;
        }

        private bool IsAnyClientWaitingForLateJoinerSync()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  IsAnyClientWaitingForLateJoinerSync");
            #endif
            DataDictionary leftClientsLut = GetLeftClientsLut();
            for (int i = 0; i < allClientStatesCount; i++)
                if (allClientStates[i] == ClientState.WaitingForLateJoinerSync
                    && !leftClientsLut.ContainsKey(allClientIds[i]))
                    return true;
            return false;
        }

        private void FactoryReset()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FactoryReset");
            #endif
            if (IsProcessingLJGameStates || lockstepIsInitialized)
            {
                Debug.LogError("[Lockstep] It is invalid to call FactoryReset once IsProcessingLJGameStates or "
                    + "lockstepIsInitialized is true, since that implies that OnInit or game state "
                    + "deserialization has already happened. After either of those points in time factory "
                    + "resetting would mean the system would perform one of those 2 things again, which goes "
                    + "against the specification of this lockstep implementation. (While technically "
                    + "IsProcessingLJGameStates can be true without any game state deserialization having "
                    + "happened, it still does not make sense to factory reset in this case.)");
                return;
            }
            if (isMaster)
            {
                Debug.LogError("[Lockstep] Impossible because when isMaster is true, "
                    + "lockstepIsInitialized is also true which was checked previously.");
                return;
            }
            // isSinglePlayer is only ever true when isMaster is true, therefore no need to check nor reset.

            // Only ever used (on the master) once lockstepIsInitialized is true.
            // byteCountForLatestLJSync = -1;

            // Not needed to call CompletelyCancelMasterChangeRequest since it would require input actions to
            // run before FactoryReset gets called which itself would be invalid.

            tickSync.ClearInputActionsToRun();
            ForgetAboutUnprocessedLJSerializedGameSates();
            ForgetAboutLeftPlayers();
            ForgetAboutInputActionsWaitingToBeSent();
            SetClientStatesToNull();
            firstMutableTick = 0u; // Technically not needed, but it makes a lot of sense to be here.
            latestInputActionIndexByPlayerIdForLJ = null;
            currentlyNoMaster = true;
            StopWaitingForCandidates();
            someoneIsAskingForMasterCandidates = false;
            acceptForcedCandidate = false;
            // The times where ignoreLocalInputActions would be false, FactoryReset should no longer get called.
            stillAllowLocalClientJoinedIA = false;
            ignoreIncomingInputActions = true;
            isWaitingToSendClientJoinedIA = true;
            isWaitingForLateJoinerSync = false;
            lateJoinerInputActionSync.lockstepIsWaitingForLateJoinerSync = false;
            inputActionsByUniqueId.Clear();
            uniqueIdsByTick.Clear();
            isTickPaused = true;
        }

        private bool CouldTakeOverMaster()
        {
            return !(isWaitingToSendClientJoinedIA || isWaitingForLateJoinerSync);
        }

        public void CheckMasterChange()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckMasterChange");
            #endif

            if (inputActionSyncForLocalPlayer == null)
            {
                // This can happen when on ownership transferred runs on LockstepTickSync,
                // in which case nothing needs to happen here, because
                // inputActionSyncForLocalPlayer still being null is handled by OnPlayerLeft
                // which will eventually call CheckMasterChange, if needed.
                return;
            }

            if (isMaster || !currentlyNoMaster || !Networking.IsMaster)
                return;

            if (IsProcessingLJGameStates)
            {
                checkMasterChangeAfterProcessingLJGameStates = true;
                return;
            }

            if (!CouldTakeOverMaster())
            {
                // The master left before finishing sending late joiner data and we are now the new VRC master
                // without all the data. There is no longer a guarantee that the new VRC master (which is the
                // local client in this case) is the one which has been in the instance the longest. Therefore
                // we must now ask all existing clients to tell us if they have the previous game state. If
                // yes then they should become master, otherwise a factory reset is required.
                // Cannot look through clientStates and ask an already known client to become master, because
                // that introduces race conditions where ultimately multiple clients could become master at
                // the same time (for example when this client instantly leaves after telling another one to
                // become master, and then the new VRC instance master also becomes master along side the one
                // that was told to become master by the previous VRC instance master.)
                AskForBetterMasterCandidate();
                return;
            }

            if (someoneIsAskingForMasterCandidates)
            {
                // We have no way of knowing if that other client had sent a confirmation to some client which
                // we may not have received yet. Waiting is not an option since that is a race condition.
                // So the only thing we can do is talk with every single client in the instance before
                // anybody - including the local client - may become master.
                AskForBetterMasterCandidate();
                return;
            }

            BecomeNewMaster();
        }

        private void BecomeNewMaster()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  BecomeNewMaster - currentTick: {currentTick}, lastRunnableTick: {lastRunnableTick}");
            #endif

            isMaster = true; // currentlyNoMaster will be set to false in OnMasterClientChangedIA later.
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            ignoreIncomingInputActions = false;
            tickStartTimeShift = 0f;

            // If this client has never run ticks before then currentTick will still be 0,
            // while lastRunnableTick will never be 0 here.
            // If currentTick is past lastRunnableTick (which it will only ever be by 1) then this client is
            // fully caught up with whatever the last tick the previous master had run was.
            bool instantlyBecomeMaster = currentTick > lastRunnableTick;

            if (instantlyBecomeMaster)
                FinishCatchingUpOnMaster(); // We weren't actually catching up but the logic is the same.
            else
            {
                isCatchingUp = true; // Catch up as quickly as possible to the lastRunnableTick. Unless it was
                // already catching up, this should usually only quickly advance by 1 or 2 ticks, which is fine.
                // The real reason this is required is for the public IsMaster property to behave correctly.
                // Leave isInitialCatchUp untouched, as this may in fact still be the initial catch up, if
                // isCatchingUp was already true.
                SetDilatedTickStartTime();
            }
            isTickPaused = false;

            // The immutable tick prevents any newly enqueued input actions from being enqueued too early,
            // to prevent desyncs when not in single player as well as poor IA ordering in general.
            // AssociateUnassociatedInputActionsWithTicks also requires currentTick to be <= lastRunnableTick.
            firstMutableTick = currentTick;

            Networking.SetOwner(localPlayer, lateJoinerInputActionSync.gameObject);
            Networking.SetOwner(localPlayer, tickSync.gameObject);
            tickSync.RequestSerialization();

            AssociateUnassociatedInputActionsWithTicks();

            if (instantlyBecomeMaster)
                UpdateClientStatesForNewMaster(localPlayerId);
            else
            {
                SendMasterChangedIA(); // Do it here to have it happen in the first mutable tick, which is the
                // first tick the previous master didn't get to run anymore.
                // And process left players afterwards which guarantees that there is always 1 master in the
                // client states.
            }

            if (masterChangeRequestInProgress)
                SendCancelledMasterChangeIA();

            processLeftPlayersSentCount++;
            ProcessLeftPlayers();
            if (isSinglePlayer) // In case it was already single player before BecomeNewMaster ran.
                InstantlyRunInputActionsWaitingToBeSent();
        }

        private void StartWaitingForCandidatesLoop()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartWaitingForCandidatesLoop");
            #endif
            if (waitingForCandidatesLoopRunning)
                return;
            waitingForCandidatesLoopRunning = true;
            // 2 seconds should be more than enough for a full roundtrip to every client.
            SendCustomEventDelayedSeconds(nameof(WaitingForCandidatesLoop), 2f);
        }

        public void WaitingForCandidatesLoop()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  WaitingForCandidatesLoop");
            #endif
            if (!isAskingForMasterCandidates)
            {
                waitingForCandidatesLoopRunning = false;
                return;
            }

            DataDictionary toKeep = new DataDictionary();
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            foreach (VRCPlayerApi player in players)
                if (player != null && player.IsValid() // Severe trust issues.
                    && notYetRespondedCandidates.ContainsKey(player.playerId))
                    toKeep.Add(player.playerId, true);
            notYetRespondedCandidates = toKeep;

            if (notYetRespondedCandidates.Count == 0) // Even still continue running this loop, until a candidate
                AllCandidatesHaveResponded(); // actually becomes master. This handles poorly timed leaves.

            SendCustomEventDelayedSeconds(nameof(WaitingForCandidatesLoop), 2f);
        }

        private bool TryGetMasterCandidate(out uint candidateClientId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  TryGetMasterCandidate");
            #endif
            for (int i = 0; i < acceptingCandidatesCount; i++)
            {
                uint candidateId = acceptingCandidates[i];
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById((int)candidateId);
                if (player != null && player.IsValid()) // Trust issues.
                {
                    candidateClientId = candidateId;
                    return true;
                }
            }
            candidateClientId = 0u;
            return false;
        }

        private void AskForBetterMasterCandidate()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AskForBetterMasterCandidate");
            #endif

            if (TryGetMasterCandidate(out uint candidateClientId))
            {
                StartWaitingForCandidatesLoop();
                // Oh hey the local client had already asked previously and there's still a potential
                // candidate in the instance, ask that client to become master.
                SendAcceptedMasterCandidateIA(candidateClientId, force: false);
                return;
            }

            if (isAskingForMasterCandidates)
                return;

            // Even if someone else was already asking, we cannot take over the process due to race conditions,
            // so just restart from the beginning.
            someoneIsAskingForMasterCandidates = false;
            acceptForcedCandidate = false;

            isAskingForMasterCandidates = true;
            notYetRespondedCandidates = new DataDictionary();
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players); // Includes local player.
            foreach (VRCPlayerApi player in players)
                if (player != null && player.IsValid()) // Severe trust issues.
                    notYetRespondedCandidates.Add((uint)player.playerId, true);
            ArrList.Clear(ref acceptingCandidates, ref acceptingCandidatesCount);

            StartWaitingForCandidatesLoop();
            SendAskForBetterMasterCandidateIA();
        }

        private void SendAskForBetterMasterCandidateIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendAskForBetterMasterCandidateIA");
            #endif
            // If someone was already asking for candidates when we became instance master, then the local
            // client may in fact be able to become master and yet still have to go through this process.
            SendInstantAction(askForBetterMasterCandidateIAId, doRunLocally: true);
        }

        [SerializeField] [HideInInspector] private uint askForBetterMasterCandidateIAId;
        [LockstepInputAction(nameof(askForBetterMasterCandidateIAId))]
        public void OnAskForBetterMasterCandidateIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnAskForBetterMasterCandidateIA");
            #endif
            if (isAskingForMasterCandidates && sendingPlayerId != localPlayerId)
            {
                Debug.LogError("[Lockstep] Impossible because for this to happen there would have to be 2 "
                    + "VRChat instance masters simultaneously.");
                return;
            }
            someoneIsAskingForMasterCandidates = true;
            clientIdAskingForCandidates = sendingPlayerId;
            SendResponseForBetterMasterCandidateIA(sendingPlayerId, CouldTakeOverMaster());
        }

        private void SendResponseForBetterMasterCandidateIA(uint askingPlayerIdRoundtrip, bool couldBecomeMaster)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendResponseForBetterMasterCandidateIA");
            #endif
            WriteSmallUInt(askingPlayerIdRoundtrip);
            WriteByte((byte)(isMaster ? 2u : couldBecomeMaster ? 1u : 0u));
            SendInstantAction(responseForBetterMasterCandidateIAId, doRunLocally: askingPlayerIdRoundtrip == localPlayerId);
        }

        [SerializeField] [HideInInspector] private uint responseForBetterMasterCandidateIAId;
        [LockstepInputAction(nameof(responseForBetterMasterCandidateIAId))]
        public void OnResponseForBetterMasterCandidateIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnResponseForBetterMasterCandidateIA");
            #endif
            uint askingPlayerIdRoundtrip = ReadSmallUInt();
            byte couldBecomeMaster = ReadByte();
            if (!isAskingForMasterCandidates)
                return;
            if (askingPlayerIdRoundtrip != localPlayerId) // A different player asked, we don't care.
                return;
            if (couldBecomeMaster == 2u)
            {
                StopWaitingForCandidates();
                // Tell all the other clients that we stopped asking about it. This approach prevents race
                // conditions since both the question and the stop action would be sent by the same IA sync script.
                SendStoppedAskingForCandidatesThanksToExistingMasterIA();
                return;
            }
            if (!notYetRespondedCandidates.ContainsKey(sendingPlayerId))
                return; // Uh what? I guess the remote player left already and we're receiving this afterwards.
            // Or this is a new client that joined right after we sent the question/request.

            notYetRespondedCandidates.Remove(sendingPlayerId);
            if (couldBecomeMaster != 0u)
                ArrList.Add(ref acceptingCandidates, ref acceptingCandidatesCount, sendingPlayerId);
            if (notYetRespondedCandidates.Count != 0)
                return;
            AllCandidatesHaveResponded();
        }

        private void AllCandidatesHaveResponded(bool force = false)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AllCandidatesHaveResponded");
            #endif

            if (!TryGetMasterCandidate(out uint candidateClientId))
            {
                FactoryReset();
                BecomeInitialMaster();
                return;
            }

            SendAcceptedMasterCandidateIA(candidateClientId, force);
        }

        private void StopWaitingForCandidates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StopWaitingForCandidatesAsAMasterAlreadyExists");
            #endif

            isAskingForMasterCandidates = false;
            notYetRespondedCandidates = null;
            // Not clearing acceptingCandidates as it could be reused later.
        }

        private void SendStoppedAskingForCandidatesThanksToExistingMasterIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendStoppedAskingForCandidatesThanksToExistingMasterIA");
            #endif
            SendInstantAction(sendStoppedAskingForCandidatesThanksToExistingMasterIAId, doRunLocally: true);
        }

        [SerializeField] [HideInInspector] private uint sendStoppedAskingForCandidatesThanksToExistingMasterIAId;
        [LockstepInputAction(nameof(sendStoppedAskingForCandidatesThanksToExistingMasterIAId))]
        public void OnStoppedAskingForCandidatesThanksToExistingMasterIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnStoppedAskingForCandidatesThanksToExistingMasterIA");
            #endif
            if (sendingPlayerId != clientIdAskingForCandidates) // If they don't match then more players have
                return; // left and another one is asking for candidates, which may also mean that the
            // lockstep master had left while this currently running IA was in transit.
            someoneIsAskingForMasterCandidates = false;
            acceptForcedCandidate = false;
        }

        private void SendAcceptedMasterCandidateIA(uint acceptedPlayerId, bool force)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendAcceptedMasterCandidateIA");
            #endif
            WriteSmallUInt(acceptedPlayerId);
            WriteFlags(force);
            SendInstantAction(acceptedMasterCandidateIAId, doRunLocally: true);
        }

        [SerializeField] [HideInInspector] private uint acceptedMasterCandidateIAId;
        [LockstepInputAction(nameof(acceptedMasterCandidateIAId))]
        public void OnAcceptedMasterCandidateIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnAcceptedMasterCandidateIA");
            #endif
            uint acceptedPlayerId = ReadSmallUInt();
            ReadFlags(out bool force);
            if (acceptedPlayerId != localPlayerId
                || !isMaster // This client was already asked to become master previously, ignore another confirmation.
                || !currentlyNoMaster) // A different client already became master, or this client is getting
            { // asked too early - before getting a player left event - so it cannot take master yet.
                acceptForcedCandidate = false;
                return;
            }
            if (clientIdAskingForCandidates != sendingPlayerId
                && (!force || !acceptForcedCandidate || sendingPlayerId != acceptForcedCandidateFromPlayerId))
            {
                acceptForcedCandidate = true;
                acceptForcedCandidateFromPlayerId = sendingPlayerId;
                SendRefusedMasterCandidateConfirmationIA(sendingPlayerId);
                return;
            }
            acceptForcedCandidate = false;
            BecomeNewMaster();
        }

        private void SendRefusedMasterCandidateConfirmationIA(uint askingPlayerIdRoundtrip)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendRefusedMasterCandidateConfirmationIA");
            #endif
            WriteSmallUInt(askingPlayerIdRoundtrip);
            SendInstantAction(refusedMasterCandidateConfirmationIAId, doRunLocally: true);
        }

        [SerializeField] [HideInInspector] private uint refusedMasterCandidateConfirmationIAId;
        [LockstepInputAction(nameof(refusedMasterCandidateConfirmationIAId))]
        public void OnRefusedMasterCandidateConfirmationIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnRefusedMasterCandidateConfirmationIA");
            #endif
            uint askingPlayerIdRoundtrip = ReadSmallUInt();
            if (askingPlayerIdRoundtrip != localPlayerId
                || !isAskingForMasterCandidates
                || notYetRespondedCandidates.Count != 0)
                return; // Whichever client just refused a confirmation, we don't care about it.
            AllCandidatesHaveResponded(force: true); // Resend confirmation to the client we actually did accept.
            // (Which may not actually be the client which just refused either, mind you.)
        }

        private void FinishCatchingUpOnMaster()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FinishCatchingUpOnMaster");
            #endif
            lastRunnableTick = uint.MaxValue;

            if (IsAnyClientWaitingForLateJoinerSync())
            {
                flagForLateJoinerSyncSentCount++;
                FlagForLateJoinerSync();
            }
        }

        private void AssociateUnassociatedInputActionsWithTicks()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AssociateUnassociatedInputActionsWithTicks");
            #endif
            if (!isMaster)
            {
                Debug.LogError("[Lockstep] Attempt to use AssociateUnassociatedInputActionsWithTicks on non master.");
                return;
            }
            if (currentTick > firstMutableTick)
            {
                Debug.LogError("[Lockstep] For simplicity AssociateUnassociatedInputActionsWithTicks is only "
                    + "allowed to be called when the current tick is less than or equal to the first mutable "
                    + "tick. The main reason is that there is currently a guarantee that "
                    + "inputActionsByUniqueId is empty on the master as soon as the first mutable tick is "
                    + "passed. Therefore associating input actions with ticks once they are mutable would "
                    + $"break this guarantee. currentTick: {currentTick}, firstMutableTick: {firstMutableTick}.");
                return;
            }

            DataDictionary associatedUniqueIds = new DataDictionary();
            DataList uniqueIdLists = uniqueIdsByTick.GetValues();
            int count = uniqueIdLists.Count;
            for (int i = 0; i < count; i++)
                foreach (ulong uniqueId in ((ulong[])uniqueIdLists[i].Reference))
                    associatedUniqueIds.Add(uniqueId, true);

            // Must exclude those which are still waiting to be sent by the local client,
            // otherwise they'll end up being associated with a tick twice.
            inputActionSyncForLocalPlayer.AddUniqueIdsWaitingToBeSentToHashSet(associatedUniqueIds);

            count = inputActionsByUniqueId.Count;
            if (count == associatedUniqueIds.Count) // Every input action is already associated with a tick.
                return;
            DataList uniqueIds = inputActionsByUniqueId.GetKeys();
            // Must use DataList because System.Array.Sort is not exposed... and this is faster than manually
            // implementing insert sort, because Udon is slow.
            DataList unassociatedUniqueIds = new DataList();
            int unassociatedCount = count - associatedUniqueIds.Count;
            if (unassociatedCount > unassociatedUniqueIds.Capacity)
                unassociatedUniqueIds.Capacity = unassociatedCount;
            for (int i = 0; i < count; i++)
            {
                ulong uniqueId = uniqueIds[i].ULong;
                if (associatedUniqueIds.ContainsKey(uniqueId))
                    continue;
                unassociatedUniqueIds.Add(uniqueId);
            }
            // Must be sorted because we cannot rely on the iteration order of the DataDictionary, however the
            // input actions with lower index must come first as they were sent first, and that is part of
            // the specification of lockstep. By simply sorting the entire array it does mean that players
            // with lower player id will get their input actions associated and run earlier, but that is fine.
            unassociatedUniqueIds.Sort();

            for (int i = 0; i < unassociatedCount; i++)
            {
                ulong uniqueId = unassociatedUniqueIds[i].ULong;
                AssociateInputActionWithTickOnMaster(firstMutableTick, uniqueId);
            }
        }

        private void InstantlyRunInputActionsWaitingToBeSent()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  InstantlyRunInputActionsWaitingToBeSent");
            #endif
            // It doesn't really make sense to call RunInputActionsForThisFrame because that has no guarantee
            // to actually clear the list of input actions to be run this frame. So calling it vs not makes
            // no difference, aside from running some input actions right now rather than in the next Update
            // loop.
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: true);
        }

        private void ForgetAboutInputActionsWaitingToBeSent()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ForgetAboutInputActionsWaitingToBeSent");
            #endif
            ForgetAboutInputActionsForThisFrame();
            inputActionSyncForLocalPlayer.DequeueEverything(doCallback: false);
        }

        public void ProcessLeftPlayers()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ProcessLeftPlayers");
            #endif
            if ((--processLeftPlayersSentCount) != 0)
                return;
            if (!isMaster)
                return;

            CheckSingePlayerModeChange();

            // Must be a backwards loop, because SendClientLeftIA may ultimately remove the player id from
            // the leftClients list. Specifically when only the master is left in the instance, causing input
            // actions to be run instantly, and the OnClientLeftIA handler removes the player id.
            for (int i = leftClientsCount - 1; i >= 0; i--)
                SendClientLeftIA(leftClients[i]);
            ArrList.Clear(ref leftClients, ref leftClientsCount);
        }

        private void ForgetAboutLeftPlayers()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ForgetAboutLeftPlayers");
            #endif
            ArrList.Clear(ref leftClients, ref leftClientsCount);
        }

        private void CheckSingePlayerModeChange()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckSingePlayerModeChange");
            #endif
            bool shouldBeSinglePlayer = allClientStatesCount - leftClientsCount <= 1;
            if (isSinglePlayer == shouldBeSinglePlayer)
                return;
            if (shouldBeSinglePlayer)
                EnterSingePlayerMode();
            else
                ExitSinglePlayerMode();
        }

        private void EnterSingePlayerMode()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  EnterSingePlayerMode");
            #endif
            isSinglePlayer = true;
            tickSync.isSinglePlayer = true;
            lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            InstantlyRunInputActionsWaitingToBeSent();
            tickSync.ClearInputActionsToRun();
        }

        private void ExitSinglePlayerMode()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ExitSinglePlayerMode");
            #endif
            isSinglePlayer = false;
            tickSync.isSinglePlayer = false;
            // (0u, 0uL) Indicate that any input actions associated with ticks
            // before this client became master should be dropped. This isn't
            // useful for the true initial master, but if a client reset the instance
            // because the master left before sending late joiner data finished,
            // then this is required to get rid of any old lingering input actions.
            // The system being MP, turning SP and then MP again is also no problem
            // for this here, as turning MP means a new client joined, so nothing
            // would actually get dropped.
            // And this must be done through the tick sync script, not using an input
            // action, in order to avoid race conditions, because those would
            // absolutely happen when using an input action to clear tick associations.
            tickSync.AddInputActionToRun(0u, 0uL);
            tickSync.RequestSerialization(); // Restart the tick sync loop.
        }

        private void SendMasterChangedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendMasterChangedIA");
            #endif
            SendInputAction(masterChangedIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint masterChangedIAId;
        [LockstepInputAction(nameof(masterChangedIAId))]
        public void OnMasterClientChangedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnMasterClientChangedIA");
            #endif
            UpdateClientStatesForNewMaster(sendingPlayerId);
        }

        private void SendClientJoinedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientJoinedIA");
            #endif
            if (inputActionSyncForLocalPlayer == null)
            {
                Debug.LogError("[Lockstep] Impossible, the inputActionSyncForLocalPlayer is null inside of "
                    + "SendClientJoinedIA. All code paths leading to SendClientJoinedIA are supposed to "
                    + "prevent this from happening.");
                return;
            }
            isWaitingToSendClientJoinedIA = false;
            isWaitingForLateJoinerSync = true;
            lateJoinerInputActionSync.lockstepIsWaitingForLateJoinerSync = true;
            SetClientStatesToNull(); // To know if this client actually received all data, first to last.
            currentlyNoMaster = true; // When clientStates is null, this must be true.
            latestInputActionIndexByPlayerIdForLJ = null;
            WriteString(localPlayerDisplayName);
            SendInputAction(clientJoinedIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint clientJoinedIAId;
        [LockstepInputAction(nameof(clientJoinedIAId))]
        public void OnClientJoinedIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientJoinedIA");
            #endif
            string playerName = ReadString();
            DataToken keyToken = sendingPlayerId;
            if (TryGetClientState(sendingPlayerId, out ClientState currentState))
            {
                if (currentState != ClientState.WaitingForLateJoinerSync)
                    return;
            }
            else
                AddClientState(sendingPlayerId, ClientState.WaitingForLateJoinerSync, playerName);

            if (isMaster)
            {
                CheckSingePlayerModeChange();
                clientsJoinedInTheLastFiveMinutes++;
                SendCustomEventDelayedSeconds(nameof(PlayerJoinedFiveMinutesAgo), 300f);
                flagForLateJoinerSyncSentCount++;
                float lateJoinerSyncDelay = Mathf.Min(8f, 2.5f + 0.5f * (float)clientsJoinedInTheLastFiveMinutes);
                SendCustomEventDelayedSeconds(nameof(FlagForLateJoinerSync), lateJoinerSyncDelay);
            }

            RaiseOnPreClientJoined(sendingPlayerId);
        }

        public void PlayerJoinedFiveMinutesAgo()
        {
            clientsJoinedInTheLastFiveMinutes--;
        }

        public void FlagForLateJoinerSync()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FlagForLateJoinerSync");
            #endif
            if ((--flagForLateJoinerSyncSentCount) != 0)
                return;

            // If isMaster && isCatchingUp, this actually does _not_ need special handling, because
            // sendLateJoinerDataAtEndOfTick is only checked after catching up is done.
            if (IsAnyClientWaitingForLateJoinerSync())
                sendLateJoinerDataAtStartOfTick = true;
        }

        private int Clamp(int value, int min, int max)
        {
            return System.Math.Min(max, System.Math.Max(min, value));
        }

        private uint GetLatestInputActionIndex(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetLatestInputActionIndex - playerId: {playerId}");
            #endif
            DataToken playerIdToken = playerId;
            if (inputActionSyncByPlayerId.TryGetValue(playerIdToken, out DataToken inputActionSyncToken))
                return ((InputActionSync)inputActionSyncToken.Reference).latestInputActionIndex;
            // If inputActionSyncByPlayerId doesn't contain it, this this is guaranteed to contain it.
            return latestInputActionIndexByPlayerId[playerIdToken].UInt;
        }

        private void SendLateJoinerData()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendLateJoinerData");
            #endif
            if (flaggedToContinueNextFrame)
                suspendedInLJSerialization = false;
            else
            {
                if (isImporting)
                {
                    Debug.LogError("[Lockstep] Attempt to SendLateJoinerData while an import is still going on "
                        + "which if it was supported would be complete waste of networking bandwidth. So it isn't "
                        + "supported, and this call to SendLateJoinerData is ignored.");
                    return;
                }
                if (lateJoinerInputActionSync.QueuedBytesCount >= Clamp(byteCountForLatestLJSync / 2, 2048, 2048 * 5))
                    lateJoinerInputActionSync.DequeueEverything(doCallback: false);

                SendLateJoinerInternalGameStatesIA();
            }

            SendLateJoinerCustomGameStatesIAs();
            if (flaggedToContinueNextFrame)
                return;

            SendLateJoinerCurrentTickIA();

            byteCountForLatestLJSync = lateJoinerInputActionSync.QueuedBytesCount;
        }

        private void SendLateJoinerInternalGameStatesIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendLateJoinerInternalGameStatesIA");
            #endif
            // Client states game state.
            WriteSmallUInt((uint)allClientStatesCount);
            for (int i = 0; i < allClientStatesCount; i++)
            {
                uint playerId = allClientIds[i];
                WriteSmallUInt(playerId);
                WriteByte((byte)allClientStates[i]);
                WriteString(allClientNames[i]);
                WriteSmallUInt(GetLatestInputActionIndex(playerId));
            }

            // Singleton input actions game state.
            WriteSmallUInt(nextSingletonId);
            int count = singletonInputActions.Count;
            WriteSmallUInt((uint)count);
            DataList keys = singletonInputActions.GetKeys();
            for (int i = 0; i < count; i++)
            {
                DataToken keyToken = keys[i];
                WriteSmallUInt(keyToken.UInt); // singletonId
                object[] inputActionData = (object[])singletonInputActions[keyToken].Reference;
                WriteSmallUInt((uint)inputActionData[0]); // inputActionId
                byte[] singletonInputActionData = (byte[])inputActionData[1]; // singletonInputActionData
                WriteSmallUInt((uint)singletonInputActionData.Length);
                WriteBytes(singletonInputActionData);
                WriteFlags((bool)inputActionData[2]); // requiresTimeTracking
                WriteSmallUInt((uint)inputActionData[3]); // sendTick
            }

            // Delayed events game state.
            count = delayedEventsByTick.Count;
            WriteSmallUInt((uint)count);
            keys = delayedEventsByTick.GetKeys();
            for (int i = 0; i < count; i++)
            {
                DataToken keyToken = keys[i];
                WriteSmallUInt(keyToken.UInt); // tick
                DataList eventDataList = delayedEventsByTick[keyToken].DataList;
                int eventsCount = eventDataList.Count;
                WriteSmallUInt((uint)eventsCount);
                for (int j = 0; j < eventsCount; j++)
                {
                    object[] eventData = (object[])eventDataList[i].Reference;
                    WriteSmallUInt((uint)eventData[0]); // inputActionId
                    byte[] inputActionData = (byte[])eventData[1]; // inputActionData
                    WriteSmallUInt((uint)inputActionData.Length);
                    WriteBytes(inputActionData);
                }
            }

            lateJoinerInputActionSync.SendInputAction(LJInternalGameStatesIAId, writeStream, writeStreamSize);
            ResetWriteStream();
        }

        private void SendLateJoinerCustomGameStatesIAs()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendLateJoinerCustomGameStatesIAs");
            #endif
            for (int i = suspendedIndexInArray; i < allGameStatesCount; i++)
            {
                #if LockstepDebug
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                #endif
                byte[] unsuspendedWriteStream = null;
                if (flaggedToContinueNextFrame)
                {
                    flaggedToContinueNextFrame = false;
                    unsuspendedWriteStream = writeStream;
                    writeStream = suspendedWriteStream;
                    writeStreamSize = suspendedWriteStreamSize;
                    suspendedWriteStream = null;
                    suspendedWriteStreamSize = 0;
                    suspendedIndexInArray = 0;
                    isContinuationFromPrevFrame = true;
                }
                allGameStates[i].SerializeGameState(false, null);
                isContinuationFromPrevFrame = false;
                if (flaggedToContinueNextFrame)
                {
                    suspendedInLJSerialization = true;
                    suspendedWriteStream = writeStream;
                    suspendedWriteStreamSize = writeStreamSize;
                    suspendedIndexInArray = i;
                    writeStream = unsuspendedWriteStream ?? new byte[MinWriteStreamCapacity];
                    ResetWriteStream();
                    return;
                }
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] [sw] Lockstep  SendLateJoinerData (inner) - serialize GS ms: {sw.Elapsed.TotalMilliseconds}, GS internal name: {allGameStates[i].GameStateInternalName}, writeStreamSize: {writeStreamSize}");
                #endif
                lateJoinerInputActionSync.SendInputAction(LJFirstCustomGameStateIAId + (uint)i, writeStream, writeStreamSize);
                ResetWriteStream();
            }
        }

        private void SendLateJoinerCurrentTickIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendLateJoinerCurrentTickIA");
            #endif
            WriteSmallUInt(currentTick);
            lateJoinerInputActionSync.SendInputAction(LJCurrentTickIAId, writeStream, writeStreamSize);
            ResetWriteStream();
        }

        private void OnLJInternalGameStatesIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLJClientStatesIA");
            #endif
            // If this client was already receiving data, but then it restarted from
            // the beginning, forget about everything that's been received so far.
            ForgetAboutUnprocessedLJSerializedGameSates();

            // Client states game state.
            SetClientStatesToEmpty();
            latestInputActionIndexByPlayerIdForLJ = new DataDictionary();
            int count = (int)ReadSmallUInt();
            for (int i = 0; i < count; i++)
            {
                uint playerId = ReadSmallUInt();
                ClientState clientState = (ClientState)ReadByte();
                string clientName = ReadString();
                uint latestInputActionIndex = ReadSmallUInt();
                ArrList.Add(ref allClientIds, ref allClientIdsCount, playerId);
                ArrList.Add(ref allClientStates, ref allClientStatesCount, clientState);
                ArrList.Add(ref allClientNames, ref allClientNamesCount, clientName);
                latestInputActionIndexByPlayerIdForLJ.Add(playerId, latestInputActionIndex);
                if (clientState == ClientState.Master) // clientStates always has exactly 1 Master.
                    SetMasterPlayerId(playerId);
            }

            RemoveClientsFromLeftClientsWhichAreNotInClientStates();

            // Singleton input actions game state.
            singletonInputActions.Clear();
            nextSingletonId = ReadSmallUInt();
            count = (int)ReadSmallUInt();
            for (int i = 0; i < count; i++)
            {
                uint singletonId = ReadSmallUInt();
                uint inputActionId = ReadSmallUInt();
                byte[] singletonInputActionData = ReadBytes((int)ReadSmallUInt());
                ReadFlags(out bool requiresTimeTracking);
                uint sendTick = ReadSmallUInt();
                singletonInputActions.Add(singletonId, new DataToken(new object[]
                {
                    inputActionId,
                    singletonInputActionData,
                    requiresTimeTracking,
                    sendTick,
                    float.NaN, // Don't know the tick start time yet, cannot calculate the local send time.
                }));
            }

            // Delayed events game state.
            delayedEventsByTick.Clear();
            count = (int)ReadSmallUInt();
            for (int i = 0; i < count; i++)
            {
                DataList eventDataList = new DataList();
                uint tick = ReadSmallUInt();
                int eventsCount = (int)ReadSmallUInt();
                for (int j = 0; j < eventsCount; j++)
                {
                    uint inputActionId = ReadSmallUInt();
                    byte[] inputActionData = ReadBytes((int)ReadSmallUInt());
                    eventDataList.Add(new DataToken(new object[] { inputActionId, inputActionData }));
                }
                delayedEventsByTick.Add(tick, eventDataList);
            }
        }

        private void RemoveClientsFromLeftClientsWhichAreNotInClientStates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RemoveClientsFromLeftClientsWhichAreNotInClientStates");
            #endif
            int i = 0;
            int newI = 0;
            while (i < leftClientsCount)
            {
                if (PlayerIdHasClientState(leftClients[i]))
                    leftClients[newI++] = leftClients[i];
                else
                    leftClientsCount--;
                i++;
            }
        }

        private void OnLJCustomGameStateIA(uint inputActionId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLJCustomGameStateIA - allClientStates is null: {allClientStates == null}");
            #endif
            if (allClientStates == null) // This data was not meant for this client. Continue waiting.
                return;

            if (inputActionId - LJFirstCustomGameStateIAId != (uint)unprocessedLJSerializedGSCount)
            {
                Debug.LogError($"[Lockstep] Expected game state index {unprocessedLJSerializedGSCount}, "
                    + $"got {inputActionId - LJFirstCustomGameStateIAId}. Either some math "
                    + $"is wrong or the game states are somehow out of order.");
                return;
            }
            ArrList.Add(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount, readStream);
        }

        private void OnLJCurrentTickIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnLJCurrentTickIA - allClientStates is null: {allClientStates == null}");
            #endif
            if (allClientStates == null) // This data was not meant for this client. Continue waiting.
            {
                SendCustomEventDelayedSeconds(nameof(AskForLateJoinerSyncAgain), 2.5f);
                return;
            }

            currentTick = ReadSmallUInt();

            isWaitingForLateJoinerSync = false;
            lateJoinerInputActionSync.lockstepIsWaitingForLateJoinerSync = false;
            TryMoveToNextLJSerializedGameState();
        }

        public void AskForLateJoinerSyncAgain()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AskForLateJoinerSyncAgain");
            #endif
            // Does not need to keep track of the amount of time this has been raised to only run for the last
            // one, because even after the first time it was sent, by the time this runs the condition below
            // should already be true. And if it isn't, no new late joiner data has been received, so it it
            // should be impossible for this event to have been sent twice. But even if that were to happen,
            // late joiner sync initialization has a 5 second delay on the master, so it doesn't really
            // matter.
            if (allClientStates != null)
                return;

            Debug.LogWarning($"[Lockstep] The master has not sent another set of late joiner data for 2.5 seconds "
                + "since the last set finished, however this client is still waiting on that data. This "
                + "should be impossible because the master keeps track of joined clients, however "
                + "through mysterious means the first input action for late joiner sync may have been "
                + "lost to the ether, through means unknown to me. Therefore this client is asking the "
                + "master to send late joiner data again.");
            SendClientJoinedIA();
        }

        private void TryMoveToNextLJSerializedGameState()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  TryMoveToNextLJSerializedGameState");
            #endif
            nextLJGameStateToProcess++;
            nextLJGameStateToProcessTime = Time.realtimeSinceStartup + LJGameStateProcessingFrequency;
            if (nextLJGameStateToProcess >= unprocessedLJSerializedGSCount)
                DoneProcessingLJGameStates();
        }

        private void ProcessNextLJSerializedGameState()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ProcessNextLJSerializedGameState");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            #endif
            int gameStateIndex = nextLJGameStateToProcess;
            if (!flaggedToContinueNextFrame)
                SetReadStream(unprocessedLJSerializedGameStates[gameStateIndex]);
            else
            {
                flaggedToContinueNextFrame = false;
                isContinuationFromPrevFrame = true;
            }
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ProcessNextLJSerializedGameState (inner) - readStream.Length: {readStream.Length}, readStreamPosition: {readStreamPosition}, GS internal name: {allGameStates[gameStateIndex].GameStateInternalName}");
            #endif
            // Specifically explicitly leaving inGameStateSafeEvent as false.
            // Modification of game states is not allowed, must only restoring the exact state that's been serialized.
            SetDilatedTickStartTime(); // Right before DeserializeGameState.
            string errorMessage = allGameStates[gameStateIndex].DeserializeGameState(false, 0u, null);
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] [sw] Lockstep  ProcessNextLJSerializedGameState (inner) - deserialize GS ms: {sw.Elapsed.TotalMilliseconds}, GS internal name: {allGameStates[gameStateIndex].GameStateInternalName}");
            #endif
            isContinuationFromPrevFrame = false;
            if (flaggedToContinueNextFrame)
                return;
            if (errorMessage != null)
                RaiseOnLockstepNotification($"Receiving late joiner data for '{allGameStates[gameStateIndex].GameStateDisplayName}' resulted in an error:\n{errorMessage}");
            LockstepGameStateOptionsUI exportUI = allGameStates[gameStateIndex].ExportUI;
            LockstepGameStateOptionsUI importUI = allGameStates[gameStateIndex].ImportUI;
            if (exportUI != null)
                exportUI.InitWidgetDataInternal();
            if (importUI != null)
                importUI.InitWidgetDataInternal();
            TryMoveToNextLJSerializedGameState();
        }

        private void ForgetAboutUnprocessedLJSerializedGameSates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ForgetAboutUnprocessedLJSerializedGameSates");
            #endif
            nextLJGameStateToProcess = -1;
            checkMasterChangeAfterProcessingLJGameStates = false;
            ArrList.Clear(ref unprocessedLJSerializedGameStates, ref unprocessedLJSerializedGSCount);
        }

        private void CleanUpOldInputActions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CleanUpOldInputActions");
            #endif
            if (latestInputActionIndexByPlayerIdForLJ == null)
            {
                Debug.Log("[Lockstep] Impossible, latestInputActionIndexByPlayerIdForLJ is null inside of "
                    + "CleanUpOldInputActions. The function should only ever be called once, which happens "
                    + "after latestInputActionIndexByPlayerIdForLJ got populated.");
                return;
            }
            DataDictionary uniqueIdsForUnknownPlayers = new DataDictionary();
            DataDictionary shouldKeepIAsForUnknownPlayers = new DataDictionary();

            DataList uniqueIds = inputActionsByUniqueId.GetKeys();
            int count = uniqueIds.Count;
            for (int i = 0; i < count; i++)
            {
                ulong uniqueId = uniqueIds[i].ULong;
                uint playerId = (uint)(uniqueId >> PlayerIdKeyShift);
                uint inputActionIndex = (uint)(uniqueId & InputActionIndexBits);
                if (latestInputActionIndexByPlayerIdForLJ.TryGetValue(playerId, out DataToken latestToken))
                {
                    // Forget about input actions that are older than the latest input action index for a
                    // given player. Must use <= because the latest index is the latest one that has already
                    // been run by the master. Therefore it must not run anymore, it already modified the game
                    // state which got serialized and sent to this client.
                    if (inputActionIndex <= latestToken.UInt)
                        inputActionsByUniqueId.Remove(uniqueId);
                    continue;
                }
                // At this point the given player id for the input action does not exist in the client states
                // game state. It may either no longer exist or it might not yet exist.
                // If it no longer exists then the input actions for the given player can all be removed.
                // If the player does not yet exist in the client states game state then there must be a
                // client joined input action for the given player id that will be run during catch up.
                // There may also be multiple client joined input actions, which is very unlikely to happen
                // while catching up, however if it were to happen while catching up it would be because of
                // master changes during catching up, which means all input actions sent by the joined client
                // - including the multiple joined client input actions - must be kept, because the new master
                // would end up running all of them. This should be guaranteed.
                // TL;DR: if this point is reached, if there is any client joined input action for the given
                // player id then all their input actions must be kept. If not, they must all be forgotten
                // about.
                if (!uniqueIdsForUnknownPlayers.TryGetValue(playerId, out DataToken listToken))
                {
                    listToken = new DataList();
                    uniqueIdsForUnknownPlayers.Add(playerId, listToken);
                }
                listToken.DataList.Add(uniqueId);

                uint inputActionId = (uint)(((object[])inputActionsByUniqueId[uniqueId].Reference)[0]);
                if (inputActionId == clientJoinedIAId)
                    shouldKeepIAsForUnknownPlayers.SetValue(playerId, true);
            }

            DataList playerIds = uniqueIdsForUnknownPlayers.GetKeys();
            count = playerIds.Count;
            for (int i = 0; i < count; i++)
            {
                DataToken playerIdToken = playerIds[i];
                if (shouldKeepIAsForUnknownPlayers.ContainsKey(playerIdToken))
                    continue;
                DataList uniqueIdsToForget = uniqueIdsForUnknownPlayers[playerIdToken].DataList;
                for (int j = 0; j < uniqueIdsToForget.Count; j++)
                    inputActionsByUniqueId.Remove(uniqueIdsToForget[j]);
            }

            latestInputActionIndexByPlayerIdForLJ = null;
        }

        private void DoneProcessingLJGameStates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  DoneProcessingLJGameStates");
            #endif
            bool doCheckMasterChange = checkMasterChangeAfterProcessingLJGameStates;
            ForgetAboutUnprocessedLJSerializedGameSates();
            CleanUpOldInputActions();
            ignoreLocalInputActions = false;
            stillAllowLocalClientJoinedIA = false;
            SendClientGotLateJoinerDataIA(); // Must be before OnClientBeginCatchUp, because that can also send input actions.
            lockstepIsInitialized = true; // Close before OnClientBeginCatchUp.
            SetDilatedTickStartTime(); // Right before OnClientBeginCatchUp.
            RaiseOnClientBeginCatchUp(localPlayerId);
            isTickPaused = false;
            isCatchingUp = true;
            isInitialCatchUp = true;

            if (doCheckMasterChange)
                CheckMasterChange();
        }

        private void CheckIfLateJoinerSyncShouldStop()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckIfLateJoinerSyncShouldStop");
            #endif
            if (isMaster && !IsAnyClientWaitingForLateJoinerSync())
            {
                sendLateJoinerDataAtStartOfTick = false;
                lateJoinerInputActionSync.DequeueEverything(doCallback: false);
            }
        }

        private void SendClientGotLateJoinerDataIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientGotLateJoinerDataIA");
            #endif
            SendInputAction(clientGotLateJoinerDataIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint clientGotLateJoinerDataIAId;
        [LockstepInputAction(nameof(clientGotLateJoinerDataIAId))]
        public void OnClientGotLateJoinerDataIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientGotLateJoinerDataIA");
            #endif
            if (GetClientStateUnsafe(sendingPlayerId) == ClientState.Master)
                Debug.LogError("[Lockstep] Impossible, a client started catching up while already being master, "
                    + "however the got LJ data IA gets sent before the master change check happens.");
            SetClientState(sendingPlayerId, ClientState.CatchingUp);
            CheckIfLateJoinerSyncShouldStop();
            RaiseOnClientJoined(sendingPlayerId);
        }

        private void SendClientLeftIA(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientLeftIA");
            #endif
            WriteSmallUInt(playerId);
            // One of the few internal input actions which forces a frame delay. For two reasons:
            // To process left players in the order in which they actually left. If the input action were to
            // run instantly then it would modify the left clients array, breaking the loop which is sending
            // these left client input actions.
            SendInputAction(clientLeftIAId, forceOneFrameDelay: true);
        }

        [SerializeField] [HideInInspector] private uint clientLeftIAId;
        [LockstepInputAction(nameof(clientLeftIAId))]
        public void OnClientLeftIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientLeftIA");
            #endif
            uint playerId = ReadSmallUInt();
            CheckIfImportingPlayerLeft(playerId); // Can raise an event, must therefore happen before removing the client state.
            RemoveClientState(playerId, out ClientState clientState, out string playerName);
            // leftClients may not contain playerId, and that is fine.
            ArrList.Remove(ref leftClients, ref leftClientsCount, playerId);
            // Only one of the following is going to still contain data for the given player id. No need to
            // check for which one it is though, because all that needs to happen is removal.
            DataToken keyToken = playerId;
            inputActionSyncByPlayerId.Remove(keyToken);
            latestInputActionIndexByPlayerId.Remove(keyToken);

            if (clientState == ClientState.Master)
                Debug.LogError("[Lockstep] Impossible, OnClientLeftIA got run for the master client. When "
                    + "the master leaves a new client should have taken the master state before this IA ran.");

            CheckIfLateJoinerSyncShouldStop();
            CheckIfSingletonInputActionGotDropped(playerId);
            CheckIfRequestedMasterClientLeft(playerId);
            leftClientName = playerName;
            RaiseOnClientLeft(playerId);
            leftClientName = null;
        }

        private void SendClientCaughtUpIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendClientCaughtUpIA");
            #endif
            WriteFlags(isInitialCatchUp); // `doRaise`.
            SendInputAction(clientCaughtUpIAId, forceOneFrameDelay: false);
        }

        [SerializeField] [HideInInspector] private uint clientCaughtUpIAId;
        [LockstepInputAction(nameof(clientCaughtUpIAId))]
        public void OnClientCaughtUpIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnClientCaughtUpIA");
            #endif
            ReadFlags(out bool doRaise);
            if (GetClientStateUnsafe(sendingPlayerId) != ClientState.Master)
                SetClientState(sendingPlayerId, ClientState.Normal);
            if (doRaise)
                RaiseOnClientCaughtUp(sendingPlayerId);
        }

        /// <summary>
        /// <para>These must not (and do not) use <see cref="FlagToContinueNextFrame"/>.</para>
        /// </summary>
        /// <param name="inputActionId"></param>
        /// <returns></returns>
        private bool IsInstantActionIAId(uint inputActionId)
        {
            return inputActionId == askForBetterMasterCandidateIAId
                || inputActionId == responseForBetterMasterCandidateIAId
                || inputActionId == acceptedMasterCandidateIAId;
        }

        public void ReceivedInputAction(bool isLateJoinerSync, uint inputActionId, ulong uniqueId, float sendTime, byte[] inputActionData)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ReceivedInputAction - isLateJoinerSync: {isLateJoinerSync}, inputActionId: {inputActionId}, uniqueId: 0x{uniqueId:x16}{(isLateJoinerSync ? "" : $", event name {(inputActionId < inputActionHandlerEventNames.Length ? inputActionHandlerEventNames[inputActionId] : "<id/index out of bounds>")}")}");
            #endif
            if (isLateJoinerSync)
            {
                if (!isWaitingForLateJoinerSync)
                    return;
                ResetReadStream();
                readStream = inputActionData;
                if (inputActionId == LJInternalGameStatesIAId)
                    OnLJInternalGameStatesIA();
                else if (inputActionId == LJCurrentTickIAId)
                    OnLJCurrentTickIA();
                else
                    OnLJCustomGameStateIA(inputActionId);
                return;
            }

            if (IsInstantActionIAId(inputActionId))
            {
                RunInputAction(inputActionId, inputActionData, uniqueId, SendTimeForNonTimedIAs, bypassValidityCheck: true);
                return;
            }

            if (ignoreIncomingInputActions)
                return;

            if (isMaster)
                TryToInstantlyRunInputActionOnMaster(inputActionId, uniqueId, sendTime, inputActionData);
            else
                inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData, sendTime }));
        }

        private ulong TryToInstantlyRunInputActionOnMaster(uint inputActionId, ulong uniqueId, float sendTime, byte[] inputActionData, bool forceOneFrameDelay = false)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  TryToInstantlyRunInputActionOnMaster");
            #endif
            if (currentTick < firstMutableTick
                || (currentTick == firstMutableTick && !disallowAssociatingWithCurrentTick))
            {
                // Can't run it in the current tick. A check for isCatchingUp is not needed,
                // because the condition above will always be true when isCatchingUp is true.
                if (uniqueId == 0uL)
                {
                    // It'll only be 0 if the local player is the one trying to instantly run it.
                    // Received data always has a unique id.
                    uniqueId = inputActionSyncForLocalPlayer.MakeUniqueId();
                }
                inputActionsByUniqueId.Add(uniqueId, new DataToken(new object[] { inputActionId, inputActionData, sendTime }));
                AssociateInputActionWithTickOnMaster(firstMutableTick, uniqueId);
                return uniqueId;
            }

            if (!isSinglePlayer)
            {
                if (uniqueId == 0uL)
                {
                    Debug.LogError("[Lockstep] Impossible, the uniqueId when instantly running an input action "
                        + "on master cannot be 0 while not in single player, because every input action "
                        + "gets sent over the network and gets a unique id assigned in the process. "
                        + "Something is very wrong in the code. Ignoring this action.");
                    return 0uL;
                }
                tickSync.AddInputActionToRun(currentTick, uniqueId);
            }
            else if (uniqueId == 0uL) // In single player, do make a unique id.
                uniqueId = inputActionSyncForLocalPlayer.MakeUniqueId();

            if (forceOneFrameDelay || flaggedToContinueNextFrame)
                ArrList.Add(ref inputActionsToRunNextFrame, ref iatrnCount, new object[] { inputActionId, inputActionData, uniqueId, sendTime });
            else
            {
                RunInputAction(inputActionId, inputActionData, uniqueId, sendTime);
                if (flaggedToContinueNextFrame)
                    suspendedInStandaloneIA = true;
            }
            return uniqueId;
        }

        private void ClearUniqueIdsByTick()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ClearUniqueIdsByTick");
            #endif
            // When only clearing uniqueIdsByTick we can't just clear inputActionsByUniqueId,
            // because that may contain very new input actions which have not been associated
            // with a tick yet, so they'd be associated with future ticks which means we must
            // keep them alive. The below logic just removes the ones we know for certain that
            // they can be removed, just to free up some memory.
            DataList allValues = uniqueIdsByTick.GetValues();
            for (int i = 0; i < allValues.Count; i++)
                foreach (ulong uniqueId in (ulong[])allValues[i].Reference)
                    inputActionsByUniqueId.Remove(uniqueId);
            uniqueIdsByTick.Clear();
        }

        public void AssociateIncomingInputActionWithTick(uint tickToRunIn, ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AssociateIncomingInputActionWithTick - tickToRunIn: {tickToRunIn}, uniqueId: 0x{uniqueId:x16}");
            #endif
            if (tickToRunIn == 0u && uniqueId == 0uL)
            {
                ClearUniqueIdsByTick();
                return;
            }

            if (ignoreIncomingInputActions)
                return;
            if (isMaster)
            {
                Debug.LogWarning("[Lockstep] The master client (which is this client) should "
                    + "not be receiving data about running an input action at a tick...");
            }

            AssociateInputActionWithTick(tickToRunIn, uniqueId);
        }

        private void AssociateInputActionWithTickOnMaster(uint tickToRunIn, ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AssociateInputActionWithTickOnMaster - tickToRunIn: {tickToRunIn}, uniqueId: 0x{uniqueId:x16}");
            #endif
            AssociateInputActionWithTick(tickToRunIn, uniqueId);
            if (!isSinglePlayer)
                tickSync.AddInputActionToRun(tickToRunIn, uniqueId);
        }

        private void AssociateInputActionWithTick(uint tickToRunIn, ulong uniqueId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AssociateInputActionWithTick - tickToRunIn: {tickToRunIn}, uniqueId: 0x{uniqueId:x16}");
            #endif
            // Mark the input action to run at the given tick.
            DataToken tickToRunInToken = new DataToken(tickToRunIn);
            if (uniqueIdsByTick.TryGetValue(tickToRunInToken, out DataToken uniqueIdsToken))
            {
                ulong[] uniqueIds = (ulong[])uniqueIdsToken.Reference;
                int oldLength = uniqueIds.Length;
                ulong[] newUniqueIds = new ulong[oldLength + 1];
                uniqueIds.CopyTo(newUniqueIds, 0);
                newUniqueIds[oldLength] = uniqueId;
                uniqueIdsByTick.SetValue(tickToRunInToken, new DataToken(newUniqueIds));
                return;
            }
            uniqueIdsByTick.Add(tickToRunInToken, new DataToken(new ulong[] { uniqueId }));
        }

        private UdonSharpBehaviour[] CleanUpRemovedListeners(UdonSharpBehaviour[] listeners, int destroyedCount, string eventName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CleanUpRemovedListeners");
            #endif
            Debug.LogError($"[Lockstep] An event listener for {eventName} has been destroyed at runtime. "
                + "Lockstep event listeners must not be destroyed in order to guarantee deterministic behavior.");
            int length = listeners.Length;
            UdonSharpBehaviour[] newListeners = new UdonSharpBehaviour[length - destroyedCount];
            int j = 0;
            for (int i = 0; i < length; i++)
            {
                UdonSharpBehaviour listener = listeners[i];
                if (listener != null)
                    newListeners[j++] = listener;
            }
            return newListeners;
        }

        private void RaiseOnInit() // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnInit");
            #endif
            inGameStateSafeEvent = true; // Calling function is not an IA.
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onInitListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnInit));
            if (destroyedCount != 0)
                onInitListeners = CleanUpRemovedListeners(onInitListeners, destroyedCount, nameof(LockstepEventType.OnInit));
            inGameStateSafeEvent = false;
        }

        private void RaiseOnClientBeginCatchUp(uint catchingUpPlayerId) // Not game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientBeginCatchUp");
            #endif
            this.catchingUpPlayerId = catchingUpPlayerId;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onClientBeginCatchUpListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnClientBeginCatchUp));
            if (destroyedCount != 0)
                onClientBeginCatchUpListeners = CleanUpRemovedListeners(onClientBeginCatchUpListeners, destroyedCount, nameof(LockstepEventType.OnClientBeginCatchUp));
            this.catchingUpPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        private void RaiseOnPreClientJoined(uint joinedPlayerId) // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnPreClientJoined");
            #endif
            this.joinedPlayerId = joinedPlayerId;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onPreClientJoinedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnPreClientJoined));
            if (destroyedCount != 0)
                onPreClientJoinedListeners = CleanUpRemovedListeners(onPreClientJoinedListeners, destroyedCount, nameof(LockstepEventType.OnPreClientJoined));
            this.joinedPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        private void RaiseOnClientJoined(uint joinedPlayerId) // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientJoined");
            #endif
            inGameStateSafeEvent = true; // Calling function is potentially not an IA.
            this.joinedPlayerId = joinedPlayerId;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onClientJoinedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnClientJoined));
            if (destroyedCount != 0)
                onClientJoinedListeners = CleanUpRemovedListeners(onClientJoinedListeners, destroyedCount, nameof(LockstepEventType.OnClientJoined));
            this.joinedPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
            inGameStateSafeEvent = false;
        }

        private void RaiseOnClientCaughtUp(uint catchingUpPlayerId) // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientCaughtUp");
            #endif
            this.catchingUpPlayerId = catchingUpPlayerId;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onClientCaughtUpListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnClientCaughtUp));
            if (destroyedCount != 0)
                onClientCaughtUpListeners = CleanUpRemovedListeners(onClientCaughtUpListeners, destroyedCount, nameof(LockstepEventType.OnClientCaughtUp));
            this.catchingUpPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        private void RaiseOnClientLeft(uint leftPlayerId) // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnClientLeft");
            #endif
            this.leftPlayerId = leftPlayerId;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onClientLeftListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnClientLeft));
            if (destroyedCount != 0)
                onClientLeftListeners = CleanUpRemovedListeners(onClientLeftListeners, destroyedCount, nameof(LockstepEventType.OnClientLeft));
            this.leftPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
        }

        /// <summary>
        /// <para>Make sure to set <see cref="masterPlayerId"/> before raising this event.</para>
        /// </summary>
        private void RaiseOnMasterClientChanged(uint oldMasterPlayerId) // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnMasterClientChanged");
            #endif
            inGameStateSafeEvent = true; // Calling function is potentially not an IA.
            this.oldMasterPlayerId = oldMasterPlayerId;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onMasterClientChangedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnMasterClientChanged));
            if (destroyedCount != 0)
                onMasterClientChangedListeners = CleanUpRemovedListeners(onMasterClientChangedListeners, destroyedCount, nameof(LockstepEventType.OnMasterClientChanged));
            this.oldMasterPlayerId = 0u; // To prevent misuse of the API which would cause desyncs.
            inGameStateSafeEvent = false;
        }

        private void RaiseOnTick() // Game state safe.
        {
            // #if LockstepDebug
            // Debug.Log($"[LockstepDebug] Lockstep  RaiseOnTick");
            // #endif
            inGameStateSafeEvent = true; // Calling function is not an IA.
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onLockstepTickListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnLockstepTick));
            if (destroyedCount != 0)
                onLockstepTickListeners = CleanUpRemovedListeners(onLockstepTickListeners, destroyedCount, nameof(LockstepEventType.OnLockstepTick));
            inGameStateSafeEvent = false;
        }

        private void RaiseOnExportStart() // Not game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnExportStart");
            #endif
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onExportStartListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnExportStart));
            if (destroyedCount != 0)
                onExportStartListeners = CleanUpRemovedListeners(onExportStartListeners, destroyedCount, nameof(LockstepEventType.OnExportStart));
        }

        private void RaiseOnExportFinished() // Not game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnExportFinished");
            #endif
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onExportFinishedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnExportFinished));
            if (destroyedCount != 0)
                onExportFinishedListeners = CleanUpRemovedListeners(onExportFinishedListeners, destroyedCount, nameof(LockstepEventType.OnExportFinished));
        }

        private void RaiseOnImportStart() // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnImportStart");
            #endif
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onImportStartListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnImportStart));
            if (destroyedCount != 0)
                onImportStartListeners = CleanUpRemovedListeners(onImportStartListeners, destroyedCount, nameof(LockstepEventType.OnImportStart));
        }

        private void RaiseOnImportOptionsDeserialized() // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnImportOptionsDeserialized");
            #endif
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onImportOptionsDeserializedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnImportOptionsDeserialized));
            if (destroyedCount != 0)
                onImportOptionsDeserializedListeners = CleanUpRemovedListeners(onImportOptionsDeserializedListeners, destroyedCount, nameof(LockstepEventType.OnImportOptionsDeserialized));
        }

        private void RaiseOnImportedGameState() // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnImportedGameState");
            #endif
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onImportedGameStateListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnImportedGameState));
            if (destroyedCount != 0)
                onImportedGameStateListeners = CleanUpRemovedListeners(onImportedGameStateListeners, destroyedCount, nameof(LockstepEventType.OnImportedGameState));
        }

        private void RaiseOnImportFinished() // Game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnImportFinished");
            #endif
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onImportFinishedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnImportFinished));
            if (destroyedCount != 0)
                onImportFinishedListeners = CleanUpRemovedListeners(onImportFinishedListeners, destroyedCount, nameof(LockstepEventType.OnImportFinished));
        }

        private bool markedForOnExportOptionsForAutosaveChanged;
        private void MarkForOnExportOptionsForAutosaveChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  MarkForOnExportOptionsForAutosaveChanged");
            #endif
            if (markedForOnExportOptionsForAutosaveChanged)
                return;
            markedForOnExportOptionsForAutosaveChanged = true;
            SendCustomEventDelayedFrames(nameof(RaiseOnExportOptionsForAutosaveChanged), 1);
        }

        public void RaiseOnExportOptionsForAutosaveChanged() // Not game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnExportOptionsForAutosaveChanged");
            #endif
            markedForOnExportOptionsForAutosaveChanged = false;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onExportOptionsForAutosaveChangedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnExportOptionsForAutosaveChanged));
            if (destroyedCount != 0)
                onExportOptionsForAutosaveChangedListeners = CleanUpRemovedListeners(onExportOptionsForAutosaveChangedListeners, destroyedCount, nameof(LockstepEventType.OnExportOptionsForAutosaveChanged));
        }

        private bool markedForOnAutosaveIntervalSecondsChanged;
        private void MarkForOnAutosaveIntervalSecondsChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  MarkForOnAutosaveIntervalSecondsChanged");
            #endif
            if (markedForOnAutosaveIntervalSecondsChanged)
                return;
            markedForOnAutosaveIntervalSecondsChanged = true;
            SendCustomEventDelayedFrames(nameof(RaiseOnAutosaveIntervalSecondsChanged), 1);
        }

        public void RaiseOnAutosaveIntervalSecondsChanged() // Not game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnAutosaveIntervalSecondsChanged");
            #endif
            markedForOnAutosaveIntervalSecondsChanged = false;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onAutosaveIntervalSecondsChangedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnAutosaveIntervalSecondsChanged));
            if (destroyedCount != 0)
                onAutosaveIntervalSecondsChangedListeners = CleanUpRemovedListeners(onAutosaveIntervalSecondsChangedListeners, destroyedCount, nameof(LockstepEventType.OnAutosaveIntervalSecondsChanged));
        }

        private bool markedForOnIsAutosavePausedChanged;
        private void MarkForOnIsAutosavePausedChanged()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  MarkForOnIsAutosavePausedChanged");
            #endif
            if (markedForOnIsAutosavePausedChanged)
                return;
            markedForOnIsAutosavePausedChanged = true;
            SendCustomEventDelayedFrames(nameof(RaiseOnIsAutosavePausedChanged), 1);
        }

        public void RaiseOnIsAutosavePausedChanged() // Not game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnIsAutosavePausedChanged");
            #endif
            markedForOnIsAutosavePausedChanged = false;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onIsAutosavePausedChangedListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnIsAutosavePausedChanged));
            if (destroyedCount != 0)
                onIsAutosavePausedChangedListeners = CleanUpRemovedListeners(onIsAutosavePausedChangedListeners, destroyedCount, nameof(LockstepEventType.OnIsAutosavePausedChanged));
        }

        public void RaiseOnLockstepNotification(string message) // Not game state safe.
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  RaiseOnLockstepNotification");
            #endif
            notificationMessage = message;
            int destroyedCount = 0;
            foreach (UdonSharpBehaviour listener in onLockstepNotificationListeners)
                if (listener == null)
                    destroyedCount++;
                else
                    listener.SendCustomEvent(nameof(LockstepEventType.OnLockstepNotification));
            if (destroyedCount != 0)
                onLockstepNotificationListeners = CleanUpRemovedListeners(onLockstepNotificationListeners, destroyedCount, nameof(LockstepEventType.OnLockstepNotification));
            notificationMessage = null;
        }

        public override string GetDisplayName(uint playerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetDisplayName - playerId: {playerId}");
            #endif
            int index = ArrList.BinarySearch(ref allClientIds, ref allClientIdsCount, playerId);
            if (index >= 0)
                return allClientNames[index];
            if (leftClientName != null && playerId == leftPlayerId)
                return leftClientName; // We are inside of the OnClientLeft event.
            Debug.LogError("[Lockstep] Attempt to call GetDisplayName with a playerId which is not currently "
                + "part of the game state. This is indication of misuse of the API, make sure to fix this.");
            return null;
        }

        private const int MinWriteStreamCapacity = 256;
        private byte[] writeStream = new byte[MinWriteStreamCapacity];
        private int writeStreamSize = 0;
        private byte[] serializedSizeBuffer = new byte[5];

        public override void ShiftWriteStream(int sourcePosition, int destinationPosition, int count)
        {
            ArrList.EnsureCapacity(ref writeStream, destinationPosition + count);
            System.Buffer.BlockCopy(writeStream, sourcePosition, writeStream, destinationPosition, count);
        }

        public override void ResetWriteStream() => writeStreamSize = 0;
        public override int WriteStreamPosition { get => writeStreamSize; set => writeStreamSize = value; }
        public override void WriteSByte(sbyte value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteByte(byte value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteFlags(bool flag1) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)(flag1 ? 1 : 0));
        public override void WriteFlags(bool flag1, bool flag2) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)((flag1 ? 1 : 0) | (flag2 ? 2 : 0)));
        public override void WriteFlags(bool flag1, bool flag2, bool flag3) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)((flag1 ? 1 : 0) | (flag2 ? 2 : 0) | (flag3 ? 4 : 0)));
        public override void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)((flag1 ? 1 : 0) | (flag2 ? 2 : 0) | (flag3 ? 4 : 0) | (flag4 ? 8 : 0)));
        public override void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)((flag1 ? 1 : 0) | (flag2 ? 2 : 0) | (flag3 ? 4 : 0) | (flag4 ? 8 : 0) | (flag5 ? 16 : 0)));
        public override void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5, bool flag6) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)((flag1 ? 1 : 0) | (flag2 ? 2 : 0) | (flag3 ? 4 : 0) | (flag4 ? 8 : 0) | (flag5 ? 16 : 0) | (flag6 ? 32 : 0)));
        public override void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5, bool flag6, bool flag7) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)((flag1 ? 1 : 0) | (flag2 ? 2 : 0) | (flag3 ? 4 : 0) | (flag4 ? 8 : 0) | (flag5 ? 16 : 0) | (flag6 ? 32 : 0) | (flag7 ? 64 : 0)));
        public override void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5, bool flag6, bool flag7, bool flag8) => DataStream.Write(ref writeStream, ref writeStreamSize, (byte)((flag1 ? 1 : 0) | (flag2 ? 2 : 0) | (flag3 ? 4 : 0) | (flag4 ? 8 : 0) | (flag5 ? 16 : 0) | (flag6 ? 32 : 0) | (flag7 ? 64 : 0) | (flag8 ? 128 : 0)));
        public override void WriteShort(short value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteUShort(ushort value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteInt(int value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteUInt(uint value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteLong(long value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteULong(ulong value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteFloat(float value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteDouble(double value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteDecimal(decimal value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteVector2(Vector2 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteVector3(Vector3 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteVector4(Vector4 value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteQuaternion(Quaternion value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteChar(char value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteString(string value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        public override void WriteDateTime(System.DateTime value) => DataStream.Write(ref writeStream, ref writeStreamSize, value);
        [RecursiveMethod]
        public override void WriteCustomNullableClass(SerializableWannaBeClass instance) => WriteCustomNullableClass(instance, isSerializingForExport);
        [RecursiveMethod]
        public override void WriteCustomNullableClass(SerializableWannaBeClass instance, bool isExport)
        {
            if (WriteCustomNullableClassPreprocess(instance, isExport)) // Keep the body of the RecursiveMethod small, because it is slow.
                return;
            WriteCustomClass(instance, isExport);
        }
        private bool WriteCustomNullableClassPreprocess(SerializableWannaBeClass instance, bool isExport)
        {
            if (instance == null)
            {
                if (isExport)
                    WriteSmallUInt(0u);
                else
                    WriteByte(0);
                return true;
            }
            if (!isExport)
                WriteByte(1);
            return false;
        }
        [RecursiveMethod]
        public override void WriteCustomClass(SerializableWannaBeClass instance) => WriteCustomClass(instance, isSerializingForExport);
        [RecursiveMethod]
        public override void WriteCustomClass(SerializableWannaBeClass instance, bool isExport)
        {
            if (!WriteCustomClassPreprocess(instance, isExport)) // Keep the body of the RecursiveMethod small, because it is slow.
                return;
            int startPosition = writeStreamSize;
            // Shift by one preemptively since the majority of classes will serialize in less than 128 bytes,
            // saving the ShiftWriteStream call in most cases.
            writeStreamSize++;
            instance.Serialize(isExport: true);
            WriteCustomClassPostprocess(startPosition); // Keep the body of the RecursiveMethod small, because it is slow.
        }
        private bool WriteCustomClassPreprocess(SerializableWannaBeClass instance, bool isExport)
        {
            if (instance == null)
                Debug.LogError($"[Lockstep] Attempt to WriteCustomClass where instance is null. "
                    + "This is invalid and will throw an exception.");
            if (!isExport)
            {
                instance.Serialize(isExport: false);
                return false;
            }
            if (!instance.SupportsImportExport)
            {
                WriteSmallUInt(0u);
                return false;
            }
            WriteSmallUInt(instance.DataVersion + 1u);
            return true;
        }
        private void WriteCustomClassPostprocess(int startPosition)
        {
            int customDataSize = writeStreamSize - startPosition - 1;
            int sizeSize = 0;
            DataStream.WriteSmall(ref serializedSizeBuffer, ref sizeSize, (uint)customDataSize);
            writeStreamSize = startPosition + sizeSize + customDataSize;
            if (sizeSize > 1)
                ShiftWriteStream(startPosition + 1, startPosition + sizeSize, customDataSize);
            if (customDataSize == 0)
                ArrList.EnsureCapacity(ref writeStream, writeStreamSize);
            System.Buffer.BlockCopy(serializedSizeBuffer, 0, writeStream, startPosition, sizeSize);
        }
        public override void WriteBytes(byte[] bytes) => DataStream.Write(ref writeStream, ref writeStreamSize, bytes);
        public override void WriteBytes(byte[] bytes, int startIndex, int length) => DataStream.Write(ref writeStream, ref writeStreamSize, bytes, startIndex, length);
        public override void WriteSmallShort(short value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmallUShort(ushort value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmallInt(int value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmallUInt(uint value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmallLong(long value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);
        public override void WriteSmallULong(ulong value) => DataStream.WriteSmall(ref writeStream, ref writeStreamSize, value);

        ///<summary>Arrays assigned to this variable always have the exact length of the data that is actually
        ///available to be read, and once assigned to this variable they are immutable.</summary>
        private byte[] readStream = new byte[0];
        private int readStreamPosition = 0;
        public override int ReadStreamPosition { get => readStreamPosition; set => readStreamPosition = value; }
        public override int ReadStreamLength => readStream.Length;

        public override void SetReadStream(byte[] stream, int startIndex, int length)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetReadStream");
            #endif
            if (flaggedToContinueNextFrame)
            {
                Debug.LogError($"[Lockstep] Attempt to call SetReadStream while flaggedToContinueNextFrame "
                    + $"is true, indicating that an input action or deserialization has been suspended for "
                    + $"and extended by one frame. Overwriting the read stream would interrupt this process.");
                return;
            }
            readStream = new byte[length];
            System.Buffer.BlockCopy(readStream, startIndex, stream, 0, length);
            ResetReadStream();
        }
        public override void SetReadStream(byte[] stream)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SetReadStream");
            #endif
            if (flaggedToContinueNextFrame)
            {
                Debug.LogError($"[Lockstep] Attempt to call SetReadStream while flaggedToContinueNextFrame "
                    + $"is true, indicating that an input action or deserialization has been suspended for "
                    + $"and extended by one frame. Overwriting the read stream would interrupt this process.");
                return;
            }
            readStream = stream;
            ResetReadStream();
        }

        private void ResetReadStream() => readStreamPosition = 0;
        public override sbyte ReadSByte() => DataStream.ReadSByte(readStream, ref readStreamPosition);
        public override byte ReadByte() => DataStream.ReadByte(readStream, ref readStreamPosition);
        public override void ReadFlags(out bool flag1)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
        }
        public override void ReadFlags(out bool flag1, out bool flag2)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
            flag2 = (value & 2) != 0;
        }
        public override void ReadFlags(out bool flag1, out bool flag2, out bool flag3)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
            flag2 = (value & 2) != 0;
            flag3 = (value & 4) != 0;
        }
        public override void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
            flag2 = (value & 2) != 0;
            flag3 = (value & 4) != 0;
            flag4 = (value & 8) != 0;
        }
        public override void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
            flag2 = (value & 2) != 0;
            flag3 = (value & 4) != 0;
            flag4 = (value & 8) != 0;
            flag5 = (value & 16) != 0;
        }
        public override void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5, out bool flag6)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
            flag2 = (value & 2) != 0;
            flag3 = (value & 4) != 0;
            flag4 = (value & 8) != 0;
            flag5 = (value & 16) != 0;
            flag6 = (value & 32) != 0;
        }
        public override void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5, out bool flag6, out bool flag7)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
            flag2 = (value & 2) != 0;
            flag3 = (value & 4) != 0;
            flag4 = (value & 8) != 0;
            flag5 = (value & 16) != 0;
            flag6 = (value & 32) != 0;
            flag7 = (value & 64) != 0;
        }
        public override void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5, out bool flag6, out bool flag7, out bool flag8)
        {
            int value = (int)DataStream.ReadByte(readStream, ref readStreamPosition);
            flag1 = (value & 1) != 0;
            flag2 = (value & 2) != 0;
            flag3 = (value & 4) != 0;
            flag4 = (value & 8) != 0;
            flag5 = (value & 16) != 0;
            flag6 = (value & 32) != 0;
            flag7 = (value & 64) != 0;
            flag8 = (value & 128) != 0;
        }
        public override short ReadShort() => DataStream.ReadShort(readStream, ref readStreamPosition);
        public override ushort ReadUShort() => DataStream.ReadUShort(readStream, ref readStreamPosition);
        public override int ReadInt() => DataStream.ReadInt(readStream, ref readStreamPosition);
        public override uint ReadUInt() => DataStream.ReadUInt(readStream, ref readStreamPosition);
        public override long ReadLong() => DataStream.ReadLong(readStream, ref readStreamPosition);
        public override ulong ReadULong() => DataStream.ReadULong(readStream, ref readStreamPosition);
        public override float ReadFloat() => DataStream.ReadFloat(readStream, ref readStreamPosition);
        public override double ReadDouble() => DataStream.ReadDouble(readStream, ref readStreamPosition);
        public override decimal ReadDecimal() => DataStream.ReadDecimal(readStream, ref readStreamPosition);
        public override Vector2 ReadVector2() => DataStream.ReadVector2(readStream, ref readStreamPosition);
        public override Vector3 ReadVector3() => DataStream.ReadVector3(readStream, ref readStreamPosition);
        public override Vector4 ReadVector4() => DataStream.ReadVector4(readStream, ref readStreamPosition);
        public override Quaternion ReadQuaternion() => DataStream.ReadQuaternion(readStream, ref readStreamPosition);
        public override char ReadChar() => DataStream.ReadChar(readStream, ref readStreamPosition);
        public override string ReadString() => DataStream.ReadString(readStream, ref readStreamPosition);
        public override System.DateTime ReadDateTime() => DataStream.ReadDateTime(readStream, ref readStreamPosition);
        public override bool SkipCustomClass(out uint dataVersion, out byte[] data) => SkipCustomClass(out dataVersion, out data, isDeserializingForImport);
        public override bool SkipCustomClass(out uint dataVersion, out byte[] data, bool isImport)
        {
            if (!isImport)
            {
                Debug.LogError("[Lockstep] Attempt to call SkipCustomClass outside of an import, which is not supported.");
                dataVersion = 0u;
                data = null;
                return false;
            }
            dataVersion = ReadSmallUInt();
            if (dataVersion == 0u)
            {
                data = null;
                return false;
            }
            dataVersion--;
            data = ReadBytes((int)ReadSmallUInt());
            return true;
        }
        [RecursiveMethod]
        public override SerializableWannaBeClass ReadCustomNullableClass(string className) => ReadCustomNullableClass(className, isDeserializingForImport);
        [RecursiveMethod]
        public override SerializableWannaBeClass ReadCustomNullableClass(string className, bool isImport)
        {
            if (isImport)
                return ReadCustomClass(className, isImport);
            if (ReadByte() == 0)
                return null;
            return ReadCustomClass(className, isImport);
        }
        [RecursiveMethod]
        public override SerializableWannaBeClass ReadCustomClass(string className) => ReadCustomClass(className, isDeserializingForImport);
        [RecursiveMethod]
        public override SerializableWannaBeClass ReadCustomClass(string className, bool isImport)
        {
            if (!isImport)
            {
                readCustomClassResult = (SerializableWannaBeClass)wannaBeClassesManager.NewDynamic(className);
                readCustomClassResult.Deserialize(isImport: false, importedDataVersion: 0u);
                return readCustomClassResult;
            }
            if (!ReadCustomClassInterludeOne(className)) // Keep the body of the RecursiveMethod small, because it is slow.
                return null;
            readCustomClassResult.Deserialize(isImport: true, importedDataVersion);
            return readCustomClassResult;
        }
        private SerializableWannaBeClass readCustomClassResult;
        private uint readCustomClassImportedDataVersion;
        private bool ReadCustomClassInterludeOne(string className)
        {
            readCustomClassImportedDataVersion = ReadSmallUInt();
            if (readCustomClassImportedDataVersion == 0u)
                return false;
            readCustomClassImportedDataVersion--;
            int customDataSize = (int)ReadSmallUInt();
            readCustomClassResult = (SerializableWannaBeClass)wannaBeClassesManager.NewDynamic(className);
            if (!readCustomClassResult.SupportsImportExport || readCustomClassResult.LowestSupportedDataVersion < readCustomClassImportedDataVersion)
            {
                readStreamPosition += customDataSize;
                readCustomClassResult.Delete();
                return false;
            }
            return true;
        }
        [RecursiveMethod]
        public override bool ReadCustomNullableClass(SerializableWannaBeClass instance) => ReadCustomNullableClass(instance, isDeserializingForImport);
        [RecursiveMethod]
        public override bool ReadCustomNullableClass(SerializableWannaBeClass instance, bool isImport)
        {
            if (isImport)
                return ReadCustomClass(instance, isImport);
            if (ReadByte() == 0)
                return false;
            return ReadCustomClass(instance, isImport);
        }
        [RecursiveMethod]
        public override bool ReadCustomClass(SerializableWannaBeClass instance) => ReadCustomClass(instance, isDeserializingForImport);
        [RecursiveMethod]
        public override bool ReadCustomClass(SerializableWannaBeClass instance, bool isImport)
        {
            if (!isImport)
            {
                instance.Deserialize(isImport: false, importedDataVersion: 0u);
                return true;
            }
            if (!ReadCustomClassInterludeTwo(instance)) // Keep the body of the RecursiveMethod small, because it is slow.
                return false;
            instance.Deserialize(isImport: true, readCustomClassImportedDataVersion);
            return true;
        }
        private bool ReadCustomClassInterludeTwo(SerializableWannaBeClass instance)
        {
            readCustomClassImportedDataVersion = ReadSmallUInt();
            if (readCustomClassImportedDataVersion == 0u)
                return false;
            readCustomClassImportedDataVersion--;
            int customDataSize = (int)ReadSmallUInt();
            if (!instance.SupportsImportExport || instance.LowestSupportedDataVersion < readCustomClassImportedDataVersion)
            {
                readStreamPosition += customDataSize;
                return false;
            }
            return true;
        }
        public override byte[] ReadBytes(int byteCount, bool skip = false)
        {
            if (skip)
            {
                readStreamPosition += byteCount;
                return null;
            }
            return DataStream.ReadBytes(readStream, ref readStreamPosition, byteCount);
        }
        public override short ReadSmallShort() => DataStream.ReadSmallShort(readStream, ref readStreamPosition);
        public override ushort ReadSmallUShort() => DataStream.ReadSmallUShort(readStream, ref readStreamPosition);
        public override int ReadSmallInt() => DataStream.ReadSmallInt(readStream, ref readStreamPosition);
        public override uint ReadSmallUInt() => DataStream.ReadSmallUInt(readStream, ref readStreamPosition);
        public override long ReadSmallLong() => DataStream.ReadSmallLong(readStream, ref readStreamPosition);
        public override ulong ReadSmallULong() => DataStream.ReadSmallULong(readStream, ref readStreamPosition);

        public override LockstepGameStateOptionsData[] CloneAllOptions(LockstepGameStateOptionsData[] allOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CloneAllOptions");
            #endif
            LockstepGameStateOptionsData[] other = new LockstepGameStateOptionsData[allOptions.Length];
            for (int i = 0; i < allOptions.Length; i++)
            {
                LockstepGameStateOptionsData options = allOptions[i];
                other[i] = options == null ? null : options.Clone();
            }
            return other;
        }

        public override void CleanupAllExportOptions(LockstepGameStateOptionsData[] allExportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CleanupAllOptions");
            #endif
            foreach (LockstepGameStateOptionsData options in allExportOptions)
                if (options != null)
                    options.DecrementRefsCount();
        }

        public override void CleanupAllImportOptions(DataDictionary allImportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CleanupAllOptions");
            #endif
            DataList values = allImportOptions.GetValues();
            int count = values.Count;
            for (int i = 0; i < count; i++)
            {
                LockstepGameStateOptionsData options = (LockstepGameStateOptionsData)values[i].Reference;
                if (options != null)
                    options.DecrementRefsCount();
            }
        }

        public override void CleanupImportedGameStatesData(object[][] importedGameStates)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CleanupImportedGameStatesData");
            #endif
            foreach (object[] importedGS in importedGameStates)
            {
                LockstepGameStateOptionsData importOptions = LockstepImportedGS.GetImportOptions(importedGS);
                if (importOptions != null)
                    importOptions.DecrementRefsCount();
            }
        }

        private bool isSerializingForExport = false;
        public override bool IsSerializingForExport => isSerializingForExport;

        public override LockstepGameStateOptionsData[] GetNewExportOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetNewExportOptions");
            #endif
            LockstepGameStateOptionsData[] allOptions = new LockstepGameStateOptionsData[gameStatesSupportingImportExportCount];
            for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
            {
                LockstepGameStateOptionsUI exportUI = gameStatesSupportingImportExport[i].ExportUI;
                if (exportUI == null)
                    continue;
                allOptions[i] = exportUI.NewOptions();
            }
            return allOptions;
        }

        public override int FillInMissingExportOptionsWithDefaults(LockstepGameStateOptionsData[] allExportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FillInMissingExportOptionsWithDefaults");
            #endif
            int filledInCount = 0;
            for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
            {
                LockstepGameStateOptionsUI exportUI = gameStatesSupportingImportExport[i].ExportUI;
                if (exportUI != null && allExportOptions[i] == null)
                {
                    allExportOptions[i] = exportUI.NewOptions();
                    filledInCount++;
                }
            }
            return filledInCount;
        }

        public override bool AnyExportOptionsCurrentlyShown()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetNewExportOptions");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ExportUI != null && gameState.ExportUI.CurrentlyShown)
                    return true;
            return false;
        }

        public override void UpdateAllCurrentExportOptionsFromWidgets()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  UpdateAllCurrentExportOptionsFromWidgets");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ExportUI != null && gameState.ExportUI.CurrentlyShown)
                    gameState.ExportUI.UpdateCurrentOptionsFromWidgets();
        }

        public override LockstepGameStateOptionsData[] GetAllCurrentExportOptions(bool weakReferences)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetAllCurrentExportOptions");
            #endif
            LockstepGameStateOptionsData[] allOptions = new LockstepGameStateOptionsData[gameStatesSupportingImportExportCount];
            for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
            {
                LockstepGameStateOptionsUI exportUI = gameStatesSupportingImportExport[i].ExportUI;
                if (exportUI == null)
                    continue;
                LockstepGameStateOptionsData options = exportUI.CurrentOptions;
                if (options == null)
                    continue;
                exportUI.UpdateCurrentOptionsFromWidgets();
                if (!weakReferences)
                    options.IncrementRefsCount();
                allOptions[i] = options;
            }
            return allOptions;
        }

        public override void ValidateExportOptions(LockstepGameStateOptionsData[] allExportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ValidateExportOptions");
            #endif
            for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
            {
                LockstepGameStateOptionsUI exportUI = gameStatesSupportingImportExport[i].ExportUI;
                if (exportUI == null)
                    continue;
                LockstepGameStateOptionsData exportOptions = allExportOptions[i];
                if (exportOptions != null)
                    exportUI.ValidateOptions(exportOptions);
            }
        }

        public override void ShowExportOptionsEditor(LockstepOptionsEditorUI ui, LockstepGameStateOptionsData[] allExportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ShowExportOptionsEditor");
            #endif
            for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
            {
                LockstepGameStateOptionsUI exportUI = gameStatesSupportingImportExport[i].ExportUI;
                if (exportUI == null)
                    continue;
                if (exportUI.CurrentlyShown)
                    exportUI.HideOptionsEditor();
                LockstepGameStateOptionsData options = allExportOptions[i];
                if (options != null)
                    exportUI.ShowOptionsEditor(ui, options);
            }
        }

        public override void HideExportOptionsEditor()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  HideExportOptionsEditor");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
            {
                LockstepGameStateOptionsUI exportUI = gameState.ExportUI;
                if (exportUI == null)
                    continue;
                if (exportUI.CurrentlyShown)
                    exportUI.HideOptionsEditor();
            }
        }

        public override DataDictionary GetNewImportOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetNewImportOptions");
            #endif
            DataDictionary allOptions = new DataDictionary();
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ImportUI != null)
                    allOptions.Add(gameState.GameStateInternalName, gameState.ImportUI.NewOptions());
            return allOptions;
        }

        public override int FillInMissingImportOptionsWithDefaults(DataDictionary allImportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FillInMissingImportOptionsWithDefaults");
            #endif
            int filledInCount = 0;
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ImportUI != null && !allImportOptions.ContainsKey(gameState.GameStateInternalName))
                {
                    allImportOptions.Add(gameState.GameStateInternalName, gameState.ImportUI.NewOptions());
                    filledInCount++;
                }
            return filledInCount;
        }

        public override int FillInMissingImportOptionsWithDefaults(object[][] importedGameStates)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  FillInMissingImportOptionsWithDefaults");
            #endif
            int filledInCount = 0;
            foreach (object[] importedGS in importedGameStates)
            {
                LockstepGameState gameState = LockstepImportedGS.GetGameState(importedGS);
                if (gameState == null || gameState.ImportUI == null)
                    continue;
                LockstepGameStateOptionsData importOptions = LockstepImportedGS.GetImportOptions(importedGS);
                if (importOptions != null)
                    continue;
                importOptions = gameState.ImportUI.NewOptions();
                LockstepImportedGS.SetImportOptions(importedGS, importOptions);
                filledInCount++;
            }
            return filledInCount;
        }

        public override bool AnyImportOptionsCurrentlyShown()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetNewImportOptions");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ImportUI != null && gameState.ImportUI.CurrentlyShown)
                    return true;
            return false;
        }

        public override void UpdateAllCurrentImportOptionsFromWidgets()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  UpdateAllCurrentImportOptionsFromWidgets");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ImportUI != null && gameState.ImportUI.CurrentlyShown)
                    gameState.ImportUI.UpdateCurrentOptionsFromWidgets();
        }

        public override DataDictionary GetAllCurrentImportOptions(bool weakReferences)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  GetAllCurrentImportOptions");
            #endif
            DataDictionary allOptions = new DataDictionary();
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
            {
                LockstepGameStateOptionsUI importUI = gameState.ImportUI;
                if (importUI == null)
                    continue;
                LockstepGameStateOptionsData importOptions = importUI.CurrentOptions;
                if (importOptions == null)
                    continue;
                importUI.UpdateCurrentOptionsFromWidgets();
                if (!weakReferences)
                    importOptions.IncrementRefsCount();
                allOptions.Add(gameState.GameStateInternalName, importOptions);
            }
            return allOptions;
        }

        public override void AssociateImportOptionsWithImportedGameStates(object[][] importedGameStates, DataDictionary allImportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AssociateImportOptionsWithImportedGameStates");
            #endif
            foreach (object[] importedGS in importedGameStates)
            {
                LockstepGameState gameState = LockstepImportedGS.GetGameState(importedGS);
                if (gameState == null || gameState.ImportUI == null)
                    continue;
                LockstepGameStateOptionsData prev = LockstepImportedGS.GetImportOptions(importedGS);
                if (prev != null)
                    prev.DecrementRefsCount();
                LockstepGameStateOptionsData importOptions = null;
                if (allImportOptions.TryGetValue(gameState.GameStateInternalName, out DataToken optionsToken))
                {
                    importOptions = (LockstepGameStateOptionsData)optionsToken.Reference;
                    importOptions.IncrementRefsCount();
                }
                LockstepImportedGS.SetImportOptions(importedGS, importOptions);
            }
        }

        public override void ValidateImportOptions(DataDictionary allImportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ValidateImportOptions");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ImportUI != null && allImportOptions.TryGetValue(gameState.GameStateInternalName, out DataToken optionsToken))
                {
                    LockstepGameStateOptionsData importOptions = (LockstepGameStateOptionsData)optionsToken.Reference;
                    if (importOptions != null)
                        gameState.ImportUI.ValidateOptions(importOptions);
                }
        }

        public override void ShowImportOptionsEditor(LockstepOptionsEditorUI ui, object[][] importedGameStates)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ShowImportOptionsEditor");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                if (gameState.ImportUI && gameState.ImportUI.CurrentlyShown)
                    gameState.ImportUI.HideOptionsEditor();
            foreach (object[] importedGS in importedGameStates)
            {
                LockstepGameState gameState = LockstepImportedGS.GetGameState(importedGS);
                if (gameState == null)
                    continue;
                LockstepGameStateOptionsUI importUI = gameState.ImportUI;
                LockstepGameStateOptionsData importOptions = LockstepImportedGS.GetImportOptions(importedGS);
                if (importUI == null || importOptions == null)
                    continue;
                SetReadStream(LockstepImportedGS.GetBinaryData(importedGS));
                importUI.ShowOptionsEditor(ui, importOptions);
            }
        }

        public override void HideImportOptionsEditor()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  HideImportOptionsEditor");
            #endif
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
            {
                LockstepGameStateOptionsUI importUI = gameState.ImportUI;
                if (importUI == null || !importUI.CurrentlyShown)
                    continue;
                importUI.HideOptionsEditor();
            }
        }

        private bool isExporting = false;
        private string currentExportName;
        private LockstepGameStateOptionsData[] currentAllExportOptions;
        private string exportResult = null;
        public override bool IsExporting => isExporting;
        public override string ExportName => currentExportName;
        public override string ExportResult => exportResult;

        public override bool StartExport(string exportName, LockstepGameStateOptionsData[] allExportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartExport");
            #endif
            if (!PrepareForExport(exportName, allExportOptions))
                return false;
            currentExportName = exportName;
            currentAllExportOptions = allExportOptions;
            isExporting = true;
            RaiseOnExportStart();
            suspendedInExportPreparation = true;
            suspendedInExport = true;
            FlagToContinueNextFrame();
            return true;
        }

        private uint[] crc32LookupCache;
        private bool PrepareForExport(string exportName, LockstepGameStateOptionsData[] allExportOptions)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  PrepareForExport");
            #endif
            if (!lockstepIsInitialized)
            {
                Debug.LogError("[Lockstep] Attempt to call Export before OnInit or OnClientBeginCatchUp, ignoring.");
                return false;
            }
            if (exportName != null && (exportName.Contains("\n") || exportName.Contains("\r")))
            {
                Debug.LogError("[Lockstep] Attempt to call Export where the given exportName contains '\\n' "
                    + "and or '\\r' (newline characters), which is forbidden. Ignoring.");
                return false;
            }
            if (gameStatesSupportingImportExportCount == 0) // No error log here, because this is the only sensible
                return false; // and by definition acceptable error case in which by definition null is returned.

            if (isExporting)
            {
                Debug.LogError($"[Lockstep] Attempt to call Export while another export is currently running. "
                    + $"Check IsExporting");
                return false;
            }

            bool isUsingTemporaryExportOptions = false;
            if (allExportOptions == null)
            {
                allExportOptions = GetNewExportOptions();
                isUsingTemporaryExportOptions = true;
            }
            else
            {
                if (allExportOptions.Length != gameStatesSupportingImportExportCount)
                {
                    Debug.LogError($"[Lockstep] Expected length {gameStatesSupportingImportExportCount}, got {allExportOptions.Length} "
                        + "for allExportOptions as an argument to Export.");
                    return false;
                }
                for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
                    if (gameStatesSupportingImportExport[i].ExportUI != null && allExportOptions[i] == null)
                    {
                        Debug.LogError($"[Lockstep] Missing export options for game state "
                            + $"{gameStatesSupportingImportExport[i].GameStateInternalName} (index {i}), canceling Export.");
                        return false;
                    }
            }

            ResetWriteStream();

            WriteDateTime(System.DateTime.UtcNow);
            WriteString(WorldName);
            WriteString(exportName);
            WriteSmallUInt((uint)gameStatesSupportingImportExportCount);

            for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
            {
                LockstepGameStateOptionsData exportOptions = allExportOptions[i];
                gameStatesSupportingImportExport[i].SetProgramVariable("optionsForCurrentExport", exportOptions);
                if (!isUsingTemporaryExportOptions && exportOptions != null) // Temporary ones already have a non weak ref
                    exportOptions.IncrementRefsCount(); // Ensure none get deleted during any SerializeGameState calls
            }

            return true;
        }

        private void ExportGameStates()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ExportGameStates");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            #endif
            byte[] unsuspendedWriteStream = null;
            LockstepGameState gameState = gameStatesSupportingImportExport[suspendedGSIndexInExport];
            if (!flaggedToContinueNextFrame)
            {
                WriteString(gameState.GameStateInternalName);
                WriteString(gameState.GameStateDisplayName);
                WriteSmallUInt(gameState.GameStateDataVersion);
                suspendedExportGSSizePosition = writeStreamSize;
                writeStreamSize += 4;
            }
            else
            {
                flaggedToContinueNextFrame = false;
                unsuspendedWriteStream = writeStream;
                writeStream = suspendedWriteStream;
                writeStreamSize = suspendedWriteStreamSize;
                suspendedWriteStream = null;
                suspendedWriteStreamSize = 0;
                suspendedInExport = false;
                isContinuationFromPrevFrame = true;
            }
            #if LockstepDebug
            double serializeStartMs = sw.Elapsed.TotalMilliseconds;
            #endif
            gameState.SerializeGameState(true, currentAllExportOptions[suspendedGSIndexInExport]);
            #if LockstepDebug
            double serializeMs = sw.Elapsed.TotalMilliseconds - serializeStartMs;
            #endif
            isContinuationFromPrevFrame = false;
            if (flaggedToContinueNextFrame)
            {
                suspendedInExport = true;
                suspendedWriteStream = writeStream;
                suspendedWriteStreamSize = writeStreamSize;
                writeStream = unsuspendedWriteStream ?? new byte[MinWriteStreamCapacity];
                ResetWriteStream();
                return;
            }
            int stopPosition = writeStreamSize;
            writeStreamSize = suspendedExportGSSizePosition;
            WriteInt(stopPosition - suspendedExportGSSizePosition - 4); // The 4 bytes got reserved prior, cannot use WriteSmall.
            writeStreamSize = stopPosition;
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] [sw] Lockstep  Export (inner) - serialization ms: {serializeMs}, GS internal name: {gameState.GameStateInternalName}, GS binary size: {stopPosition - suspendedExportGSSizePosition - 4}");
            #endif

            suspendedGSIndexInExport++;
            if (suspendedGSIndexInExport == gameStatesSupportingImportExportCount)
                return;
            FlagToContinueNextFrame();
            suspendedInExport = true;
            suspendedInExportPreparation = true;
        }

        private void ExportInternal()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ExportInternal");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            #endif

            if (suspendedInExportPreparation)
            {
                suspendedInExportPreparation = false;
                suspendedInExport = false;
                flaggedToContinueNextFrame = false;
            }

            isSerializingForExport = true;
            ExportGameStates();
            isSerializingForExport = false;
            if (flaggedToContinueNextFrame)
                return;
            suspendedGSIndexInExport = 0;

            for (int i = 0; i < gameStatesSupportingImportExportCount; i++)
            {
                gameStatesSupportingImportExport[i].SetProgramVariable("optionsForCurrentExport", null);
                LockstepGameStateOptionsData exportOptions = currentAllExportOptions[i];
                if (exportOptions != null)
                    exportOptions.DecrementRefsCount(); // This'll also delete the options if they were temporary ones
            }
            currentAllExportOptions = null;

            #if LockstepDebug
            double crcStartMs = sw.Elapsed.TotalMilliseconds;
            #endif
            uint crc = CRC32.Compute(ref crc32LookupCache, writeStream, 0, writeStreamSize);
            #if LockstepDebug
            double crcMs = sw.Elapsed.TotalMilliseconds - crcStartMs;
            #endif
            WriteUInt(crc);

            byte[] exportedData = new byte[writeStreamSize];
            System.Buffer.BlockCopy(writeStream, 0, exportedData, 0, writeStreamSize);
            ResetWriteStream();

            exportResult = Base64.Encode(exportedData);
            Debug.Log("[Lockstep] Export:" + (currentExportName != null ? $" {currentExportName}:\n" : "\n") + exportResult);
            #if LockstepDebug
            sw.Stop();
            Debug.Log($"[LockstepDebug] [sw] Lockstep  ExportInternal (inner) - binary size: {writeStreamSize}, crc: {crc}, crc calculation ms: {crcMs}, total ms: {sw.Elapsed.TotalMilliseconds}");
            #endif
            isExporting = false;
            RaiseOnExportFinished();
            exportResult = null;
            currentExportName = null;
        }

        public override object[][] ImportPreProcess(string exportedString, out System.DateTime exportedDate, out string exportWorldName, out string exportName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ImportPreProcess");
            #endif
            exportedDate = System.DateTime.MinValue;
            exportWorldName = null;
            exportName = null;

            byte[] suspendedReadStream = readStream;
            int suspendedReadPosition = readStreamPosition;

            if (!Base64.TryDecode(exportedString, out readStream))
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  ImportPreProcess (inner) - invalid base64 encoding:\n{exportedString}");
                #endif
                return null;
            }
            if (readStream.Length < 4)
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  ImportPreProcess (inner) - exported data too short:\n{exportedString}");
                #endif
                return null;
            }

            #if LockstepDebug
            System.Diagnostics.Stopwatch crcStopwatch = new System.Diagnostics.Stopwatch();
            crcStopwatch.Start();
            #endif
            uint gotCrc = CRC32.Compute(ref crc32LookupCache, readStream, 0, readStream.Length - 4);
            #if LockstepDebug
            crcStopwatch.Stop();
            #endif
            readStreamPosition = readStream.Length - 4;
            uint expectedCrc = ReadUInt();
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] [sw] Lockstep  ImportPreProcess (inner) - binary size: {readStream.Length}, expected crc: {expectedCrc}, got crc: {gotCrc}, crc calculation ms: {crcStopwatch.Elapsed.TotalMilliseconds}");
            #endif
            if (gotCrc != expectedCrc)
                return null;

            ResetReadStream();

            exportedDate = ReadDateTime();
            exportWorldName = SanitizeWorldName(ReadString()); // Sanitize to handle fabricated export strings.
            exportName = ReadString();
            if (exportName != null)
            {
                // While the Export function will refuse export names with newlines, it is possible to
                // fabricate an import/export string which could in turn contain newlines in the name. However
                // the whole point of Export refusing them is that the API guarantees that the name shall
                // never under any circumstances contain newlines. So they must be removed here.
                exportName = exportName.Replace('\n', ' ').Replace('\r', ' ');
            }
            int gameStatesCount = (int)ReadSmallUInt();

            object[][] importedGameStates = new object[gameStatesCount][];
            for (int i = 0; i < gameStatesCount; i++)
            {
                object[] importedGS = LockstepImportedGS.New();
                importedGameStates[i] = importedGS;
                string internalName = ReadString();
                LockstepImportedGS.SetInternalName(importedGS, internalName);
                LockstepImportedGS.SetDisplayName(importedGS, ReadString());
                uint dataVersion = ReadSmallUInt();
                LockstepImportedGS.SetDataVersion(importedGS, dataVersion);
                int dataSize = ReadInt();
                byte[] binaryData = new byte[dataSize];
                System.Buffer.BlockCopy(readStream, readStreamPosition, binaryData, 0, dataSize);
                readStreamPosition += dataSize;
                LockstepImportedGS.SetBinaryData(importedGS, binaryData);
                DataToken internalNameToken = internalName;
                if (!gameStatesByInternalName.TryGetValue(internalNameToken, out DataToken gameStateToken))
                    LockstepImportedGS.SetErrorMsg(importedGS, "not in this world");
                else
                {
                    LockstepGameState gameState = (LockstepGameState)gameStateToken.Reference;
                    LockstepImportedGS.SetGameState(importedGS, gameState);
                    LockstepImportedGS.SetGameStateIndex(importedGS, gameStateIndexesByInternalName[internalNameToken].Int);
                    if (!gameState.GameStateSupportsImportExport)
                        LockstepImportedGS.SetErrorMsg(importedGS, "no longer supports import");
                    else if (dataVersion > gameState.GameStateDataVersion)
                        LockstepImportedGS.SetErrorMsg(importedGS, "imported version too new");
                    else if (dataVersion < gameState.GameStateLowestSupportedDataVersion)
                        LockstepImportedGS.SetErrorMsg(importedGS, "imported version too old");
                }
            }

            if (flaggedToContinueNextFrame)
            {
                readStream = suspendedReadStream;
                readStreamPosition = suspendedReadPosition;
            }

            return importedGameStates;
        }

        public override void StartImport(object[][] importedGameStates, System.DateTime exportDate, string exportWorldName, string exportName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartImport");
            #endif
            if (!lockstepIsInitialized)
            {
                Debug.LogError("[Lockstep] Attempt to call StartImport before OnInit or OnClientBeginCatchUp, ignoring.");
                return;
            }
            if (isImporting)
            {
                Debug.LogError("[Lockstep] Attempt to call StartImport while IsImporting is true, ignoring.");
                return;
            }
            foreach (object[] importedGS in importedGameStates)
            {
                LockstepGameState gameState = LockstepImportedGS.GetGameState(importedGS);
                if (gameState != null
                    && gameState.ImportUI != null
                    && LockstepImportedGS.GetImportOptions(importedGS) == null)
                {
                    Debug.LogError($"[Lockstep] Missing import options for game state "
                        + $"{gameState.GameStateInternalName}, ignoring StartImport attempt.");
                    return;
                }
            }

            int count = 0;
            DataDictionary validImportedGSLut = new DataDictionary();
            foreach (object[] importedGS in importedGameStates)
                if (LockstepImportedGS.GetErrorMsg(importedGS) == null)
                {
                    count++;
                    validImportedGSLut.Add(LockstepImportedGS.GetInternalName(importedGS), new DataToken(importedGS));
                }
            if (count == 0)
                return;
            object[][] validImportedGSs = new object[count][];
            count = 0;
            ResetWriteStream();
            // Use gameStatesSupportingImportExport because that is sorted by game state dependencies, importedGameStates is not.
            foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
            {
                if (!validImportedGSLut.TryGetValue(gameState.GameStateInternalName, out DataToken importedGSToken))
                    continue;
                object[] importedGS = (object[])importedGSToken.Reference;
                validImportedGSs[count++] = importedGS;
                LockstepGameStateOptionsData importOptions = LockstepImportedGS.GetImportOptions(importedGS);
                WriteCustomNullableClass(importOptions);
                byte[] serializedImportOptions = new byte[writeStreamSize];
                System.Buffer.BlockCopy(writeStream, 0, serializedImportOptions, 0, writeStreamSize);
                ResetWriteStream();
                LockstepImportedGS.SetSerializedImportOptions(importedGS, serializedImportOptions);
            }
            if (exportName != null)
                exportName = exportName.Replace('\n', ' ').Replace('\r', ' ');
            SendImportStartIA(validImportedGSs, exportDate, SanitizeWorldName(exportWorldName), exportName);
        }

        ///<summary>LockstepImportedGS[]</summary>
        private object[][] importedGSsToSend;

        // None of this is part of an internal game state, which is fine because late joiner sync will not be
        // performed while isImporting is true.
        private bool isImporting = false;
        private void SetIsImporting(bool value)
        {
            if (isImporting == value)
                return;
            isImporting = value;
            StartOrStopAutosave();
            if (value)
                RaiseOnImportStart();
            else
            {
                RaiseOnImportFinished();
                // To make these properties game state safe.
                importingPlayerId = 0u;
                importingFromDate = new System.DateTime();
                importingFromWorldName = null;
                importingFromName = null;
                gameStatesBeingImported = null;
                gameStatesBeingImportedDataVersions = null;
                gameStatesBeingImportedFinishedCount = 0;
                foreach (LockstepGameState gameState in gameStatesSupportingImportExport)
                    if (gameState.OptionsForCurrentImport != null)
                    {
                        gameState.OptionsForCurrentImport.DecrementRefsCount();
                        gameState.SetProgramVariable("optionsForCurrentImport", null);
                    }
            }
        }
        private uint importingPlayerId;
        private System.DateTime importingFromDate;
        private string importingFromWorldName;
        private string importingFromName;
        private LockstepGameState importedGameState;
        private string importErrorMessage;
        private uint importedDataVersion;
        public override bool IsImporting => isImporting;
        public override uint ImportingPlayerId => importingPlayerId;
        private bool isDeserializingForImport = false;
        public override bool IsDeserializingForImport => isDeserializingForImport;

        public override System.DateTime ImportingFromDate => importingFromDate;
        public override string ImportingFromWorldName => importingFromWorldName;
        public override string ImportingFromName => importingFromName;
        public override LockstepGameState ImportedGameState => importedGameState;
        public override string ImportErrorMessage => importErrorMessage;
        public override uint ImportedDataVersion => importedDataVersion;
        public override LockstepGameState[] GameStatesBeingImported
        {
            get
            {
                if (gameStatesBeingImported == null)
                    return new LockstepGameState[0];
                int length = gameStatesBeingImported.Length;
                LockstepGameState[] result = new LockstepGameState[length];
                gameStatesBeingImported.CopyTo(result, 0);
                return result;
            }
        }
        public override uint[] GameStatesBeingImportedDataVersions
        {
            get
            {
                if (gameStatesBeingImportedDataVersions == null)
                    return new uint[0];
                int length = gameStatesBeingImportedDataVersions.Length;
                uint[] result = new uint[length];
                gameStatesBeingImportedDataVersions.CopyTo(result, 0);
                return result;
            }
        }
        public override int GameStatesBeingImportedCount => gameStatesBeingImported == null ? 0 : gameStatesBeingImported.Length;
        public override int GameStatesBeingImportedFinishedCount => gameStatesBeingImportedFinishedCount;
        private LockstepGameState[] gameStatesBeingImported = null;
        private uint[] gameStatesBeingImportedDataVersions = null;
        private int gameStatesBeingImportedFinishedCount = 0;

        ///<summary>LockstepImportedGS[] importedGSs</summary>
        private void SendImportStartIA(object[][] importedGSs, System.DateTime exportDate, string exportWorldName, string exportName)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendImportStartIA");
            #endif
            if (importedGSs.Length == 0)
            {
                Debug.LogError("[Lockstep] Attempt to SendImportStartIA with 0 game states to import, ignoring.");
                return;
            }
            WriteDateTime(exportDate);
            WriteString(exportWorldName);
            WriteString(exportName);
            WriteSmallUInt((uint)importedGSs.Length);
            foreach (object[] importedGS in importedGSs)
            {
                WriteSmallUInt((uint)LockstepImportedGS.GetGameStateIndex(importedGS));
                WriteSmallUInt(LockstepImportedGS.GetDataVersion(importedGS));
            }
            importedGSsToSend = importedGSs;
            SendInputAction(importStartIAId);
        }

        [SerializeField] [HideInInspector] private uint importStartIAId;
        [LockstepInputAction(nameof(importStartIAId))]
        public void OnImportStartIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnImportStartIA");
            #endif
            if (isImporting)
            {
                foreach (object[] importedGS in importedGSsToSend)
                    LockstepImportedGS.SetSerializedImportOptions(importedGS, null); // Cleanup and make GC happy.
                importedGSsToSend = null;
                return;
            }
            importingPlayerId = SendingPlayerId;
            importingFromDate = ReadDateTime();
            importingFromWorldName = ReadString();
            importingFromName = ReadString();
            int importedGSsCount = (int)ReadSmallUInt();
            if (importedGSsCount == 0)
            {
                Debug.Log($"[Lockstep] Impossible: Importing 0 game states should never get this far.");
                return;
            }
            gameStatesBeingImported = new LockstepGameState[importedGSsCount];
            gameStatesBeingImportedDataVersions = new uint[importedGSsCount];
            gameStatesBeingImportedFinishedCount = 0; // Should already be zero, but do it anyway for clarity.
            for (int i = 0; i < importedGSsCount; i++)
            {
                gameStatesBeingImported[i] = allGameStates[(int)ReadSmallUInt()];
                gameStatesBeingImportedDataVersions[i] = ReadSmallUInt();
            }
            SetIsImporting(true); // Raises an event, do it last so all the fields are populated.

            if (SendingPlayerId != localPlayerId)
                return;

            SendImportGameStatesIA(importedGSsToSend);
            importedGSsToSend = null;
        }

        ///<summary>LockstepImportedGS[] importedGSsToSend</summary>
        private void SendImportGameStatesIA(object[][] importedGSsToSend)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  SendImportGameStatesIA");
            #endif
            foreach (object[] importedGS in importedGSsToSend)
            {
                byte[] optionsBytes = LockstepImportedGS.GetSerializedImportOptions(importedGS);
                LockstepImportedGS.SetSerializedImportOptions(importedGS, null); // Cleanup and make GC happy.
                byte[] gsBytes = LockstepImportedGS.GetBinaryData(importedGS);
                WriteSmallUInt((uint)optionsBytes.Length);
                WriteSmallUInt((uint)gsBytes.Length);
                WriteBytes(optionsBytes);
                WriteBytes(gsBytes);
            }
            SendInputAction(importGameStatesIAId);
        }

        [SerializeField] [HideInInspector] private uint importGameStatesIAId;
        [LockstepInputAction(nameof(importGameStatesIAId))]
        public void OnImportGameStatesIA()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  OnImportGameStatesIA");
            #endif

            if (!isContinuationFromPrevFrame)
            {
                currentIncomingGSDataIndex = 0;
                int length = gameStatesBeingImported.Length;
                if (length == 0)
                {
                    Debug.Log($"[Lockstep] Impossible: Importing 0 game states should never get this far.");
                    return;
                }
                incomingGameStateData = new object[length][];
                for (int i = 0; i < length; i++)
                {
                    int optionsByteCount = (int)ReadSmallUInt();
                    int gsByteCount = (int)ReadSmallUInt();
                    byte[] optionsBytes = ReadBytes(optionsByteCount);
                    byte[] gsBytes = ReadBytes(gsByteCount);
                    incomingGameStateData[i] = new object[]
                    {
                        optionsByteCount,
                        optionsBytes,
                        gsBytes,
                    };
                }
                suspendedInImportOptionsDeserialization = true;
                FlagToContinueNextFrame();
                return;
            }

            if (suspendedInImportOptionsDeserialization)
            {
                DeserializeImportOptions();
                FlagToContinueNextFrame();
                return;
            }

            ImportGameState();
            if (flaggedToContinueNextFrame)
                return;
            if (gameStatesBeingImportedFinishedCount != gameStatesBeingImported.Length)
            {
                FlagToContinueNextFrame();
                return;
            }
            incomingGameStateData = null; // Make GC happy.
            isContinuationFromPrevFrame = false; // Must be false inside of OnImportFinished.
            SetIsImporting(false);
        }

        private void DeserializeImportOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  DeserializeImportOptions");
            #endif
            object[] incomingData = incomingGameStateData[currentIncomingGSDataIndex];
            if (!flaggedToContinueInsideOfGSImport)
                SetReadStream((byte[])incomingData[1]/*optionsBytes*/);
            LockstepGameState gameState = gameStatesBeingImported[currentIncomingGSDataIndex];
            isContinuationFromPrevFrame = flaggedToContinueInsideOfGSImport;
            flaggedToContinueInsideOfGSImport = false;
            LockstepGameStateOptionsData importOptions = ReadImportOptions(gameState);
            if (flaggedToContinueNextFrame)
            {
                flaggedToContinueInsideOfGSImport = true;
                return;
            }
            gameState.SetProgramVariable("optionsForCurrentImport", importOptions);
            currentIncomingGSDataIndex++;
            if (currentIncomingGSDataIndex != gameStatesBeingImported.Length)
                return;
            currentIncomingGSDataIndex = 0;
            suspendedInImportOptionsDeserialization = false;
            isContinuationFromPrevFrame = false; // Must be false in the raised event below.
            RaiseOnImportOptionsDeserialized();
        }

        private LockstepGameStateOptionsData ReadImportOptions(LockstepGameState gameState)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ReadImportOptions");
            #endif
            if (gameState.ImportUI != null)
                return (LockstepGameStateOptionsData)ReadCustomNullableClass(gameState.ImportUI.OptionsClassName);
            if (ReadByte() == 0) // Reading the byte that was written by WriteCustomNullableClass.
                return null;
            Debug.LogError($"[Lockstep] Impossible: The game state {gameState.GameStateInternalName} received "
                + $"import options even though it does not have an import UI which subsequently means it "
                + $"does not have an associated import options class name. Ignoring incoming options data.");
            return null;
        }

        private void ImportGameState()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  ImportGameState");
            #endif
            if (!flaggedToContinueInsideOfGSImport)
            {
                object[] incomingData = incomingGameStateData[currentIncomingGSDataIndex++];
                SetReadStream((byte[])incomingData[2]/*gsBytes*/);
                ResetReadStream();
                importedGameState = gameStatesBeingImported[gameStatesBeingImportedFinishedCount];
                importedDataVersion = gameStatesBeingImportedDataVersions[gameStatesBeingImportedFinishedCount];
            }
            isContinuationFromPrevFrame = flaggedToContinueInsideOfGSImport;
            flaggedToContinueInsideOfGSImport = false;
            isDeserializingForImport = true;
            #if LockstepDebug
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            #endif
            importErrorMessage = importedGameState.DeserializeGameState(isImport: true, importedDataVersion, importedGameState.OptionsForCurrentImport);
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] [sw] Lockstep  ImportGameState (inner) - deserialize GS ms: {sw.Elapsed.TotalMilliseconds}, GS internal name: {importedGameState.GameStateInternalName}");
            #endif
            isContinuationFromPrevFrame = false; // Must be false inside of the other raised events down below.
            isDeserializingForImport = false;
            if (flaggedToContinueNextFrame)
            {
                flaggedToContinueInsideOfGSImport = true;
                return;
            }
            if (importErrorMessage != null)
                RaiseOnLockstepNotification($"Importing '{importedGameState.GameStateDisplayName}' resulted in an error:\n{importErrorMessage}");
            gameStatesBeingImportedFinishedCount++; // Before raising the event.
            RaiseOnImportedGameState();
            importedGameState = null;
            importErrorMessage = null;
            importedDataVersion = 0u;
        }

        private void CheckIfImportingPlayerLeft(uint leftPlayerId)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  CheckIfImportingPlayerLeft");
            #endif
            if (!isImporting || leftPlayerId != ImportingPlayerId)
                return;
            SetIsImporting(false);
        }

        private bool AutosaveShouldBeRunning
            => lockstepIsInitialized
            && !(isCatchingUp && isInitialCatchUp)
            && autosavePauseScopeCount == 0
            && !isImporting;

        private void StartOrStopAutosave()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartOrStopAutosave");
            #endif
            if (exportOptionsForAutosave == null)
            {
                autosaveTimerStart = -1f;
                autosaveTimerPausedAt = float.PositiveInfinity;
                return;
            }

            bool doRun = AutosaveShouldBeRunning;
            if (autosaveTimerStart == -1f) // Timer has not been started yet, start it.
            {
                autosaveTimerStart = Time.realtimeSinceStartup;
                autosaveTimerPausedAt = doRun ? -1f : 0f;
                if (doRun)
                    SendCustomEventDelayedSeconds(nameof(AutosaveLoop), autosaveIntervalSeconds);
                return;
            }

            if (doRun == (autosaveTimerPausedAt == -1f)) // Expected running state matches current state.
                return;

            if (!doRun) // Pause the timer.
            {
                autosaveTimerPausedAt = Time.realtimeSinceStartup - autosaveTimerStart;
                return;
            }

            // Resume the timer.
            autosaveTimerStart = Time.realtimeSinceStartup - autosaveTimerPausedAt;
            autosaveTimerPausedAt = -1f;
            SendCustomEventDelayedSeconds(nameof(AutosaveLoop), SecondsUntilNextAutosave);
        }

        private LockstepGameStateOptionsData[] exportOptionsForAutosave = null;
        public override LockstepGameStateOptionsData[] ExportOptionsForAutosave
        {
            get
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  ExportOptionsForAutosave.get");
                #endif
                return CloneAllOptions(exportOptionsForAutosave);
            }
            set
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  ExportOptionsForAutosave.set");
                #endif
                if (value == null && exportOptionsForAutosave == null)
                    return;
                if (exportOptionsForAutosave != null)
                    for (int i = 0; i < exportOptionsForAutosave.Length; i++)
                        if (exportOptionsForAutosave[i] != null)
                            exportOptionsForAutosave[i].DecrementRefsCount();
                if (value == null)
                    exportOptionsForAutosave = null;
                else
                {
                    exportOptionsForAutosave = CloneAllOptions(value);
                    FillInMissingExportOptionsWithDefaults(exportOptionsForAutosave);
                }
                StartOrStopAutosave();
                MarkForOnExportOptionsForAutosaveChanged();
            }
        }
        public override bool HasExportOptionsForAutosave => exportOptionsForAutosave != null;

        private float autosaveIntervalSeconds = 300f;
        public override float AutosaveIntervalSeconds
        {
            get => autosaveIntervalSeconds;
            set
            {
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] Lockstep  AutosaveIntervalSeconds.set");
                #endif
                if (float.IsInfinity(value) || float.IsNaN(value))
                {
                    Debug.LogError($"[Lockstep] Attempt to write {value} to {nameof(AutosaveIntervalSeconds)} "
                        + $"which is invalid (+-inf and nan are invalid), ignoring. Treat this like an exception.");
                    return;
                }
                float newValue = Mathf.Max(0f, value);
                if (newValue == autosaveIntervalSeconds)
                    return;
                autosaveIntervalSeconds = newValue;
                // If it is not paused, send another loop event which should be received at the right time.
                if (autosaveTimerPausedAt == -1f)
                    SendCustomEventDelayedSeconds(nameof(AutosaveLoop), SecondsUntilNextAutosave);
                MarkForOnAutosaveIntervalSecondsChanged();
            }
        }

        /// <summary>
        /// <para><p>-1f</p> means the timer is not running.</para>
        /// </summary>
        private float autosaveTimerStart = -1f;
        /// <summary>
        /// <para><p>-1f</p> means the timer is not paused. It is only ever -1f if
        /// <see cref="autosaveTimerStart"/> is non -1f, so it's actually running.</para>
        /// </summary>
        private float autosaveTimerPausedAt = float.PositiveInfinity;

        public override float SecondsUntilNextAutosave => autosaveTimerPausedAt == -1f
            ? Mathf.Max(0f, autosaveIntervalSeconds - (Time.realtimeSinceStartup - autosaveTimerStart))
            : float.IsInfinity(autosaveTimerPausedAt) ? autosaveTimerPausedAt
            : Mathf.Max(0f, autosaveIntervalSeconds - autosaveTimerPausedAt);

        private int autosavePauseScopeCount = 0;
        public override bool IsAutosavePaused => autosavePauseScopeCount != 0;

        public override void StartScopedAutosavePause()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StartScopedAutosavePause");
            #endif
            autosavePauseScopeCount++;
            if (autosavePauseScopeCount == 1)
            {
                StartOrStopAutosave();
                MarkForOnIsAutosavePausedChanged();
            }
        }

        public override void StopScopedAutosavePause()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  StopScopedAutosavePause");
            #endif
            if (autosavePauseScopeCount == 0)
                return;
            autosavePauseScopeCount--;
            if (autosavePauseScopeCount == 0)
            {
                StartOrStopAutosave();
                MarkForOnIsAutosavePausedChanged();
            }
        }

        private int autosaveCount = 0;

        public void AutosaveLoop()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] Lockstep  AutosaveLoop");
            #endif
            if (autosaveTimerPausedAt != -1f) // Autosaving is paused or straight up no longer running.
                return;
            float timePassed = Time.realtimeSinceStartup - autosaveTimerStart;
            if (timePassed + 1.5f < autosaveIntervalSeconds) // Accept the event 1.5 seconds early, but if it's
                return; // earlier than that, nope, too soon, ignore this call. It's caused by duplicate calls.
            string autosaveName = $"autosave {++autosaveCount} (tick: {currentTick})";
            ValidateExportOptions(exportOptionsForAutosave);
            StartExport(autosaveName, exportOptionsForAutosave); // Export writes to the log file.
            autosaveTimerStart = Time.realtimeSinceStartup;
            SendCustomEventDelayedSeconds(nameof(AutosaveLoop), autosaveIntervalSeconds);
        }
    }
}
