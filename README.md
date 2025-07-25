
# Lockstep

Lockstep networking (at least similar) infrastructure for UdonSharp.

Lockstep relies on determinism to keep the state of the world in sync. Rather than syncing the whole state of a given system each time, just input actions get synced which modify the state. This allows for large and constantly mutating synced states.

Input actions and lockstep events are run in the same lockstep ticks in the exact same order on every single client. This is required for determinism, and as a byproduct there are no network race conditions.

Ultimately all syncing is done using byte arrays, both for game state syncing for late joiners as well as for input actions. Lockstep can take these byte arrays and let the user **import and export** them as strings/text, similar to save files in games, so long as game states - the systems using lockstep - have support for it.

# Installing

Head to my [VCC Listing](https://jansharp.github.io/vrc/vcclisting.xhtml) and follow the instructions there.

# Contributing

This project uses [custom git filters](.gitattributes) to reduce the amount of noise generated by Udon and UdonSharp. These filters are not required in order to contribute, though without them all UdonSharp asset files will show as modified and those "changes" should not be committed.

# Overview for World Creators

The majority of what Lockstep is and its documentation does not matter to world creators. However a few things are quite relevant and are outlined [here](Documentation~/overview.md).

# Documentation

Online: [On github](https://github.com/JanSharp/VRCLockstep/blob/main/Documentation~/index.md).

Offline: In the `Documentation~` folder, as the [Unity's Manual](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-document.html) suggests. Probably the easiest way to do it how Unity wants you to do it is by right clicking the package in the Project window (the Packages list is collapsed by default), wait for the packages panel to finish loading, and then press the Documentation button there.
