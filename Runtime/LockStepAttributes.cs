
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
}
