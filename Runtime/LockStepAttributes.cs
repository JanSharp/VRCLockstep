
namespace JanSharp {
    [System.Flags]
    public enum LockStepEventType
    {
        OnInit = 1 << 0,
        ///<summary>Before raising, an int 'lockStepPlayerId' program variable will be set to the joined player's id.</summary>
        OnClientJoined = 1 << 1,
        ///<summary>Before raising, an int 'lockStepPlayerId' program variable will be set to the player's id who is beginning catch up.</summary>
        OnClientBeginCatchUp = 1 << 2,
        ///<summary>Before raising, an int 'lockStepPlayerId' program variable will be set to the player's id who has caught up.</summary>
        OnClientCaughtUp = 1 << 3,
        ///<summary>Before raising, an int 'lockStepPlayerId' program variable will be set to the left player's id.</summary>
        OnClientLeft = 1 << 4,
        ///<summary>Before raising, an int 'lockStepPlayerId' program variable will be set to the new master's id.</summary>
        OnMasterChanged = 1 << 5,
        OnTick = 1 << 6,
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class LockStepEventAttribute : System.Attribute
    {
        private readonly LockStepEventType eventType;
        public LockStepEventType EventType => eventType;

        /// <summary>The method name must match whichever event type is chosen.</summary>
        public LockStepEventAttribute(LockStepEventType eventType)
        {
            this.eventType = eventType;
        }
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class LockStepInputActionAttribute : System.Attribute
    {
        private readonly string idFieldName;
        public string IdFieldName => idFieldName;

        /// <summary>
        /// <para>Use 'nameof(fieldName)' as the parameter.</para>
        /// <para>The id field must be a uint. It is the id to use when calling SendInputAction</para>
        /// <para>The field must be serialized, but doesn't have to be public. It should definitely have the
        /// [HideInInspector] attribute, as it is not set from the inspector.</para>
        /// <para>When it is private it must have the [SerializeField] attribute.</para>
        /// </summary>
        public LockStepInputActionAttribute(string idFieldName)
        {
            this.idFieldName = idFieldName;
        }
    }
}
