
[To index](index.md)

# Input Action Latency

[Input actions](input-actions.md) get sent, the [Lockstep master](overview.md#lockstep-master) receives them, decides which tick to run them in, sends this tick association to all clients, they receive it and the input action gets run. This is **significant latency**, minimum 100ms, likely over 200ms.

Latency is no fun. A player pressing a button and not getting immediate response is tiring.

Unfortunately there are scenarios where it is **required** to implement a latency state. Imagine for example an item system. A player picking up an item and moving it around expects that item to move instantly as they are moving their hand. It would be horrible if that item were to float behind their hand delayed by 200ms, or worse rubber band between their hand and the 200ms delayed position. This similarly applies to other systems the player interacts with through hand/body movement. Even things like moving a UI slider pretty much require a latency state.

# Latency States

The solution is hiding this latency through latency states.

A latency state is intentionally desynced from the game state. Actions performed by the local player get applied to the latency state instantly while an input action is in transit. Then when the input action gets run, the game state gets updated and the latency state should be synced with the game state again.

# Implementation

Implementing latency hiding is inherently complex due to the introduction of a second state. Going from 1 of something to 2 in programming is quite often a big jump in complexity. Additionally it requires thinking about order of operations a lot more than normal input actions modifying game states require.

That said however the latency state may not need to be a complete duplicate of the game state. It likely makes sense to split the state such that:

- The game state represents the data structure which is always in sync
- The latency state represents the visual state presented to the player
  - Mainly backed by the game state
  - With some additional data in order to be able to present the intentional desync to the player

There are a few approaches to actually hiding latency:

(**Latency hidden actions** refers to input actions which effect has already been applied to the latency state preemptively, before the input action actually runs.)

- Keep a counter of currently latency hidden actions
  - Non zero means the latency state can differ from the game state however it wants
  - Zero means the latency state is in perfect sync with the game state
  - There can be multiple counters relating to different parts of the game state and input actions
- Remember the unique ids of latency hidden actions (similar to the above)
  - While here is any unique ids stored, the latency state can differ from the game state
  - No stored unique ids means the latency state must match the game state
  - When receiving an input action first modify the game state and then check if the `SendingUniqueId` is one of the latency hidden ids
    - if there are zero stored unique ids, simply modify the latency state
    - if yes (it is the unique id of a latency hidden action), remove it from the list and do not modify the latency state as the latency state has already been affected at the time of sending the input action
    - if no, clear the list of unique ids and reset the latency state to match the game state. Since the game state has been modified already by this input action and now the latency state has been made to match the game state, once again do not further modify the latency state
  - There can be multiple lists of unique ids relating to different parts of the game state and input actions
- Remember the entire list of latency hidden actions and their data (very cumbersome)
  - Every time an action gets performed, reset the latency state back to the game state
  - If the input action's sending unique id matches one of the latency hidden actions, remove it from the list
  - Then apply all latency hidden actions to the latency state again
- There are probably more ways to do latency hiding

Some latency states must use the unique id returned by `SendInputAction` in order to have a unique way of identifying latency hidden data. Then once the input action actually runs a proper id can be assigned to the newly created data.

This kind of latency hiding is particularly annoying however since there could be input actions required to be sent while referenced latency hidden data does not have an id from the game state yet. Therefore there end up being 2 ways to refer to the same data, at first using the unique id, then later using the proper id.

Using unique ids as permanent ids is discouraged because they take 8 bytes when using `WriteULong`, or 5 to 6 bytes when using `WriteSmallULong` (because unique ids are 2 4 byte numbers combined, but this is an implementation detail which cannot be relied on), both of which is pretty inefficient compared to a simple ascending `uint` id, which with `WriteSmallUInt` ends up being 1 byte for the first 127, then 2 bytes up to 16383, etc.

# Example

Expanding upon the example from the [game states page](game-states.md#example), with these changes (this uses the "counter of currently latency hidden actions" approach):

- Add `private uint localPlayerId;` which gets set in `Start`
- Add `teamOneLatencyScore` and `teamTwoLatencyScore` fields
- Add `teamOneLatencyCounter` and `teamTwoLatencyCounter` fields
- Change `UpdateUI` to use latency scores rather than game state
- Make `DeserializeGameState` also
  - set latency scores to the same value as the game state
  - reset latency counters
- Make reset IA to reset latency scores and latency counters as well
- Change score change IAs to this

```cs
// public so other scripts are not limited to just incrementing and decrementing.
// Could be made private however.
public void SendTeamOneScoreChangeIA(int delta)
{
    // Should only modify the latency state if the IA can actually get sent.
    if (!lockstep.IsInitialized)
        return;
    lockstep.WriteSmallInt(delta);
    lockstep.SendInputAction(teamOneScoreChangeIAId);
    teamOneLatencyCounter++;
    teamOneLatencyScore += delta;
    UpdateUI();
}

[HideInInspector] [SerializeField] private uint teamOneScoreChangeIAId;
[LockstepInputAction(nameof(teamOneScoreChangeIAId))]
public void OnTeamOneScoreChangeIA()
{
    int delta = lockstep.ReadSmallInt();
    teamOneScore += delta;
    if (lockstep.SendingPlayerId != localPlayerId)
        teamOneLatencyScore += delta; // Not '= teamOneScore' because teamOneLatencyCounter may be != 0u.
    else if (teamOneLatencyCounter == 0u || (--teamOneLatencyCounter) == 0u)
        teamOneLatencyScore = teamOneScore;
    UpdateUI(); // There is a case where it did not actually change, but that is fine.
}
```

<details>
<summary><b>The whole updated script</b></summary>

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

        private uint localPlayerId;

        // This is the game state data structure.
        private int teamOneScore = 0;
        private int teamTwoScore = 0;
        // Other scripts can only read, not write.
        public int TeamOneScore => teamOneScore;
        public int TeamTwoScore => teamTwoScore;

        // This is the latency state data structure.
        private int teamOneLatencyScore = 0;
        private int teamTwoLatencyScore = 0;
        // Other scripts can only read, not write.
        public int TeamOneLatencyScore => teamOneLatencyScore;
        public int TeamTwoLatencyScore => teamTwoLatencyScore;

        // Zero means it is in sync with the game state.
        // Non zero means there are some actions sent by the local player
        // which have already been applied to the latency state.
        private uint teamOneLatencyCounter = 0u;
        private uint teamTwoLatencyCounter = 0u;

        private void Start()
        {
            localPlayerId = (uint)Networking.LocalPlayer.playerId;
        }

        // These public functions could be called by other scripts or through UI buttons.
        public void IncrementTeamOneScore() => SendTeamOneScoreChangeIA(1);
        public void DecrementTeamOneScore() => SendTeamOneScoreChangeIA(-1);
        public void IncrementTeamTwoScore() => SendTeamTwoScoreChangeIA(1);
        public void DecrementTeamTwoScore() => SendTeamTwoScoreChangeIA(-1);

        // This one too.
        public void SendResetScoresIA()
        {
            // Should only modify the latency state if the IA can actually get sent.
            if (!lockstep.IsInitialized)
                return;
            lockstep.SendInputAction(resetScoresIAId);
            teamOneLatencyScore = 0;
            teamTwoLatencyScore = 0;
            teamOneLatencyCounter++;
            teamTwoLatencyCounter++;
            UpdateUI();
        }

        [HideInInspector] [SerializeField] private uint resetScoresIAId;
        [LockstepInputAction(nameof(resetScoresIAId))]
        public void OnResetScoresIA()
        {
            teamOneScore = 0;
            teamTwoScore = 0;
            teamOneLatencyScore = 0;
            teamTwoLatencyScore = 0;
            teamOneLatencyCounter = 0u;
            teamTwoLatencyCounter = 0u;
            UpdateUI();
        }

        // public so other scripts are not limited to just incrementing and decrementing.
        // Could be made private however.
        public void SendTeamOneScoreChangeIA(int delta)
        {
            // Should only modify the latency state if the IA can actually get sent.
            if (!lockstep.IsInitialized)
                return;
            lockstep.WriteSmallInt(delta);
            lockstep.SendInputAction(teamOneScoreChangeIAId);
            teamOneLatencyCounter++;
            teamOneLatencyScore += delta;
            UpdateUI();
        }

        [HideInInspector] [SerializeField] private uint teamOneScoreChangeIAId;
        [LockstepInputAction(nameof(teamOneScoreChangeIAId))]
        public void OnTeamOneScoreChangeIA()
        {
            int delta = lockstep.ReadSmallInt();
            teamOneScore += delta;
            if (lockstep.SendingPlayerId != localPlayerId)
                teamOneLatencyScore += delta; // Not '= teamOneScore' because teamOneLatencyCounter may be != 0u.
            else if (teamOneLatencyCounter == 0u || (--teamOneLatencyCounter) == 0u)
                teamOneLatencyScore = teamOneScore;
            UpdateUI(); // There is a case where it did not actually change, but that is fine.
        }

        // public so other scripts are not limited to just incrementing and decrementing.
        // Could be made private however.
        public void SendTeamTwoScoreChangeIA(int delta)
        {
            // Should only modify the latency state if the IA can actually get sent.
            if (!lockstep.IsInitialized)
                return;
            lockstep.WriteSmallInt(delta);
            lockstep.SendInputAction(teamTwoScoreChangeIAId);
            teamTwoLatencyCounter++;
            teamTwoLatencyScore += delta;
            UpdateUI();
        }

        [HideInInspector] [SerializeField] private uint teamTwoScoreChangeIAId;
        [LockstepInputAction(nameof(teamTwoScoreChangeIAId))]
        public void OnTeamTwoScoreChangeIA()
        {
            int delta = lockstep.ReadSmallInt();
            teamTwoScore += delta;
            if (lockstep.SendingPlayerId != localPlayerId)
                teamTwoLatencyScore += delta; // Not '= teamTwoScore' because teamTwoLatencyCounter may be != 0u.
            else if (teamTwoLatencyCounter == 0u || (--teamTwoLatencyCounter) == 0u)
                teamTwoLatencyScore = teamTwoScore;
            UpdateUI(); // There is a case where it did not actually change, but that is fine.
        }

        private void UpdateUI()
        {
            // TODO: Update UI using teamOneLatencyScore and teamTwoLatencyScore.
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
            lockstep.WriteSmallInt(teamOneScore);
            lockstep.WriteSmallInt(teamTwoScore);
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
            teamOneScore = lockstep.ReadSmallInt();
            teamTwoScore = lockstep.ReadSmallInt();
            teamOneLatencyScore = teamOneScore;
            teamTwoLatencyScore = teamTwoScore;
            if (isImport)
            {
                // There is no harm in doing this even when isImport is false.
                // The if is just there for clarity that this is only needed when doing an import.
                teamOneLatencyCounter = 0u;
                teamTwoLatencyCounter = 0u;
            }
            UpdateUI();
            return null;
        }
    }
}
```
</details>
