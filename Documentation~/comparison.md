
[To index](index.md)

# Introduction

Lockstep of course uses VRChat's networking API internally, however using lockstep is entirely different than using VRChat's networking.

One could call them different networking systems. Which would mean there are now 2 different systems to choose from. But how to choose?

<!-- In order to determine which system is best for a given task one must know how both systems work. In detail, realistically. Therefore this page provides an explanation for how VRChat networking works, and then a few comparisons at the end. However to properly compare them it would be good to read through the rest of the lockstep API documentation. -->

In order to best determine if builtin VRChat networking would be good for a given task (and therefore Lockstep would not be needed), take a look at [this page](vrchat-networking.md).

Otherwise, if builtin VRChat networking is not good at something, chances are Lockstep is either good at it or at least more bearable.

# Comparison

Again, best way to know which system is the best is to have a full grasp of how both work. This is important, because lockstep is not inherently better than VRChat networking in every way.

This comparison only focuses on what systems are designed for, not if something is technically possible. Just a few key differences:

<!-- Yes this formatting is 10/10 -->

|                                              | VRChat Networking                                                                         | Lockstep                                                                                                                           |
| -------------------------------------------- | ----------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Supported synced state size                  | small                                                                                     | large ([game states](game-states.md))                                                                                              |
| Network events                               | yes ([`SendCustomNetworkEvent`](vrchat-networking.md#networked-events))                   | yes ([input actions](input-actions.md))                                                                                            |
| Parameterized events                         | yes ([`SendCustomNetworkEvent`](vrchat-networking.md#networked-events))                   | yes ([input actions](input-actions.md))                                                                                            |
| Simultaneous interaction by multiple players | [no / race conditions](vrchat-networking.md#multiple-people-interacting-at-the-same-time) | makes no difference                                                                                                                |
| Saving/Exporting states                      | yes, with some work                                                                       | [explicit support](game-states.md#exports-and-imports)                                                                             |
| Syncing for instantiated objects             | no                                                                                        | [yes, through ids](synced-objects.md)                                                                                              |
| Networking Overhead (bytes)                  | notable, but ok when not using arrays                                                     | [much more notable (0.68 kb/s while idle)](https://vrchat.canny.io/udon/p/synced-arrays-have-unexpectedly-large-overhead-in-bytes) |
| Performance Overhead                         | whatever custom code is doing                                                             | (on most PCs) less than 0.02 ms Update while idle, plus unknown time per input action                                              |
