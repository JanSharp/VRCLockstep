
[To index](index.md)

# Input Action Latency

[Input actions](input-actions.md) get sent, the [Lockstep master](overview.md#lockstep-master) receives them, decides which tick to run them in, sends this tick association to all clients, they receive it and the input action gets run. This is **significant latency**, minimum 100ms, likely over 200ms.

Latency is no fun. A player pressing a button and not getting immediate response is tiring.

Unfortunately there are scenarios where it is **required** to implement a latency state. Imagine for example an item system. A player picking up an item and moving it around expects that item to move instantly as they are moving their hand. It would be horrible if that item were to float behind their hand delayed by 200ms, or worse rubber band between their hand and the 200ms delayed position. This similarly applies to other systems the player interacts with through hand/body movement. Even things like moving a UI slider pretty much require a latency state.

# Latency States

The solution is hiding this latency through latency states.

A latency state is intentionally desynced from the game state. Actions performed by the local player get applied to the latency state instantly while an input action is in transit. Then when the input action gets run, the game state gets updated and the latency state is in sync with the game state again.

## Implementation

Implementing latency hiding is inherently complex due to the introduction of a second state. Going from 1 of something to 2 in programming is quite often a big jump in complexity.

That said however the latency state does not need to be a complete duplicate of the game state. It likely makes more sense to split the state such that:

- The game state represents the data structure which is always in sync
- The latency state represents the visual state presented to the player
  - Mainly backed by the game state
  - With some additional data in order to be able to present the intentional desync to the player

When an action is performed which should be latency hidden the general approach is the following:

- On a client performing an action
  - Prepare for sending an input action, saving values in variables and writing them to the [write stream](serialization.md#data-streams)
  - Send an input action and **save the returned unique id** in a variable
  - Put this unique id into a dictionary, remembering that it is a latency hidden input action
  - Immediately modify the latency state
- When the input action runs
  - Update the game state as per usual for input actions
  - Get the `lockstep.SendingUniqueId` to check if this input action was latency hidden on the local client
  - If yes then do not update the latency state, which is visible to the player
  - If no then perform the action similarly to how the action was performed on the sending client
