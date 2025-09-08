
[To index](index.md)

# Data Streams

Lockstep contains 2 internal streams, a write and a read stream. When using functions with the `Write` and `Read` prefixes from the LockstepAPI, those are the streams being interacted with internally.

Since the write stream is inside of the Lockstep system, **only 1 stream can be written to concurrently**. `ResetWriteStream` must be called if serialization is cancelled prematurely as to prevent polluting future serializations.

Similarly for `Read`ing, only 1 stream can be read from concurrently, however this limitation only matters when using `SetReadStream`. Otherwise Lockstep itself manages the read stream and systems using Lockstep do not have to worry about it.

A data stream is actually a byte array and an associated position variable.

Interaction with the underlying byte array and position variables more directly is exposed in the LockstepAPI, like shifting the write stream, although most systems have no need for these APIs. `Write` and `Read` is sufficient, with the occasional `ResetWriteStream`.

# Data Types

The LockstepAPI contains functions with the `Write` and `Read` prefixes for the following data types:

- `byte` (and array variants)
- `sbyte`
- `short` (and a space optimized variant)
- `ushort` (and a space optimized variant)
- `int` (and a space optimized variant)
- `uint` (and a space optimized variant)
- `long` (and a space optimized variant)
- `ulong` (and a space optimized variant)
- `float`
- `double`
- `decimal`
- `bool` (called flags)
- `char`
- `string`
- `Vector2`
- `Vector3`
- `Vector4`
- `Quaternion`
- `Color`
- `Color32`
- `DateTime`
- `TimeSpan`
- `SerializableWannaBeClass` (called CustomClass)
- [`SerializableRNG`](randomnees.md) (derives from `SerializableWannaBeClass` so also called CustomClass, just mentioned explicitly for discoverability)

## Other Data Types

In order to read or write data types which are not listed above, said data must be taken apart into pieces with data types which Lockstep has APIs for.

For example an array of 4 `float`s could be described in the following format:

- `int` length
- `float` value at index 0
- `float` value at index 1
- `float` value at index 2
- `float` value at index 3

To optimize for binary size use `WriteSmallUInt(uint)` for the length value.

The code may look like this:

```cs
float[] myFloats = new float[4]; // Using 4 here but
// the code below assumes the length to be unknown.
int length = myFloats.Length;
lockstep.WriteSmallUInt((uint)length);
for (int i = 0; i < length; i++)
    lockstep.WriteFloat(myFloats[i]);

// ...

int length = (int)lockstep.ReadSmallUInt();
float[] myFloats = new float[length];
for (int i = 0; i < length; i++)
    myFloats[i] = lockstep.ReadFloat();
```

Similarly for dictionaries. Get the keys and get the values, then serialize and deserialize them as a list of pairs.

Other custom data structures are most likely just a collection of variables with supported data types. Some might even derive from `SerializableWannaBeClass` in which case there are helper `Write` and `Read` functions specifically for those, which also support recursion.

Ultimately every form of data is possible to be represented using a flat list of primitives... Well, unless Udon doesn't expose the internal values. This may apply to VRCUrl objects, not sure. If that's the case, well you're screwed, you simply won't be able to sync that data using this system. I know that sucks, but I'm not going to add _a ton_ of extra complexity just for those few special values.

# Serialization and Deserialization

In any serialization context it is expected to call `Write` functions for all custom data to be serialized.

If serialization gets cancelled however some data has already been written to the write stream, `ResetWriteStream` must be called to prevent polluting future serializations.

In any deserialization context it is expected to call `Read` functions in the exact same order as the `Write` functions were called, with perfectly matching [data types](#data-types).

For example to send a `float value` for an [input action](input-actions.md):

- Call `lockstep.WriteFloat(value);` in the send function
- Call `lockstep.SendInputAction(...);` to actually send the input action
- Do `float value = lockstep.ReadFloat();` in the input action event handler

### Serialization contexts

- [Input action](input-actions.md) sending
- [Game state](game-states.md) serialization
- [Game state exporting](game-states.md#exports-and-imports)

### Deserialization contexts

- [Input action](input-actions.md) event handlers
- [Game state](game-states.md) deserialization
- [Game state importing](game-states.md#exports-and-imports)
