using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
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
        /// <see cref="SerializeGameState(bool)"/> and <see cref="DeserializeGameState(bool, uint)"/> will
        /// naturally never be <see langword="true"/>.</para>
        /// </summary>
        public abstract bool GameStateSupportsImportExport { get; }
        /// <summary>
        /// <para>Current version of the binary data output from <see cref="SerializeGameState(bool)"/> when
        /// exporting, and subsequently the latest version <see cref="DeserializeGameState(bool, uint)"/> is
        /// capable of importing.</para>
        /// <para>Recommended to start at <c>0u</c>.</para>
        /// </summary>
        public abstract uint GameStateDataVersion { get; }
        /// <summary>
        /// <para>The lowest and therefore oldest binary data version
        /// <see cref="DeserializeGameState(bool, uint)"/> is capable of importing.</para>
        /// <para>Must be less than or equal to <see cref="GameStateDataVersion"/>.</para>
        /// </summary>
        public abstract uint GameStateLowestSupportedDataVersion { get; }
        /// <summary>
        /// TODO: docs
        /// </summary>
        public abstract LockstepGameStateOptionsUI ExportUI { get; }
        /// <summary>
        /// TODO: docs
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
        /// TODO: docs
        /// </summary>
        public LockstepGameStateOptionsData OptionsForCurrentExport => optionsForCurrentExport;
        /// <summary>
        /// TODO: docs
        /// </summary>
        public LockstepGameStateOptionsData OptionsForCurrentImport => optionsForCurrentImport;

        /// <summary>
        /// TODO: docs
        /// make sure to fix all broken signatures in references to this in other documentation
        /// <para>This function must call <c>Write</c> functions on <see cref="LockstepAPI"/> just like other
        /// functions would do before calling <see cref="LockstepAPI.SendInputAction(uint)"/>,
        /// <see cref="LockstepAPI.SendSingletonInputAction(uint)"/>, its overload or
        /// <see cref="LockstepAPI.SendEventDelayedTicks(uint, uint)"/>.</para>
        /// <para>This function may get called at any point in time once game states have been initialized, so
        /// after <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/>.</para>
        /// <para>This function is disallowed to fail serialization of the game state. It must always
        /// succeed.</para>
        /// </summary>
        /// <param name="isExport"><para>When <see langword="true"/> this function shall output all data
        /// necessary for <see cref="DeserializeGameState(bool, uint)"/> to be capable of restoring the
        /// current game state.</para>
        /// <para>Only ever <see langword="true"/> if <see cref="GameStateSupportsImportExport"/> is
        /// <see langword="true"/>.</para></param>
        public abstract void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions);
        /// <summary>
        /// TODO: docs
        /// make sure to fix all broken signatures in references to this in other documentation
        /// <para>This function must call <c>Read</c> functions on <see cref="LockstepAPI"/> in the same order
        /// as <see cref="Serialize(bool)"/> with matching data types, just like input actions or delayed
        /// events.</para>
        /// <para>Ignoring importing, this function will never be called on the very first client, as on that
        /// client <see cref="LockstepEventType.OnInit"/> gets raised to initialize game states. On every
        /// other - future - client this function will run exactly once, before any game state safe events
        /// raised by lockstep.</para>
        /// <para>Still ignoring importing, the purpose of this function is to initialize the game state such
        /// that the resulting game state perfectly matches the initially serialized game state coming from
        /// <see cref="SerializeGameState(bool)"/>.</para>
        /// <para>When importing the purpose of this function is to restore the given state. This can happen
        /// any amount of times. For consistency it is recommended for the current state to be reset/discarded
        /// before importing such that the state after importing is exactly the imported state, without any of
        /// the existing state polluting it, at least from what the user can tell. There can be internal data
        /// such as player associated data, which may be required to stay or be merged with imported data for
        /// a system to continue to function after the import.</para>
        /// </summary>
        /// <param name="isImport">How many times this is <see langword="false"/> or <see langword="true"/> is
        /// described in the main summary. However if <see cref="GameStateSupportsImportExport"/> is
        /// <see langword="false"/> then <paramref name="isImport"/> is guaranteed to never be
        /// <see langword="true"/>.</param>
        /// <param name="importedDataVersion"><para>When <paramref name="isImport"/> is
        /// <see langword="true"/>, this is the version of the binary data which is being imported.</para>
        /// <para>It is guaranteed to be within the valid supported range as defined by
        /// <see cref="GameStateLowestSupportedDataVersion"/> and <see cref="GameStateDataVersion"/>.</para>
        /// </param>
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
