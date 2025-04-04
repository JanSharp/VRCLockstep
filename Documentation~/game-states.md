
[To index](index.md)

# Game States

A game state is a class deriving from the `LockstepGameState` class. Said abstract class will require defining some properties and implementing a few methods.

A game state can look however it wants, the data structure truly does not matter and is 100% user (programmer) defined.

In order for some data structure to qualify as a game state, interaction with that data must follow a set of rules:

- Reading from it can happen any time
- Writing to it must only happen inside of [game state safe events](events.md#game-state-safe-events) raised by Lockstep
- Writing to it must only use data that is
  - Part of the game state
  - Passed to the event through [Lockstep's read stream](serialization.md#data-streams), like for [input actions](input-actions.md)
  - Part of the Lockstep API where its intellisense declares a value to be [game state safe](#game-state-safe-data)
- The only exception to the above is `OnInit` which is allowed to [use any data it wants](data-lifecycle.md#first-client)
- Game state modifications must be [deterministic](#determinism)

Additionally to these rules there are `SerializeGameState` and `DeserializeGameState` functions game states must implement. Here [serialization rules](serialization.md#serialization-and-deserialization) also apply.

- A serialize => deserialize cycle must result in the **exact same** data structure
- [Exporting and Importing](#exports-and-imports) uses these same functions, though it behaves notably differently

Only when the rules for modifying data are followed and serialization + deserialization has been implemented, then that data can be considered part of the game state, and that data therefore also becomes game state safe.

There can be multiple game states, however when it comes to game state safe events and game state safe data, all game states functionally form one big game state.

## Game State Safe Data

Data that is part of game states as well as some properties in the lockstep API is considered "game state safe". Only this data is allowed to be used when making modifications to game states.

## Determinism

Determinism simply means that an operation is going to have the exact same result every time it is given the same input values.

Game state modification relies on determinism in order to maintain the guarantee that they remain in perfect sync across all [clients](clients.md).

This usually gets complicated as soon as multiple architectures are involved. Arm vs x86. Android is probably using arm, while basically every PC is using x86. The way they handle integer overflow, floating point rounding might also differ... though who knows for sure. (I, JanSharp, am not very knowledgeable in this field.)

But trying to do handle this in Udon would be incredibly annoying so the solution is to pretend and assume that - so long as purely [game state safe data](#game-state-safe-data) is being used - the result is going to be identical on all clients. But just in case that is not truly the case, floating point values may be better to be compared using some "almost equals" function (like `Mathf.Approximately` for example) rather than exact equality (`==`, `!=`).

## Singleton Tips

The JanSharp Common package provides a concept called singletons, [see here](https://github.com/JanSharp/VRCJanSharpCommon?tab=readme-ov-file#libraries).

Most game states are singletons, meaning there is either 0 or 1 instances of that script in a scene, never more.

Therefore it commonly makes sense to do the following:

- Create a prefab which has the game state script component in it
- Open the `.prefab.meta` file for the created prefab with a text editor
- Copy the guid, so the value after `guid:`
- Add the `[SingletonScript("guid goes here")]` attribute to the game state class

Now other scripts can reference this game state simply by doing:

```cs
using JanSharp;
// ...
[HideInInspector] [SerializeField] [SingletonReference] private MySystem mySystem;
```

This causes the game state's prefab to get instantiated into the scene at build time if any other script in the scene has a `[SingletonReference]` (or `[SingletonDependency]`) for the game state.

Fields with that `[SingletonReference]` attribute get populated at build time too, no dragging references into fields in the inspector.

# Exports And Imports

Optionally game states can define `GameStateSupportsImportExport` as `true`.

- Exporting enables saving the current state of the game state into an external text file
- Importing then enables taking this previously saved data and restoring that game state
- Data might be imported:
  - at a later point in time in the same instance of the world
  - into a new instance of the world
  - into a future version of the world
  - into a future version of the game state data structure
  - or even into a different world which has the same game state in it

When flagged as supporting import/export, the `SerializeGameState` and `DeserializeGameState` functions must have separate/extra handling for import/export.

- `SerializeGameState` should serialize extra data, if needed, such as metadata about the current version of the world for example
- `DeserializeGameState` should read that all the data, including extra data, to restore the state correctly
  - It should generally completely replace the current state with the imported state in order to have consistent and predictable behavior for users
    - There can be exceptions for internal data such as player associated data, which may be required to stay or be merged with imported data for a system to continue to function
    - If a [custom import UI](#custom-options-uis) is defined then imports can do pretty much whatever they want so long as the UI keeps behavior predictable
  - It must be able to handle data from older versions of the map
    - potentially discarding data - partially or fully
    - falling back to defaults
    - etc.
  - It may handle old versions of the game state data structure. Specified using the `GameStateLowestSupportedDataVersion` property

Backwards compatibility may be dropped at any point in time, up to the programmers discretion, however note that with how VRChat works, there's no way to load an older version of the map to migrate an exported data set to a newer version, so by dropping backwards compatibility, there may be exported data someone has stored on their machine which becomes completely unusable.

## Custom Options UIs

Double optionally (since import/export is itself optional) game states can define custom UIs for export or import options. The 2 are separate from each other.

A custom options UI consists of 2 parts:

- An Options UI class, deriving from `LockstepGameStateOptionsUI`
  - Defines how the UI looks
  - Handles user input
- An Options Data class, deriving from `LockstepGameStateOptionsData`
  - Defines the backing data that the UI presents and modifies
  - Can exist without a UI currently showing it

In order to make proper use of this API an understanding of 2 other (external) systems is required:

- The [`GenericValueEditor`](https://github.com/JanSharp/VRCGenericValueEditor) and its Widgets/WidgetData
- The [`WannaBeClass`](https://github.com/JanSharp/VRCJanSharpCommon?tab=readme-ov-file#wannabeclasses) concept from the JanSharp Common package

Both `LockstepGameStateOptionsData` and `WidgetData` derive from `WannaBeClass`, making it particularly important to understand this concept to avoid memory leaks.

# Example

This example is a small game state with just 2 integers, a score value for 2 teams. It supports

- Incrementing, decrementing and resetting teams's scores
  - Could be done through UI buttons or other scripts
  - Uses [input actions](input-actions.md)
- [Exports and Imports](#exports-and-imports)
- Having some UI to display the score values (not implemented, but see `UpdateUI`)

When using this as a template, make sure to **read the intellisense** for each **overridden member**, there are a lot of important details in there.

```cs
using JanSharp;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace Example
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    // This is where the [SingletonScript] attribute would go, see 'Singleton Tips'.
    public class TeamScores : LockstepGameState
    {
        public override string GameStateInternalName => "example.team-scores";
        public override string GameStateDisplayName => "Team Scores";
        // Import/Export happens to be incredibly simple to implement for this game state.
        public override bool GameStateSupportsImportExport => true;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;
        public override LockstepGameStateOptionsUI ExportUI => null;
        public override LockstepGameStateOptionsUI ImportUI => null;

        // This is the game state data structure.
        private int teamOneScore = 0;
        private int teamTwoScore = 0;
        // Other scripts can only read, not write.
        public int TeamOneScore => teamOneScore;
        public int TeamTwoScore => teamTwoScore;

        // These public functions could be called by other scripts or through UI buttons.
        public void IncrementTeamOneScore() => SendTeamOneScoreChangeIA(1);
        public void DecrementTeamOneScore() => SendTeamOneScoreChangeIA(-1);
        public void IncrementTeamTwoScore() => SendTeamTwoScoreChangeIA(1);
        public void DecrementTeamTwoScore() => SendTeamTwoScoreChangeIA(-1);

        // This one too.
        public void SendResetScoresIA()
        {
            lockstep.SendInputAction(resetScoresIAId);
        }

        [HideInInspector] [SerializeField] private uint resetScoresIAId;
        [LockstepInputAction(nameof(resetScoresIAId))]
        public void OnResetScoresIA()
        {
            teamOneScore = 0;
            teamTwoScore = 0;
            UpdateUI();
        }

        // public so other scripts are not limited to just incrementing and decrementing.
        // Could be made private however.
        public void SendTeamOneScoreChangeIA(int delta)
        {
            lockstep.WriteSmallInt(delta);
            lockstep.SendInputAction(teamOneScoreChangeIAId);
        }

        [HideInInspector] [SerializeField] private uint teamOneScoreChangeIAId;
        [LockstepInputAction(nameof(teamOneScoreChangeIAId))]
        public void OnTeamOneScoreChangeIA()
        {
            int delta = lockstep.ReadSmallInt();
            teamOneScore += delta;
            UpdateUI();
        }

        // public so other scripts are not limited to just incrementing and decrementing.
        // Could be made private however.
        private void SendTeamTwoScoreChangeIA(int delta)
        {
            lockstep.WriteSmallInt(delta);
            lockstep.SendInputAction(teamTwoScoreChangeIAId);
        }

        [HideInInspector] [SerializeField] private uint teamTwoScoreChangeIAId;
        [LockstepInputAction(nameof(teamTwoScoreChangeIAId))]
        public void OnTeamTwoScoreChangeIA()
        {
            int delta = lockstep.ReadSmallInt();
            teamTwoScore += delta;
            UpdateUI();
        }

        private void UpdateUI()
        {
            // TODO: Update UI for team one's and two's score.
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
            lockstep.WriteSmallInt(teamOneScore);
            lockstep.WriteSmallInt(teamTwoScore);
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
            // Does not even need to check isImport, it is the same logic either way.
            teamOneScore = lockstep.ReadSmallInt();
            teamTwoScore = lockstep.ReadSmallInt();
            UpdateUI();
            return null;
        }
    }
}
```
