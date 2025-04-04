
[To index](index.md)

# Core Concepts

Lockstep is a networking library designed for keeping potentially larger states reliably synchronized.

[Game states](game-states.md) contain and define these synced states. When talking about "synced states" the term "game state" is generally used instead.

[Input Actions](input-actions.md) modify/mutate game states [deterministically](game-states.md#determinism) ensured by only using [game state safe data](game-states.md#game-state-safe-data).

Input actions are run in the same tick in the same order on all [clients](clients.md).

Late joiners syncing is performed through game state [serialization and deserialization](serialization.md). After syncing, input actions get run quickly in order to [catch up](data-lifecycle.md) to the current tick all other clients are at.

Game states may support [exporting](game-states.md#exports-and-imports) which likely includes additional information during serialization. This information is used by future [imports](game-states.md#exports-and-imports) in order to restore the game state at a future point in time, in a new instance or even different world.

Lockstep raises [events](events.md) throughout many processes which systems can listen to by using [attributes on methods](events.md#listening-to-events).

[Latency States](latency-states.md) get preemptively modified in order to hide [input action latency](latency-states.md#input-action-latency). This is effectively an intentional desync from the game state to make it more responsive, or sometimes out of necessity.
