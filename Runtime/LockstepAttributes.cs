
namespace JanSharp {
    public enum LockstepEventType
    {
        /// <summary>
        /// <para>Game state safe.</para>
        /// </summary>
        OnInit,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.JoinedPlayerId"/> to get the id of the joined client.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        OnClientJoined,
        /// <summary>
        /// <para>Use <see cref="LockstepAPI.CatchingUpPlayerId"/> to get the id of the client which is
        /// beginning to catch up.</para>
        /// <para>Not game state safe - only raised on one client, the one beginning catch up.</para>
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
        /// <para>Game state safe.</para>
        /// </summary>
        OnTick,
        /// <summary>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportStart,
        /// <summary>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportedGameState,
        /// <summary>
        /// <para>Game state safe.</para>
        /// </summary>
        OnImportFinished,
        /// <summary>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon).</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnGameStatesToAutosaveChanged,
        /// <summary>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon).</para>
        /// <para>Not game state safe - autosaving is local only.</para>
        /// </summary>
        OnAutosaveIntervalSecondsChanged,
        /// <summary>
        /// <para>Gets raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes
        /// within a frame the event only gets raised once (you can thank Udon).</para>
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
