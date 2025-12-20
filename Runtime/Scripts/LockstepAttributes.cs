
namespace JanSharp
{
    public enum LockstepEventType
    {
        /// <summary>
        /// <para>The very first event to ever get raised, only ever raised once throughout the lifetime of
        /// the entire lockstep system across all clients, so only once on the very first client.</para>
        /// <para>There are edge cases where it does run multiple times throughout the lifetime of a VRChat
        /// world instance, such as when a client joins and all other clients instantly leave afterwards,
        /// leaving the new client without late joiner data and alone, therefore it behaves just like a
        /// completely fresh instance of the world.</para>
        /// <para>From within this event and going forward sending input actions is allowed. However since
        /// <see cref="OnInit"/> gets raised when there is just a single client and it is game state safe,
        /// it is also possible to simply directly initialize and modify game states.</para>
        /// <para><see cref="OnInit"/> has the special purpose of initializing game states for the very first
        /// time, as all other clients in the future are going to receive late joiner data containing the
        /// existing game states. Since <see cref="OnInit"/> is the very first initialization, it is allowed
        /// to use any data to initialize the game state(s). This is natural since there is no previous game
        /// state data to mutate.</para>
        /// <para>Unlike most events, <see cref="LockstepAPI.FlagToContinueNextFrame"/> can be used within
        /// <see cref="OnInit"/>.</para>
        /// <para>Even though <see cref="OnInit"/> is raised before <see cref="OnClientJoined"/>, the master
        /// client already exists in the internal client states game state inside of
        /// <see cref="OnInit"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnInit,
        /// <summary>
        /// <para>Gets raised immediately after <see cref="OnInit"/>, see there fore more details.</para>
        /// <para>The primary difference between <see cref="OnInit"/> and <see cref="OnInitFinished"/> is that
        /// <see cref="LockstepAPI.FlagToContinueNextFrame"/> cannot be used in <see cref="OnInitFinished"/>.
        /// All event listeners for this event run within the same frame, making it a true notification that
        /// lockstep has been initialized, as well as every system using lockstep being initialized by the
        /// end of this frame.</para>
        /// <para><see cref="LockstepAPI.IsInitialized"/> is <see langword="true"/> within
        /// <see cref="OnInit"/>, however if <see cref="OnInit"/> ends up spanning multiple frames, in the
        /// time between those frames <see cref="LockstepAPI.IsInitialized"/> is set back to
        /// <see langword="false"/>. Therefore when checking the value of
        /// <see cref="LockstepAPI.IsInitialized"/> at any point in time (that isn't within some
        /// <see cref="OnInit"/> or <see cref="OnInitFinished"/> listener itself), it being
        /// <see langword="true"/> provides the guarantee that every system has received both the
        /// <see cref="OnInit"/> and <see cref="OnInitFinished"/> events.</para>
        /// <para>The purpose of this event is enabling systems interacting with other systems, potentially
        /// even unknown systems, to handle initialization more cleanly. Most systems only need
        /// <see cref="OnInit"/> and the rest of the documentation generally only mentions
        /// <see cref="OnInit"/> for simplicity, even though mentioning both would be more accurate.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnInitFinished,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.CatchingUpPlayerId"/> to get the id of the client which is
        /// beginning to catch up.</para>
        /// <para>This is the very first event raised on every client which isn't the very first client, since
        /// for the first client <see cref="OnInit"/> gets raised instead. While
        /// <see cref="OnClientBeginCatchUp"/> is not game state safe, the 2 events serve the same purpose in
        /// allowing other systems - non game states - to initialize and to start accepting user input.</para>
        /// <para>From within this event and going forward sending input actions is allowed.</para>
        /// <para>At the <see cref="LockstepAPI.CurrentTick"/>, the only way for this client - the local
        /// client - to be part of custom game states is if they listen to the <see cref="OnPreClientJoined"/>
        /// event. When systems are using the <see cref="OnClientJoined"/> event, that has not been raised yet
        /// for the local client (again, as of the <see cref="LockstepAPI.CurrentTick"/>).</para>
        /// <para>Since the <see cref="VRC.SDKBase.VRCPlayerApi"/> is not part of the game state, if it is
        /// desired to input <see cref="VRC.SDKBase.VRCPlayerApi"/> data into the game state, sending input
        /// actions from within this event may make sense.</para>
        /// <para>Unlike most events, <see cref="LockstepAPI.FlagToContinueNextFrame"/> can be used within
        /// <see cref="OnClientBeginCatchUp"/>.</para>
        /// <para>The state of the local client in and after this event is
        /// <see cref="ClientState.WaitingForLateJoinerSync"/>. <see cref="LockstepAPI.IsCatchingUp"/> is
        /// <see langword="true"/> however. It may seem odd that the client state is not
        /// <see cref="ClientState.CatchingUp"/>, however since the local client is still behind in time, the
        /// internal client states game state is still in an older state.</para>
        /// <para><b>Not game state safe</b> - only raised on one client, the one beginning catch up.</para>
        /// </summary>
        OnClientBeginCatchUp,
        /// <summary>
        /// <para>Gets raised immediately after <see cref="OnClientBeginCatchUp"/>, see there fore more
        /// details.</para>
        /// <para>The difference between <see cref="OnClientBeginCatchUp"/> and
        /// <see cref="OnPostClientBeginCatchUp"/> is effectively equivalent to the difference between
        /// <see cref="OnInit"/> and <see cref="OnInitFinished"/>. Therefore see <see cref="OnInitFinished"/>
        /// for relevant documentation, all of its xml annotations apply here too just with the event names
        /// interchanged. All of them, technically even the very first one with the event name
        /// interchanged.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnPostClientBeginCatchUp,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.JoinedPlayerId"/> to get the id of the joined client.</para>
        /// <para>The <see cref="VRC.SDKBase.VRCPlayerApi"/> for the joining player may not even exist anymore
        /// by the time this event gets raised. It is not part of the game state.</para>
        /// <para>This is the very first event to be raised for the joined client, however said client is
        /// waiting for late joiner data. It cannot send input actions yet, and any input actions sent may or
        /// may not actually be run on the joined client, depending on which tick the master client decides to
        /// send late joiner data to the joined client. The timing of this is undefined.</para>
        /// <para>In less technical terms this is like a notification that a client is about to join, but it
        /// has not fully joined yet, and should therefore be treated as a not yet joined client.</para>
        /// <para>From this point forward until <see cref="OnClientJoined"/> there are guaranteed to be no
        /// events raised for/about the joined client.</para>
        /// <para>This event is not raised on the client which joined, since it is raised before late joiner
        /// data is sent to the client.</para>
        /// <para>The state of the joined client in and after this event is
        /// <see cref="ClientState.WaitingForLateJoinerSync"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnPreClientJoined,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.JoinedPlayerId"/> to get the id of the joined client.</para>
        /// <para>This event is guaranteed to be raised before any input actions sent by the joining client
        /// are received and run.</para>
        /// <para>The <see cref="VRC.SDKBase.VRCPlayerApi"/> for the joining player may not even exist anymore
        /// by the time this event gets raised. It is not part of the game state.</para>
        /// <para>While this is the second event for/about the joined client - only
        /// <see cref="OnPreClientJoined"/> comes before <see cref="OnClientJoined"/> - before this event the
        /// joined client should be treated as though they have not joined yet, making
        /// <see cref="OnClientJoined"/> the true "this player has joined" event. As the name implies.</para>
        /// <para>This event is also raised on the client which joined, including the very first client (right
        /// after <see cref="OnInit"/>).</para>
        /// <para>The state of the joined client in and after this event is
        /// <see cref="ClientState.CatchingUp"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnClientJoined,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.CatchingUpPlayerId"/> to get the id of the client which has
        /// caught up.</para>
        /// <para>It is possible for a client to become master before finishing catching up, in which case
        /// <see cref="OnMasterClientChanged"/> will get raised before <see cref="OnClientCaughtUp"/>.</para>
        /// <para>The <see cref="VRC.SDKBase.VRCPlayerApi"/> for the joining player may not even exist anymore
        /// by the time this event gets raised. It is not part of the game state.</para>
        /// <para>The state of the catching up client in and after this event is either
        /// <see cref="ClientState.Normal"/> or <see cref="ClientState.Master"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnClientCaughtUp,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.LeftPlayerId"/> to get the id of the left client.</para>
        /// <para>It is guaranteed that after this event got raised, not a single input action sent by the
        /// left client shall be received.</para>
        /// <para>The <see cref="VRC.SDKBase.VRCPlayerApi"/> for the left player does not exist anymore by the
        /// time this event gets raised.</para>
        /// <para>This event is not raised on the client which left, lockstep simply stops running as soon as
        /// the local player leaves, unlike VRChat's
        /// <see cref="UdonSharp.UdonSharpBehaviour.OnPlayerLeft(VRC.SDKBase.VRCPlayerApi)"/> event which does
        /// get raised for the local client, at least as of 2024-08-31.</para>
        /// <para>The state of the left client in and after this event is <see cref="ClientState.None"/>, as
        /// the client is no longer in the internal client states game state.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnClientLeft,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.OldMasterPlayerId"/> to get the id of the old master client.
        /// </para>
        /// <para>Use <see cref="LockstepAPI.MasterPlayerId"/> to get the id of the new master client.
        /// (<see cref="LockstepAPI.MasterPlayerId"/> is not limited to the scope of this event.)</para>
        /// <para>It is possible for a client to become master before finishing catching up, in which case
        /// <see cref="OnMasterClientChanged"/> will get raised before <see cref="OnClientCaughtUp"/>.</para>
        /// <para>The state of the old master client in and after this event is
        /// <see cref="ClientState.Normal"/>, for the new master it is
        /// <see cref="ClientState.Master"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnMasterClientChanged,
        /// <summary>
        /// <para>Raised <see cref="LockstepAPI.TickRate"/> times per second.</para>
        /// <para>Raised at the end of a tick. Guaranteed to be the last event in a tick since there are no
        /// events which could instantly get raised through any actions that any <see cref="OnLockstepTick"/>
        /// handler could take.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnLockstepTick,
        /// <summary>
        /// <para>Use "export" related properties on <see cref="LockstepAPI"/> for information about the
        /// started export.</para>
        /// <para>Not game state safe - exporting is local only.</para>
        /// </summary>
        OnExportStart,
        /// <summary>
        /// <para>Use "export" related properties on <see cref="LockstepAPI"/> for information about the
        /// finished export.</para>
        /// <para>Not game state safe - exporting is local only.</para>
        /// </summary>
        OnExportFinished,
        /// <summary>
        /// <para>Use "import" related properties on <see cref="LockstepAPI"/> for information about the
        /// started import.</para>
        /// <para><see cref="LockstepAPI.IsImporting"/> gets set to <see langword="true"/> right before this
        /// event gets raised.</para>
        /// <para>All <see cref="LockstepGameState.OptionsForCurrentImport"/> are still going to be
        /// <see langword="null"/> in this event, use <see cref="OnImportOptionsDeserialized"/> instead if
        /// those are required.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportStart,
        /// <summary>
        /// <para>Use "import" related properties on <see cref="LockstepAPI"/> for information about the
        /// starting import.</para>
        /// <para>Gets raised right after all <see cref="LockstepGameState.OptionsForCurrentImport"/> have
        /// been populated and right before
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// gets run for each game state.</para>
        /// <para>Gets raised regardless of if any game states defined any import options, ultimately making
        /// it a generic "about to start deserializing game states for import" event too.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportOptionsDeserialized,
        /// <summary>
        /// <para>Use "import" related properties on <see cref="LockstepAPI"/> for information about the
        /// imported game state.</para>
        /// <para>Gets raised right after
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> got
        /// run for a game state. <see cref="LockstepAPI.GameStatesBeingImportedFinishedCount"/> gets
        /// incremented right before this event. To get the game state which just got imported
        /// <see cref="LockstepAPI.ImportedGameState"/> can be used, does not require accessing
        /// <see cref="LockstepAPI.GameStatesBeingImported"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportedGameState,
        /// <summary>
        /// <para>Use "import" related properties on <see cref="LockstepAPI"/> for information about the
        /// finished import.</para>
        /// <para><see cref="LockstepAPI.IsImporting"/> gets set to <see langword="false"/> right before this
        /// event gets raised, all other import related properties get reset after this event ran.</para>
        /// <para>Could be raised without any import actually having happened, in which case
        /// <see cref="OnImportOptionsDeserialized"/> did not get raised and
        /// <see cref="LockstepAPI.GameStatesBeingImportedFinishedCount"/> is <c>0</c>. Additionally the
        /// <see cref="OnClientLeft"/> event gets raised shortly after.</para>
        /// <para>Unlike most events, <see cref="LockstepAPI.FlagToContinueNextFrame"/> can be used within
        /// <see cref="OnClientBeginCatchUp"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportFinished,
        /// <summary>
        /// <para>Gets raised immediately after <see cref="OnImportFinished"/>, see there fore more
        /// details. Import related properties are still usable within
        /// <see cref="OnPostImportFinished"/>.</para>
        /// <para>The primary difference between <see cref="OnImportFinished"/> and
        /// <see cref="OnPostImportFinished"/> is that <see cref="LockstepAPI.FlagToContinueNextFrame"/>
        /// cannot be used in <see cref="OnPostImportFinished"/>. All event listeners for this event run
        /// within the same frame, making it a true notification that the import has finished.</para>
        /// <para>Game state safe.</para>
        /// <para><see cref="LockstepAPI.IsImporting"/> is <see langword="false"/> within
        /// <see cref="OnImportFinished"/>, however if <see cref="OnImportFinished"/> ends up spanning
        /// multiple frames, in the time between those frames <see cref="LockstepAPI.IsImporting"/> is set
        /// back to <see langword="true"/>. Therefore when checking the value of
        /// <see cref="LockstepAPI.IsImporting"/> at any point in time (that isn't within some
        /// <see cref="OnImportFinished"/> or <see cref="OnPostImportFinished"/> listener itself), it being
        /// <see langword="false"/> provides the guarantee that every system has received both the
        /// <see cref="OnImportFinished"/> and <see cref="OnPostImportFinished"/> events.</para>
        /// <para>The purpose of this event is enabling systems interacting with other systems, potentially
        /// even unknown systems, to handle finishing up of imports more cleanly. Most systems only need
        /// <see cref="OnImportFinished"/> and the rest of the documentation generally only mentions
        /// <see cref="OnImportFinished"/> for simplicity, even though mentioning both would be more
        /// accurate.</para>
        /// </summary>
        OnPostImportFinished,
        /// <summary>
        /// <para>Raised whenever <see cref="LockstepAPI.ExportOptionsForAutosave"/> changed.</para>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon). Which subsequently means the
        /// value may not actually be different from the last time it was read.</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnExportOptionsForAutosaveChanged,
        /// <summary>
        /// <para>Raised whenever <see cref="LockstepAPI.AutosaveIntervalSeconds"/> changed.</para>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon). Which subsequently means the
        /// value may not actually be different from the last time it was read.</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnAutosaveIntervalSecondsChanged,
        /// <summary>
        /// <para>Raised whenever <see cref="LockstepAPI.IsAutosavePaused"/> changed.</para>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon). Which subsequently means the
        /// value may not actually be different from the last time it was read.</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnIsAutosavePausedChanged,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.NotificationMessage"/> to get the message which was sent.</para>
        /// <para>Notifications are messages sent by lockstep with the intent for them to be shown to the
        /// local player.</para>
        /// <para><see cref="LockstepAPI.InGameStateSafeEvent"/> may be <see langword="true"/> inside of this
        /// event, however as stated below it generally not game state safe because this is not always the
        /// case.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        OnLockstepNotification,
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class LockstepEventAttribute : CustomRaisedEventBaseAttribute
    {
        /// <summary>
        /// <para>The method this attribute gets applied to must be public.</para>
        /// <para>The name of the function this attribute is applied to must have the exact same name as the
        /// name of the <paramref name="eventType"/>.</para>
        /// <para>Event registration is performed at OnBuild, which is to say that scripts with these kinds of
        /// event handlers must exist in the scene at build time, any runtime instantiated objects with these
        /// scripts on them will not receive these events.</para>
        /// <para>Disabled scripts still receive events.</para>
        /// </summary>
        /// <param name="eventType">The event to register this function as a listener to.</param>
        public LockstepEventAttribute(LockstepEventType eventType)
            : base((int)eventType)
        { }
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class LockstepInputActionAttribute : System.Attribute
    {
        private readonly string idFieldName;
        public string IdFieldName => idFieldName;

        /// <summary>
        /// <para>The method this attribute gets applied to must be public.</para>
        /// <para>The id field must be a <see cref="uint"/>. It is the id to use when calling
        /// <see cref="LockstepAPI.SendInputAction(uint)"/>,
        /// <see cref="LockstepAPI.SendSingletonInputAction(uint)"/>, its overload or
        /// <see cref="LockstepAPI.SendEventDelayedTicks(uint, uint)"/>.</para>
        /// <para>The field must be "serialized" in unity, so it must either be public or have the
        /// [<see cref="UnityEngine.SerializeField"/>] attribute. It should definitely have the
        /// [<see cref="UnityEngine.HideInInspector"/>] attribute, as it is not set from the inspector. It is
        /// set by lockstep in an OnBuild handler, so when entering play mode, when uploading or when manually
        /// running handlers through Tools in the unity editor.</para>
        /// <para>Disabled scripts still receive events.</para>
        /// </summary>
        /// <param name="idFieldName">Use <c>nameof(fieldName)</c>.</param>
        public LockstepInputActionAttribute(string idFieldName)
        {
            this.idFieldName = idFieldName;
        }

        /// <summary>
        /// <para>Input actions with <see cref="TrackTiming"/> set to <see langword="true"/> will make
        /// lockstep keep track of the amount of time which passes in the
        /// <see cref="UnityEngine.Time.realtimeSinceStartup"/> scale from
        /// <see cref="LockstepAPI.SendInputAction(uint)"/> or
        /// <see cref="LockstepAPI.SendSingletonInputAction(uint)"/> getting called until the input action
        /// gets run.</para>
        /// <para>This is synced, however not game state safe since timing is going to be different on every
        /// client.</para>
        /// <para>Inside of the input action handler, <see cref="LockstepAPI.SendingTime"/> and
        /// <see cref="LockstepAPI.RealtimeSinceSending"/> can be used to obtain this timing
        /// information.</para>
        /// <para>When timing is needed for late joiners as well, store some tick in a game state and use
        /// <see cref="LockstepAPI.RealtimeAtTick(uint)"/> on the joined client.</para>
        /// <para><see cref="TrackTiming"/> is ignored by
        /// <see cref="LockstepAPI.SendEventDelayedTicks(uint, uint)"/>.</para>
        /// </summary>
        public bool TrackTiming { get; set; } = false;
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public sealed class LockstepOnNthTickAttribute : System.Attribute
    {
        readonly uint interval;
        /// <summary>
        /// <para>On nth tick event handlers are raised whenever <see cref="LockstepAPI.CurrentTick"/> divided
        /// by <see cref="Interval"/> leaves 0 rest.</para>
        /// </summary>
        public uint Interval => interval;

        /// <summary>
        /// <para>On nth tick event handlers are executed ordered by <see cref="Interval"/> ascending, then by
        /// <see cref="Order"/> ascending and then by method name ascending.</para>
        /// <para>Default: 0.</para>
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// <para>The method this attribute gets applied to must be public.</para>
        /// <para>This marks a method as an on nth ick event handler, which is game state safe.</para>
        /// <para>It is raised at the end of the tick, after delayed events
        /// (see <see cref="LockstepAPI.SendEventDelayedTicks(uint, uint)"/>) but before
        /// <see cref="LockstepEventType.OnLockstepTick"/>.</para>
        /// <para>It is raised every <paramref name="interval"/> ticks, meaning whenever
        /// <see cref="LockstepAPI.CurrentTick"/> divided by <paramref name="interval"/> leaves 0 rest.</para>
        /// <para>On nth tick event handlers are executed ordered by <see langword="internal"/> ascending,
        /// then by <see cref="Order"/> ascending and then by method name ascending.</para>
        /// <para>Disabled scripts still receive events.</para>
        /// </summary>
        /// <param name="interval">Must be greater than 0.</param>
        public LockstepOnNthTickAttribute(uint interval)
        {
            this.interval = interval;
        }
    }

    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public sealed class LockstepGameStateDependencyAttribute : System.Attribute
    {
        readonly System.Type gameStateType;
        /// <summary>
        /// <para>The type of a <see cref="LockstepGameState"/> which should be ordered before this the
        /// <see cref="LockstepGameState"/> that the <see cref="LockstepGameStateDependencyAttribute"/> is
        /// applied to.</para>
        /// </summary>
        public System.Type GameStateType => gameStateType;

        /// <summary>
        /// <para>A game state dependency tells Lockstep that serializing and deserializing (including
        /// exporting and importing) of <see cref="LockstepGameState"/>s should happen in a specific
        /// order.</para>
        /// <para>The <see cref="LockstepGameState"/> the <see cref="LockstepGameStateDependencyAttribute"/>
        /// is applied to is going to be ordered after the given <paramref name="gameStateType"/>. After
        /// dependencies they are sorted by <see cref="LockstepGameState.GameStateDisplayName"/> (converted to
        /// lowercase for case insensitivity), and then by
        /// <see cref="LockstepGameState.GameStateInternalName"/> (not converted to lower case to prevent
        /// ambiguity).</para>
        /// <para>Lockstep generally does not support inheritance for <see cref="LockstepGameState"/>, with
        /// the only exception being <see langword="abstract"/> classes to a limited degree. Any
        /// <see langword="abstract"/> <see cref="LockstepGameState"/> must only be derived from/implemented
        /// by a single class. Therefore any <see cref="SingletonDependencyAttribute"/> on an
        /// <see langword="abstract"/> class, or pointing to an <see langword="abstract"/> class, will be
        /// treated as though they were on/pointing to the deriving class implementing the aforementioned
        /// <see langword="abstract"/> class.</para>
        /// <para>This does not enforce that a given game state exists in the scene, if that is desired use
        /// <see cref="SingletonScriptAttribute"/> and <see cref="SingletonReferenceAttribute"/> or
        /// <see cref="SingletonDependencyAttribute"/>.</para>
        /// </summary>
        /// <param name="gameStateType"></param>
        public LockstepGameStateDependencyAttribute(System.Type gameStateType)
        {
            this.gameStateType = gameStateType;
        }
    }
}
