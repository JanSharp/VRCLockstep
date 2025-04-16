
[To index](index.md)

# Builtin VRChat Networking

There is some documentation from VRChat [here](https://creators.vrchat.com/worlds/udon/networking/), however the target audience there vs on this page here is quite different. This page is oriented towards programmers and it just focuses on what VRChat networking is designed for and good or bad at, less so giving a generic overview.

## API Size

<details>
  <summary><b>Scripts using VRChast networking for what it is designed for just use this part of the API</b></summary>

  - `UdonBehaviourSyncMode` attribute
  - `BehaviourSyncMode.Manual` enum field
  - `UdonSynced` attribute
  - `UdonSharpBehaviour.RequestSerialization` function
  - `OnPostSerialization` event
  - `OnDeserialization` event
  - `Networking.LocalPlayer` static property
  - `Networking.SetOwner` static function
  - And maybe:
    - `VRCPlayerApi.isLocal` property
    - `VRCPlayerApi.isMaster` property
    - `VRCPlayerApi.IsOwner` function
    - `Networking.IsOwner` static function
    - `OnPreSerialization` event
    - `FieldChangeCallback` attribute
    - `UdonSharpBehaviour.SendCustomNetworkEvent` function
    - `Networking.IsNetworkSettled` static property
    - `Networking.GetPlayerObjects` static function
    - `Networking.FindComponentInPlayerObjects` static function
    - `OnPlayerRestored` event
    - `OnPlayerDataUpdated` event
    - `VRCPlayerApi.GetPlayerObjects` function
</details>
<br/>
<details>
  <summary><b>The whole VRChat networking API is not much bigger</b></summary>

  - `UdonBehaviourSyncMode` attribute
  - `BehaviourSyncMode` enum
    - `None`
    - `NoVariableSync`
    - `Manual`
    - `Continuous`
  - `UdonSynced` attribute
  - `UdonSharpBehaviour.RequestSerialization` function
  - `OnPreSerialization` event
  - `OnPostSerialization` event
  - `OnDeserialization` event
  - `FieldChangeCallback` attribute
  - `UdonSharpBehaviour.SendCustomNetworkEvent` function
  - `OnOwnershipRequest` event
  - `OnOwnershipTransferred` event
  - `Networking.GetOwner` static function
  - `Networking.SetOwner` static function
  - `Networking.IsOwner` static function
  - `Networking.IsMaster` static property
  - `Networking.Master` static property
  - `Networking.LocalPlayer` static property
  - `Networking.IsClogged` static property
  - `Networking.IsNetworkSettled` static property
  - `Networking.GetPlayerObjects` static function
  - `Networking.FindComponentInPlayerObjects` static function
  - `OnPlayerRestored` event
  - `OnPlayerDataUpdated` event
  - `VRCPlayerApi.GetPlayerObjects` function
  - `VRCPlayerApi.GetPlayerById` static function
  - `VRCPlayerApi.GetPlayerCount` static function
  - `VRCPlayerApi.GetPlayerId` static function
  - `VRCPlayerApi.GetPlayers` static function
  - `VRCPlayerApi.playerId` property
  - `VRCPlayerApi.displayName` property
  - `VRCPlayerApi.isLocal` property
  - `VRCPlayerApi.isMaster` property
  - `VRCPlayerApi.IsOwner` function
</details>

## Synced Objects

In order for an object to be sync-able:

- The game object must exist in the scene at build time
- Must not be instantiated at runtime
- Has one or more UdonSharpBehaviours on it which has a non `None` sync mode
  - Or it has the VRCObjectSync component in which case the object is forced to use `Continuous` sync mode
  - Or it has the VRCObjectPool component. Unsure what sync mode this uses, most likely `Manual` though

It is possible to put multiple scripts on an object, however the system is not designed around supporting it. Especially if the sync modes mismatch - including if one is `None` and another is `Manual` (where one might expect the `None` to just be ignored when doing syncing, but no) - simply do not put these scripts on the same object.

Scripts on the same object as VRCObjectSyncs can have `None` sync mode without warnings or issues, but the object is going to be continuous at the end of the day. Putting `Manual` sync mode scripts on that same object is not possible.

Even when there are multiple `Manual` sync mode scripts on the same object, as soon as _any_ script out of these does syncing they _all_ get synced. When using VRChat networking the way it is "supposed" to be used then this does not break anything, but it certainly wastes network traffic and computation time.

## Object ownership

Every [Synced Object](#synced-objects) has an owner, which is a player in the world. (The concept of a server does not exist.)

When using `UdonSharpBehaviour.RequestSerialization`, the player running that code must be the owner of the synced object in order for syncing to actually happen.

There is technically more to ownership, like instance masters, ownership transfer requests, but none of those actually matter when using VRChat networking the way it is "supposed" to be used.

## Network Congestion

It might be surprising that this is a concept exposed in a system designed for new programmers, but it is. Not a big deal though, just implement exponential back off in every script. It might sound hard but it isn't. It is just tedium. Here's an example:

```cs
private const float MinBackOffTime = 1f;
private const float MaxBackOffTime = 16f;
private float currentBackOffTime = MinBackOffTime;

public override void OnPostSerialization(SerializationResult result)
{
    if (result.success)
        currentBackOffTime = MinBackOffTime;
    else
    {
        SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), currentBackOffTime);
        currentBackOffTime = Mathf.Min(currentBackOffTime * 2, MaxBackOffTime); // Exponential back off.
    }
}

public void RequestSerializationDelayed() => RequestSerialization();
```

When otherwise using VRChat networking the way it is "supposed" to be used, that snippet of code can be copy pasted as is into every script and it would be correct.

Oh but make sure to **never have a synced array variable be null**, it is always going to fail syncing - `result.success` being `false`.

## The Intended Usage

To stop beating around the bush, the intended way to use VRChat networking is to rely on "eventual consistency". Really just a fancy way of saying that eventually things are going to be in sync.

In programming terms this is equivalent to saying that eventually every player is going to agree that some set of synced variables for a [synced object](#synced-objects) is the latest one that everybody should use.

It boils down to this:

- `Networking.SetOwner(Networking.LocalPlayer, this.gameObject);` before every `RequestSerialization();`
- Implement [exponential back off](#network-congestion)
- Use `OnDeserialization()` to update the local state ot match the value of `[UdonSynced]` variables

So here is an example script:

```cs
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Example
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SyncedGameObjectsToggle : UdonSharpBehaviour
    {
        [Tooltip("Upon interaction, all theses GameObjects get toggled - they flip their active state. "
            + "This means that these GameObjects can be a mixture of active and inactive ones, they all "
            + "simply get their active state inverted.")]
        public GameObject[] gameObjects;
        [UdonSynced] private bool syncedState = false;
        private bool currentState = false;

        private const float MinBackOffTime = 1f;
        private const float MaxBackOffTime = 16f;
        private float currentBackOffTime = MinBackOffTime;

        public override void Interact()
        {
            syncedState = !syncedState;
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            RequestSerialization();
            OnDeserialization();
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (result.success)
                currentBackOffTime = MinBackOffTime;
            else
            {
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), currentBackOffTime);
                currentBackOffTime = Mathf.Min(currentBackOffTime * 2, MaxBackOffTime); // Exponential back off.
            }
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnDeserialization()
        {
            // Could use a property and the FieldChangeCallback attribute instead of having 2 bools and
            // comparing them here, however for most synced scripts that have a tiny bit more going on than
            // just 1 synced variable the OnDeserialization approach is easier. So might as well always use it.
            if (syncedState == currentState)
                return;
            currentState = syncedState;

            foreach (GameObject go in gameObjects)
                if (go != null)
                    go.SetActive(!go.activeSelf);
        }
    }
}
```

That's it. That's the whole magic. When doing this, and just this, not deviating from the intended usage in any way, VRChat networking is reliable and easy.

## Networked Events

`UdonSharpBehaviour.SendCustomNetworkEvent` may be used in some scripts in order to perform some temporary synced actions.

These actions **must not have any lasting effects**, otherwise late joiners are going to inherently be desynced. For lasting effects use the normal serialization approach.

There has been a bug with `SendCustomNetworkEvent` with ClientSim though, unknown if it has been fixed, where it quite simply did not run the sent event while testing in the editor. Even though the object has the correct sync mode (so non `None`), and when testing in VRChat it works just fine. So keep that in mind when using `SendCustomNetworkEvent`, it may or may just not work in editor.

## Timing

There is timing information, such as how much time has passed from `OnPreSerialization` on the sending player until `OnDeserialization` on every other player, which can be used in a way that is intended by VRChat and it does not cause desyncs.

Here's an example script which lets players type a number of seconds into an input field and it is going to count down to zero. It is synced for every player in the world as well as for late joiners.

The important takeaway from this example is:

- In `OnPreSerialization` the `endTime` gets converted into what could be described as a relative value rather than absolute, making `syncedTimeLeft` not dependent on the local player's `Time.realtimeSinceStartup`
- In `OnDeserialization` the `result.receiveTime` is basically the point in time when `OnPreSerialization` ran, which can be quite a while ago for late joiners. And that is fine, it works even if the timer already reached its end

```cs
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace Example
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class Timer : UdonSharpBehaviour
    {
        public TMP_InputField timerField;

        /// <summary>In <see cref="Time.realtimeSinceStartup"/> scale rather than <see cref="Time.time"/> to
        /// prevent lag spikes from desyncing the timer for some players.</summary>
        private float endTime;
        [UdonSynced] private float syncedTimeLeft;
        private bool updateLoopRunning = false;

        private float totalTime; // These 2 are just used to go from OnValueChanged to the delayed function.
        private bool waitingToProcessValueChange = false;

        private const float MinBackOffTime = 1f;
        private const float MaxBackOffTime = 16f;
        private float currentBackOffTime = MinBackOffTime;

        // Yes OnValueChanged, not OnEndEdit. We are working around this issue:
        // https://vrchat.canny.io/sdk-bug-reports/p/worlds-316-vrcinputfield-inputfield-no-longer-sends-onendedit-event
        public void OnTimerFieldValueChanged()
        {
            if (!float.TryParse(timerField.text, out totalTime))
                return;
            // This nonsense is not needed when running in VRChat.
            // But it exists to make in editor testing possible by pasting values into the field at least.
            // (Pasting raises on value changed for each character being pasted, so this deduplication is required.)
            if (waitingToProcessValueChange)
                return;
            waitingToProcessValueChange = true;
            SendCustomEventDelayedFrames(nameof(OnTimerFieldValueChangedDelayed), 1);
        }

        public void OnTimerFieldValueChangedDelayed()
        {
            waitingToProcessValueChange = false;
            endTime = Time.realtimeSinceStartup + totalTime;
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            RequestSerialization();
            StartUpdateLoop();
        }

        private void StartUpdateLoop()
        {
            if (updateLoopRunning)
                return;
            updateLoopRunning = true;
            TimerUpdateLoop();
        }

        public void TimerUpdateLoop()
        {
            float timeLeft = Mathf.Max(0f, endTime - Time.realtimeSinceStartup);
            timerField.SetTextWithoutNotify(timeLeft.ToString("f3"));
            if (timeLeft != 0f)
                SendCustomEventDelayedFrames(nameof(TimerUpdateLoop), 1);
            else
                updateLoopRunning = false;
        }

        public override void OnPreSerialization()
        {
            syncedTimeLeft = endTime - Time.realtimeSinceStartup;
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (result.success)
                currentBackOffTime = MinBackOffTime;
            else
            {
                SendCustomEventDelayedSeconds(nameof(RequestSerializationDelayed), currentBackOffTime);
                currentBackOffTime = Mathf.Min(currentBackOffTime * 2, MaxBackOffTime); // Exponential back off.
            }
        }

        public void RequestSerializationDelayed() => RequestSerialization();

        public override void OnDeserialization(DeserializationResult result)
        {
            float timePassed = result.receiveTime - result.sendTime;
            endTime = Time.realtimeSinceStartup + syncedTimeLeft - timePassed;
            StartUpdateLoop();
        }
    }
}
```

## Multiple People Interacting At The Same Time

Well well well, good luck have fun. Only the input from 1 player is going to win.

For example if there is a score board controlled through increment and decrement buttons, if 2 people press these buttons at the same time, only 1 player's input is going to go through. Which player is unknown.

Unless we start exploring using VRChat networking the way it is not supposed to be used, there is nothing to be done about this.

This becomes quite relevant in the section below.

## VRCObjectSync and VRCObjectPool

Oh wait, right, these exist. So much for VRChat networking being easy.

To put it simply, creating a system which can spawn and despawn `VRCObjectSync`s which are in `VRCObjectPool`s in a synced manner is one big trap. It is going to seem like it is working, but every now and then it just doesn't. There are good reasons for it not working, but people tend to just call it VRChat scuff and ignore it. It is sad that these bugs have become the norm and people brush them off almost as though it was expected behaviour. That said though world creators realistically cannot do much about this even if they wanted to, as described near the end of this section. It is simply a product of how VRChat networking works.

Those 2 components make it look easy (assuming `VRCObjectSync` are children of their associated `VRCObjectPool`):

```cs
public GameObject Spawn(VRCObjectPool objectPool, Transform location)
{
    Networking.SetOwner(Networking.LocalPlayer, objectPool.gameObject);
    GameObject spawnedObject = objectPool.TryToSpawn();
    if (spawnedObject == null)
        return null;
    Networking.SetOwner(Networking.LocalPlayer, spawnedObject);
    VRCObjectSync objectSync = spawnedObject.GetComponent<VRCObjectSync>();
    objectSync.FlagDiscontinuity();
    objectSync.TeleportTo(location);
    return spawnedObject;
}

public void Despawn(GameObject objectToDespawn)
{
    if (!Networking.IsOwner(objectToDespawn)) // Only relevant if Despawn is
        return; // called directly from an OnTriggerEnter, for example.
    VRCObjectPool objectPool = objectToDespawn.transform.GetComponentInParent<VRCObjectPool>();
    if (objectPool == null) // Should be impossible, unless Despawn could be
        return; // called for objects which legitimately are not part of object pools.
    VRC_Pickup pickup = objectToDespawn.GetComponent<VRC_Pickup>();
    if (pickup != null) // Can do without this null check if all objects
        pickup.Drop(); // that get passed to Despawn are known to be pickups.
    Networking.SetOwner(Networking.LocalPlayer, objectPool.gameObject);
    objectPool.Return(objectToDespawn);
}
```

That's it, that's a whole item system. After using this system one might start receiving bug reports such as:

- Trying to spawn an item sometimes does not spawn the item
- Trying to spawn an item sometimes makes it appear for a split second just for it to vanish again
- Trying to despawn an item does nothing sometimes

And the larger the user base gets, especially the more people are in a world and are actively interacting with the system, the more frequent such issues become.

Fixing these issues is possible through workarounds, however for the majority of the worlds they would be too rare to care about, and for most world creators who likely aren't even programmers, those workarounds require far too deep of an understanding of how VRChat networking works. Even this entire page here does not contain enough information to figure out how to work around these issues or to even know why they are happening.

One of the more obvious issues is the fact that [multiple people](#multiple-people-interacting-at-the-same-time) could be trying to spawn or despawn the same type of item at the same time, in order words interacting with the same object pool simultaneously. As described in the linked section, this is relatively straight forward to understand. Working around it though is difficult, so much so that I (JanSharp, the writer of this page) have not even bothered trying to figure out how to do it. It requires dealing with race conditions that are a nightmare to think about and cannot be reliably reproduced to test them.

And in regards to items spawning and instantly vanishing again, here is why:

- An item is despawned
- A player joins
- That player tries to spawn that same item (which means nobody else spawned it since it was last despawned)
- That new player becomes owner of it and sets its location
- `VRCObjectSync` presumably runs its deserialization now that the object has been activated
- The last synced location it receives is the last location the item was at before getting despawned
- `VRCObjectSync` teleports the item there
- Ownership transfer goes through
- Now every player thinks the item should be back where it was last despawned instead of where it was supposed to be spawned

This issue can be worked around without having to deal with race conditions much, so I (JanSharp, as mentioned before) have implemented such a workaround before. It works by putting spawned items on a watch list, making sure they do not suddenly teleport away shortly after getting spawned. The definition of "teleport" here is "the object moved more than x meters per second". I chose 1 m/s as the breaking point, and objects are watched for 2 seconds after spawning. Do note that this introduces new potential edge cases, just even more rare ones so it is at least better.

# Persistence

Never used it, only read https://creators.vrchat.com/worlds/udon/persistence/ so that is all I know.

# PlayerData

Never used it, only read https://creators.vrchat.com/worlds/udon/persistence/player-data so that is all I know.

If you want my personal thoughts on it, simple: This does not scale, at all. Anything that's a couple dozens of bytes that gets updated frequently is probably already too much. And as soon as there is any system that uses this for "save states", nothing else can use PlayerData as something that gets updated even semi frequently, because everything gets synced every time. Because of course it would.

# PlayerObjects

Never used it, only read https://creators.vrchat.com/worlds/udon/persistence/player-object so that is all I know.

If you want my personal thoughts on these: I hope they're good. But they're already inherently bad because there is no event for an object for a player having been instantiated, nor does the documentation state anything about when exactly they get instantiated. This leaves us with no guarantee that when an object for the local player exists that its counter part on every other player also exists. And also no guarantee that they've actually been instantiated before Start. Better hope and pray. Then cry.

Furthermore, there is no event for an object being deleted (they do get destroyed, right?) either (or can we use Unity's `OnDestroy`?), neither is there any documentation stating how this deletion process might work. Are we guaranteed that when some player synced some data on their player object right before leaving that every other player in the world is going to receive that synced data or are some going to receive it and some others going to delete the object before receiving the data? Who knows. I don't trust it, and I require this level of a guarantee if I were to use the system for lockstep. So no, lockstep is not using PlayerObjects, it is using
