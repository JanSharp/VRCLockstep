
# Input Action Sending

- SendInputAction
  - Locally: save as pending
    - Enqueue input action in local players's InputActionSync
  - RemoteLy: save as pending
- Locally, when InputActionSync successfully sent an input action
  - If client is master
    - Remove from pending
    - Run input action in current tick
    - Add to input actions to run in tick sync
- Remotely, when InputActionSync successfully receives an input action
  - Save as pending
  - If client was waiting on this input action
    - Check if all input actions for the next tick have been received
      - If yes, unpause ticks
- When receiving TickSync data, on every client that isn't the master
  - Enqueue each input action to run at their respective tick
  - OnTick
    - Before running a tick, ensure all input actions for this tick have been received already
      - If yes, run all input actions associated with this tick
      - If no, mark missing input actions for this tick as "waiting on" and pause ticks

## Input Action Sending in Single Player Mode

When the send input action function is called and Lock Step is currently in single player mode, the action is simply instantly performed, without even touching the InputActionSync script. This is not only an optimization, it is a requirement because when only a single client is in an instance, one can mark an object for serialization using RequestSerialization, however it will never actually serialize, not until another client joins the instance. And the above system for sending input actions requires the OnPostSerialization event.

Similarly, while in single player mode, the tick in which the input action got run does not get sent to the tick sync script.

# Internal Client States

There is both a game state and some local only flags.

The game state: probably a dictionary: playerId => state, with state being:

- Master
- WaitingForLateJoinerSync
- Normal

Non game state flags:

- leftClients: collection of clients (player ids) which have left the instance but are still in the above game state.

# Player Join

<!-- cSpell:ignore Factorio, desync, desyncs -->

- Ignore the OnPlayerJoin event
- On the new client, when an InputActionSync gets assigned to the local player
  - Wait 2 seconds
  - Check if the local player (the new client) is master
    - If yes, this client is either the first client or the previous master instantly left after this client joined, so perform the following and abort
      - Mark as isMaster in the LockStep script
      - Initialize the internal "client states" game state
        - Save this client as the master client in said game state
      - Enable [single player mode](#single-player-mode)
      - Fully enable input action handling
      - Raise `OnInit()`
      - Raise `OnClientJoined(int playerId)`
      - Save Time.time as the start time for running ticks
      - Enable running ticks
      - Done
  - Mark LockStep input action handling as initialized
    - Don't actually run any ticks
    - Save any and all received input actions as pending
    - Save any and all received LockStepTickSync "input actions to run on tick"
    - Ignore and discard any attempts at sending input actions locally
      - `ClientJoinIA` is an exception:
        - Allow it to be sent
        - Do not save it as pending
          - (Technically it could, however it would never get run locally anyway, as it happens before the game states that will be sent to this client get captured and serialized)
  - Send `ClientJoinIA`
- Receive `ClientJoinIA`
  - On Every client, except sending
    - Mark the given player id as "waiting for late joiner sync" in an internal "client state" game state
  - On master
    - Wait 5 seconds
      - Reset timer if another `ClientJoinIA` is received
    - [Initiate late joiner sync](#initiate-late-joiner-sync)
- On newly joined client
  - Receive every game state input action (it is actually legitimately only the new client receiving this data, because every other client already has the late joiner sync script disabled)
    - Deserialize game states using callback functions/events
  - Receive the current tick number
  - Disable the late joiner InputActionSync script, as this client has received all data it needed from it
  - Enable processing of input actions fully, including allowing sending any input actions locally
  - Send `ClientGotLateJoinerDataIA`
    - Must send this before raising the `OnClientBeginCatchUp` event as to guarantee that the first input action sent by this client is always `ClientGotLateJoinerDataIA` (aside from `ClientJoinIA`)
  - Initiate catch up
    - The client knows the tick at which all of the game states where captured
    - The client knows the current game tick all other clients are already at, thanks to LockStepTickSync
    - Raise the `OnClientBeginCatchUp(int playerId)` event
      - Allows systems to initialize non game states or register conditional event handlers, based on the current game states
      - Note that this event must not modify any game states. If you know Factorio, think about on_load. This is the only event with this exception
- Receive `ClientGotLateJoinerDataIA`
  - On every client (though sending client will run it a bit later, while catching up)
    - Change the "waiting for late joiner sync" state for the given player id to "normal"
    - Raise the `OnClientJoined(int playerId)` event
- On newly joined client
  - Catch up
    - Run ticks very quickly
      - Limit the amount of time spent catching up each frame to something like 5 to 10 milliseconds
    - Go through the list of input actions marked as "run on tick x", if "x" is outdated, discard of this input action as it was already included in the game states received initially
  - Save current Time.time as the start time for the local timer used for ticks
  - Start running ticks normally
  - Send `ClientCaughtUpIA`
- Receive `ClientCaughtUpIA`
  - On every client
    - Raise the `OnClientCaughtUp(int playerId)` event to allow systems to further initialize... well whatever they want
      - Keep in mind that VRCPlayerApi objects themselves are not, and cannot be, part of the game state. Any action taken using data from them which affects the game state must go through another input action, most likely and preferably only sent by the joining client

Any systems using this should disable functionality that would require sending input actions until they get the `OnClientCaughtUp` event. If they really want to they could use some heavy latency state logic to make it seem like actions are being registered for the local player, but then only actually perform the actions once they get `OnClientCaughtUp`. This is effectively an intentional desync, so be very careful with this kind of approach. Latency states even when it is already caught up are intentional desyncs, for the record.

## Initiate late joiner sync

- Initiate late joiner sync
  - Right before moving to the next tick
    - Using the late joiner InputActionSync script
      - Send every game state as an input action, including internal ones (they're really not any different)
        - Serialize game states using some callback function/event which were provided when defining (not creating) the game states
      - Send current tick number (not next)
      - (Note that "sending" really means enqueueing, see sending input actions section, though late joiner input actions are a bit different)

# Player Leave

When the world converts from multiplayer to single player, the system must remove all queued data from the InputActionSync scripts (both the local player's one and the late joiner one), and instantly raise their associated actions (though there's no thing to do for the late joiner ones, just drop them).

- Ignore OnPlayerLeave (for now? So long as I can trust the player object pool I suppose)
- When an InputActionSync get unassigned
  - On every client that isn't the master
    - Check if the given client still exists in the internal "client state" game state
      - If yes, Mark the given client as "left" locally
      - Check if the leaving client had the "master" state in the internal "client state" game state
        - If yes
        - Set extra flag for "master left"
        - Wait 1 frame (Because I have trust issues with VRChat)
        - Call CheckMasterChange
  - On the master client
    - Mark the given client as "left" locally
    - Wait 1 second
    - Check if [single player mode](#single-player-mode) should be activated, and activate it if yes
    - Send `ClientLeftIA`
- Receive `ClientLeftIA`
  - On every client
    - Remove the client from the "client state" game state
    - Raise the `OnClientLeft(int playerId)` event

## Master Transfer

- OnOwnerShipTransfer on LockStepTickSync
  - On every client
    - Wait 1 frame (Because I have trust issues with VRChat)
    - Check if the local client is now instance master
      - Set the "became master" flag
      - If yes, call CheckMasterChange

- CheckMasterChange
  - If the "master left" and the "became master" flags are set
    - Unset both flags
    - Set isMaster to true in the Lock Step script
    - Reenable the late joiner InputActionSync script
      - Take ownership
      - Ignore any incoming data
    - Check if [single player mode](#single-player-mode) should be activated, and activate it if yes
    - Send `MasterChangedIA`
    - Send `ClientLeftIA` for each client currently marked as left
    - If there are any clients marked as "waiting for late joiner sync" in the internal "client states" game state
      - [Initiate late joiner sync](#initiate-late-joiner-sync)
- Receive `MasterChangedIA`
  - Mark the new master as the master in the internal "client states" game state

## Single Player Mode

- Check if single player mode should be activated
  - If the count of clients in "client state" minus the count of players marked as left == 1
    - Yes, enter single player mode

- Enter single player mode
  - Clear and discard all queued data in the late joiner InputActionSync script
  - Clear queued data from the local player's InputActionSync script and instantly run all input actions that were queued
  - Clear the "run action on tick" collection on the LockStepTickSync script

While in single player mode, input actions are handled differently. See [input action sending](#input-action-sending-in-single-player-mode) for more info.

# Game States

There can be multiple game states. One could argue though that all of them combined are functionally simply one big game state.

A game state can look however it wants, the data structure truly does not matter and is 100% user (programmer) defined.

In order for some data structure to qualify as a game state, interaction with that data must follow a set of rules:

- Reading from it can happen any time
- Writing to it must only happen inside of input actions, or otherwise "game state" declared events raised by the lock step system itself, like the on tick event
- Writing to it must only use data that is apart of the game state, or passed in as parameters to the input action/event that got raised

When the rules for writing to it are not followed it results in a desync.

Additionally there must be a serialize and deserialize function defined for the data structure.

- A serialize => deserialize cycle must result in the exact same data structure

Optionally a game state can be flagged as supporting import/export. When flagged as such, the serialization and deserialization events must have extra handling for import/export. When serializing or deserializing, a bool parameter is set to true indicating that this is an import/export call. The serialize function should then serialize extra data, if needed, in order for the state to be fully restored when importing the serialized data into another - potentially future version - of the world. The deserialize function should read that extra data to restore the state correctly, including handling old versions of the data since it might be exported data from on old version of the system. Backwards compatibility may be dropped at any point in time, up to the programmers discretion, however note that with how VRChat works, there's no way to load an older version of the map to migrate an exported data set to a newer version, so by dropping backwards compatibility, there may be exported data someone has stored on their machine that is now no longer usable.

In order to make your life easier, when supporting import/export, it is recommended to use an integer as the first value in the serialized data which is a simple incrementing version number of the data structure version. This makes it easy when deserializing to know what data format to expect in the serialized data stream, plus it allows for easy detection for old - no longer supported - versions of exported data, which therefore allows for graceful early aborting.

# Lock Step Events

- OnInit
- OnClientBeginCatchUp (non game state event)
- OnClientCaughtUp (non game state event)

# Serialization Rules

<!-- cSpell:ignore sbyte, ushort, ulong -->

For any kind of serialization in the Lock Step system, DataLists taking a certain form are used:

- The list must be flat (no other lists or dictionaries contained within)
- Every value must be a primitive:
  - float, double
     - sbyte, byte, short, ushort, int, uint, long, ulong <!-- TODO: Check if long and ulong are limited in size due to json being potentially all doubles. -->
  - char, string (null strings are allowed) <!-- TODO: Unless Udon and VRC Json are truly stupid, need to test it. -->
  - bool

If serialization of lists, dictionaries or similar is required, simply have a sequence of values starting with the a length integer and then all values proceeding said length value.

If serialization of other data structures like vectors or quaternions is required, simply unroll them into a set of floats. In the case of quaternions make sure to use the x,y,z,w representation, 4 floats.

Every form of data is possible to be represented using a flat list of primitives... Well, unless Udon doesn't expose the internal values, which may apply to VRCUrl objects, not sure. If that's the case, well you're screwed, you simply won't be able to sync that data using this system. I know that sucks, but I'm not going to add _a ton_ of extra complexity just for those few special values.

Note about internals: The system is currently using json for syncing, which has a stupid amount of overhead. Not that synced variables also have a stupid amount of overhead (4 bytes per variable or so for normal variables, 20 bytes for arrays... But it doesn't seem to be linear. Maybe (hopefully) there's compression involved). It would very much preferable if the system were to serialize the data into a byte array, however there's currently no way to get the internal bytes for floating point numbers. I do want to check if it's possible to abuse shaders to serialize the data into bytes though, but that's for much later, when the system is actually functional and useful, and if I really feel like it.

# Data Lifecycle

## Initialization

There's 2 parts to this:

- Definition of game states
  - These can only be done at initialization, cannot be defined during runtime
- Registration of Lock Step events
  - These can both be registered during initialization as well as registered or deregistered at runtime

Defining games states is done using an abstract class as a base class. Said abstract class will require you to implement a few methods and properties:

- bool CanImportExport { get; } // When false, the isExport and isImport parameters will naturally never be true.
- DataList SerializeState(bool isExport); // Game states must be able to successfully be serialized at every point in time. Must create a new instance of a DataList, not reuse an existing one.
- string DeserializeState(DataList data, bool isImport); // A non null return value indicates failure and the string value is an error message.

Registration of Lock Step events at initialization is done using attributes on methods which can be on any UdonSharpBehaviour. The system will find all instances of UdonSharpBehaviours with such methods in the scene at build time (entering play mode or before uploading), and save them as registered. This means instantiating a new instance of such scripts at runtime will not cause them to get registered automatically, in that case the new instance and the methods would have to get registered manually. More on registration of events during runtime later.

**Important:** Registration of events must **not** happen in `Start()`. The order in which events get registered must be deterministic and to my knowledge the order in which Start gets called on each behaviour in the scene is undefined.

## First Client

The first client joining a world is special, it is the one which is going to initialize all systems that are using Lock Step.

After a bit of time, `OnInit()` will get raised, any handlers listening to `OnInit` have certain jobs:

- Initialize game states, using any source data they wish
- Conditionally register event handlers that weren't registered in the initialization process (so ones that aren't using attributes)

Input actions can be sent as soon as `OnInit` is running, before then they are invalid and ignored. While sending input actions inside of `OnInit` is possible, there's little reason to, as `OnInit` is allowed to modify game states already anyway. However if it's easier to send some input actions, that's perfectly fine, they'll run _very_ shortly after OnInit, if not in the same game tick.

## Every Other Client

Every other client does not run `OnInit`. Instead, all game states will be sent to new clients. Once all data has been received and deserialized, `OnClientBeginCatchUp(int playerId)` is raised **only on the new client**. This means `OnClientBeginCatchUp` is rather limited:

- Can conditionally register event handlers that weren't registered in the initialization process (so ones that aren't using attributes, or were instantiated), using game states to determine which ones to register. After `OnClientBeginCatchUp` has run, the set of registered events must match the ones on every other client at that game tick. Even the order in which events got registered must match. This is the only non game state event allowed to change event registration.
- **Must not** modify any game state. It is the only event raised by Lock Step with this restriction
- Can, however, be used to save some non game state values. Just remember than when using said non game state values in order to modify the game state, you must send an input action and only modify the game state from within the input action handler

Speaking of input actions, similar to `OnInit`, as soon as `OnClientBeginCatchUp` is running, sending input actions is allowed, before then they are invalid and ignored. Therefore if you'd like to modify the game state in `OnClientBeginCatchUp` you must send an input action first. Note however that this input action has a very high likely hood of running many many game ticks after it was sent, since as the name of the event suggests, the client is just starting to catch up with all other clients.

**Important:** Since even the order in which conditionally registered event handlers get registered must match on all clients, conditional event handlers are probably the most prone to desyncs. So while it is possible to do use them, and may potentially help with organization in some rare cases (involving instantiation, most likely), the vast majority of the time they only add a lot of complexity and risk for desyncs with little gain.

After `OnClientBeginCatchUp` the Lock Step system begins rapidly running game ticks and raising events as well as input action handlers in those game ticks in order to catch up with every other client in the world. The reason why this client is behind in the first place is because sending all the game states to it may take some time, depending on the size of game states, so the client then has to run all actions performed after the game states were captured and sent over the network.

Note that during this process, it is good to keep in mind that any input action sent will run many many game ticks in the future, after the clint is fully caught up. Depending on what the input action is for it may make more sense not to send it at all. To do so, remember that the system is currently catching up using some variable set in `OnClientBeginCatchUp`. The given variable can then be unset in `OnClientCaughtUp`, see below.

Once the client has fully caught up and is now running at the game tick every other client is at, the `OnClientCaughtUp(int playerId)` event will be raised on _every client_. This is a Lock Step event like any other, which means modification to game states or changing event registration is allowed within this event handler.

## Joining and Leaving Clients

There are events for players joining and leaving:

- `OnClientJoined(int playerId)`
  - It is **guaranteed** to run **before** any input action sent from within the joining client's `OnClientBeginCatchUp` handler
  - It runs on every client, including the joining client, be it delayed in real time - but still on the same game tick as everywhere else - since that client has to catch up first
  - The VRCPlayerApi for the given playerId may not even exist by the time this is raised. (VRCPlayerApi objects are not, and cannot be, part of any game state)
    - To input player specific data into the game state:
      - Send an input action in `OnClientBeginCatchUp`
      - Send an input action in `OnClientCaughtUp`, but only on the joining client (by checking if the playerId is the local player's id)
  - Also runs for the first client, shortly after `OnInit`
- `OnClientLeft(int playerId)`
  - The VRCPlayerApi for the given playerId does not exist anymore

# Sending InputActions within InputAction

Sending an input action from within another input action unconditionally ultimately means that every client is sending an input action. This may of course be intentional, however when it is unintentional it can cause problems. And determining if it is intentional or not may be difficult.

So when to send an input action from within another input action?

When every client must input some non game state data **that exists only on their client** (or is player specific data, like the local player's VRCPlayerApi) at the same time.

In every other scenario only a single, or a select few, clients should send an input action, not every single one.

A good example is a player joining the instance and you'd like to make their avatar size apart of the game state:\
The simple way to do this would be to send an input action in `OnClientBeginCatchUp`, since that event only runs on the newly joined client. However depending on what you're doing it may be more useful to send the data later, specifically once the client is fully caught up. In that case, the `OnClientCaughtUp` event would the the appropriate place to send the event from, however this event runs on every client, therefore you would first check that the given playerId passed to the event is that id of the local player, and only if that's the case send an input action.

Keep in mind that `OnClientBeginCatchUp` is the only non game state event (and only runs on one client), **every other event runs on every client**.

# Events

## Game State Events

Game state events are raised on every client in the exact some order on on the same game tick.

Only inside of these events, modification of game states and changing Lock Step event registration is allowed. See [game states](#game-states) for what rules to follow when modifying game states.

- `OnInit()` (This is [special](#first-client). Also, it only runs on the first client, but at that time it is the only - therefore every - client.)
- `OnClientJoined(int playerId)`
- `OnPlayerCaughtUp(int playerId)`
- `OnClientLeft(int playerId)`
- `OnTick()`
- Every custom input action handler

## Non Game State Events

Non game state events are events running on either just one client or every client, and are not running on any specific tick, just whenever they happen.

These events are not allowed to modify the game states, nor change registration of Lock Step events.

- `OnClientBeginCatchUp(int playerId)` (This is an exception as it is allowed to change event registration, but still must not modify game states)
- Every unity or VRChat event
