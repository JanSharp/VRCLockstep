
[To index](index.md)

# Clients vs Players

Lockstep distinguishes between clients and players.

- Players more so refer to the actual person
  - A player may join and leave multiple times
  - They are identified through their display name, since the player id changes when they leave and rejoin
  - Note that multiple players can have the same display name, unfortunately
- Clients refer to a player being part of the lockstep game state
  - The lifetime of a client is one join-leave cycle of a player
  - A rejoining player ends up being a different client
  - Lockstep stores the player's display name as part of the lockstep game state to enable checking if a client actually refers to a player which was in the world previously
  - A client exists separate from the `VRCPlayerApi`. In particular when a player leaves, the client stays in the game state for a little bit longer, so the `VRCPlayerApi` for that client's player id no longer exits
