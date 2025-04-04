
[To index](index.md)

# Synced Objects

Synced objects are likely different from [game states](game-states.md). A game state is a single system, managing a state. This state can include managing state of what are perceived to be synced objects.

Synced objects do not really do any syncing themselves. They just have an **id** which is part of the game state and can be used by [input actions](input-actions.md) to refer to a specific object.

## Objects Already In The Scene

Assigning ids to objects which already exist in the scene - manually created so to speak - can be done using [BuildTimeIDAssignment](https://github.com/JanSharp/VRCJanSharpCommon?tab=readme-ov-file#libraries), a feature from the JanSharp Common package.

Here is an example of the fields required to make the build time id assignment system find specific objects and assign ids for them. The objects it should find must all have a custom UdonSharpBehaviour script component, as that is what the `myObjects` array gets populated with.

```cs
[BuildTimeIdAssignment(nameof(myIds), nameof(myHighestId))]
[HideInInspector] [SerializeField] private MyObject[] myObjects;
[HideInInspector] [SerializeField] private uint[] myIds;
[HideInInspector] [SerializeField] private uint myHighestId;
```

(See intellisense for `BuildTimeIdAssignment` for details.)

Then in `Start`, or some other time, a for loop through `myObjects` plus `myIds` can be used to set some `id` field inside of each `MyObject` instance, and a `DataDictionary` can be built to be able to lookup objects by their ids. This works since the `myObjects` and `myIds` arrays match each other.

## Instantiated Objects

Assigning ids for these is kind of easier than objects already in the scene. All that is needed is

- A uint (presumably) "next id", which is part of the game state. Likely starting at `1u`, that way `0u` can be used as an invalid id
- A `DataDictionary` with ids being the keys and objects being the values
- Maybe a list of all objects
- Each object has an `id` field of some kind

Then any [game state safe event](events.md#non-game-state-safe-events), such as [input actions](input-actions.md) for example, can create an instance of an object and assign an id to it simply by using the current "next id" and then incrementing it. It's part of the game state, race conditions do not exist. It is that simple.

## Both Preexisting And Instantiated Objects

This combines both approaches above. Only difference being that the "next id" for [instantiated objects](#instantiated-objects) must start past the `myHighestId`which is part of the [objects already in the scene](#objects-already-in-the-scene).

# Exports And Imports

For reference: [exports and imports for game states](game-states.md#exports-and-imports).

When just having preexisting objects, matching imported ids with existing ids is pretty much dead simple. If an id exists in the current world and in the imported data, that's a match. That's it.

When just having instantiated objects, there are no matching ids. Some imported ids may already be used by existing instantiated objects, but that does not mean anything. Those are 2 different objects. Without any [custom import options](game-states.md#custom-options-uis) this is simple to handle: First remove all existing objects, then create imported objects. The imported data can also include the "next id" at the time of export for simplicity.

When having both preexisting and instantiated objects make sure to have the `myHighestId` be part of the exported data. This is required in order to determine which imported id actually refers to a preexisting object vs an instantiated one, since in future versions of a world there could have been preexisting objects added to the scene. This would mean the `myHighestId` in the current version of the world is higher than it was at the time of the export. Therefore the `myHighestId` at the time of export is important to determine which imported ids actually were preexisting objects at that time and should be linked up with current objects.
