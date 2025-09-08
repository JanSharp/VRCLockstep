using UdonSharp;

namespace JanSharp
{
    /// <summary>
    /// <para>A wrapper around <see cref="RNG"/> to make it clearer and easier to implement game states which
    /// require deterministic randomness, as the state of the random number generator can be serialized and
    /// deserialized.</para>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SerializableRNG : SerializableWannaBeClass
    {
        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0u;
        public override uint LowestSupportedDataVersion => 0u;

        /// <summary>
        /// <para>The underlying random number generator.</para>
        /// </summary>
        [System.NonSerialized] public RNG rng;

        public override void WannaBeConstructor()
        {
            rng = WannaBeClasses.New<RNG>(nameof(RNG));
        }

        public override void Serialize(bool isExport)
        {
            lockstep.WriteULong(rng.seed);
            lockstep.WriteULong(rng.lcg);
            lockstep.WriteULong(rng.hash);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
            rng.seed = lockstep.ReadULong();
            rng.lcg = lockstep.ReadULong();
            rng.hash = lockstep.ReadULong();
        }
    }
}
