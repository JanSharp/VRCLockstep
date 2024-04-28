
namespace JanSharp {
    public enum LockstepEventType
    {
        /// <summary>
        /// <para>The very first event to ever get raised, only ever raised throughout the lifetime of the
        /// entire lockstep system across all clients, so only once on the very first client.</para>
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
        /// <para>Game state safe.</para>
        /// </summary>
        OnInit,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.JoinedPlayerId"/> to get the id of the joined client.</para>
        /// <para>This is the first event to be raised for the joined client, and it is also guaranteed to be
        /// raised before any input actions sent by the joining client are received and run.</para>
        /// <para>Clients to exist in the internal game state a little bit before this event gets raised,
        /// those clients are still waiting on late joiner data and are for all intents and purposes not yet
        /// loaded into the world.</para>
        /// <para>This event is also raised on the client which joined, including the very first client (right
        /// after <see cref="OnInit"/>).</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnClientJoined,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.CatchingUpPlayerId"/> to get the id of the client which is
        /// beginning to catch up.</para>
        /// <para>This is the very first event raised on every client which isn't the very first client, since
        /// for the first client <see cref="OnInit"/> gets raised instead. While
        /// <see cref="OnClientBeginCatchUp"/> is not game state safe, the 2 events serve the same purpose in
        /// allowing other systems - non game states - to initialize and to start accepting user input.</para>
        /// <para>From within this event and going forward sending input actions is allowed.</para>
        /// <para>At this point in time, this client - the local client - is not yet part of the game state in
        /// any of the custom game states. <see cref="OnClientJoined"/> eventually gets raised after this
        /// event.</para>
        /// <para><b>Not game state safe</b> - only raised on one client, the one beginning catch up.</para>
        /// </summary>
        OnClientBeginCatchUp,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.CatchingUpPlayerId"/> to get the id of the client which has
        /// caught up.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnClientCaughtUp,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.LeftPlayerId"/> to get the id of the left client.</para>
        /// <para>It is guaranteed that after this event got raised, not a single input action sent by the
        /// left client shall be received.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnClientLeft,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.OldMasterPlayerId"/> to get the id of the old master client.
        /// </para>
        /// <para>Use <see cref="LockstepAPI.MasterPlayerId"/> to get the id of the new master client.
        /// (<see cref="LockstepAPI.MasterPlayerId"/> is not limited to the scope of this event.)</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnMasterChanged,
        /// <summary>
        /// <para>Raised <see cref="LockstepAPI.TickRate"/> times per second.</para>
        /// <para>Raised at the start of a tick, the very first function to run in a tick. Except for the
        /// very first client which runs <see cref="OnInit"/> right before <see cref="OnTick"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnTick,
        /// <summary>
        /// <para>Use "import" related properties on <see cref="LockstepAPI"/> for information about the
        /// started import.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportStart,
        /// <summary>
        /// <para>Use "import" related properties on <see cref="LockstepAPI"/> for information about the
        /// imported game state.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportedGameState,
        /// <summary>
        /// <para>Use "import" related properties on <see cref="LockstepAPI"/> for information about the
        /// finished import.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportFinished,
        /// <summary>
        /// <para>Raised whenever <see cref="LockstepAPI.GameStatesToAutosave"/> changed.</para>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon). Which subsequently means the
        /// value may not actually be different from the last time you've read it.</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnGameStatesToAutosaveChanged,
        /// <summary>
        /// <para>Raised whenever <see cref="LockstepAPI.AutosaveIntervalSeconds"/> changed.</para>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon). Which subsequently means the
        /// value may not actually be different from the last time you've read it.</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnAutosaveIntervalSecondsChanged,
        /// <summary>
        /// <para>Raised whenever <see cref="LockstepAPI.IsAutosavePaused"/> changed.</para>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon). Which subsequently means the
        /// value may not actually be different from the last time you've read it.</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnIsAutosavePausedChanged,
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class LockstepEventAttribute : System.Attribute
    {
        private readonly LockstepEventType eventType;
        public LockstepEventType EventType => eventType;

        /// <summary>
        /// <para>The name of the function this attribute is applied to must have the exact same name as the
        /// name of the <paramref name="eventType"/>.</para>
        /// <para>Event registration is performed at OnBuild, which is to say that scripts with these kinds of
        /// event handlers must exist in the scene at build time, any runtime instantiated objects with these
        /// scripts on them will not receive these events.</para>
        /// </summary>
        /// <param name="eventType">The event to register this function as a listener to.</param>
        public LockstepEventAttribute(LockstepEventType eventType)
        {
            this.eventType = eventType;
        }

        /// <summary>
        /// <para>The lower the order the sooner this event handler shall be called when the event gets
        /// raised.</para>
        /// <para>If registrations share the same order then their order of execution is undefined.</para>
        /// </summary>
        public int Order { get; set; } = 0;
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class LockstepInputActionAttribute : System.Attribute
    {
        private readonly string idFieldName;
        public string IdFieldName => idFieldName;

        /// <summary>
        /// <para>The id field must be a <see cref="uint"/>. It is the id to use when calling
        /// <see cref="LockstepAPI.SendInputAction(uint)"/>,
        /// <see cref="LockstepAPI.SendSingletonInputAction(uint)"/> or its overload.</para>
        /// <para>The field must be "serialized" in unity, so it must either be public or have the
        /// [<see cref="UnityEngine.SerializeField"/>] attribute. It should definitely have the
        /// [<see cref="UnityEngine.HideInInspector"/>] attribute, as it is not set from the inspector. It is
        /// set by lockstep in an OnBuild handler, so when entering play mode, when uploading or when manually
        /// running handlers through Tools in the unity editor.</para>
        /// </summary>
        /// <param name="idFieldName">Use <c>nameof(fieldName)</c>.</param>
        public LockstepInputActionAttribute(string idFieldName)
        {
            this.idFieldName = idFieldName;
        }
    }
}
