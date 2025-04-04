
[To index](index.md)

# Concept

The high level concept is described [in the overview](overview.md#lockstep-master-preference).

Note that the `LockstepMasterPreference` prefab must be in the scene in order for the concept of master preference to exist.

Luckily however the prefab is not required to be dragged in manually specifically when scripts are defining a reference to the script like this:

```cs
using JanSharp;
// ...
[HideInInspector] [SerializeField] [SingletonReference] private LockstepMasterPreference masterPreference;
```

# Introduction

The master preference system implements a [latency state](latency-states.md). This concept quickly explodes in complexity however this API is kept rather simple.

Regardless of the value of `lockstep.InGameStateSafeEvent`, `SetPreference` can be called to change the preference for a given player to be master.

If `SetPreference` is called outside of a [game state safe event](events.md#non-game-state-safe-events) then it modifies the latency state instantly which raises the [`OnLatencyHiddenMasterPreferenceChanged()` event](#events), and the getter functions in the [API](#api) for the latency hidden preference reflect the change accordingly.

Then eventually the [game state](game-states.md) gets modified, `OnMasterPreferenceChanged` gets raised, and the latency state matches the game state again.

For further details about `SetPreference` refer to its intellisense, for example by hovering over the function in code.

# Events

Provides 2 events through the `[LockstepMasterPreferenceEvent]` attribute, same as how the [`[LockstepEvent]` attribute](events.md) works.

Both [game state safe events](events.md#non-game-state-safe-events).

- `OnMasterPreferenceChanged()` Use `ChangedPlayerId` and the rest of the api
- `OnLatencyHiddenMasterPreferenceChanged()` Use `ChangedPlayerId` and the rest of the api

# API

- `void SetPreference(uint playerId, int preference, bool valueIsGSSafe = false)`
- `uint ChangedPlayerId { get; }`
- `int GetPreference(uint playerId)`
- `int GetHighestPreference()`
- `int GetLowestPreference()`
- `int GetLatencyHiddenPreference(uint playerId)`
- `int GetHighestLatencyHiddenPreference()`
- `int GetLowestLatencyHiddenPreference()`
