
# Updating to 1.1.0

## Initialization Changes

- `IsInitialized` is no longer `true` inside of `OnInit` and APIs requiring `IsInitialized` to be `true` can thus no longer be used inside of `OnInit` either.
- `LockstepIsInitialized` has been added, which is `true` inside of `OnInit`. Input actions and delayed events check this property, which is to say they can still be sent inside of `OnInit`.
- `OnInitFinished` has been added, which runs immediately after `OnInit`, and `IsInitialized` is `true` inside of `OnInitFinished`.
- `OnInit` has been changed to be allowed to be spread out across frames, see `FlagToContinueNextFrame`.
- `OnInitFinished` cannot be spread out across frames, thus it is a guarantee that by the end of the frame this event gets raised in all game states are fully initialized.

With all that in mind updating can be a matter of not having to change anything, having to move some logic from `OnInit` to `OnInitFinished` or changing some `IsInitialized` checks to `LockstepIsInitialized`.

There are 2 reasons for this change:

- `IsInitialized` is now a trustworthy method of checking if all game states have run their `OnInit` handlers. Technically game states may not be finished initializing if they rely on `OnInitFinished`, however the majority of game states can and should finish initialization inside of `OnInit`. And by the end of the frame they are all guaranteed to be finished initializing.
- `OnInit` gaining the ability to be spread out across frames enables having expensive initialization logic, without causing lag spikes or even risking running into the 10 second Udon time out.

## Catching Up Changes

`OnClientBeginCatchUp` has been changed the exact same way as `OnInit` in regards to `IsInitialized`, `LockstepIsInitialized` and the ability to be spread out across frames.

`OnPostClientBeginCatchUp` has been added, akin to `OnInitFinished`.

## Import Finishing Changes

`OnImportFinishingUp` has been added. Inside of it `IsImporting` is still `true`, unlike `OnImportFinished`. Also unlike `OnImportFinished`, `OnImportFinishingUp` can be spread out across frames, see `FlagToContinueNextFrame`.

It would be good to move logic to `OnImportFinishingUp` where possible, as it is going to reduce the amount of logic run in a single frame, reducing lag spikes, as well as making `OnImportFinished` a more useful notification of an import being finished.

`OnImportFinished` did not receive any breaking changes.
