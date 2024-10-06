
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
  - OnLockstepTick
    - Before running a tick, ensure all input actions for this tick have been received already
      - If yes, run all input actions associated with this tick
      - If no, mark missing input actions for this tick as "waiting on" and pause ticks

## Input Action Sending in Single Player Mode

When the send input action function is called and Lockstep is currently in single player mode, the action is simply instantly performed, without even touching the InputActionSync script. This is not only an optimization, it is a requirement because when only a single client is in an instance, one can mark an object for serialization using RequestSerialization, however it will never actually serialize, not until another client joins the instance. And the above system for sending input actions requires the OnPostSerialization event.

Similarly, while in single player mode, the tick in which the input action got run does not get sent to the tick sync script.

# Internal Client States

There is both a game state and some local only flags.

The game state: probably a dictionary: playerId => state, with state being:

- Master
- WaitingForLateJoinerSync
- CatchingUp
- Normal

Non game state flags:

- `leftClients`: collection of clients (player ids) which have left the instance but are still in the above game state.
- `currentlyNoMaster`: bool

# Player Join

<!-- cSpell:ignore Factorio, desync, desyncs -->

- Ignore the OnPlayerJoin event
- On the new client, when an InputActionSync gets assigned to the local player
  - Wait 2 seconds
  - Check if the local player (the new client) is master
    - If yes, this client is either the first client or the previous master instantly left after this client joined, so perform the following and abort
      - Mark as isMaster in the Lockstep script
      - Initialize the internal "client states" game state
        - Save this client as the master client in said game state
      - Enable [single player mode](#single-player-mode)
      - Fully enable input action handling
      - Raise `OnInit()`
      - Raise `OnClientJoined(int playerId)`
      - Save Time.time as the start time for running ticks
      - Enable running ticks
      - Done
  - Mark Lockstep input action handling as initialized
    - Don't actually run any ticks
    - Save any and all received input actions as pending
    - Save any and all received LockstepTickSync "input actions to run on tick"
    - Ignore and discard any attempts at sending input actions locally
      - `ClientJoinIA` is an exception:
        - Allow it to be sent
        - Do not save it as pending
          - (Technically it could, however it would never get run locally anyway, as it happens before the game states that will be sent to this client get captured and serialized)
  - Send `ClientJoinIA`
- Receive `ClientJoinIA`
  - On Every client, except sending (because the sending client will never run it)
    - Mark the given player id as "waiting for late joiner sync" in an internal "client states" game state
  - On master
    - Wait 3 seconds * the amount of player joined in the last 5 minutes, capped at 30 seconds
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
    - The client knows the current game tick all other clients are already at, thanks to LockstepTickSync
    - Raise the `OnClientBeginCatchUp(int playerId)` event
      - Allows systems to initialize non game states based on the current game states
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

What if there were input actions that were supposed to run but we never received them? Like when the master sent and ran an action, but the action itself was never received, but the tick sync for it was?\
It's supposed to be impossible. An input action only get enqueued to run on a specific tick if it has already been successfully sent, making it incredibly unlikely for the tick sync with the input action enqueued in some tick to arrive before the input action itself. If it does happen though, then the system cannot recover, as it doesn't know if it is only the local client missing the input action, so it cannot drop the input action. Even when the local client became master, it cannot make that decision, because other clients may have already run past this tick because they did receive the input action. If this can actually happen, I don't know. But since I don't know, I can't make any assumptions, so again, it is unrecoverable if it does happen.

When the world converts from multiplayer to single player, the system must remove all queued data from the InputActionSync scripts (both the local player's one and the late joiner one), and instantly raise their associated actions (though there's nothing to do for the late joiner ones, just drop them).

- Ignore OnPlayerLeave (for now? So long as I can trust the player object pool I suppose)
- When an InputActionSync get unassigned
  - On the master client
    - Mark the given client as "left" locally
    - Wait 1 second
    - Check if [single player mode](#single-player-mode) should be activated, and activate it if yes
    - Send `ClientLeftIA`
  - On every client that isn't the master
    - Check if the given client still exists in the internal "client states" game state
      - If no
        - Check if there **aren't** any clients in the internal "client states" game state
          - Set `currentlyNoMaster` flag
          - Wait 1 frame (Because I have trust issues with VRChat)
          - Call CheckMasterChange
      - If yes
        - Mark the given client as "left" locally
        - Check if the leaving client had the "master" state in the internal "client states" game state
          - If yes
            - Set `currentlyNoMaster` flag
            - Wait 1 frame (Because I have trust issues with VRChat)
            - Call CheckMasterChange
- Receive `ClientLeftIA`
  - On every client
    - Remove the client from the "client state" game state
    - If isMaster
      - Check if there are any clients waiting for late joiner data
        - If no, clear late joiner sync queue
    - Raise the `OnClientLeft(int playerId)` event

## Master Transfer

- OnOwnershipTransfer on LockstepTickSync
  - On every client
    - Wait 1 frame (Because I have trust issues with VRChat)
    - Call CheckMasterChange

- CheckMasterChange
  - If this client isn't master, the `currentlyNoMaster` flag is set and Networking.IsMaster is true
    - Unset `currentlyNoMaster` flag
    - Set isMaster to true in the Lockstep script
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
  - Set `currentlyNoMaster` to false

## Single Player Mode

- Check if single player mode should be activated
  - If the count of clients in "client state" minus the count of players marked as left == 1
    - Yes, enter single player mode

- Enter single player mode
  - Clear and discard all queued data in the late joiner InputActionSync script
  - Clear queued data from the local player's InputActionSync script and instantly run all input actions that were queued
  - Clear the "run action on tick" collection on the LockstepTickSync script

While in single player mode, input actions are handled differently. See [input action sending](#input-action-sending-in-single-player-mode) for more info.

# Game States

There can be multiple game states. One could argue though that all of them combined are functionally simply one big game state.

A game state can look however it wants, the data structure truly does not matter and is 100% user (programmer) defined.

In order for some data structure to qualify as a game state, interaction with that data must follow a set of rules:

- Reading from it can happen any time
- Writing to it must only happen inside of input actions, or otherwise game state safe events raised and declared as such by the Lockstep system itself, like the on tick event
- Writing to it must only use data that is apart of the game state, or passed in as arguments to the input action/event that got raised
  - The only except is OnInit which is allowed to use any data it wants

When the rules for writing to it are not followed it results in a desync.

Additionally there must be a serialize and deserialize function defined for the data structure.

- A serialize => deserialize cycle must result in the exact same data structure

Optionally a game state can be flagged as supporting import/export. When flagged as such, the serialization and deserialization events must have extra handling for import/export. When serializing or deserializing, a bool parameter is set to true indicating that this is an import/export call. The serialize function should then serialize extra data, if needed, in order for the state to be fully restored when importing the serialized data into another - potentially future version - of the world. The deserialize function should read that extra data to restore the state correctly, including handling old versions of the data since it might be exported data from on old version of the system. Backwards compatibility may be dropped at any point in time, up to the programmers discretion, however note that with how VRChat works, there's no way to load an older version of the map to migrate an exported data set to a newer version, so by dropping backwards compatibility, there may be exported data someone has stored on their machine that is now no longer usable.

# Serialization Rules

<!-- cSpell:ignore structs -->

For any kind of serialization in the Lockstep system, binary "streams" are used.

The Lockstep API contains Read and Write functions for all primitive data structures as well as some structs such as vectors and quaternions, or DateTimes.

In any serialization context it is expected to simply call Write functions on Lockstep to write data to an internal stream inside of Lockstep.

In any deserialization context it is expected to call Read* functions on Lockstep in the exact same order as the Write functions to read from an internal stream inside of Lockstep.

So basically to send an `int value` in an input action, `lockstep.WriteInt(value)` in the send function, call `lockstep.SendInputAction(...)`, and `int value = lockstep.ReadInt()` in the input action event.

The Write and Read functions and their data types must match up perfectly.

The de/serialization contexts are input actions send/receive and game state de/serialization.

Since both the write and read functions are interacting with internal streams inside of the Lockstep instance, **only 1 stream can be written concurrently**. Same for reading, but that's effectively enforced by Lockstep anyway.

If serialization of lists, dictionaries or similar is required, simply Write the length of the given list/data structure and then Write each value or key value pair or similar n times afterwards. Then deserialization can be achieved by reading the length and having a for loop looping n times reading the contained values of the list/dictionary or otherwise.

(For optimization I would recommend to `WriteSmallUInt(uint)` for lengths of arrays/lists/dictionaries, but `WriteSmallInt(int)` would also work.)

If serialization of other/custom data structures is required, simply unroll them into primitives.

Every form of data is possible to be represented using a flat list of primitives... Well, unless Udon doesn't expose the internal values, which may apply to VRCUrl objects, not sure. If that's the case, well you're screwed, you simply won't be able to sync that data using this system. I know that sucks, but I'm not going to add _a ton_ of extra complexity just for those few special values.

# Data Lifecycle

## Initialization

There's 2 parts to this, both can only be done at initialization - in other words it must all exist in the scene at build time and cannot be changed at runtime:

- Definition of game states
- Registration of Lockstep events

Defining games states is done using an abstract class as a base class. Said abstract class will require you to implement a few methods and properties:

- string GameStateInternalName { get; } // Must be a completely unique name for this game state. Use whatever you like, but I'd recommend `author-name.package-name` (or just some descriptive name of your system as the `package-name`.). (If you're making a package it's effectively the internal package name without the `com.` prefix.)
- string GameStateDisplayName { get; } // A user readable and identifiable name for this system/game state.
- bool GameStateSupportsImportExport { get; } // When false, the isExport and isImport parameters will naturally never be true.
- uint GameStateDataVersion { get; } // Current version used by SerializeState and DeserializeState. Can start at 0, but doesn't really matter.
- uint GameStateLowestSupportedDataVersion { get; } // Lowest version of exported data DeserializeState is capable of importing.
- void SerializeState(bool isExport); // Game states must be able to successfully be serialized at every point in time. Uses `Lockstep.Write` calls to serialize data.
- string DeserializeState(bool isImport); // A non null return value indicates failure and the string value is an error message. Must return the same value on all clients (in other words: be game state safe). Uses `Lockstep.Read` calls to serialize data.

Registration of Lockstep events at initialization is done using attributes on methods which can be on any UdonSharpBehaviour. The system will find all instances of UdonSharpBehaviours with such methods in the scene at build time (entering play mode or before uploading), and save them as registered. This means instantiating a new instance of such scripts at runtime will not register these event listeners and there's no way to register them at runtime. If this is required, use a manager script which has the event listener and have it have a list of other listening scripts. When using that kind of setup however make sure to keep order of execution in mind when modifying the game state. A different order or amount of the listeners in the manager could cause a desync if those listeners are modifying the game state.

## First Client

The first client joining a world is special, it is the one which is going to initialize all systems that are using Lockstep.

After a bit of time, `OnInit()` will get raised, any handlers listening to `OnInit` have just one job:

- Initialize game states, using any source data they wish

Input actions can be sent as soon as `OnInit` is running, before then they are invalid and ignored. While sending input actions inside of `OnInit` is possible, there's little reason to, as `OnInit` is allowed to modify game states already anyway. However if it's easier to send some input actions, that's perfectly fine, they'll run _very_ shortly after OnInit, if not in the same game tick.

## Every Other Client

Every other client does not run `OnInit`. Instead, all game states will be sent to new clients. Once all data has been received and deserialized, `OnClientBeginCatchUp(int playerId)` is raised **only on the new client**. This means `OnClientBeginCatchUp` is rather limited:

- **Must not** modify any game state. It is the only event raised by Lockstep with this restriction
- Can, however, be used to save some non game state values. Just remember that when using said non game state values in order to modify the game state, you must send an input action and only modify the game state from within the input action handler

Speaking of input actions, similar to `OnInit`, as soon as `OnClientBeginCatchUp` is running, sending input actions is allowed, before then they are invalid and ignored. Therefore if you'd like to modify the game state in `OnClientBeginCatchUp` you must send an input action first. Note however that this input action has a very high likely hood of running many many game ticks after it was sent, since as the name of the event suggests, the client is just starting to catch up with all other clients.

After `OnClientBeginCatchUp` the Lockstep system begins rapidly running game ticks and raising events as well as input action handlers in those game ticks in order to catch up with every other client in the world. The reason why this client is behind in the first place is because sending all the game states to it may take some time, depending on the size of game states, so the client then has to run all actions performed after the game states were captured and sent over the network.

Note that during this process, it is good to keep in mind that any input action sent will run many many game ticks in the future, after the clint is fully caught up. Depending on what the input action is for it may make more sense not to send it at all. To do so check the `IsCatchingUp` flag on Lockstep.

Once the client has fully caught up and is now running at the game tick every other client is at, the `OnClientCaughtUp(int playerId)` event will be raised on _every client_. This is a Lockstep event like any other, which means modification to game states is allowed within this event handler.

## Joining and Leaving Clients

There are events for players joining and leaving:

- `OnClientJoined(int playerId)`
  - It is **guaranteed** to run **before** any input action sent from within the joining client's `OnClientBeginCatchUp` handler
  - It runs on every client, including the joining client, be it delayed in real time - but still on the same game tick as everywhere else - since that client has to catch up first
  - The VRCPlayerApi for the given playerId may not even exist by the time this is raised. (VRCPlayerApi objects are not, and cannot be, part of any game state)
    - To input player specific data into the game state:
      - Send an input action in `OnClientBeginCatchUp`
      - Send an input action in `OnClientCaughtUp`, but only on the joining client (by checking if the playerId is the local player's id)
  - `OnClientJoined` also gets raised for the first client, shortly after `OnInit`
- `OnClientLeft(int playerId)`
  - The VRCPlayerApi for the given playerId does not exist anymore

# Sending InputActions within InputAction

Sending an input action from within another input action unconditionally ultimately means that every client is sending an input action. This may of course be intentional, however when it is unintentional it can cause problems. And determining if it is intentional or not may be difficult.

So when to send an input action from within another input action?

When every client must input some non game state data **that exists only on their client** (or is player specific data, like the local player's VRCPlayerApi) at the same time.

In every other scenario only a single, or a select few, clients should send an input action, not every single one. `SendSingletonInputAction` may be usable in some of these scenarios.

A good example is a player joining the instance and you'd like to make their avatar size apart of the game state:\
The simple way to do this would be to send an input action in `OnClientBeginCatchUp`, since that event only runs on the newly joined client. However depending on what you're doing it may be more useful to send the data later, specifically once the client is fully caught up. In that case, the `OnClientCaughtUp` event would be the appropriate place to send the input action from, however this event runs on every client, therefore you would first check that the given playerId passed to the event is that id of the local player, and only if that's the case send an input action. You could also use `SendSingletonInputAction` with the joined client's id being the responsible player, however the major difference would be that if the joined client instantly left after sending the input action, this input action would still be sent but by a different player - after the `OnClientLeft` event. Which makes `SendSingletonInputAction` the wrong pick in this scenario, however it may be the right one in others.

Keep in mind that `OnClientBeginCatchUp` is the only non game state event (and only runs on one client), **every other event runs on every client**.

# Events

## Game State Events

Game state safe events are raised on every client in the exact same order on the same game tick.

Only inside of these events, modification of game states is allowed. See [game states](#game-states) for what rules to follow when modifying game states.

- `OnInit()` (This is [special](#first-client). Also, it only runs on the first client, but at that time it is the only - therefore every - client.)
- `OnClientCaughtUp()` Use `CatchingUpPlayerId` from the lockstep api.
- `OnPreClientJoined()` Use `JoinedPlayerId` from the lockstep api.
- `OnClientJoined()` Use `JoinedPlayerId` from the lockstep api.
- `OnClientLeft()` Use `LeftPlayerId` from the lockstep api.
- `OnMasterClientChanged` Use `OldMasterPlayerId` and `MasterPlayerId` from the lockstep api.
- `OnLockstepTick()`
- `OnImportStart()` Use import related properties on Lockstep inside this event to know more about the import process.
- `OnImportedGameState()` Use import related properties on Lockstep inside this event to know more about the import process.
- `OnImportFinished()` Use import related properties on Lockstep inside this event to know more about the import process.
- Every custom input action handler

## Non Game State Events

Non game state safe events are events running on either just one client or every client, and are not running on any specific tick, just whenever they happen.

These events are not allowed to modify the game states:

- `OnClientBeginCatchUp()` Use `CatchingUpPlayerId` from the lockstep api.
- `OnGameStatesToAutosaveChanged()`
- `OnAutosaveIntervalSecondsChanged()`
- `OnIsAutosavePausedChanged()`
- `OnLockstepNotification()` Use `NotificationMessage` from the lockstep api.
- Every unity or VRChat event

These 2 aren't really events, but they are called by the Lockstep system.

- SerializeState
- DeserializeState (Allowed to modify (or initialize) the game state it is associated with)

TODO: add master preference game state
  - the player with the highest preference to be master will become master
  - when in the scene, enable a slider for the local player in the info UI
  - when in the scene, enable a slider for each player in the client states list in the info UI
  - import export support
TODO: add game state safe prng
TODO: make a tool to automatically extract autosaves and neatly arrange them in a folder, like the user's documents folder
TODO: on nth tick? game state safe.
TODO: raise event delayed by ticks, game state safe.

- [ ] more consistently send the client joined IA... though looking at it it's already pretty consistent. I don't know how it was possible for the client joined IA to just not get run on the master, as though it wasn't even received...
- [ ] maybe handle receiving tick sync data even when we are owner of the tick sync script
- [x] if candidates are being asked for when we become instance master, either take over that process or reset it and ask again
- [x] if a candidate has been confirmed to become master, remember that id and wait for them to take master, preventing the local client to become master
- [x] if the candidate that has been confirmed was already in leftClients then wait a second before cancelling the candidate asking process and run check master change again
- [x] if the candidate that has been confirmed leaves, wait a second and do the same as above
- [ ] there's apparently a way for the master client to have the client state "normal"... and it makes no sense... for now I've simply added a log message in the only location where it seems like it would be possible to happen, as well as having handled OnClientJoinedIA running late
- [x] maybe ignore player left event for local player
- [x] handle event listeners being null due to being destroyed
- [x] keep late joiner script always enabled to prevent it from buffering all received data forever
- [ ] import/export ui needs a settings button for each game state, probably in the root/main list... maybe just in every list. That'll pop up a ui next to the main UI which has game state specific settings
- [x] clients can fall behind in ticks most likely whenever there is a lag spike... maybe Time.time is not consistent enough and it should use Time.realtimeSinceStartup
- [x] remove LogBinaryData as I've only used it once to debug something and even then it wasn't really useful, and it's a big waste of debug performance
- [x] add current VRChat master and Lockstep master name to the top panel in the info ui
- [x] add ClientStateToString function to lockstep api, which uses a string array internally to "convert" the ClientState byte into the associated string.
- [x] maybe show "become master" button in info ui,
  - [ ] potentially depending on whether or not the master preference game state is in the scene, or depending on a bool setting on in the ui script
- [x] maybe show "make master" button for each client in the info ui.
- [ ] maybe make become master and make master buttons automatically increase your master preference to match the current highest preference out of all clients
- [x] arrange the top panel of the info UI in 2 columns - left labels, right values - to improve readability
- [ ] maybe add option for other systems to add per client info into the info ui
- [x] add api function to check if a player id exists in the internal game state, also mention that function in the docs for SendMasterChangeRequestIA
- [x] change info ui to not require any extra hooks inside of lockstep, just using lockstep events and api
- [ ] fix that requesting for a client to be master when that client is still waiting for late joiner sync soft locks lockstep
- [x] fix lockstep AssociateUnassociatedInputActionsWithTicks setting capacity on a data list can set the capacity to be lower than what it currently has as its capacity which throws an exception
- [ ] fix that spamming Make Master in the clients list back and forth on 2 client entries pretty quickly causes an IA tick association to stay lingering in unique ids by tick. The master change seems to go through successfully, so surely no input action is getting dropped, but this is alarming
- [x] make game states list api return null or none state when called before lockstep has been initialized - so before OnInit or OnClientBeginCatchup
- [x] make useSceneNameAsWorldName public to get rid of the "unused" warning
- [ ] make tick timing more consistent around master changes to improve RealtimeAtTick's usefulness

TODO: ensure that any functions that previously were guaranteed to only ever run on the master are now checking if the local client is still master
