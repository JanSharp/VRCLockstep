
# Input Action Sending

- SendInputAction
  - Locally: save as pending
    - Enqueue input action in local players's InputActionSync
  - RemoteLy: save as pending
- Locally, when InputActionSync successfully sent an input action
  - If client is master
    - Remove from pending
    - Run input action in current tick
    - Add to input actions to run in tick sync
- Remotely, when InputActionSync successfully receives an input action
  - Save as pending
  - If client was waiting on this input action
    - Check if all input actions for the next tick have been received
      - If yes, unpause ticks
- When receiving TickSync data, on every client that isn't the master
  - Enqueue each input action to run at their respective tick
  - OnLockstepTick
    - Before running a tick, ensure all input actions for this tick have been received already
      - If yes, run all input actions associated with this tick
      - If no, mark missing input actions for this tick as "waiting on" and pause ticks

## Input Action Sending in Single Player Mode

When the send input action function is called and Lockstep is currently in single player mode, the action is simply instantly performed, without even touching the InputActionSync script. This is not only an optimization, it is a requirement because when only a single client is in an instance, one can mark an object for serialization using RequestSerialization, however it will never actually serialize, not until another client joins the instance. And the above system for sending input actions requires the OnPostSerialization event.

Similarly, while in single player mode, the tick in which the input action got run does not get sent to the tick sync script.

# Internal Client States

There is both a game state and some local only flags.

The game state: probably a dictionary: playerId => state, with state being:

- Master
- WaitingForLateJoinerSync
- CatchingUp
- Normal

Non game state flags:

- `leftClients`: collection of clients (player ids) which have left the instance but are still in the above game state.
- `currentlyNoMaster`: bool

# Player Join

<!-- cSpell:ignore Factorio, desync, desyncs -->

- Ignore the OnPlayerJoin event
- On the new client, when an InputActionSync gets assigned to the local player
  - Wait 2 seconds
  - Check if the local player (the new client) is master
    - If yes, this client is either the first client or the previous master instantly left after this client joined, so perform the following and abort
      - Mark as isMaster in the Lockstep script
      - Initialize the internal "client states" game state
        - Save this client as the master client in said game state
      - Enable [single player mode](#single-player-mode)
      - Fully enable input action handling
      - Raise `OnInit()`
      - Raise `OnClientJoined(int playerId)`
      - Save Time.time as the start time for running ticks
      - Enable running ticks
      - Done
  - Mark Lockstep input action handling as initialized
    - Don't actually run any ticks
    - Save any and all received input actions as pending
    - Save any and all received LockstepTickSync "input actions to run on tick"
    - Ignore and discard any attempts at sending input actions locally
      - `ClientJoinIA` is an exception:
        - Allow it to be sent
        - Do not save it as pending
          - (Technically it could, however it would never get run locally anyway, as it happens before the game states that will be sent to this client get captured and serialized)
  - Send `ClientJoinIA`
- Receive `ClientJoinIA`
  - On Every client, except sending (because the sending client will never run it)
    - Mark the given player id as "waiting for late joiner sync" in an internal "client states" game state
  - On master
    - Wait 3 seconds * the amount of player joined in the last 5 minutes, capped at 30 seconds
      - Reset timer if another `ClientJoinIA` is received
    - [Initiate late joiner sync](#initiate-late-joiner-sync)
- On newly joined client
  - Receive every game state input action (it is actually legitimately only the new client receiving this data, because every other client already has the late joiner sync script disabled)
    - Deserialize game states using callback functions/events
  - Receive the current tick number
  - Disable the late joiner InputActionSync script, as this client has received all data it needed from it
  - Enable processing of input actions fully, including allowing sending any input actions locally
  - Send `ClientGotLateJoinerDataIA`
    - Must send this before raising the `OnClientBeginCatchUp` event as to guarantee that the first input action sent by this client is always `ClientGotLateJoinerDataIA` (aside from `ClientJoinIA`)
  - Initiate catch up
    - The client knows the tick at which all of the game states where captured
    - The client knows the current game tick all other clients are already at, thanks to LockstepTickSync
    - Raise the `OnClientBeginCatchUp(int playerId)` event
      - Allows systems to initialize non game states based on the current game states
      - Note that this event must not modify any game states. If you know Factorio, think about on_load. This is the only event with this exception
- Receive `ClientGotLateJoinerDataIA`
  - On every client (though sending client will run it a bit later, while catching up)
    - Change the "waiting for late joiner sync" state for the given player id to "normal"
    - Raise the `OnClientJoined(int playerId)` event
- On newly joined client
  - Catch up
    - Run ticks very quickly
      - Limit the amount of time spent catching up each frame to something like 5 to 10 milliseconds
    - Go through the list of input actions marked as "run on tick x", if "x" is outdated, discard of this input action as it was already included in the game states received initially
  - Save current Time.time as the start time for the local timer used for ticks
  - Start running ticks normally
  - Send `ClientCaughtUpIA`
- Receive `ClientCaughtUpIA`
  - On every client
    - Raise the `OnClientCaughtUp(int playerId)` event to allow systems to further initialize... well whatever they want
      - Keep in mind that VRCPlayerApi objects themselves are not, and cannot be, part of the game state. Any action taken using data from them which affects the game state must go through another input action, most likely and preferably only sent by the joining client

Any systems using this should disable functionality that would require sending input actions until they get the `OnClientCaughtUp` event. If they really want to they could use some heavy latency state logic to make it seem like actions are being registered for the local player, but then only actually perform the actions once they get `OnClientCaughtUp`. This is effectively an intentional desync, so be very careful with this kind of approach. Latency states even when it is already caught up are intentional desyncs, for the record.

## Initiate late joiner sync

- Initiate late joiner sync
  - Right before moving to the next tick
    - Using the late joiner InputActionSync script
      - Send every game state as an input action, including internal ones (they're really not any different)
        - Serialize game states using some callback function/event which were provided when defining (not creating) the game states
      - Send current tick number (not next)
      - (Note that "sending" really means enqueueing, see sending input actions section, though late joiner input actions are a bit different)

# Player Leave

What if there were input actions that were supposed to run but we never received them? Like when the master sent and ran an action, but the action itself was never received, but the tick sync for it was?\
It's supposed to be impossible. An input action only get enqueued to run on a specific tick if it has already been successfully sent, making it incredibly unlikely for the tick sync with the input action enqueued in some tick to arrive before the input action itself. If it does happen though, then the system cannot recover, as it doesn't know if it is only the local client missing the input action, so it cannot drop the input action. Even when the local client became master, it cannot make that decision, because other clients may have already run past this tick because they did receive the input action. If this can actually happen, I don't know. But since I don't know, I can't make any assumptions, so again, it is unrecoverable if it does happen.

When the world converts from multiplayer to single player, the system must remove all queued data from the InputActionSync scripts (both the local player's one and the late joiner one), and instantly raise their associated actions (though there's nothing to do for the late joiner ones, just drop them).

- Ignore OnPlayerLeave (for now? So long as I can trust the player object pool I suppose)
- When an InputActionSync get unassigned
  - On the master client
    - Mark the given client as "left" locally
    - Wait 1 second
    - Check if [single player mode](#single-player-mode) should be activated, and activate it if yes
    - Send `ClientLeftIA`
  - On every client that isn't the master
    - Check if the given client still exists in the internal "client states" game state
      - If no
        - Check if there **aren't** any clients in the internal "client states" game state
          - Set `currentlyNoMaster` flag
          - Wait 1 frame (Because I have trust issues with VRChat)
          - Call CheckMasterChange
      - If yes
        - Mark the given client as "left" locally
        - Check if the leaving client had the "master" state in the internal "client states" game state
          - If yes
            - Set `currentlyNoMaster` flag
            - Wait 1 frame (Because I have trust issues with VRChat)
            - Call CheckMasterChange
- Receive `ClientLeftIA`
  - On every client
    - Remove the client from the "client state" game state
    - If isMaster
      - Check if there are any clients waiting for late joiner data
        - If no, clear late joiner sync queue
    - Raise the `OnClientLeft(int playerId)` event

## Master Transfer

- OnOwnershipTransfer on LockstepTickSync
  - On every client
    - Wait 1 frame (Because I have trust issues with VRChat)
    - Call CheckMasterChange

- CheckMasterChange
  - If this client isn't master, the `currentlyNoMaster` flag is set and Networking.IsMaster is true
    - Unset `currentlyNoMaster` flag
    - Set isMaster to true in the Lockstep script
    - Reenable the late joiner InputActionSync script
      - Take ownership
      - Ignore any incoming data
    - Check if [single player mode](#single-player-mode) should be activated, and activate it if yes
    - Send `MasterChangedIA`
    - Send `ClientLeftIA` for each client currently marked as left
    - If there are any clients marked as "waiting for late joiner sync" in the internal "client states" game state
      - [Initiate late joiner sync](#initiate-late-joiner-sync)
- Receive `MasterChangedIA`
  - Mark the new master as the master in the internal "client states" game state
  - Set `currentlyNoMaster` to false

## Single Player Mode

- Check if single player mode should be activated
  - If the count of clients in "client state" minus the count of players marked as left == 1
    - Yes, enter single player mode

- Enter single player mode
  - Clear and discard all queued data in the late joiner InputActionSync script
  - Clear queued data from the local player's InputActionSync script and instantly run all input actions that were queued
  - Clear the "run action on tick" collection on the LockstepTickSync script

While in single player mode, input actions are handled differently. See [input action sending](#input-action-sending-in-single-player-mode) for more info.
