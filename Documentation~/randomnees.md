
[To index](index.md)

# Randomness

As implied in the [non game state events](events.md#non-game-state-safe-events) section, Unity's `Random` class is not game state safe. What to do if a game state requires deterministic randomness but it cannot use `Random`?

The JanSharp Common package provides a `RNG` WannaBeClass which also exposes the internal state of the random number generator - making it possible to be part of a game state - and lockstep provides a wrapper around this class called `SerializableRNG`.

To create a `SerializableRNG` instance call `lockstep.NewSerializableRNG()` inside of a [game state safe event](events.md#non-game-state-safe-events), and then access the actual random number generator using the `rng` field. The random number generator is deterministic.

To make this rng instance part of the game state, use `lockstep.WriteCustomClass` when serializing and `lockstep.ReadCustomClass` when deserializing.

# Shuffling

Shuffling is similarly not game state safe and must therefore be shuffled manually using a game state safe random number generator as described above.

<!-- TODO: Mention array shuffling api if it gets implemented in the JanSharp Common package. -->
