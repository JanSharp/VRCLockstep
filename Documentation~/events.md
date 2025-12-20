
[To index](index.md)

# Listening to Events

In order to listen to events raised by lockstep use the `[LockstepEvent]` or `[LockstepOnNthTick]` attribute on a public method.

For `[LockstepEvent]` listeners, the method name must match the `LockstepEventType` enum field name.

Scripts listening to events must:

- Exist in the scene at build time
- Not be instantiated at runtime
- Not be destroyed at runtime

**Disabled scripts still receive all events raised by lockstep.** This is required in order to upkeep [determinism](game-states.md#determinism). If a script should ignore events when it is disabled, it can do so by checking a variable inside of the event handler and early returning, however keep in mind that in [game state safe events](#game-state-safe-events) this variable must also be a game state safe variable, otherwise any modifications made to the game state in the event would result in desyncs.

## Instantiation and Listeners

As mentioned above, event listeners using attributes cannot be instantiated at runtime, they simply would not receive any event.

If listening to events on instantiated objects is required use a manager which keeps a list of instantiated objects.

When doing so for game state safe events make sure to be mindful of whether the list of instantiated objects is itself game state safe or not. If it isn't then raising events on these objects inside of a game state safe event turns these inner raised events into non game state safe events.

However `lockstep.InGameStateSafeEvent` would not reflect this change, making this a potential source of bugs (`lockstep.InGameStateSafeEvent` being `true` when in reality the context is non game state safe). In order to avoid this issue it would be best for the list of instantiated objects to be game state safe.

# Detailed Docs

For more detailed information about each event, see intellisense for each member of the `LockstepEventType` enum as well as the `[LockstepEvent]` and `[LockstepOnNthTick]` attributes.

# Game State Safe Events

Game state safe events are raised on every client in the exact same order in the same tick.

Modification of game states is restricted to inside of these events. See [game states](game-states.md) for what rules to follow when modifying game states.

- `OnInit()` (This is [special](data-lifecycle.md#first-client). Also, it only runs on the first client, but at that time it is the only - therefore every - client.)
- `OnInitFinished()` Exists for unusual use cases. See its xml annotations
- `OnPreClientJoined()` Use `JoinedPlayerId` from the lockstep api
- `OnClientJoined()` Use `JoinedPlayerId` from the lockstep api
- `OnClientCaughtUp()` Use `CatchingUpPlayerId` from the lockstep api
- `OnClientLeft()` Use `LeftPlayerId` from the lockstep api
- `OnMasterClientChanged()` Use `OldMasterPlayerId` and `MasterPlayerId` from the lockstep api
- `OnLockstepTick()`
- `[LockstepOnNthTick]` (Methods with this attribute)
- `OnExportStart()` Use export related properties on Lockstep
- `OnExportFinished()` Use export related properties on Lockstep
- `OnImportStart()` Use import related properties on Lockstep
- `OnImportOptionsDeserialized()` Use import related properties on Lockstep
- `OnImportedGameState()` Use import related properties on Lockstep
- `OnImportFinished()` Use import related properties on Lockstep
- `OnPostImportFinished()` Exists for unusual use cases. See its xml annotations
- Every custom input action event handler
- Game state deserialization for import specifically (late joiner deserialization is different)

This isn't really an event, but it is called by the Lockstep system:

- `DeserializeGameState`
  - When not importing this is actually a **non game state safe event**
  - When [importing](game-states.md#exports-and-imports) this can and should modify the game state it is associated with, potentially more than just that, see linked docs

# Non Game State Safe Events

Non game state safe events are raised on any amount of clients and not running on any specific tick, just whenever they happen.

These events are not allowed to modify the game states.

- `OnClientBeginCatchUp()` Use `CatchingUpPlayerId` from the lockstep api
- `OnPostClientBeginCatchUp()` Exists for unusual use cases. See its xml annotations
- `OnExportStart()` Use export related properties from the lockstep api
- `OnExportFinished()` Use export related properties from the lockstep api
- `OnExportOptionsForAutosaveChanged()`
- `OnAutosaveIntervalSecondsChanged()`
- `OnIsAutosavePausedChanged()`
- `OnLockstepNotification()` Use `NotificationMessage` from the lockstep api
- Every unity or VRChat event

These aren't really events, but they are called by the Lockstep system:

- `SerializeGameState`
- `DeserializeGameState`
  - When not importing (so for late joiners effectively), it is allowed and required to initialize the game state it is associated with
  - When [importing](game-states.md#exports-and-imports) this is actually a **game state safe event**
- Every function implemented in classes deriving from `LockstepGameStateOptionsUI` and `LockstepGameStateOptionsData`

# Delayed Events

`SendEventDelayedTicks` shares a lot of infrastructure with [input actions](input-actions.md), however they themselves are not input actions.
