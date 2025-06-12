using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace JanSharp
{
    public enum ClientState : byte
    {
        Master,
        WaitingForLateJoinerSync,
        CatchingUp,
        Normal,
        /// <summary>
        /// <para>This state represents invalid or non existent clients. It is impossible for any client which
        /// has a state inside of Lockstep to have this state, therefore
        /// <see cref="LockstepAPI.AllClientStates"/> also never returns <see cref="None"/>.</para>
        /// </summary>
        None,
    }

    [SingletonScript("4f5050760fb932762b18e249c98c7afd")] // Runtime/Prefabs/Lockstep.prefab
    public abstract class LockstepAPI : UdonSharpBehaviour
    {
        /// <summary>
        /// <para>The internal tick rate of lockstep. Also the amount of times
        /// <see cref="LockstepEventType.OnLockstepTick"/> gets raised per second.</para>
        /// </summary>
        public const float TickRate = 10f;
        /// <summary>
        /// <para>The amount of times lockstep sends input action tick associations and the current tick from
        /// the lockstep master to other clients per second.</para>
        /// </summary>
        public const float NetworkTickRate = 10f;
        /// <summary>
        /// <para>The current name of the world as defined in the inspector for the Lockstep script (on the
        /// prefab instance). Unless the world creator explicitly set the name it defaults to the scene name,
        /// which likely is not the same as the VRChat world name.</para>
        /// <para>Included in the exported data from
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/>.</para>
        /// <para>There is (naturally) no guarantee for this to uniquely identify a world. Entirely different
        /// worlds could share the same name.</para>
        /// <para>Guaranteed to not be <see langword="null"/>, nor be an empty string, nor contain any
        /// "<c>\n</c>" nor "<c>\r</c>", nor have any leading or trailing space.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract string WorldName { get; }
        /// <summary>
        /// <para>The first tick is <c>1u</c>, not <c>0u</c>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint CurrentTick { get; }
        /// <summary>
        /// <para>Calculate an estimate of when a tick got run. Due to lag spikes or other irregularities in
        /// tick running it is not possible to guarantee this to be accurate, however the majority of the time
        /// its accuracy should be within 1 or maybe 2 ticks worth of time.</para>
        /// <para>For reference, 1 tick lasts for (<c>1f</c> divided by <see cref="TickRate"/>)
        /// seconds.</para>
        /// <para>While <see cref="IsCatchingUp"/> is <see langword="true"/>, passing
        /// <see cref="CurrentTick"/> to this function is likely going to return a notably different value
        /// than <see cref="Time.realtimeSinceStartup"/>. More specifically, it likely returns a smaller
        /// value, a past point in time. This should help synced timing in particular during the initial
        /// catching up period.</para>
        /// <para>Usable inside of
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>,
        /// and then usable again once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        /// <param name="tick">Even though <c>0u</c> is an invalid tick - Lockstep starts at tick <c>1u</c> -
        /// this function accepts it.</param>
        /// <returns>The approximate <see cref="Time.realtimeSinceStartup"/> at which the given
        /// <paramref name="tick"/> got run.</returns>
        public abstract float RealtimeAtTick(uint tick);
        /// <summary>
        /// <para>While this is <see langword="true"/> this client is rapidly running ticks and input actions
        /// in order to catch up to real time.</para>
        /// <para><see langword="true"/> only for the initial catch up period, so starting with
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/>, and stops being <see langword="true"/> a
        /// notable amount of time before receiving the <see cref="LockstepEventType.OnClientCaughtUp"/>
        /// event, since the client finishes catching up and then sends an internal input action to actually
        /// update the internal game state as well as raise the
        /// <see cref="LockstepEventType.OnClientCaughtUp"/> event.</para>
        /// <para>Never <see langword="true"/> on the first client, on which
        /// <see cref="LockstepEventType.OnInit"/> gets/got raised.</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsCatchingUp { get; }
        /// <summary>
        /// <para>When this is <see langword="true"/> sent input actions are going to be run just 1 frame
        /// delayed.</para>
        /// <para>The intended use for this property is disabling latency hiding while this is
        /// <see langword="true"/> if a given latency hiding and latency state implementation is
        /// computationally expensive.</para>
        /// <para>If one were to track the amount of clients in an instance using
        /// <see cref="LockstepEventType.OnClientJoined"/> and <see cref="LockstepEventType.OnClientLeft"/>
        /// then <see cref="IsSinglePlayer"/> will <b>not match</b> the "expected" value based on player
        /// count.</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsSinglePlayer { get; }
        /// <summary>
        /// <para>The length of the <see cref="AllGameStates"/> array. To prevent unnecessary array copies for
        /// when all that's needed is the length/count.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int AllGameStatesCount { get; }
        /// <summary>
        /// <para>An effectively static readonly list of all game states in the world. The getter for this
        /// property returns a copy of the internal array to prevent modifications.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract LockstepGameState[] AllGameStates { get; }
        /// <summary>
        /// <para>The length of the <see cref="GameStatesSupportingImportExport"/> array. To prevent
        /// unnecessary array copies for when all that's needed is the length/count.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int GameStatesSupportingImportExportCount { get; }
        /// <summary>
        /// <para>An effectively static readonly list of all game states in the world for which
        /// <see cref="LockstepGameState.GameStateSupportsImportExport"/> is <see langword="true"/>. The
        /// getter for this property returns a copy of the internal array to prevent modifications.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract LockstepGameState[] GameStatesSupportingImportExport { get; }
        /// <summary>
        /// <para>Note that <see cref="TryGetClientState(uint, out ClientState)"/> can be used to both get the
        /// current state and check if the given client exists, making <see cref="ClientStateExists(uint)"/>
        /// only useful in cases where the only thing being checked is existence.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        /// <param name="playerId">Any player id.</param>
        /// <returns><see langword="true"/> if the given <paramref name="playerId"/> exists in lockstep's
        /// internal client states game state.</returns>
        public abstract bool ClientStateExists(uint playerId);
        /// <summary>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        /// <param name="playerId">Any player id. Non existent ones are valid.</param>
        /// <returns>The current <see cref="ClientState"/> of the given <paramref name="playerId"/>, or
        /// <see cref="ClientState.None"/> if the player does not exist in the internal game state.</returns>
        public abstract ClientState GetClientState(uint playerId);
        /// <summary>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        /// <param name="playerId">Any player id. Non existent ones are valid.</param>
        /// <param name="clientState">The current <see cref="ClientState"/> of the given
        /// <paramref name="playerId"/>, or <see cref="ClientState.None"/> if the player does not exist in the
        /// internal game state.</param>
        /// <returns><see langword="true"/> when the given <paramref name="playerId"/> exists in lockstep's
        /// internal game state.</returns>
        public abstract bool TryGetClientState(uint playerId, out ClientState clientState);
        /// <summary>
        /// <para>The current amount of players in lockstep's internal client states game state.</para>
        /// <para>When <see cref="AllClientPlayerIds"/> and <see cref="AllClientStates"/> are
        /// <see langword="null"/>, this simply returns <c>0</c>.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int ClientStatesCount { get; }
        /// <summary>
        /// <para>Returns a copy of the current list of all player ids in lockstep's internal client states
        /// game state.</para>
        /// <para>Ordered ascending.</para>
        /// <para>Usable any time, however before <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised this may be <see langword="null"/>.
        /// After that this is never <see langword="null"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint[] AllClientPlayerIds { get; }
        /// <summary>
        /// <para>Returns a copy of the current list of all players' client states in lockstep's internal
        /// game state.</para>
        /// <para>Order of client states matches the player ids in <see cref="AllClientPlayerIds"/>.</para>
        /// <para>Usable any time, however before <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised this may be <see langword="null"/>.
        /// After that this is never <see langword="null"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract ClientState[] AllClientStates { get; }
        /// <summary>
        /// <para>Returns a copy of the current list of all players' display names in lockstep's internal
        /// client states game state.</para>
        /// <para>Order of display names matches the player ids in <see cref="AllClientPlayerIds"/>.</para>
        /// <para>Usable any time, however before <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised this may be <see langword="null"/>.
        /// After that this is never <see langword="null"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract string[] AllClientDisplayNames { get; }
        /// <summary>
        /// <para>Convert a <see cref="ClientState"/> to a human readable string.</para>
        /// <para>Very similar to how <see cref="object.ToString"/> works for enums in normal C#, but since in
        /// Udon enums don't exist at runtime, they're just numbers, calling ToString directly on them just
        /// gives you the internal number of the enum. This function gives you a readable string, unless the
        /// underlying value is outside of the bounds of a valid enum, then it just returns the number
        /// converted to a string.</para>
        /// </summary>
        /// <param name="clientState">Any client state to convert to a string.</param>
        /// <returns>Never <see langword="null"/>.</returns>
        public abstract string ClientStateToString(ClientState clientState);
        /// <summary>
        /// <para>Guaranteed to be <see langword="true"/> on exactly 1 client during the execution of any game
        /// state safe event. Outside of those it is possible for this to be true for 0 clients at some point
        /// in time.</para>
        /// <para>If the goal is to send an input action from only 1 client even though the running function
        /// is a game state event and therefore runs on all clients, it is most likely preferable to use
        /// <see cref="SendSingletonInputAction(uint)"/> or its overload instead of checking
        /// <see cref="IsMaster"/> as that is exactly what that function is made for. However outside of game
        /// state safe events <see cref="SendSingletonInputAction(uint)"/> cannot be used, so as a mostly
        /// reliable alternative <see cref="IsMaster"/> may do the trick.</para>
        /// <para>This does not match <see cref="Networking.IsMaster"/>. This <see cref="IsMaster"/> relates
        /// to the lockstep master which could be any client, though by default the 2 masters are often the
        /// same.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsMaster { get; }
        /// <summary>
        /// <para>The player id of the lockstep master, which differs from VRChat's master. The lockstep
        /// master is effectively the acting server for the networking system.</para>
        /// <para>Which client may be lockstep master is undefined. When the master leaves the system will
        /// simply choose some player which has been in the instance long enough to know the game state.
        /// Outside of that there are no automatic master changes, however the master can be changed through
        /// <see cref="RequestLocalClientToBecomeMaster"/> or <see cref="SendMasterChangeRequestIA(uint)"/>.
        /// </para>
        /// <para>If the master leaves, this id remains unchanged until the
        /// <see cref="LockstepEventType.OnMasterClientChanged"/> event is raised.</para>
        /// <para><see cref="IsMaster"/> may be <see langword="true"/> even before
        /// <see cref="MasterPlayerId"/> is equal to the local player id.</para>
        /// <para><see cref="VRCPlayerApi"/> is not guaranteed to be valid for the given player id.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint MasterPlayerId { get; }
        /// <summary>
        /// <para>The id of the client which was the master right before the new master client.</para>
        /// <para><see cref="VRCPlayerApi"/> is not guaranteed to be valid for the given player id.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnMasterClientChanged"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint OldMasterPlayerId { get; }
        /// <summary>
        /// <para>The id of the joined client.</para>
        /// <para><see cref="VRCPlayerApi"/> is not guaranteed to be valid for the given player id.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnClientJoined"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint JoinedPlayerId { get; }
        /// <summary>
        /// <para>The id of the left client.</para>
        /// <para><see cref="VRCPlayerApi"/> is not guaranteed to be valid for the given player id.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnClientLeft"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint LeftPlayerId { get; }
        /// <summary>
        /// <para>The id of the client which is beginning to catch up or has caught up.</para>
        /// <para><see cref="VRCPlayerApi"/> is not guaranteed to be valid for the given player id.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnClientBeginCatchUp"/> and
        /// <see cref="LockstepEventType.OnClientCaughtUp"/>.</para>
        /// <para>Game state safe (but keep in mind that <see cref="LockstepEventType.OnClientBeginCatchUp"/>
        /// is not a game state safe event. <see cref="LockstepEventType.OnClientCaughtUp"/> is
        /// however).</para>
        /// </summary>
        public abstract uint CatchingUpPlayerId { get; }
        /// <summary>
        /// <para>The player id of the client which sent the currently running input action. It is guaranteed
        /// to be an id for which the <see cref="LockstepEventType.OnClientJoined"/> event has been raised,
        /// and the <see cref="LockstepEventType.OnClientLeft"/> event has not been raised.</para>
        /// <para><see cref="VRCPlayerApi"/> is not guaranteed to be valid for the given player id.</para>
        /// <para>Usable inside of input actions.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint SendingPlayerId { get; }
        /// <summary>
        /// <para>The unique id of the input action that is currently running, which is the same unique id
        /// returned by <see cref="SendInputAction(uint)"/>, <see cref="SendSingletonInputAction(uint)"/>
        /// and its overload.</para>
        /// <para>Never 0uL (see note below), since that is an invalid unique id.</para>
        /// <para>Note that even though event handlers for <see cref="SendEventDelayedTicks(uint, uint)"/> are
        /// defined the same way as input actions, <see cref="SendingUniqueId"/> is going to be 0uL in
        /// those.</para>
        /// <para>The intended purpose is making implementations of latency state and latency hiding easier.
        /// </para>
        /// <para>Usable inside of input actions.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract ulong SendingUniqueId { get; }
        /// <summary>
        /// <para>For input actions which have <see cref="LockstepInputActionAttribute.TrackTiming"/> set to
        /// <see langword="true"/> this will be the point in time at which <see cref="SendInputAction(uint)"/>
        /// or <see cref="SendSingletonInputAction(uint)"/> got called, in the
        /// <see cref="Time.realtimeSinceStartup"/> scale.</para>
        /// <para><see cref="RealtimeSinceSending"/> exists as a shorthand to get the amount of time which has
        /// passed since sending until now.</para>
        /// <para>For those with <see cref="LockstepInputActionAttribute.TrackTiming"/> set to
        /// <see langword="false"/> this will be <see cref="float.NaN"/>.</para>
        /// <para>Usable inside of input actions.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract float SendingTime { get; }
        /// <summary>
        /// <para>For input actions which have <see cref="LockstepInputActionAttribute.TrackTiming"/> set to
        /// <see langword="true"/> this will be the amount of time which has passed since
        /// <see cref="SendInputAction(uint)"/> or <see cref="SendSingletonInputAction(uint)"/> got called
        /// until now, in the <see cref="Time.realtimeSinceStartup"/> scale.</para>
        /// <para>(It is basically just the current <see cref="Time.realtimeSinceStartup"/> minus
        /// <see cref="SendingTime"/>.)</para>
        /// <para>For those with <see cref="LockstepInputActionAttribute.TrackTiming"/> set to
        /// <see langword="false"/> this will be <see cref="float.NaN"/>.</para>
        /// <para>Usable inside of input actions.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract float RealtimeSinceSending { get; }
        /// <summary>
        /// <para>Enables checking if <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> have been raised. Gets set to
        /// <see langword="true"/> right before those events.</para>
        /// <para>Many things only work once either of those two events have been raised, things like
        /// <see cref="SendInputAction(uint)"/>, <see cref="SendSingletonInputAction(uint)"/> and its
        /// overload, <see cref="SendEventDelayedTicks(uint, uint)"/>, export and import
        /// <see cref="LockstepGameStateOptionsData"/> related apis,
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/> and
        /// <see cref="StartImport(object[][], System.DateTime, string, string)"/>, etc, ect.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract bool IsInitialized { get; }
        /// <summary>
        /// <para><see langword="true"/> when the currently running function is in a game state safe context,
        /// such as input actions, delayed events, game state deserialization, and most other lockstep events.
        /// Each <see cref="LockstepEventType"/> states whether it is game state safe or not.</para>
        /// </summary>
        public abstract bool InGameStateSafeEvent { get; }
        /// <summary>
        /// <para>The message which lockstep sent as a notification with the intent for it to be shown to the
        /// local player.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnLockstepNotification"/>.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract string NotificationMessage { get; }
        /// <summary>
        /// <para>Send an input action from one client which, since it is an input action, will then run on
        /// all clients in the same tick in the same order.</para>
        /// <para>To pass data from the sending client to the input action, use <c>Write</c> functions before
        /// calling <see cref="SendInputAction(uint)"/>. Then on the receiving side call <c>Read</c> functions
        /// with matching types and in matching order.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="inputActionId">The id associated with a method with the
        /// <see cref="LockstepInputActionAttribute"/> to be sent.</param>
        /// <returns>The unique id of the input action that got sent. If <see cref="IsInitialized"/> is
        /// <see langword="false"/> then 0uL - an invalid id - indicating that it did not get sent will be
        /// returned. Inside of the input action <see cref="SendingUniqueId"/> can then be used in order to
        /// match it up with some part of the latency state, for example.</returns>
        public abstract ulong SendInputAction(uint inputActionId);
        /// <summary>
        /// <para>Send an input action from one client which, since it is an input action, will then run on
        /// all clients in the same tick in the same order.</para>
        /// <para>To pass data from the sending client to the input action, use <c>Write</c> functions before
        /// calling <see cref="SendInputAction(uint)"/>. Then on the receiving side call <c>Read</c> functions
        /// with matching types and in matching order.</para>
        /// <para>The major difference is that <see cref="SendInputAction(uint)"/> simply sends an input
        /// action, meaning if multiple players call it, multiple input actions will be sent. This may be
        /// undesirable when inside of a game state safe event since that runs on every client, however there
        /// may be some non game state data that must be input into the game state. This is what the
        /// <see cref="SendSingletonInputAction(uint)"/> and
        /// <see cref="SendSingletonInputAction(uint, uint)"/> are for. They guarantee that the input action
        /// is sent exactly once, even if the initial responsible player leaves immediately after sending the
        /// input action, in which case a different client becomes responsible for sending it.</para>
        /// <para>Usable inside of game state safe events.</para>
        /// </summary>
        /// <param name="inputActionId">The id associated with a method with the
        /// <see cref="LockstepInputActionAttribute"/> to be sent.</param>
        /// <returns>The unique id of the input action that got sent. If <see cref="IsInitialized"/> is
        /// <see langword="false"/> then 0uL - an invalid id - indicating that it did not get sent will be
        /// returned. The unique id is only returned on the initial responsible client, on all other clients
        /// it is going to be 0uL. Inside of the input action <see cref="SendingUniqueId"/> can then be used
        /// in order to match it up with some part of the latency state, for example.</returns>
        public abstract ulong SendSingletonInputAction(uint inputActionId);
        /// <inheritdoc cref="SendSingletonInputAction(uint)"/>
        /// <param name="responsiblePlayerId">The player id of the client which takes initial responsibility
        /// of sending the input action.</param>
        public abstract ulong SendSingletonInputAction(uint inputActionId, uint responsiblePlayerId);
        /// <summary>
        /// <para>Send an event from a game state safe event delayed by 1 or more ticks. The event is
        /// subsequently also going to be a game state safe event.</para>
        /// <para>Delayed events are run in the order in which they get sent.</para>
        /// <para>Delayed events are raised at the end of a tick, but before
        /// <see cref="LockstepOnNthTickAttribute"/> and
        /// <see cref="LockstepEventType.OnLockstepTick"/>.</para>
        /// <para>To pass data from the sending function to the event handler, use <c>Write</c> functions
        /// before calling <see cref="SendEventDelayedTicks(uint, uint)"/>. Then in the event handler call
        /// <c>Read</c> functions with matching types and in matching order.</para>
        /// <para>Even though delayed events use a lot of the same infrastructure as input actions, <b>the
        /// data passed from the sending event to the receiving event must be game state safe</b>, unlike
        /// input actions, because it will not be sent over the network. (Except for clients joining after a
        /// delayed event got sent, but before it got run, then the delayed event's data will be sent to the
        /// joining client.)</para>
        /// <para>When it comes to import export support, unfortunately delayed events further complicate the
        /// process. Events which got sent before an import occurs, but get run after an import occurred may
        /// require additional handling, as the state they're related to may no longer exist or be
        /// mutated making the delayed event irrelevant or even invalid.</para>
        /// <para>Usable only inside of game state safe events.</para>
        /// </summary>
        /// <param name="inputActionId">The id associated with a method with the
        /// <see cref="LockstepInputActionAttribute"/> to be sent.</param>
        /// <param name="tickDelay"><para><c>0u</c> is an invalid delay.</para>
        /// <para>The amount of ticks to delay this event by. The given event will run in
        /// in the <see cref="CurrentTick"/> plus <paramref name="tickDelay"/>. There are
        /// <see cref="TickRate"/> ticks per second.</para></param>
        public abstract void SendEventDelayedTicks(uint inputActionId, uint tickDelay);
        /// <summary>
        /// <para>Simply a wrapper around <see cref="SendMasterChangeRequestIA(uint)"/> with the local
        /// client's id passed in as the new master client id.</para>
        /// </summary>
        /// <returns>Returns <see langword="true"/> if the input action was sent successfully.</returns>
        public abstract bool RequestLocalClientToBecomeMaster();
        /// <summary>
        /// <para>Sends an input action requesting to change the current lockstep master.</para>
        /// <para>It is not guaranteed that it will actually change master to the given client id, due to edge
        /// cases around players leaving. If it is desired to really force a client to become master then it
        /// may be best to listen to the <see cref="LockstepEventType.OnMasterClientChanged"/> event to check
        /// if the desired client actually became master, and if not then re send the request. Just make sure
        /// to check if the desired client is even still in the world, for example using
        /// <see cref="ClientStateExists(uint)"/>.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="newMasterClientId"><para>The client id which should become the lockstep
        /// master.</para>
        /// <para>If the given client id is already master then no request is sent.</para>
        /// <para>Giving this a client id which does not exist in lockstep's internal client states game state
        /// is invalid.</para>
        /// </param>
        /// <returns>Returns <see langword="true"/> if the input action was sent successfully. It will only be
        /// sent if <see cref="IsInitialized"/> is <see langword="true"/>, if the
        /// <paramref name="newMasterClientId"/> is different than the <see cref="MasterPlayerId"/>, if the
        /// <see cref="ClientState"/> of the <paramref name="newMasterClientId"/> is
        /// <see cref="ClientState.Normal"/> and if
        /// there is no master change request currently in progress.</returns>
        public abstract bool SendMasterChangeRequestIA(uint newMasterClientId);
        /// <summary>
        /// <para>The display names are saved in an internal lockstep game state. They are available starting
        /// from within <see cref="LockstepEventType.OnClientJoined"/> all the way until the end of
        /// <see cref="LockstepEventType.OnClientLeft"/> - even though by the time
        /// <see cref="LockstepEventType.OnClientLeft"/> runs the client is no longer in the internal game
        /// state. This is a special case with special handling to make the name still available there.</para>
        /// <para>Since they are saved and handled as an internal game state, the display names are the same
        /// on all clients and unchanging.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="playerId">The <see cref="VRCPlayerApi.playerId"/> (obtained through any means) of a
        /// client in lockstep's internal game state.</param>
        /// <returns>Display name for the given <paramref name="playerId"/>, or <see langword="null"/> if the
        /// given <paramref name="playerId"/> is not in the lockstep internal game state.</returns>
        public abstract string GetDisplayName(uint playerId);

        /// <summary>
        /// <para>While inside of
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/> or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> or
        /// any input action, singleton input action or delayed action event handler, calling
        /// <see cref="FlagToContinueNextFrame"/> tells lockstep to pause any and all regular processing, no
        /// game state safe events will get run, no ticks being advanced, until next frame where the event
        /// handler which called <see cref="FlagToContinueNextFrame"/> gets called again.
        /// <see cref="IsContinuationFromPrevFrame"/> is going to be <see langword="true"/> inside of the
        /// event handler in this case.</para>
        /// <para>It may call <see cref="FlagToContinueNextFrame"/> again.</para>
        /// <para>Once the event handler does not call <see cref="FlagToContinueNextFrame"/>, it indicates
        /// that it is done processing incoming data (or in the case of game state serialization done
        /// serializing) and lockstep can resume normal operation.</para>
        /// <para>Usable only inside of
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/>,
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> and
        /// inside of game state safe events which have serialized data to deserialize. So notably
        /// <see cref="LockstepEventType"/> and <see cref="LockstepOnNthTickAttribute"/> are excluded.</para>
        /// </summary>
        public abstract void FlagToContinueNextFrame();
        /// <summary>
        /// <para>Gets set to <see langword="true"/> by <see cref="FlagToContinueNextFrame"/>.</para>
        /// <para>The purpose of this being exposed is to allow systems which wrap around callbacks into other
        /// systems to check if those callbacks called <see cref="FlagToContinueNextFrame"/>.</para>
        /// <para>Though it can of course also be used within the same system, cross system interaction merely
        /// demonstrates the purpose more clearly.</para>
        /// <para>Usable only inside of
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/>,
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> and
        /// inside of game state safe events which have serialized data to deserialize. So notably
        /// <see cref="LockstepEventType"/> and <see cref="LockstepOnNthTickAttribute"/> are excluded.</para>
        /// <para>Game state safe, however mind that
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/> in
        /// particular is not a game state safe event.</para>
        /// </summary>
        public abstract bool FlaggedToContinueNextFrame { get; }
        /// <summary>
        /// <para><see langword="true"/> when inside of an event handler which interrupted itself by calling
        /// <see cref="FlagToContinueNextFrame"/> the last time it was called, which was in the previous
        /// frame.</para>
        /// <para>Usable only inside of
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/>,
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> and
        /// inside of game state safe events which have serialized data to deserialize. So notably
        /// <see cref="LockstepEventType"/> and <see cref="LockstepOnNthTickAttribute"/> are excluded.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract bool IsContinuationFromPrevFrame { get; }

        /// <summary>
        /// <para>Creates a new array and uses <see cref="LockstepGameStateOptionsData.Clone"/> to populate
        /// it.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="allOptions">Must not be <see langword="null"/>.</param>
        /// <returns></returns>
        public abstract LockstepGameStateOptionsData[] CloneAllOptions(
            LockstepGameStateOptionsData[] allOptions);
        /// <summary>
        /// <para>Cleans up such that the given <paramref name="allExportOptions"/> array can go out of scope
        /// without leaving <see cref="LockstepGameStateOptionsData"/> behind, which would cause a memory
        /// leak since those are <see cref="WannaBeClass"/>es.</para>
        /// <para>Calls <see cref="WannaBeClass.DecrementRefsCount"/> on each given non <see langword="null"/>
        /// options instance.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="allExportOptions"></param>
        public abstract void CleanupAllExportOptions(LockstepGameStateOptionsData[] allExportOptions);
        /// <summary>
        /// <para>Cleans up such that the given <paramref name="allImportOptions"/> array can go out of scope
        /// without leaving <see cref="LockstepGameStateOptionsData"/> behind, which would cause a  memory
        /// leak since those are <see cref="WannaBeClass"/>es.</para>
        /// <para>Calls <see cref="WannaBeClass.DecrementRefsCount"/> on each given options instance.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="allImportOptions">Keys: <see cref="string"/>
        /// <see cref="LockstepGameState.GameStateInternalName"/>,<br/>
        /// Values: <see cref="LockstepGameStateOptionsData"/> <c>importOptions</c>.</param>
        public abstract void CleanupAllImportOptions(DataDictionary allImportOptions);
        /// <summary>
        /// <para>Cleans up such that the given <paramref name="importedGameStates"/> array can go out of
        /// scope without leaving <see cref="LockstepGameStateOptionsData"/> behind, which would cause a
        /// memory leak since those are <see cref="WannaBeClass"/>es.</para>
        /// <para>Calls <see cref="WannaBeClass.DecrementRefsCount"/> on each given non <see langword="null"/>
        /// <see cref="LockstepImportedGS.GetImportOptions(object[])"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="importedGameStates">An array containing <see cref="LockstepImportedGS"/> objects
        /// obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>.</param>
        public abstract void CleanupImportedGameStatesData(object[][] importedGameStates);
        /// <summary>
        /// <para>Creates a new array with new instances of options data matching the game states in
        /// <see cref="GameStatesSupportingImportExport"/>. The array will contain <see langword="null"/> for
        /// each game state which has a <see langword="null"/>
        /// <see cref="LockstepGameState.ExportUI"/>.</para>
        /// <para>Uses <see cref="LockstepGameStateOptionsUI.NewOptions"/> to create options instances.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <returns></returns>
        public abstract LockstepGameStateOptionsData[] GetNewExportOptions();
        /// <summary>
        /// <para>Fills in a new options instance, using <see cref="LockstepGameStateOptionsUI.NewOptions"/>,
        /// for each <see langword="null"/> entry where the associated game state in
        /// <see cref="GameStatesSupportingImportExport"/> has a non <see langword="null"/>
        /// <see cref="LockstepGameState.ExportUI"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="allExportOptions"></param>
        /// <returns>The amount of new options instances it had to create.</returns>
        public abstract int FillInMissingExportOptionsWithDefaults(
            LockstepGameStateOptionsData[] allExportOptions);
        /// <summary>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <returns><see langword="true"/> if <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> is
        /// <see langword="true"/> for any non <see langword="null"/> <see cref="LockstepGameState.ExportUI"/>
        /// in all <see cref="GameStatesSupportingImportExport"/>.</returns>
        public abstract bool AnyExportOptionsCurrentlyShown();
        /// <summary>
        /// <para>Calls <see cref="LockstepGameStateOptionsUI.UpdateCurrentOptionsFromWidgets"/> on each non
        /// <see langword="null"/> <see cref="LockstepGameState.ExportUI"/> where
        /// <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> is <see langword="true"/> in all
        /// <see cref="GameStatesSupportingImportExport"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public abstract void UpdateAllCurrentExportOptionsFromWidgets();
        /// <summary>
        /// <para>Creates a new array with references to
        /// <see cref="LockstepGameStateOptionsUI.CurrentOptions"/> matching the game states in
        /// <see cref="GameStatesSupportingImportExport"/>. The array will contain <see langword="null"/> for
        /// each game state which has a <see langword="null"/>
        /// <see cref="LockstepGameState.ExportUI"/>.</para>
        /// <para>First calls <see cref="LockstepGameStateOptionsUI.UpdateCurrentOptionsFromWidgets"/> and
        /// then fetches the <see cref="LockstepGameStateOptionsUI.CurrentOptions"/> to populate the
        /// array. Ignores <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/>, unlike
        /// <see cref="UpdateAllCurrentExportOptionsFromWidgets"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="weakReferences">When <see langword="false"/>,
        /// <see cref="WannaBeClass.IncrementRefsCount"/> is called on each returned non
        /// <see langword="null"/> <see cref="LockstepGameStateOptionsData"/> instance.</param>
        /// <returns></returns>
        public abstract LockstepGameStateOptionsData[] GetAllCurrentExportOptions(bool weakReferences);
        /// <summary>
        /// <para>Calls <see cref="LockstepGameStateOptionsUI.ValidateOptions(LockstepGameStateOptionsData)"/>
        /// on each given non <see langword="null"/> options instance.</para>
        /// <para>Of course this can only be done on each game state with non <see langword="null"/>
        /// <see cref="LockstepGameState.ExportUI"/>, however for well formed
        /// <paramref name="allExportOptions"/> this is no concern. Having non <see langword="null"/>
        /// <see cref="LockstepGameStateOptionsData"/> with an associated <see langword="null"/>
        /// <see cref="LockstepGameState.ExportUI"/> does not make sense.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="allExportOptions">Must not be <see langword="null"/>.</param>
        public abstract void ValidateExportOptions(LockstepGameStateOptionsData[] allExportOptions);
        /// <summary>
        /// <para><see cref="LockstepGameStateOptionsUI.HideOptionsEditor"/> all
        /// <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> editor UIs and calls
        /// <see cref="LockstepGameStateOptionsUI.ShowOptionsEditor(LockstepOptionsEditorUI,
        /// LockstepGameStateOptionsData, uint)"/> for each non <see langword="null"/> entry in
        /// <paramref name="allExportOptions"/> using the matching (and also non <see langword="null"/>)
        /// <see cref="LockstepGameState.ExportUI"/> from
        /// <see cref="GameStatesSupportingImportExport"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="ui">The ui instance which gets passed along to
        /// <see cref="LockstepGameStateOptionsUI.ShowOptionsEditor(LockstepOptionsEditorUI,
        /// LockstepGameStateOptionsData, uint)"/>.</param>
        /// <param name="allExportOptions">Must not be <see langword="null"/>.</param>
        public abstract void ShowExportOptionsEditor(
            LockstepOptionsEditorUI ui,
            LockstepGameStateOptionsData[] allExportOptions);
        /// <summary>
        /// <para><see cref="LockstepGameStateOptionsUI.HideOptionsEditor"/> all
        /// <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> non <see langword="null"/>
        /// <see cref="LockstepGameState.ExportUI"/> in <see cref="GameStatesSupportingImportExport"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public abstract void HideExportOptionsEditor();
        /// <summary>
        /// <para>Creates a new dictionary with new instances of options data matching the game states in
        /// <see cref="GameStatesSupportingImportExport"/>. For <see langword="null"/>
        /// <see cref="LockstepGameState.ImportUI"/> no key value pair is added to the dictionary.</para>
        /// <para>Uses <see cref="LockstepGameStateOptionsUI.NewOptions"/> to create options instances.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <returns>Keys: <see cref="string"/> <see cref="LockstepGameState.GameStateInternalName"/>,<br/>
        /// Values: <see cref="LockstepGameStateOptionsData"/> <c>importOptions</c>.</returns>
        public abstract DataDictionary GetNewImportOptions();
        /// <summary>
        /// <para>Fills in a new options instance, using <see cref="LockstepGameStateOptionsUI.NewOptions"/>,
        /// for each non <see langword="null"/> <see cref="LockstepGameState.ImportUI"/> in
        /// <see cref="GameStatesSupportingImportExport"/> where <paramref name="allImportOptions"/> does not
        /// contain a <see cref="LockstepGameStateOptionsData"/> for the given
        /// <see cref="LockstepGameState.GameStateInternalName"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="allImportOptions">Keys: <see cref="string"/>
        /// <see cref="LockstepGameState.GameStateInternalName"/>,<br/>
        /// Values: <see cref="LockstepGameStateOptionsData"/> <c>importOptions</c>.</param>
        /// <returns>The amount of new options instances it had to create.</returns>
        public abstract int FillInMissingImportOptionsWithDefaults(DataDictionary allImportOptions);
        /// <summary>
        /// <para><see cref="LockstepImportedGS.SetImportOptions(object[], LockstepGameStateOptionsData)"/> a
        /// new options instance, using <see cref="LockstepGameStateOptionsUI.NewOptions"/>, for each
        /// <see cref="LockstepImportedGS"/> entry where
        /// <see cref="LockstepImportedGS.GetImportOptions(object[])"/> is <see langword="null"/> where the
        /// associated game state in <see cref="GameStatesSupportingImportExport"/> has a non
        /// <see langword="null"/> <see cref="LockstepGameState.ImportUI"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="importedGameStates">An array containing <see cref="LockstepImportedGS"/> objects
        /// obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>.</param>
        /// <returns>The amount of new options instances it had to create.</returns>
        public abstract int FillInMissingImportOptionsWithDefaults(object[][] importedGameStates);
        /// <summary>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <returns><see langword="true"/> if <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> is
        /// <see langword="true"/> for any non <see langword="null"/> <see cref="LockstepGameState.ImportUI"/>
        /// in all <see cref="GameStatesSupportingImportExport"/>.</returns>
        public abstract bool AnyImportOptionsCurrentlyShown();
        /// <summary>
        /// <para>Calls <see cref="LockstepGameStateOptionsUI.UpdateCurrentOptionsFromWidgets"/> on each non
        /// <see langword="null"/> <see cref="LockstepGameState.ImportUI"/> where
        /// <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> is <see langword="true"/> in all
        /// <see cref="GameStatesSupportingImportExport"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public abstract void UpdateAllCurrentImportOptionsFromWidgets();
        /// <summary>
        /// <para>Creates a new dictionary with references to
        /// <see cref="LockstepGameStateOptionsUI.CurrentOptions"/> matching the game states in
        /// <see cref="GameStatesSupportingImportExport"/> which have non <see langword="null"/>
        /// <see cref="LockstepGameState.ImportUI"/>.</para>
        /// <para>First calls <see cref="LockstepGameStateOptionsUI.UpdateCurrentOptionsFromWidgets"/> and
        /// then fetches the <see cref="LockstepGameStateOptionsUI.CurrentOptions"/> to populate the
        /// dictionary. Ignores <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/>, unlike
        /// <see cref="UpdateAllCurrentImportOptionsFromWidgets"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="weakReferences">When <see langword="false"/>,
        /// <see cref="WannaBeClass.IncrementRefsCount"/> is called on each returned
        /// <see cref="LockstepGameStateOptionsData"/> instance.</param>
        /// <returns>Keys: <see cref="string"/> <see cref="LockstepGameState.GameStateInternalName"/>,<br/>
        /// Values: <see cref="LockstepGameStateOptionsData"/> <c>importOptions</c>.</returns>
        public abstract DataDictionary GetAllCurrentImportOptions(bool weakReferences);
        /// <summary>
        /// <para>For each entry where <see cref="LockstepImportedGS.GetGameState(object[])"/> is non
        /// <see langword="null"/> and <see cref="LockstepGameState.ImportUI"/> is also non
        /// <see langword="null"/>, tries getting an <see cref="LockstepGameStateOptionsData"/> instance from
        /// <paramref name="allImportOptions"/> using the
        /// <see cref="LockstepGameState.GameStateInternalName"/> as the key, and uses that to
        /// <see cref="LockstepImportedGS.SetImportOptions(object[], LockstepGameStateOptionsData)"/>. If no
        /// options were found it assigns <see langword="null"/>.</para>
        /// <para>If there were any non <see langword="null"/>
        /// <see cref="LockstepImportedGS.GetImportOptions(object[])"/> previously, calls
        /// <see cref="WannaBeClass.DecrementRefsCount"/> on those.</para>
        /// <para>Calls <see cref="WannaBeClass.IncrementRefsCount"/> on each options instance which got a
        /// reference saved in <paramref name="importedGameStates"/>.</para>
        /// <para>This could be described as <paramref name="importedGameStates"/> holding strong references
        /// to import options, therefore the incrementing and decrementing of the refs counter. Also note
        /// <see cref="CleanupAllImportOptions(DataDictionary)"/> and
        /// <see cref="CleanupImportedGameStatesData(object[][])"/> which should very most likely be used in
        /// most cases before letting <paramref name="allImportOptions"/> or
        /// <paramref name="importedGameStates"/> go out of scope.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="importedGameStates">An array containing <see cref="LockstepImportedGS"/> objects
        /// obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>.</param>
        /// <param name="allImportOptions">Keys: <see cref="string"/>
        /// <see cref="LockstepGameState.GameStateInternalName"/>,<br/>
        /// Values: <see cref="LockstepGameStateOptionsData"/> <c>importOptions</c>.</param>
        public abstract void AssociateImportOptionsWithImportedGameStates(
            object[][] importedGameStates,
            DataDictionary allImportOptions);
        /// <summary>
        /// <para>Calls <see cref="LockstepGameStateOptionsUI.ValidateOptions(LockstepGameStateOptionsData)"/>
        /// on each given options instance.</para>
        /// <para>Of course this can be done on each game state with non <see langword="null"/>
        /// <see cref="LockstepGameState.ImportUI"/>, however for well formed
        /// <paramref name="allImportOptions"/> this is no concern. Having non <see langword="null"/>
        /// <see cref="LockstepGameStateOptionsData"/> with an associated <see langword="null"/>
        /// <see cref="LockstepGameState.ImportUI"/> does not make sense.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="allImportOptions">Keys: <see cref="string"/>
        /// <see cref="LockstepGameState.GameStateInternalName"/>,<br/>
        /// Values: <see cref="LockstepGameStateOptionsData"/> <c>importOptions</c>.</param>
        public abstract void ValidateImportOptions(DataDictionary allImportOptions);
        /// <summary>
        /// <para><see cref="LockstepGameStateOptionsUI.HideOptionsEditor"/> all
        /// <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> editor UIs and calls
        /// <see cref="LockstepGameStateOptionsUI.ShowOptionsEditor(LockstepOptionsEditorUI,
        /// LockstepGameStateOptionsData, uint)"/> for each non
        /// <see langword="null"/> <see cref="LockstepImportedGS.GetImportOptions(object[])"/> in
        /// <paramref name="importedGameStates"/> using the matching (and also
        /// non <see langword="null"/>) <see cref="LockstepGameState.ImportUI"/> from
        /// <see cref="GameStatesSupportingImportExport"/>.</para>
        /// <para>Importantly it also <see cref="SetReadStream(byte[])"/> the
        /// <see cref="LockstepImportedGS.GetBinaryData(object[])"/> before calling
        /// <see cref="LockstepGameStateOptionsUI.ShowOptionsEditor(LockstepOptionsEditorUI,
        /// LockstepGameStateOptionsData, uint)"/>, which is required by the specification of the
        /// <c>ShowOptionsEditor</c> api in order to allow import options to read data from the associated
        /// serialized game state to populate dynamic options depending on the data to be imported.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="ui">The ui instance which gets passed along to
        /// <see cref="LockstepGameStateOptionsUI.ShowOptionsEditor(LockstepOptionsEditorUI,
        /// LockstepGameStateOptionsData, uint)"/>.</param>
        /// <param name="importedGameStates">An array containing <see cref="LockstepImportedGS"/> objects
        /// obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>.</param>
        public abstract void ShowImportOptionsEditor(
            LockstepOptionsEditorUI ui,
            object[][] importedGameStates);
        /// <summary>
        /// <para><see cref="LockstepGameStateOptionsUI.HideOptionsEditor"/> all
        /// <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> non <see langword="null"/>
        /// <see cref="LockstepGameState.ImportUI"/> in <see cref="GameStatesSupportingImportExport"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public abstract void HideImportOptionsEditor();
        /// <summary>
        /// <para>Starts the export process of all <see cref="GameStatesSupportingImportExport"/> into a base
        /// 64 encoded string intended for users to copy and save externally such that the exported string can
        /// be passed to <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>
        /// at a future point in time, including in a future/new instance of the world or even different
        /// worlds.</para>
        /// <para>Note that this calls <see cref="ResetWriteStream"/>, which is to say if there were any calls
        /// to write to the internal write stream such as when sending input actions, all data written to the
        /// write stream so far will get cleared when calling
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/>.</para>
        /// <para>Even though exporting naturally uses the internal write stream as it is calling
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/>, it is safe
        /// to use <c>Write</c> functions to write the the internal write stream even while
        /// <see cref="IsExporting"/> is <see langword="true"/> (outside of SerializeGameState itself),
        /// because lockstep uses an effectively separate write stream for exporting.</para>
        /// <para>Before any
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/> calls,
        /// <see cref="LockstepGameState.OptionsForCurrentExport"/> for each game state in
        /// <see cref="GameStatesSupportingImportExport"/> gets set to the associated
        /// <see cref="LockstepGameStateOptionsData"/> from <paramref name="allExportOptions"/>. For further
        /// information about export options read <paramref name="allExportOptions"/> annotations.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="exportName">The name to save inside of the exported string which can be read back
        /// when importing in the future. <see langword="null"/> is a valid value. Must not contain
        /// "<c>\n</c>" nor "<c>\r</c>"; If it does <see langword="false"/> is returned, however it will also
        /// log an error message so this should be treated like an exception.</param>
        /// <param name="allExportOptions">An array matching the order in
        /// <see cref="GameStatesSupportingImportExport"/>. For every game state where
        /// <see cref="LockstepGameState.ExportUI"/> is non <see langword="null"/> there must a
        /// <see cref="LockstepGameStateOptionsData"/> provided, matching the
        /// <see cref="LockstepGameStateOptionsUI.OptionsClassName"/>. The bare minimum would be to use
        /// <see cref="GetNewExportOptions"/> before calling
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/> and calling
        /// <see cref="CleanupAllExportOptions(LockstepGameStateOptionsData[])"/> afterwards.</param>
        /// <returns><para>A base 64 encoded string containing a bit of metadata such as which game states
        /// have been exported, their version, the current UTC date and time, the <see cref="WorldName"/> and
        /// then of course exported data retrieved from
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/>.</para>
        /// <para>Returning <see langword="false"/> indicates that the export did not actually start. This
        /// actually indicates improper use of the API in every case except when
        /// <see cref="GameStatesSupportingImportExportCount"/> is 0, in which case it returns
        /// <see langword="false"/> without an error. An error is logged when
        /// <see cref="IsInitialized"/> is <see langword="false"/> or when given an invalid
        /// <paramref name="exportName"/> or <paramref name="allExportOptions"/>.</para></returns>
        public abstract bool StartExport(string exportName, LockstepGameStateOptionsData[] allExportOptions);
        /// <summary>
        /// <para>During exports running input actions is suspended, which guarantees that the game state does
        /// not get mutated throughout the process.</para>
        /// <para>While exporting <see cref="ResetWriteStream"/> gets called every frame. It is already part
        /// of the specification not to use lockstep's internal write stream across frames, or in other words
        /// when leaving a system's control flow the write stream must be empty. Which is to say that
        /// <see cref="ResetWriteStream"/> being called here does not add any new restrictions, if anything it
        /// enforces existing restrictions further.</para>
        /// <para>Set to <see langword="true"/> right before <see cref="LockstepEventType.OnExportStart"/>
        /// and set to <see langword="false"/> right before <see cref="LockstepEventType.OnExportFinished"/>.
        /// </para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsExporting { get; }
        /// <summary>
        /// <para>The export name which was passed to
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/> for the currently running
        /// export.</para>
        /// <para>Usable if <see cref="IsExporting"/> is <see langword="true"/>, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract string ExportName { get; }
        /// <summary>
        /// <para>The finished base 64 encoded result from the current export.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnExportFinished"/>.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract string ExportResult { get; }
        /// <summary>
        /// <para><see langword="true"/> inside of
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/> when
        /// performing an export through
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/>.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract bool IsSerializingForExport { get; }
        /// <summary>
        /// <para>Load and validate a given base 64 exported string, converting it into an array of objects
        /// containing all export information within the given string into an actually usable format.</para>
        /// <para>This data can be processed using utilities in the <see cref="LockstepImportedGS"/> class if
        /// desired.</para>
        /// <para>This is the data which can then be passed to
        /// <see cref="StartImport(object[][], System.DateTime, string, string)"/>. It is valid to create a
        /// new array which only contains some of the imported game states obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>, however
        /// modification of the <see cref="LockstepImportedGS"/> data structures is forbidden, with the only
        /// exception being the <see cref="LockstepImportedGS.ImportOptions"/> which actually must be
        /// populated manually before passing this data to
        /// <see cref="StartImport(object[][], System.DateTime, string, string)"/>.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="exportedString">The base 64 encoded string originally obtained from
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/>. Can originate from previous
        /// instances or even previous versions of the world or even completely different worlds.</param>
        /// <param name="exportedDate">The UTC date and time at which the
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/> call was made.</param>
        /// <param name="exportWorldName">
        /// <para>The <see cref="WorldName"/> at the time of exporting. Just like <see cref="WorldName"/> it
        /// does not uniquely identify worlds.</para>
        /// <para>Guaranteed to not be <see langword="null"/>, nor be an empty string, nor contain any
        /// "<c>\n</c>" nor "<c>\r</c>", nor have any leading or trailing space.</para>
        /// </param>
        /// <param name="exportName">The name which was passed to
        /// <see cref="StartExport(string, LockstepGameStateOptionsData[])"/> at the time of exporting, which
        /// can be <see langword="null"/>. It is guaranteed to never contain "<c>\n</c>" nor "<c>\r</c>". If
        /// the imported string was manually fabricated and it did contain newlines then those will silently
        /// get replaced with white spaces.</param>
        /// <returns><see cref="LockstepImportedGS"/>[] importedGameStates, or <see langword="null"/> when the
        /// given <paramref name="exportedString"/> was invalid.</returns>
        public abstract object[][] ImportPreProcess(
            string exportedString,
            out System.DateTime exportedDate,
            out string exportWorldName,
            out string exportName);
        /// <summary>
        /// <para>Start importing game states using data obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>. This requires
        /// <see cref="IsInitialized"/> being <see langword="true"/>, as well as
        /// <see cref="IsImporting"/> being <see langword="false"/>.</para>
        /// <para>All <see cref="LockstepImportedGS"/> in <paramref name="importedGameStates"/> which have a
        /// non <see langword="null"/> <see cref="LockstepImportedGS.GetErrorMsg(object[])"/> are
        /// ignored.</para>
        /// <para>The given <paramref name="importedGameStates"/> must have non <see langword="null"/>
        /// <see cref="LockstepImportedGS.GetImportOptions(object[])"/> for all game states where
        /// <see cref="LockstepGameState.ImportUI"/> is non <see langword="null"/>. The bare minimum would be
        /// to call <see cref="FillInMissingImportOptionsWithDefaults(object[][])"/> before calling
        /// <see cref="StartImport(object[][], System.DateTime, string, string)"/>, and then calling
        /// <see cref="CleanupImportedGameStatesData(object[][])"/> afterwards.</para>
        /// <para>The import process consists of 2 input actions internally. A start input action which
        /// contains just a bit of metadata. When it runs the <see cref="LockstepEventType.OnImportStart"/>
        /// event ultimately gets raised.</para>
        /// <para>The second input action is potentially very large as it contains all of the serialized
        /// import options and serialized game states, which is all custom user data.</para>
        /// <para>The system automatically spreads importing of each import options instance and importing of
        /// each game state out across frames, however custom code can also call
        /// <see cref="FlagToContinueNextFrame"/> to further spread out import work across frames to reduce
        /// lag spikes. Or simply to ensure that the 10 second timeout for running Udon code is never
        /// reached.</para>
        /// <para>Before any
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// calls, <see cref="LockstepGameState.OptionsForCurrentImport"/> for each game state in
        /// <see cref="GameStatesSupportingImportExport"/> gets set to the associated
        /// <see cref="LockstepGameStateOptionsData"/> which got deserialized. In other words all import
        /// options get deserialized first, then all game states.</para>
        /// <para>Note that this calls <see cref="ResetWriteStream"/>, which is to say if there were any calls
        /// to write to the internal write stream such as when sending input actions, all data written to the
        /// write stream so far will get cleared when calling
        /// <see cref="StartImport(object[][], System.DateTime, string, string)"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="importedGameStates">An array containing <see cref="LockstepImportedGS"/> objects
        /// obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>. Regardless of
        /// the order of game states in the given array, game states will be imported in the order of
        /// <see cref="GameStatesSupportingImportExport"/>, skipping any non imported game states of
        /// course.</param>
        /// <param name="exportDate">The UTC date and time obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>.</param>
        /// <param name="exportWorldName">The name obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>. If a
        /// different value gets passed in it will be sanitized. "<c>\n</c>" and "<c>\r</c>" get replaced with
        /// white spaces, leading and trailing spaces are removed, <see langword="null"/> and empty strings
        /// get replaced with <c>"Unnamed"</c>.</param>
        /// <param name="exportName">The name obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string, out string)"/>. If a
        /// different name gets passed in and that happens to contain "<c>\n</c>" or "<c>\r</c>" then those
        /// will silently be replaced with white spaces.</param>
        public abstract void StartImport(
            object[][] importedGameStates,
            System.DateTime exportDate,
            string exportWorldName,
            string exportName);
        /// <summary>
        /// <para>Is an import of game states currently in progress? If <see langword="true"/>, other
        /// properties with <c>Import</c> in the name can be used to obtain more information about the ongoing
        /// import.</para>
        /// <para>Set to <see langword="true"/> right before <see cref="LockstepEventType.OnImportStart"/>
        /// and set to <see langword="false"/> right before <see cref="LockstepEventType.OnImportFinished"/>.
        /// </para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract bool IsImporting { get; }
        /// <summary>
        /// <para><see langword="true"/> inside of
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// when performing an import.</para>
        /// <para>Note that an import could be in progress while other input actions are also being run,
        /// making <see cref="IsDeserializingForImport"/> be <see langword="false"/> while
        /// <see cref="IsImporting"/> is <see langword="true"/>.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract bool IsDeserializingForImport { get; }
        /// <summary>
        /// <para>The player id of the client which initiated the import and has provided the import data.
        /// </para>
        /// <para>Usable if <see cref="IsImporting"/> is <see langword="true"/>, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para><see cref="VRCPlayerApi"/> is not guaranteed to be valid for the given player id.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint ImportingPlayerId { get; }
        /// <summary>
        /// <para>The UTC time of when the currently being imported data was initially exported.</para>
        /// <para>Usable if <see cref="IsImporting"/> is <see langword="true"/>, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract System.DateTime ImportingFromDate { get; }
        /// <summary>
        /// <para>The <see cref="WorldName"/> of the world of the currently being imported data.</para>
        /// <para>Guaranteed to not be <see langword="null"/>, nor be an empty string, nor contain any
        /// "<c>\n</c>" nor "<c>\r</c>", nor have any leading or trailing space.</para>
        /// <para>Can be <see langword="null"/>.</para>
        /// <para>Usable if <see cref="IsImporting"/> is <see langword="true"/>, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract string ImportingFromWorldName { get; }
        /// <summary>
        /// <para>The name that was set during the export of the currently being imported data.</para>
        /// <para>Guaranteed to never contain "<c>\n</c>" nor "<c>\r</c>".</para>
        /// <para>Can be <see langword="null"/>.</para>
        /// <para>Usable if <see cref="IsImporting"/> is <see langword="true"/>, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract string ImportingFromName { get; }
        /// <summary>
        /// <para>The game state that has just been imported.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnImportedGameState"/> and
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>.
        /// </para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract LockstepGameState ImportedGameState { get; }
        /// <summary>
        /// <para>The return value of
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>.
        /// </para>
        /// <para><see langword="null"/> means there was no error. Otherwise there was an error, however
        /// deserialization is expected to handle errors as gracefully as possible, so the associated system
        /// should still work afterwards.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnImportedGameState"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract string ImportErrorMessage { get; }
        /// <summary>
        /// <para>The version of the data that has just been imported, which is what
        /// <see cref="LockstepGameState.GameStateDataVersion"/> was for the <see cref="ImportedGameState"/>
        /// at at the time of the export.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnImportedGameState"/> and
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// even though that gets this value through the parameter anyway, but why not.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint ImportedDataVersion { get; }
        /// <summary>
        /// <para>This returns a new copy of the array every time it is accessed.</para>
        /// <para>The game states which are part of the current import.</para>
        /// <para><see cref="GameStatesBeingImportedFinishedCount"/> indicates how many of the game states in
        /// this list have finished importing. They get imported in the order they are in this array.</para>
        /// <para>Usable if <see cref="IsImporting"/> is <see langword="true"/>, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract LockstepGameState[] GameStatesBeingImported { get; }
        /// <summary>
        /// <para>This returns a new copy of the array every time it is accessed.</para>
        /// <para>The data versions of the game states which are part of the current import.</para>
        /// <para>Matches the order of the <see cref="GameStatesBeingImported"/> array.</para>
        /// <para>Usable if <see cref="IsImporting"/> is <see langword="true"/>, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint[] GameStatesBeingImportedDataVersions { get; }
        /// <summary>
        /// <para>Returns just the length of <see cref="GameStatesBeingImported"/> such that when all
        /// that's needed is the length there isn't an entire array being constructed and copied just to be
        /// thrown away again immediately afterwards.</para>
        /// <para>Usable if <see cref="IsImporting"/> is true, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int GameStatesBeingImportedCount { get; }
        /// <summary>
        /// <para>Indicates how many of the <see cref="GameStatesBeingImported"/> have finished importing.
        /// This value gets incremented/updated right before
        /// <see cref="LockstepEventType.OnImportedGameState"/> gets raised.</para>
        /// <para>Usable if <see cref="IsImporting"/> is true, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int GameStatesBeingImportedFinishedCount { get; }

        /// <summary>
        /// <para>Autosaves are written to the
        /// <see href="https://docs.vrchat.com/docs/local-vrchat-storage">log file</see> and can be found by
        /// searching for "<p>[Lockstep] Export:</p>". Log files are deleted if they are 24 hours old and
        /// VRChat gets launched, so be sure to extract autosaves shortly after closing VRChat, if they are
        /// needed.</para>
        /// <para>Options are associated with game states by perfectly matching the order of values in
        /// <see cref="GameStatesSupportingImportExport"/>. When a game state in
        /// <see cref="GameStatesSupportingImportExport"/> odes not have a
        /// <see cref="LockstepGameState.ExportUI"/> it's associated value in
        /// <see cref="ExportOptionsForAutosave"/> will be <see langword="null"/>.</para>
        /// <para>Both the getter and setter of this property create a new array and
        /// <see cref="LockstepGameStateOptionsData.Clone"/> each options instance.</para>
        /// <para>The setter also calls
        /// <see cref="FillInMissingExportOptionsWithDefaults(LockstepGameStateOptionsData[])"/> on the copied
        /// array. The array assigned to this property remains unchanged.</para>
        /// <para>When this is non <see langword="null"/> it will automatically be autosaving at the specified
        /// <see cref="AutosaveIntervalSeconds"/>. If <see cref="IsAutosavePaused"/> is <see langword="true"/>
        /// or <see cref="IsImporting"/> is <see langword="true"/> it will effectively pause the autosave
        /// timer. Lockstep may also pause the timer at any time internally.</para>
        /// <para>Whenever this value changes,
        /// <see cref="LockstepEventType.OnExportOptionsForAutosaveChanged"/> gets raised 1 frame delayed to
        /// prevent recursion, subsequently if there are multiple changes within a frame the event only gets
        /// raised once (you can thank Udon).</para>
        /// <para>Default: <see langword="null"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Not game state safe, all APIs related to autosaving are local only.</para>
        /// </summary>
        public abstract LockstepGameStateOptionsData[] ExportOptionsForAutosave { get; set; }
        /// <summary>
        /// <para><see langword="true"/> when <see cref="ExportOptionsForAutosave"/> is non
        /// <see langword="null"/>.</para>
        /// <para>Significantly more performant than getting <see cref="ExportOptionsForAutosave"/> and
        /// comparing that with <see langword="null"/>, since <see cref="ExportOptionsForAutosave"/>'s getter
        /// performs a <see cref="LockstepGameStateOptionsData.Clone"/> on each options  instance.</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool HasExportOptionsForAutosave { get; }
        /// <summary>
        /// <para>Negative values get clamped to 0f, which means "autosave every frame" so long as autosaves
        /// are not <see cref="IsAutosavePaused"/> as well as autosaves not being blocked through other means
        /// internally, such as <see cref="IsImporting"/> being <see langword="true"/> or other internal
        /// reasons.</para>
        /// <para>Infinity or NaN values get rejected and log an error. If I could I would throw an exception.
        /// </para>
        /// <para>When writing to this property the system will immediately determine when the next autosave
        /// should occur based on the time of the previous autosave and the newly set interval. This can
        /// result in an immediate autosave.</para>
        /// <para>Since written changes take effect immediately it may be preferable to wait until 1 to 3
        /// seconds after user input from a slider or text field to prevent potential lag spikes caused by
        /// an autosave in the middle of the user writing text or moving a slider. Mainly since the size of
        /// the lag spike is unknown because it purely depends on how long it takes game states to serialize
        /// themselves and how big the resulting export data is.</para>
        /// <para>Whenever this value changes,
        /// <see cref="LockstepEventType.OnAutosaveIntervalSecondsChanged"/> gets raised 1 frame delayed to
        /// prevent recursion, subsequently if there are multiple changes within a frame the event only gets
        /// raised once (you can thank Udon).</para>
        /// <para>Default: <p>300f.</p></para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract float AutosaveIntervalSeconds { get; set; }
        /// <summary>
        /// <para>When nothing is being autosaved, which means <see cref="ExportOptionsForAutosave"/> is
        /// <see langword="null"/>, <see cref="float.PositiveInfinity"/> will be returned.</para>
        /// <para>When the internal timer is paused for any reason, it naturally also causes this property to
        /// continuously return the same - paused - value.</para>
        /// <para>Never negative, can be zero.</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract float SecondsUntilNextAutosave { get; }
        /// <summary>
        /// <para>This property is modified through <see cref="StartScopedAutosavePause"/> and
        /// <see cref="StopScopedAutosavePause"/>. There is an internal counter which increments for every
        /// <see cref="StartScopedAutosavePause"/> call and decrements for every
        /// <see cref="StopScopedAutosavePause"/> call. Whenever this internal counter is non zero,
        /// <see cref="IsAutosavePaused"/> is <see langword="true"/>.</para>
        /// <para>Autosaves are automatically started and stopped depending on if
        /// <see cref="ExportOptionsForAutosave"/> is <see langword="null"/> or not, pausing is separate from
        /// that.</para>
        /// <para>Whenever this value changes, <see cref="LockstepEventType.OnIsAutosavePausedChanged"/> gets
        /// raised 1 frame delayed to prevent recursion, subsequently if there are multiple changes within a
        /// frame the event only gets raised once (you can thank Udon).</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsAutosavePaused { get; }
        /// <summary>
        /// <para>Think of <see cref="StartScopedAutosavePause"/> and <see cref="StopScopedAutosavePause"/>
        /// just like setting a paused <see cref="bool"/> to <see langword="true"/> and
        /// <see langword="false"/>. The only difference is that multiple systems can set this theoretical
        /// "<see cref="bool"/>" to <see langword="true"/> at the same time, without having to worry what the
        /// previous state was, as autosaving will only be unpaused once <see cref="StopScopedAutosavePause"/>
        /// has been called a matching amount of times as <see cref="StartScopedAutosavePause"/>.</para>
        /// <para>This allows pausing autosaves without touching <see cref="ExportOptionsForAutosave"/> nor
        /// <see cref="AutosaveIntervalSeconds"/> and without multiple systems interfering with each other.
        /// </para>
        /// <para><see cref="StopScopedAutosavePause"/> can be called even when the internal counter is zero.
        /// </para>
        /// <para>Usable any time.</para>
        /// </summary>
        public abstract void StartScopedAutosavePause();
        /// <inheritdoc cref="StartScopedAutosavePause"/>
        public abstract void StopScopedAutosavePause();

        /// <summary>
        /// <para>Shifts <paramref name="count"/> bytes starting at <paramref name="sourcePosition"/> to
        /// <paramref name="destinationPosition"/> inside of the internal write stream.</para>
        /// <para>Overlap in the source and destination ranges is handled and will not cause any source data
        /// getting corrupted.</para>
        /// <para>This is likely only useful for generic functions, like libraries, which must insert some
        /// data into the write stream. That also involves using <see cref="WriteStreamPosition"/> which is a
        /// low level api to be used with caution; Make sure to read its documentation.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="sourcePosition">The start index of the source range.</param>
        /// <param name="destinationPosition">The start index of the destination range.</param>
        /// <param name="count">The amount of bytes to be shifted.</param>
        public abstract void ShiftWriteStream(int sourcePosition, int destinationPosition, int count);
        /// <summary>
        /// <para>When data has already been written to the internal write stream using any of the
        /// <c>Write</c> functions, however the call to <see cref="SendInputAction(uint)"/>,
        /// <see cref="SendSingletonInputAction(uint)"/>, its overload or
        /// <see cref="SendEventDelayedTicks(uint, uint)"/> ends up not happening due to an early return, it
        /// is required to call <see cref="ResetWriteStream"/>.</para>
        /// <para>This cleans up the write stream such that future sent input actions do not get this
        /// unfinished input action data prepended to them, ultimately breaking them.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        public abstract void ResetWriteStream();
        /// <summary>
        /// <para>Be very careful with this. Not only is this the position/index within the write stream where
        /// the next <c>Write</c> call would write binary data to - which then naturally automatically
        /// advances the <see cref="WriteStreamPosition"/> - but this is also used as the total length of the
        /// amount of data that has been written to the write stream. As such that amount of data will then be
        /// sent as the input action data.</para>
        /// <para>Effectively this is a low level api to jump around in the write stream, as in the process of
        /// writing data to the stream, some value may not be known at the time of writing, requiring it to be
        /// skipped and then jumped back to at a later point in time. After the jump back it also must jump
        /// forward again as otherwise the input action data would be cut short.</para>
        /// <para>A common use case for this is to skip the length of a list of values that is about to be
        /// written to the write stream (so += 4 most likely). Then loop through the elements, conditionally
        /// write data to the write stream and for each element written to it increment a counter which tracks
        /// how many elements have actually been written to the write stream. Then once the loop is finished,
        /// jump back to the saved write stream position where the length value should be at, write the length
        /// there, and then jump forwards again as to not cut the input action data short, or not to overwrite
        /// data that had just been written. Naturally this is only needed if the length of the list that will
        /// actually be written to the write stream is unknown at the beginning of the list.</para>
        /// <para>Since this is a low level api, there are no save guards in place. Negative values are
        /// invalid, however not checked for. It will simply throw an exception at some point in the future if
        /// a negative value was written to this.</para>
        /// <para>Through improper use of this variable it is possible to include random garbage data in an
        /// input action which was actually part of a previous input action. There's no reason to do this, and
        /// even if you can think of a reason to do it, it'd oh so very most likely result in either an
        /// exception inside of lockstep for indexing an array with an out of bounds index, or a random
        /// exception inside of your system due to, well, random garbage data.</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract int WriteStreamPosition { get; set; }
        /// <summary>
        /// <para>When using <see cref="SendInputAction(uint)"/>,
        /// <see cref="SendSingletonInputAction(uint)"/>, its overload,
        /// <see cref="SendEventDelayedTicks(uint, uint)"/> or
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/>, in order
        /// to pass data to the input action, delayed event handler or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// respectively, use this function to write data to an internal binary write stream which is used by
        /// lockstep to perform syncing.</para>
        /// <para>On the note of
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/> when
        /// exporting the same serialization technique is used.</para>
        /// <para>Usable any time (technically).</para>
        /// </summary>
        /// <param name="value">The value to be serialized and written to the internal write stream.</param>
        public abstract void WriteSByte(sbyte value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteByte(byte value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        /// <param name="flag1">The first flag (<c>0x01</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1);
        /// <inheritdoc cref="WriteFlags(bool)"/>
        /// <param name="flag2">The second flag (<c>0x02</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1, bool flag2);
        /// <inheritdoc cref="WriteFlags(bool, bool)"/>
        /// <param name="flag3">The third flag (<c>0x04</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1, bool flag2, bool flag3);
        /// <inheritdoc cref="WriteFlags(bool, bool, bool)"/>
        /// <param name="flag4">The fourth flag (<c>0x08</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4);
        /// <inheritdoc cref="WriteFlags(bool, bool, bool, bool)"/>
        /// <param name="flag5">The fifth flag (<c>0x10</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5);
        /// <inheritdoc cref="WriteFlags(bool, bool, bool, bool, bool)"/>
        /// <param name="flag6">The sixth flag (<c>0x20</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5, bool flag6);
        /// <inheritdoc cref="WriteFlags(bool, bool, bool, bool, bool, bool)"/>
        /// <param name="flag7">The seventh flag (<c>0x40</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5, bool flag6, bool flag7);
        /// <inheritdoc cref="WriteFlags(bool, bool, bool, bool, bool, bool, bool)"/>
        /// <param name="flag8">The eighth flag (<c>0x80</c>) packed into a single byte.</param>
        public abstract void WriteFlags(bool flag1, bool flag2, bool flag3, bool flag4, bool flag5, bool flag6, bool flag7, bool flag8);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteShort(short value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteUShort(ushort value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteInt(int value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteUInt(uint value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteLong(long value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteULong(ulong value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteFloat(float value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteDouble(double value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteDecimal(decimal value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteVector2(Vector2 value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteVector3(Vector3 value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteVector4(Vector4 value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteQuaternion(Quaternion value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteColor(Color value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteColor32(Color32 value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteChar(char value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteString(string value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteDateTime(System.DateTime value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteTimeSpan(System.TimeSpan value);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        /// <param name="instance">Can be <see langword="null"/>. Has 1 additional byte of overhead compared
        /// to <see cref="WriteCustomClass(SerializableWannaBeClass)"/> (except when exporting, no additional
        /// overhead there). When exporting also writes the
        /// <see cref="SerializableWannaBeClass.DataVersion"/>. Calls
        /// <see cref="SerializableWannaBeClass.Serialize(bool)"/>, keeps track of how many bytes were written
        /// by this call and also writes that length to the write stream, inserting it preceding the custom
        /// data that was written. Uses <see cref="WriteSmallUInt(uint)"/> to reduce overhead. <b>Can be
        /// called recursively.</b></param>
        public abstract void WriteCustomNullableClass(SerializableWannaBeClass instance);
        /// <inheritdoc cref="WriteCustomNullableClass(SerializableWannaBeClass)"/>
        /// <param name="isExport">When using the overload without this parameter,
        /// <see cref="IsSerializingForExport"/> is used.</param>
        public abstract void WriteCustomNullableClass(SerializableWannaBeClass instance, bool isExport);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        /// <param name="instance">Must not be <see langword="null"/>. When exporting also writes the
        /// <see cref="SerializableWannaBeClass.DataVersion"/>. Calls
        /// <see cref="SerializableWannaBeClass.Serialize(bool)"/>, keeps track of how many bytes were written
        /// by this call and also writes that length to the write stream, inserting it preceding the custom
        /// data that was written. Uses <see cref="WriteSmallUInt(uint)"/> to reduce overhead. <b>Can be
        /// called recursively.</b></param>
        public abstract void WriteCustomClass(SerializableWannaBeClass instance);
        /// <inheritdoc cref="WriteCustomClass(SerializableWannaBeClass)"/>
        /// <param name="isExport">When using the overload without this parameter,
        /// <see cref="IsSerializingForExport"/> is used.</param>
        public abstract void WriteCustomClass(SerializableWannaBeClass instance, bool isExport);
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        /// <param name="bytes">The raw bytes to be written to the byte stream. This does not add any length
        /// information to the binary write stream, it just takes these bytes as they are.</param>
        public abstract void WriteBytes(byte[] bytes);
        /// <inheritdoc cref="WriteBytes(byte[])"/>
        /// <param name="startIndex">The start index of the range within <paramref name="bytes"/> to be
        /// written to the byte stream.</param>
        /// <param name="length">The length of the range within <paramref name="bytes"/> to be written to the
        /// byte stream. The <paramref name="length"/> value itself does not get written to the write
        /// stream.</param>
        public abstract void WriteBytes(byte[] bytes, int startIndex, int length);
        /// <summary>
        /// <para>When using <see cref="SendInputAction(uint)"/>,
        /// <see cref="SendSingletonInputAction(uint)"/>, its overload,
        /// <see cref="SendEventDelayedTicks(uint, uint)"/> or
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/>, in order
        /// to pass data to the input action, delayed event handler or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// respectively, use this function to write data to an internal binary write stream which is used by
        /// lockstep to perform syncing.</para>
        /// <para>On the note of
        /// <see cref="LockstepGameState.SerializeGameState(bool, LockstepGameStateOptionsData)"/> when
        /// exporting the same serialization technique is used.</para>
        /// <para>The <p>WriteSmall</p> variants of these serialization functions use fewer bytes to
        /// serialize given values if the given value is small enough. For the signed variants, small signed
        /// values are also supported and will use fewer bytes, however unsigned variants are slightly more
        /// efficient, both in terms of speed and size.</para>
        /// <para>Usable any time (technically).</para>
        /// </summary>
        /// <inheritdoc cref="WriteSByte(sbyte)"/>
        public abstract void WriteSmallShort(short value);
        /// <inheritdoc cref="WriteSmallShort(short)"/>
        public abstract void WriteSmallUShort(ushort value);
        /// <inheritdoc cref="WriteSmallShort(short)"/>
        public abstract void WriteSmallInt(int value);
        /// <inheritdoc cref="WriteSmallShort(short)"/>
        public abstract void WriteSmallUInt(uint value);
        /// <inheritdoc cref="WriteSmallShort(short)"/>
        public abstract void WriteSmallLong(long value);
        /// <inheritdoc cref="WriteSmallShort(short)"/>
        public abstract void WriteSmallULong(ulong value);

        /// <summary>
        /// <para>Be very careful with this, there should effectively never be a reason to write to this
        /// property. If the desire is to skip some bytes while reading from the read stream, use
        /// <see cref="ReadBytes(int, bool)"/> with <c>skip: true</c> instead.</para>
        /// <para>Just like <see cref="WriteStreamPosition"/> this is a very low level api allowing you to
        /// read and write the current position inside of the read stream, which is the position the next
        /// <c>Read</c> call would read bytes from, and automatically advance the
        /// <see cref="ReadStreamPosition"/> of course.</para>
        /// <para>However unlike <see cref="WriteStreamPosition"/> there's very little reason to use this, so
        /// little that it took quite some consideration to even expose it. A possible use case is including
        /// the current read stream position in debug messages to make sure they match what was being written,
        /// meaning there would likely also be debug log messages which include the current write stream
        /// position in the serialization functions.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int ReadStreamPosition { get; set; }
        /// <summary>
        /// <para>The current read stream's length. There is no reason to use this in normal code, this is
        /// just exposed because <see cref="ReadStreamPosition"/> is also exposed.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int ReadStreamLength { get; }
        /// <inheritdoc cref="SetReadStream(byte[])"/>
        /// <param name="startIndex">The start index inside of <paramref name="stream"/> of the range which
        /// should be copied and become the new read stream.</param>
        /// <param name="length">The length of the range which should be copied from the
        /// <paramref name="stream"/> to a new array which will become the new read stream.</param>
        public abstract void SetReadStream(byte[] stream, int startIndex, int length);
        /// <summary>
        /// <para>Assigns the given stream directly to the internal read stream, overwriting it.</para>
        /// <para>Sets the <see cref="ReadStreamPosition"/> to 0, the start of the new read stream.</para>
        /// <para>Potentially useful in combination with
        /// <see cref="SkipCustomClass(out uint, out byte[])"/>.</para>
        /// <para>Be careful not to overwrite the read stream during other ongoing deserialization.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="stream">The new read stream, must not be <see langword="null"/>.</param>
        public abstract void SetReadStream(byte[] stream);

        /// <summary>
        /// <para>To retrieve data inside of input actions, delayed event handlers or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// <c>Read</c> functions shall be used to read from an internal read stream (a different stream than
        /// the write stream).</para>
        /// <para>The calls to the <c>Read</c> functions must match the data type and the order in which the
        /// values were initially written to the write stream on the sending side.</para>
        /// </summary>
        /// <returns>The deserialized value read from the internal read stream.</returns>
        public abstract sbyte ReadSByte();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract byte ReadByte();
        /// <inheritdoc cref="ReadSByte"/>
        /// <param name="flag1">The first flag (<c>0x01</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1);
        /// <inheritdoc cref="ReadFlags(out bool)"/>
        /// <param name="flag2">The second flag (<c>0x02</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1, out bool flag2);
        /// <inheritdoc cref="ReadFlags(out bool, out bool)"/>
        /// <param name="flag3">The third flag (<c>0x04</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1, out bool flag2, out bool flag3);
        /// <inheritdoc cref="ReadFlags(out bool, out bool, out bool)"/>
        /// <param name="flag4">The fourth flag (<c>0x08</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4);
        /// <inheritdoc cref="ReadFlags(out bool, out bool, out bool, out bool)"/>
        /// <param name="flag5">The fifth flag (<c>0x10</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5);
        /// <inheritdoc cref="ReadFlags(out bool, out bool, out bool, out bool, out bool)"/>
        /// <param name="flag6">The sixth flag (<c>0x20</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5, out bool flag6);
        /// <inheritdoc cref="ReadFlags(out bool, out bool, out bool, out bool, out bool, out bool)"/>
        /// <param name="flag7">The seventh flag (<c>0x40</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5, out bool flag6, out bool flag7);
        /// <inheritdoc cref="ReadFlags(out bool, out bool, out bool, out bool, out bool, out bool, out bool)"/>
        /// <param name="flag8">The eighth flag (<c>0x80</c>) unpacked from a single byte.</param>
        public abstract void ReadFlags(out bool flag1, out bool flag2, out bool flag3, out bool flag4, out bool flag5, out bool flag6, out bool flag7, out bool flag8);
        /// <inheritdoc cref="ReadSByte"/>
        public abstract short ReadShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ushort ReadUShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract int ReadInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract uint ReadUInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract long ReadLong();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ulong ReadULong();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract float ReadFloat();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract double ReadDouble();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract decimal ReadDecimal();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Vector2 ReadVector2();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Vector3 ReadVector3();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Vector4 ReadVector4();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Quaternion ReadQuaternion();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Color ReadColor();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Color32 ReadColor32();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract char ReadChar();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract string ReadString();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract System.DateTime ReadDateTime();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract System.TimeSpan ReadTimeSpan();
        /// <summary>
        /// <para>To retrieve data inside of input actions, delayed event handlers or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// <c>Read</c> functions shall be used to read from an internal read stream (a different stream than
        /// the write stream).</para>
        /// <para>The calls to the <c>Read</c> functions must match the data type and the order in which the
        /// values were initially written to the write stream on the sending side.</para>
        /// <para><b>Can only be used when deserializing for an import.</b></para>
        /// <para>If a non <see langword="null"/> instance which at the time of the export had
        /// <see cref="SerializableWannaBeClass.SupportsImportExport"/> being <see langword="true"/>, this
        /// will advance past the data that was written, while also returning the data through out parameters.
        /// Could be useful in combination with <see cref="SetReadStream(byte[])"/> once the current
        /// deserialization has concluded.</para>
        /// </summary>
        /// <param name="dataVersion">The <see cref="SerializableWannaBeClass.DataVersion"/> at the time of
        /// the export, or 0u when <see langword="false"/> is returned.</param>
        /// <param name="data">The custom data which was written by
        /// <see cref="SerializableWannaBeClass.Serialize(bool)"/> at the time of the export, or null when
        /// <see langword="false"/> is returned.</param>
        /// <returns><see langword="true"/> when the instance that was written at the time of the export was
        /// non <see langword="null"/> and <see cref="SerializableWannaBeClass.SupportsImportExport"/> was
        /// <see langword="true"/>.</returns>
        public abstract bool SkipCustomClass(out uint dataVersion, out byte[] data);
        /// <inheritdoc cref="SkipCustomClass(out uint, out byte[])"/>
        /// <param name="isImport">When using the overload without this parameter,
        /// <see cref="IsDeserializingForImport"/> is used.</param>
        public abstract bool SkipCustomClass(out uint dataVersion, out byte[] data, bool isImport);
        /// <summary>
        /// <para>To retrieve data inside of input actions, delayed event handlers or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// <c>Read</c> functions shall be used to read from an internal read stream (a different stream than
        /// the write stream).</para>
        /// <para>The calls to the <c>Read</c> functions must match the data type and the order in which the
        /// values were initially written to the write stream on the sending side.</para>
        /// <para>Creates a new instance of a class with the given <paramref name="className"/> and
        /// ultimately calls <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/> on it.</para>
        /// <para>In the case of imports it reads the data version which is stored in the serialized data and
        /// uses it to determine if importing is possible (see Returns section). If yes the data version is
        /// passed along to <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/>, otherwise the
        /// custom serialized data is skipped and discarded.</para>
        /// <para>Deserializes data which was originally written using
        /// <see cref="WriteCustomNullableClass(SerializableWannaBeClass)"/> or its overload.</para>
        /// </summary>
        /// <param name="className">The name of the <see cref="SerializableWannaBeClass"/> which matches the
        /// one which was used to write initially. In the case of an import, the class name is not required to
        /// match the one used at the time of export, it could have been renamed. The class name itself is not
        /// part of the serialized data.</param>
        /// <returns>The new instance with deserialized data, or <see langword="null"/> if
        /// <see langword="null"/> was written originally. In the case of imports, also returns
        /// <see langword="null"/> if <see cref="SerializableWannaBeClass.SupportsImportExport"/> is
        /// <see langword="false"/> for the class with the given <paramref name="className"/>, similarly if
        /// <see cref="SerializableWannaBeClass.SupportsImportExport"/> was <see langword="false"/> at the
        /// time of the export and also returns <see langword="null"/> if the data version in the serialized
        /// data is lower than <see cref="SerializableWannaBeClass.LowestSupportedDataVersion"/>.</returns>
        public abstract SerializableWannaBeClass ReadCustomNullableClass(string className);
        /// <inheritdoc cref="ReadCustomNullableClass(string)"/>
        /// <param name="isImport">When using the overload without this parameter,
        /// <see cref="IsDeserializingForImport"/> is used.</param>
        public abstract SerializableWannaBeClass ReadCustomNullableClass(string className, bool isImport);
        /// <summary>
        /// <para>To retrieve data inside of input actions, delayed event handlers or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// <c>Read</c> functions shall be used to read from an internal read stream (a different stream than
        /// the write stream).</para>
        /// <para>The calls to the <c>Read</c> functions must match the data type and the order in which the
        /// values were initially written to the write stream on the sending side.</para>
        /// <para>Creates a new instance of a class with the given <paramref name="className"/> and
        /// ultimately calls <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/> on it.</para>
        /// <para>In the case of imports it reads the data version which is stored in the serialized data and
        /// uses it to determine if importing is possible (see Returns section). If yes the data version is
        /// passed along to <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/>, otherwise the
        /// custom serialized data is skipped and discarded.</para>
        /// <para>Deserializes data which was originally written using
        /// <see cref="WriteCustomClass(SerializableWannaBeClass)"/> or its overload.</para>
        /// </summary>
        /// <param name="className">The name of the <see cref="SerializableWannaBeClass"/> which matches the
        /// one which was used to write initially. In the case of an import, the class name is not required to
        /// match the one used at the time of export, it could have been renamed. The class name itself is not
        /// part of the serialized data.</param>
        /// <returns>The new instance with deserialized data. In the case of imports, returns
        /// <see langword="null"/> if <see cref="SerializableWannaBeClass.SupportsImportExport"/> is
        /// <see langword="false"/> for the class with the given <paramref name="className"/>, similarly if
        /// <see cref="SerializableWannaBeClass.SupportsImportExport"/> was <see langword="false"/> at the
        /// time of the export and also returns <see langword="null"/> if the data version in the serialized
        /// data is lower than <see cref="SerializableWannaBeClass.LowestSupportedDataVersion"/>.</returns>
        public abstract SerializableWannaBeClass ReadCustomClass(string className);
        /// <inheritdoc cref="ReadCustomClass(string)"/>
        /// <param name="isImport">When using the overload without this parameter,
        /// <see cref="IsDeserializingForImport"/> is used.</param>
        public abstract SerializableWannaBeClass ReadCustomClass(string className, bool isImport);
        /// <summary>
        /// <para>To retrieve data inside of input actions, delayed event handlers or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// <c>Read</c> functions shall be used to read from an internal read stream (a different stream than
        /// the write stream).</para>
        /// <para>The calls to the <c>Read</c> functions must match the data type and the order in which the
        /// values were initially written to the write stream on the sending side.</para>
        /// <para>Ultimately calls <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/> on the given
        /// <paramref name="instance"/>.</para>
        /// <para>In the case of imports it reads the data version which is stored in the serialized data and
        /// uses it to determine if importing is possible (see Returns section). If yes the data version is
        /// passed along to <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/>, otherwise the
        /// custom serialized data is skipped and discarded.</para>
        /// <para>Deserializes data which was originally written using
        /// <see cref="WriteCustomNullableClass(SerializableWannaBeClass)"/> or its overload.</para>
        /// </summary>
        /// <param name="instance">An instance of a <see cref="SerializableWannaBeClass"/> which matches the
        /// one which was used to write initially. In the case of an import, the class name is not required to
        /// match the one used at the time of export, it could have been renamed. The class name itself is not
        /// part of the serialized data. Must not be <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/>
        /// did get called, or <see langword="false"/> if <see langword="null"/> was written originally. In the
        /// case of imports, also returns <see langword="false"/> if
        /// <see cref="SerializableWannaBeClass.SupportsImportExport"/> is
        /// <see langword="false"/> for the given <paramref name="instance"/>, similarly if
        /// <see cref="SerializableWannaBeClass.SupportsImportExport"/> was <see langword="false"/> at the
        /// time of the export and also returns <see langword="false"/> if the data version in the serialized
        /// data is lower than <see cref="SerializableWannaBeClass.LowestSupportedDataVersion"/>.</returns>
        public abstract bool ReadCustomNullableClass(SerializableWannaBeClass instance);
        /// <inheritdoc cref="ReadCustomNullableClass(SerializableWannaBeClass)"/>
        /// <param name="isImport">When using the overload without this parameter,
        /// <see cref="IsDeserializingForImport"/> is used.</param>
        public abstract bool ReadCustomNullableClass(SerializableWannaBeClass instance, bool isImport);
        /// <summary>
        /// <para>To retrieve data inside of input actions, delayed event handlers or
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>
        /// <c>Read</c> functions shall be used to read from an internal read stream (a different stream than
        /// the write stream).</para>
        /// <para>The calls to the <c>Read</c> functions must match the data type and the order in which the
        /// values were initially written to the write stream on the sending side.</para>
        /// <para>Ultimately calls <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/> on the given
        /// <paramref name="instance"/>.</para>
        /// <para>In the case of imports it reads the data version which is stored in the serialized data and
        /// uses it to determine if importing is possible (see Returns section). If yes the data version is
        /// passed along to <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/>, otherwise the
        /// custom serialized data is skipped and discarded.</para>
        /// <para>Deserializes data which was originally written using
        /// <see cref="WriteCustomClass(SerializableWannaBeClass)"/> or its overload.</para>
        /// </summary>
        /// <param name="instance">An instance of a <see cref="SerializableWannaBeClass"/> which matches the
        /// one which was used to write initially. In the case of an import, the class name is not required to
        /// match the one used at the time of export, it could have been renamed. The class name itself is not
        /// part of the serialized data. Must not be <see langword="null"/>.</param>
        /// <returns><see langword="true"/>. In the case of imports
        /// <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/> may not get called. Returns
        /// <see langword="false"/> if <see cref="SerializableWannaBeClass.SupportsImportExport"/> is
        /// <see langword="false"/> for the given <paramref name="instance"/>, similarly if
        /// <see cref="SerializableWannaBeClass.SupportsImportExport"/> was <see langword="false"/> at the
        /// time of the export and also returns <see langword="false"/> if the data version in the serialized
        /// data is lower than <see cref="SerializableWannaBeClass.LowestSupportedDataVersion"/>.</returns>
        public abstract bool ReadCustomClass(SerializableWannaBeClass instance);
        /// <inheritdoc cref="ReadCustomClass(SerializableWannaBeClass)"/>
        /// <param name="isImport">When using the overload without this parameter,
        /// <see cref="IsDeserializingForImport"/> is used.</param>
        public abstract bool ReadCustomClass(SerializableWannaBeClass instance, bool isImport);
        /// <inheritdoc cref="ReadSByte"/>
        /// <param name="byteCount">The amount of raw bytes to read from the read stream. Very most likely
        /// used in conjunction with <see cref="WriteBytes(byte[])"/>, but again said write function does not
        /// write any length information to the write stream, therefore it is up to the caller to know the
        /// length of the data to be read.</param>
        /// <param name="skip">When <see langword="true"/> it simply returns <see langword="null"/>.</param>
        public abstract byte[] ReadBytes(int byteCount, bool skip = false);
        /// <inheritdoc cref="ReadSByte"/>
        public abstract short ReadSmallShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ushort ReadSmallUShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract int ReadSmallInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract uint ReadSmallUInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract long ReadSmallLong();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ulong ReadSmallULong();
    }
}
