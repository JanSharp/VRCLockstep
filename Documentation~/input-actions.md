
[To index](index.md)

# Input Actions

Input actions refer to the input action event handler, as defined by the `LockstepInputAction` attribute.

Send functions, those that call `SendInputAction`, aren't input actions themselves.

Input actions are run on every client in the same tick in the same order.

Input actions are game state safe events.

Input actions can be used to input non [game state safe data](game-states.md#game-state-safe-data) into the game state. Effectively passing data from a send function into the game state through an input action.

The transfer of data is done through serialization in the send function and deserialization in the input action, see [serialization](serialization.md).

Input actions can be configured to keep track of how much time has passed from the Send call until the input action gets run. This **timing** information is **not game state safe**.

# Singleton Input Actions

The purpose of singleton input actions is to input non [game state safe data](game-states.md#game-state-safe-data) into the game state while inside of a game state safe context.

In other words:

- When in a non game state safe context, `SendInputAction` must be used
- When in a game state safe context, `SendSingletonInputAction` likely makes more sense, but not necessarily

`SendInputAction` simply sends an input action, meaning if multiple players call it, multiple input actions will be sent.

Game state safe contexts naturally imply that code is running on every client. Using `SendInputAction` here subsequently means every client sends an input action.

`SendSingletonInputAction` however ensures that only 1 client sends an input action. All other clients are going to wait until this input action gets run.

If the responsible client leaves immediately after sending the input action got sent it may not actually have been sent. In that case a different clients becomes responsible and effectively re-sends the input action.

Ultimately it is guaranteed that the input action only gets run once.

In some game state safe contexts it may actually be desirable for every client to send an input action, in which case regular `SendInputAction` is the function to use.
