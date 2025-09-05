
[To index](index.md)

# Data Lifecycle

## Build Time

There's 2 parts to this, both can only be done at build time - in other words these must exist in the scene when entering play mode or uploading and cannot be changed at runtime:

- Definition of [game states](game-states.md)
- Registration of [Lockstep events](events.md)

## First Client

The first client joining a world is special, it is the one which is going to initialize all systems that are using Lockstep.

After a bit of time, `OnInit()` will get raised, any handlers listening to `OnInit` have just one job:

- Initialize game states, using any source data they wish

[Input actions](input-actions.md) can be sent as soon as `OnInit` is running, before then they are invalid and ignored. While sending input actions inside of `OnInit` is possible, there's little reason to, as `OnInit` is allowed to modify game states already anyway. However if it's easier to send some input actions, that's perfectly fine, they'll run _very_ shortly after `OnInit`, if not in the same game tick.

Once `OnInit` is raised, `lockstep.IsInitialized` is `true`.

## Every Other Client

Every other client does not run `OnInit`. Instead, all [game states](game-states.md) will be sent to new clients, using [serialization](serialization.md). Once all data has been received and deserialized, `OnClientBeginCatchUp` is raised **only on the new client**. This means `OnClientBeginCatchUp` is rather limited:

- **Must not** modify any game state, as it is [non game state safe](events.md#non-game-state-safe-events)
- Can, however, be used to save some non game state safe values. Just remember that when using said non game state safe values in order to modify the game state, an input action must be sent and the game state can only be modified from within the input action handler

Speaking of input actions, similar to `OnInit` as soon as `OnClientBeginCatchUp` is running sending input actions is allowed, before then they are invalid and ignored. Therefore if modifying the game state in `OnClientBeginCatchUp` is desired, an input action must be sent first. Note however that this input action has a very high likelihood of running many many game ticks after it was sent, since as the name of the event suggests, the client is just starting to catch up to all other clients.

Once `OnClientBeginCatchUp` is raised, `lockstep.IsInitialized` is `true`.

After `OnClientBeginCatchUp` the Lockstep system begins rapidly running game ticks and raising events as well as input action handlers in those game ticks in order to catch up with every other client in the world. The reason why this client is behind in the first place is because sending all the game states to it may have taken some time, depending on the size of game states, so the client then has to run all actions performed after the game states were captured and sent over the network.

Note that during this process, it is good to keep in mind that any input action sent will run many many game ticks in the future, after the clint is fully caught up. Depending on what the input action is for it may make more sense not to send it at all. To do so check the `IsCatchingUp` flag on Lockstep.

Once the client has fully caught up and is now running at the game tick every other client is at, the `OnClientCaughtUp` event will be raised on _every client_. This is a [game state safe event](events.md#game-state-safe-events) like any other, which means modification of game states is allowed within this event handler.

## Joining and Leaving Clients

There are [events](events.md) for players joining and leaving:

- `OnPreClientJoined`
- `OnClientJoined`
- `OnClientLeft`

For details see [here](events.md#detailed-docs).
