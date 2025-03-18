using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
    /// <summary>
    /// <para>A game state defines a state which is identical across all clients, as well as input actions to
    /// modify this state and serialization/deserialization to sync the state for late joiners.</para>
    /// <para>Note that input actions are not limited to only be defined in this game state script, though
    /// any script which defines input actions must exist in the scene at build time. In other words it must
    /// not be instantiated at runtime. For syncing for instantiated objects use just one cental script and
    /// identify objects using ids. The <see cref="BuildTimeIdAssignmentAttribute"/> can help with this if
    /// objects may exist in the scene at build time as well as be instantiated at runtime.</para>
    /// <para>Optionally game states can also define export and import behavior, allowing the serialized
    /// game state to be copied out by the user, kind of like a save file, to then be imported at a later
    /// point in time, in a new instance or even a different world.</para>
    /// <para>Optionally game states can define custom options with custom UIs for the user to customize how
    /// game states are supposed to be exported and imported.</para>
    /// </summary>
    public abstract class LockstepGameState : UdonSharpBehaviour
    {
        [HideInInspector] [SingletonReference] public LockstepAPI lockstep;

        /// <summary>
        /// <para>Must be a completely unique name for this game state. Anything can be used however it is
        /// recommended to use <c>author-name.package-name</c>, everything lower case and using dashes instead
        /// of white spaces. <c>author-name</c> should be self explanatory. <c>package-name</c> could be a
        /// descriptive name for the system this game state is for.</para>
        /// <para>When creating a package (vpm package most likely) this is effectively simply the package
        /// name but without the <c>com.</c> prefix.</para>
        /// </summary>
        public abstract string GameStateInternalName { get; }
        /// <summary>
        /// <para>A user readable and identifiable name for this system/game state.</para>
        /// </summary>
        public abstract string GameStateDisplayName { get; }
        /// <summary>
        /// <para>When <see langword="false"/>, the <c>isExport</c> and <c>isImport</c> parameters for
        /// <see cref="SerializeGameState(bool, LockstepGameStateOptionsData)"/> and
        /// <see cref="DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> will naturally never
        /// be <see langword="true"/>.</para>
        /// </summary>
        public abstract bool GameStateSupportsImportExport { get; }
        /// <summary>
        /// <para>Current version of the binary data output from
        /// <see cref="SerializeGameState(bool, LockstepGameStateOptionsData)"/> when exporting, and
        /// subsequently the latest version
        /// <see cref="DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> is capable of
        /// importing.</para>
        /// <para>Recommended to start at <c>0u</c>.</para>
        /// </summary>
        public abstract uint GameStateDataVersion { get; }
        /// <summary>
        /// <para>The lowest and therefore oldest binary data version
        /// <see cref="DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> is capable of
        /// importing.</para>
        /// <para>Must be less than or equal to <see cref="GameStateDataVersion"/>.</para>
        /// </summary>
        public abstract uint GameStateLowestSupportedDataVersion { get; }
        /// <summary>
        /// <para>In order to have custom export options and an a UI for the user to modify them, this
        /// property must be defined using a references to an instance of a custom implementation of the
        /// <see cref="LockstepGameStateOptionsUI"/> class. The instance being an object in the scene and the
        /// reference set in the inspector to a field best called <c>exportUI</c> on this script. Then this
        /// property would just be defined as <c>ExportUI => exportUI;</c>, for example.</para>
        /// <para>Define this as <see langword="null"/> if no export options nor UI are implemented.</para>
        /// </summary>
        public abstract LockstepGameStateOptionsUI ExportUI { get; }
        /// <summary>
        /// <para>In order to have custom import options and an a UI for the user to modify them, this
        /// property must be defined using a references to an instance of a custom implementation of the
        /// <see cref="LockstepGameStateOptionsUI"/> class. The instance being an object in the scene and the
        /// reference set in the inspector to a field best called <c>importUI</c> on this script. Then this
        /// property would just be defined as <c>ImportUI => importUI;</c>, for example.</para>
        /// <para>Define this as <see langword="null"/> if no import options nor UI are implemented.</para>
        /// </summary>
        public abstract LockstepGameStateOptionsUI ImportUI { get; }

        /// <summary>
        /// <para>Set through <see cref="UdonSharpBehaviour.SetProgramVariable(string, object)"/> by lockstep,
        /// since this is specifically not meant to be modified by the user, there's just the public
        /// getter.</para>
        /// </summary>
        private LockstepGameStateOptionsData optionsForCurrentExport;
        /// <inheritdoc cref="optionsForCurrentExport"/>
        private LockstepGameStateOptionsData optionsForCurrentImport;
        /// <summary>
        /// <para>The instance of export options used for the current
        /// <see cref="LockstepAPI.Export(string, LockstepGameStateOptionsData[])"/> for this game state. In
        /// the export process these options get set on each game state before any call to
        /// <see cref="SerializeGameState(bool, LockstepGameStateOptionsData)"/>.</para>
        /// <para>Class type is defined by <see cref="LockstepGameStateOptionsUI.OptionsClassName"/> on the
        /// <see cref="ExportUI"/>. Cast it to that type in order to access the actual options.</para>
        /// <para>Always <see langword="null"/> if <see cref="ExportUI"/> is <see langword="null"/>.</para>
        /// <para>Usable while <see cref="LockstepAPI.IsSerializingForExport"/> is
        /// <see langword="true"/>.</para>
        /// <para>Game stat safe.</para>
        /// </summary>
        public LockstepGameStateOptionsData OptionsForCurrentExport => optionsForCurrentExport;
        /// <summary>
        /// <para>The instance of import options used for the current
        /// <see cref="LockstepAPI.StartImport(object[][], System.DateTime, string, string)"/> for this game
        /// state. In the import process these options get set on each game state before any call to
        /// <see cref="DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/>.</para>
        /// <para>Class type is defined by <see cref="LockstepGameStateOptionsUI.OptionsClassName"/> on the
        /// <see cref="ImportUI"/>. Cast it to that type in order to access the actual options.</para>
        /// <para>Always <see langword="null"/> if <see cref="ImportUI"/> is <see langword="null"/>.</para>
        /// <para>Usable while <see cref="LockstepAPI.IsDeserializingForImport"/> is
        /// <see langword="true"/>.</para>
        /// <para>Game stat safe.</para>
        /// </summary>
        public LockstepGameStateOptionsData OptionsForCurrentImport => optionsForCurrentImport;

        /// <summary>
        /// <para>This function must call <c>Write</c> functions on <see cref="lockstep"/> (which is a
        /// <see cref="LockstepAPI"/> instance) just like other functions would do before calling
        /// <see cref="LockstepAPI.SendInputAction(uint)"/>,
        /// <see cref="LockstepAPI.SendSingletonInputAction(uint)"/>, its overload or
        /// <see cref="LockstepAPI.SendEventDelayedTicks(uint, uint)"/>.</para>
        /// <para>This function may get called at any point in time once game states have been initialized, so
        /// after <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> have been raised.</para>
        /// <para>This function is disallowed to fail serialization of the game state. It must always
        /// succeed.</para>
        /// <para>Hover the parameters to read docs about exports.</para>
        /// </summary>
        /// <param name="isExport"><para>When <see langword="true"/> this function shall output all data
        /// necessary for <see cref="DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> to be
        /// capable of restoring the current game state, even in new instances of the world, different worlds
        /// and or from older <see cref="GameStateDataVersion"/>.</para>
        /// <para>Only ever <see langword="true"/> if <see cref="GameStateSupportsImportExport"/> is
        /// <see langword="true"/>.</para>
        /// <para>When <see langword="true"/>, <see cref="LockstepAPI.ex"/></para></param>
        /// <param name="exportOptions">If <see cref="ExportUI"/> is non <see langword="null"/> and
        /// <paramref name="isExport"/> is <see langword="true"/>, this will be the export options instance
        /// containing custom options to use for the current export. Must be cast to the proper type in order
        /// to access the custom options, as defined by the
        /// <see cref="LockstepGameStateOptionsUI.OptionsClassName"/> on the <see cref="ExportUI"/>.
        /// <see cref="OptionsForCurrentExport"/> holds a reference to the same options instance.</param>
        public abstract void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions);
        /// <summary>
        /// <para>This function must call <c>Read</c> functions on <see cref="lockstep"/> (which is a
        /// <see cref="LockstepAPI"/> instance) in the same order as
        /// <see cref="SerializeGameState(bool, LockstepGameStateOptionsData)"/> with matching data types,
        /// just like input actions or delayed events.</para>
        /// <para>Ignoring importing, this function will never be called on the very first client, as on that
        /// client <see cref="LockstepEventType.OnInit"/> gets raised to initialize game states. On every
        /// other - future - client this function will run exactly once, before any game state safe events
        /// get raised by lockstep.</para>
        /// <para>Still ignoring importing, the purpose of this function is to initialize the game state such
        /// that the resulting game state perfectly matches the initially serialized game state coming from
        /// <see cref="SerializeGameState(bool, LockstepGameStateOptionsData)"/>.</para>
        /// <para>When importing the purpose of this function is to restore the given state. This can happen
        /// any amount of times. For consistency - specifically when <see cref="ImportUI"/> and with it
        /// <paramref name="importOptions"/> is <see langword="null"/> - it is recommended for the current
        /// state to be reset/discarded before importing such that the state after importing is exactly the
        /// imported state, without any of the existing state polluting it, at least from what the user can
        /// tell. There can be internal data such as player associated data, which may be required to stay or
        /// be merged with imported data for a system to continue to function after the import.</para>
        /// <para>Hover the parameters to read further docs about imports.</para>
        /// </summary>
        /// <param name="isImport">How many times this is <see langword="false"/> or <see langword="true"/> is
        /// described in the main summary. However if <see cref="GameStateSupportsImportExport"/> is
        /// <see langword="false"/> then <paramref name="isImport"/> is guaranteed to never be
        /// <see langword="true"/>. Separately whenever this is <see langword="true"/>,
        /// <see cref="LockstepAPI.IsDeserializingForImport"/> is also <see langword="true"/>.</param>
        /// <param name="importedDataVersion"><para>When <paramref name="isImport"/> is
        /// <see langword="true"/>, this is the version of the binary data which is being imported.</para>
        /// <para>This is the version that <see cref="GameStateDataVersion"/> was at the time of
        /// <see cref="SerializeGameState(bool, LockstepGameStateOptionsData)"/>.</para>
        /// <para>It is guaranteed to be within the valid supported range as defined by
        /// <see cref="GameStateLowestSupportedDataVersion"/> and <see cref="GameStateDataVersion"/>.</para>
        /// </param>
        /// <param name="importOptions">If <see cref="ImportUI"/> is non <see langword="null"/> and
        /// <paramref name="isImport"/> is <see langword="true"/>, this will be the import options instance
        /// containing custom options to use for the current import. Must be cast to the proper type in order
        /// to access the custom options, as defined by the
        /// <see cref="LockstepGameStateOptionsUI.OptionsClassName"/> on the <see cref="ImportUI"/>.
        /// <see cref="OptionsForCurrentImport"/> holds a reference to the same options instance.<</param>
        /// <returns><para>A non <see langword="null"/> return value indicates failure.</para>
        /// <para>Failing at deserializing late joiner data, so when <paramref name="isImport"/> is
        /// <see langword="false"/>, means the system is in an unrecoverable state on this client and should
        /// therefore only ever be done to fail fast, which improves debugging.</para>
        /// <para>Failing while <paramref name="isImport"/> is <see langword="true"/> is a valid thing to do,
        /// since - if implemented properly - every client will fail the same way. As such the error message
        /// can be displayed to the user and the system can continue running. However it is still highly
        /// discouraged to fail this way as the user can do basically nothing about it. This should be a last
        /// resort.</para>
        /// <para>Either way, whenever an error message is returned, a
        /// <see cref="LockstepEventType.OnLockstepNotification"/> is sent where the
        /// <see cref="LockstepAPI.NotificationMessage"/> contains the given error message.</para></returns>
        public abstract string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions);
    }
}
