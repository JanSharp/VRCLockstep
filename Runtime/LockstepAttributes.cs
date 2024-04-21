
namespace JanSharp {
    public enum LockstepEventType
    {
        OnInit,
        ///<summary>Before raising, an uint 'lockstepPlayerId' program variable will be set to the joined player's id.</summary>
        OnClientJoined,
        ///<summary>Before raising, an uint 'lockstepPlayerId' program variable will be set to the player's id who is beginning catch up.</summary>
        OnClientBeginCatchUp,
        ///<summary>Before raising, an uint 'lockstepPlayerId' program variable will be set to the player's id who has caught up.</summary>
        OnClientCaughtUp,
        ///<summary>Before raising, an uint 'lockstepPlayerId' program variable will be set to the left player's id.</summary>
        OnClientLeft,
        ///<summary>Before raising, an uint 'lockstepPlayerId' program variable will be set to the new master's id.</summary>
        OnMasterChanged,
        OnTick,
        OnImportStart,
        OnImportedGameState,
        OnImportFinished,
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
