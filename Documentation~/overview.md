
[To index](index.md)

# Overview

While Lockstep is mainly a networking library, world creators - not just programmers - may end up using systems which depend on Lockstep.

This page covers concepts about Lockstep particularly relevant to world creators.

# Project Setup

In order to use Lockstep in a project, its package from the [VCC listing](https://jansharp.github.io/vrc/vcclisting.xhtml) and all of its dependencies must be added to the project.

# Scene Setup

Lockstep comes with a few prefabs in the `Runtime/Prefabs` folder inside of the package in the project window:

- `LockstepInfoUI`: Displays general info, notifications and a list of all clients in the VRChat world instance
- `GameStatesUI`: An interface to [export and import](#exports-and-imports) game states which support it. Comparable to save files in games, as exported data can be imported into new/different VRChat world instances
- `LockstepMasterPreference`: A game state for management of which clients [prefer](#lockstep-master-preference) to become [Lockstep master](#lockstep-master)
- `Lockstep`: The core. Gets automatically instantiated at build time if any system depends on Lockstep
- `LockstepDebugUI`: Displays debug information about the current state of Lockstep. Really just for debugging as it uses `Update` performance

# Exports And Imports

Systems which use Lockstep for syncing generally have a **game state**.

Said game states may support **exporting** this state into a base64 encoded string, so basically a bunch of text. This can then be **imported**:

- At a later point in time
- In a new instance of the world
- Potentially even different worlds which have the same game state(s)

The `GameStatesUI` provides access to this export and import feature.

Exported data could be saved in **text files** outside of VRChat, making it quite comparable to save files in games.

By default all game states get exported in their entirety, unless individual game states choose to provide custom options to modify their export behavior.

Similarly, when importing, all data from the given string will be imported and overwrite the existing state of game states in the world. Game states may once again choose to provide custom options to modify import behavior.

# Lockstep Master

The concept of the VRChat master is quite well known. The player in an instance which holds ownership of synced objects by default.

The Lockstep master is **separate** from the VRChat master, they serve different purposes and they could be different players.

The Lockstep master is responsible for telling all clients when they are supposed to perform actions in order for everything to stay in sync (among other things). Therefore a **stable and low latency** connection is preferable for Lockstep masters.

Unlike the VRChat master, the Lockstep master **can be changed** without the current master having to leave the instance. It can be changed through:

- Become/Make Master buttons in of the `LockstepInfoUI`
- Effectively automatically through [preferences](#lockstep-master-preference), also configurable in the `LockstepInfoUI`

## Lockstep Master Preference

By putting the `LockstepMasterPreference` prefab into a scene, the concept of configurable preference for each client becoming [Lockstep master](#lockstep-master) gets introduced.

Preference is simply a number. The system then automatically makes whichever client in the world which currently has the highest preference the Lockstep master.

Preferences are remembered for clients when they leave and rejoin the instance.

Supports [exporting and importing](#exports-and-imports).

If the `LockstepInfoUI` is in the scene, additional UI gets shown:

- A slider for the local client's preference
- A slider for each client's preference in the clients list
