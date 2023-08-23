
namespace JanSharp {
    [System.Flags]
    public enum LockStepEventType
    {
        OnInit = 1 << 0,
        /// <summary>Will set an int 'lockStepPlayerId' program variable before raising.</summary>
        OnClientJoined = 1 << 1,
        /// <summary>Will set an int 'lockStepPlayerId' program variable before raising.</summary>
        OnClientBeginCatchUp = 1 << 2,
        /// <summary>Will set an int 'lockStepPlayerId' program variable before raising.</summary>
        OnClientCaughtUp = 1 << 3,
        /// <summary>Will set an int 'lockStepPlayerId' program variable before raising.</summary>
        OnClientLeft = 1 << 4,
        OnTick = 1 << 5,
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
        /// <para>When it is private it must have the [System.SerializeField] attribute.</para>
        /// </summary>
        public LockStepInputActionAttribute(string idFieldName)
        {
            this.idFieldName = idFieldName;
        }
    }
}
