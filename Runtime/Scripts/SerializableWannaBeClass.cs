using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    /// <summary>
    /// <para>Deriving from this class enables using
    /// <see cref="LockstepAPI.WriteCustomClass(SerializableWannaBeClass)"/> and friends,
    /// <see cref="LockstepAPI.ReadCustomClass(string)"/> and friends and
    /// <see cref="LockstepAPI.SkipCustomClass(out uint, out byte[])"/> and its overload.</para>
    /// <para>However it is not required to use those api functions for serialization and deserialization of
    /// <see cref="SerializableWannaBeClass"/>es, calling <see cref="Serialize(bool)"/> and
    /// <see cref="Deserialize(bool, uint)"/> would be just as valid. It depends on if the extra logic
    /// provided by the <see cref="LockstepAPI"/> functions is required and or desired. That said, when not
    /// using the <see cref="LockstepAPI"/> functions, <see cref="SupportsImportExport"/>,
    /// <see cref="DataVersion"/> and <see cref="LowestSupportedDataVersion"/> should be respected the exact
    /// same way as the <see cref="LockstepAPI"/> functions do, see their respective documentation.</para>
    /// <para>The <see cref="SerializableWannaBeClass"/> already has a <see cref="lockstep"/> field which due
    /// to the <see cref="SingletonReferenceAttribute"/> and the nature of how
    /// <see cref="WannaBeClassesManager"/> works is prepopulated with a valid reference at build time.</para>
    /// <para>It is valid for systems to use <see cref="SerializableWannaBeClass"/>es outside of serializing
    /// and deserializing game states, in which case it is pretty much free game, not all rules may apply, and
    /// it is up to those systems to use common sense as well as provide their own documentation for how they
    /// use <see cref="SerializableWannaBeClass"/>es if necessary.</para>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class SerializableWannaBeClass : WannaBeClass
    {
        [HideInInspector][SingletonReference] public LockstepAPI lockstep;

        /// <summary>
        /// <para>When <see langword="false"/>, the <c>isExport</c> and <c>isImport</c> parameters for
        /// <see cref="Serialize(bool)"/> and <see cref="Deserialize(bool, uint)"/> will naturally never be
        /// <see langword="true"/>.</para>
        /// </summary>
        public abstract bool SupportsImportExport { get; }
        /// <summary>
        /// <para>Current version of the binary data output from <see cref="Serialize(bool)"/> when exporting,
        /// and subsequently the latest version <see cref="Deserialize(bool, uint)"/> is capable of
        /// importing.</para>
        /// <para>Recommended to start at <c>0u</c>.</para>
        /// </summary>
        public abstract uint DataVersion { get; }
        /// <summary>
        /// <para>The lowest and therefore oldest binary data version <see cref="Deserialize(bool, uint)"/>
        /// is capable of importing.</para>
        /// <para>Must be less than or equal to <see cref="DataVersion"/>.</para>
        /// </summary>
        public abstract uint LowestSupportedDataVersion { get; }

        /// <summary>
        /// <para>This function must call <c>Write</c> functions on <see cref="LockstepAPI"/> just like other
        /// functions would do before calling <see cref="LockstepAPI.SendInputAction(uint)"/>,
        /// <see cref="LockstepAPI.SendSingletonInputAction(uint)"/>, its overload or
        /// <see cref="LockstepAPI.SendEventDelayedTicks(uint, uint)"/>.</para>
        /// <para>This function may get called at any point in time once game states have been initialized, so
        /// once <see cref="LockstepAPI.IsInitialized"/> is <see langword="true"/>.</para>
        /// <para>This function is disallowed to fail serialization. It must always succeed.</para>
        /// <para>It is valid for systems to use <see cref="SerializableWannaBeClass"/> outside of serializing
        /// and deserializing game states, in which case the rules described here may not apply.</para>
        /// </summary>
        /// <param name="isExport"><para>When <see langword="true"/> this function shall output all data
        /// necessary for <see cref="Deserialize(bool, uint)"/> to be capable of restoring the current
        /// state.</para>
        /// <para>Only ever <see langword="true"/> if <see cref="SupportsImportExport"/>
        /// <see langword="true"/>.</para></param>
        public abstract void Serialize(bool isExport);
        /// <summary>
        /// <para>This function must call <c>Read</c> functions on <see cref="LockstepAPI"/> in the same order
        /// as <see cref="Serialize(bool)"/> with matching data types, just like input actions or delayed
        /// events.</para>
        /// <para>Ignoring importing, this function will never be called on the very first client, as on that
        /// client <see cref="LockstepEventType.OnInit"/> gets raised to initialize game states. On every
        /// other - future - client this function will run exactly once, before any game state safe events
        /// raised by lockstep.</para>
        /// <para>Still ignoring importing, the purpose of this function is to initialize the game state such
        /// that the resulting game state perfectly matches the initially serialized game state coming from
        /// <see cref="Serialize(bool)"/>.</para>
        /// <para>When importing the purpose of this function is to restore the given serialized state. This
        /// can happen any amount of times. Unless <see cref="LockstepGameState.OptionsForCurrentImport"/>
        /// for whichever game state(s) this class is associated with say otherwise, it is recommended for the
        /// current state to be reset/discarded before importing such that the state after importing matches
        /// the serialized state exactly, without any of the existing state polluting it at least from what
        /// the user can tell. Some internal states may not make sense to reset entirely.</para>
        /// <para>It is valid for systems to use <see cref="SerializableWannaBeClass"/> outside of serializing
        /// and deserializing game states, in which case the rules described here may not apply.</para>
        /// </summary>
        /// <param name="isImport">How many times this is <see langword="false"/> or <see langword="true"/> is
        /// described in the main summary. However if <see cref="SupportsImportExport"/> is
        /// <see langword="false"/> then <paramref name="isImport"/> is guaranteed to never be
        /// <see langword="true"/>.</param>
        /// <param name="importedDataVersion"><para>When <paramref name="isImport"/> is
        /// <see langword="true"/>, this is the version of the binary data which is being imported.</para>
        /// <para>It should be guaranteed to be within the valid supported range as defined by
        /// <see cref="LowestSupportedDataVersion"/> and <see cref="DataVersion"/>, so long as all systems
        /// which are using this class are operating within specification.</para>
        /// </param>
        public abstract void Deserialize(bool isImport, uint importedDataVersion);
    }
}
