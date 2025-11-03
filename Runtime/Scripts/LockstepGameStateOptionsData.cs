using UdonSharp;

namespace JanSharp
{
    /// <summary>
    /// <para>Implementations of this class are referenced by
    /// <see cref="LockstepGameStateOptionsUI.OptionsClassName"/> and subsequently ultimately used as either
    /// export or import options for game states. This data structure is effectively the bridge between the UI
    /// and the <see cref="LockstepGameState"/> serialize and deserialize functions.</para>
    /// <para>The data in this class should describe how an export or import should be performed.</para>
    /// <para>A suggested naming convention is to use the name of the associated game state (potentially
    /// abbreviated if it's getting a bit long) plus either <c>ExportOptions</c> or <c>ImportOptions</c> as a
    /// postfix.</para>
    /// <para>This is a <see cref="SerializableWannaBeClass"/>. Lockstep itself only actually serializes and
    /// deserializes these classes for import options (so those referenced and used by
    /// <see cref="LockstepGameState.ImportUI"/>), and in this case the
    /// <see cref="SerializableWannaBeClass.DataVersion"/> is always going to match on the serializing and
    /// deserializing ends since the import options data structure itself is not getting exported nor
    /// imported. For options used by <see cref="LockstepGameState.ExportUI"/>, Lockstep never performs
    /// serialization nor deserialization.<br/>
    /// That said however, other systems may wish to serialize and deserialize both export and or import
    /// options for example to implement import export profiles, which could even be exported and imported
    /// themselves. As in <see cref="SerializableWannaBeClass.Serialize(bool)"/> would have <c>isExport</c> be
    /// <see langword="true"/>, similarly for <see cref="SerializableWannaBeClass.Deserialize(bool, uint)"/>
    /// but with <c>isImport</c> being <see langword="true"/>, which is almost like import-export-ception. In
    /// order to keep this as a possible feature to be implemented I (JanSharp) would suggest implementing
    /// serialization and deserialization including export and import support for both export and import
    /// options data structures. But to be clear, it is not required in order for Lockstep to work as it is
    /// now (except for serialization and deserialization specifically for import options, without import
    /// export support for the options themselves, as mentioned initially.) If not, define
    /// <see cref="SerializableWannaBeClass.SupportsImportExport"/> as <see langword="false"/> of
    /// course.</para>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class LockstepGameStateOptionsData : SerializableWannaBeClass
    {
        /// <summary>
        /// <para>Must perform a deep clone/copy of the entire options data structure.</para>
        /// </summary>
        /// <returns>A new instance of this class.</returns>
        public abstract LockstepGameStateOptionsData Clone();
    }
}
