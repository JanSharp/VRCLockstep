using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
    public abstract class LockstepAPI : UdonSharpBehaviour
    {
        /// <summary>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint CurrentTick { get; }
        /// <summary>
        /// <para>While this is <see langword="true"/> this client is rapidly running input actions in order
        /// to catch up to real time.</para>
        /// <para><see langword="true"/> only for the initial catch up period, and stops being true a notable
        /// amount of time before receiving the <see cref="LockstepEventType.OnClientCaughtUp"/> event, since
        /// the client finishes catching up and then sends an internal input action to actually update the
        /// internal game state as well as raise the <see cref="LockstepEventType.OnClientCaughtUp"/> event.
        /// </para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsCatchingUp { get; }
        /// <summary>
        /// <para>When this is <see langword="true"/> sent input actions are going to be run just 1 frame
        /// delayed.</para>
        /// <para>The intended use for this property is disabling latency hiding while this is
        /// <see langword="true"/> if a given latency hiding and latency state implementation is
        /// computationally expensive.</para>
        /// <para>If one were to track the amount of clients in an instance using
        /// <see cref="LockstepEventType.OnClientJoined"/> and <see cref="LockstepEventType.OnClientLeft"/>
        /// then <see cref="IsSinglePlayer"/> will <b>not match</b> the "expected" value based on player
        /// count.</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsSinglePlayer { get; }
        /// <summary>
        /// <para>An effectively static readonly list of all game states in the world. The getter for this
        /// property returns a copy of the internal array to prevent modifications.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract LockstepGameState[] AllGameStates { get; }
        /// <summary>
        /// <para>The length of the <see cref="AllGameStates"/> array. To prevent unnecessary array copies for
        /// when all that's needed is the length/count.</para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int AllGameStatesCount { get; }
        /// <summary>
        /// <para>Guaranteed to be <see langword="true"/> on exactly 1 client during the execution of any game
        /// state safe event. Outside of those it is possible for this to be true for 0 clients at some point
        /// in time.</para>
        /// <para>If the goal is to only and an input action from 1 client even though the running function
        /// is a game state event and therefore runs on all clients, it is most likely preferable to use
        /// <see cref="SendSingletonInputAction(uint)"/> or its overload instead of checking
        /// <see cref="IsMaster"/> as that is exactly what that function is made for. However outside of game
        /// state safe events <see cref="SendSingletonInputAction(uint)"/> cannot be used, so as a mostly
        /// reliable alternative <see cref="IsMaster"/> may do the trick.</para>
        /// <para>This does not match <see cref="Networking.IsMaster"/>. This <see cref="IsMaster"/> relates
        /// to the lockstep master which could be any client, though by default the 2 masters are often the
        /// same.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool IsMaster { get; }
        /// <summary>
        /// <para>The player id of the lockstep master, which differs from VRChat's master. The lockstep
        /// master is effectively the acting server for the networking system. By default it is quite likely
        /// for lockstep's master to be the same as VRChat's master.</para>
        /// <para>If the master leaves, this id remains unchanged until the
        /// <see cref="LockstepEventType.OnMasterChanged"/> event is raised.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint MasterPlayerId { get; }
        /// <summary>
        /// <para>The id of the client which was the master right before the new master client.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnMasterChanged"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint OldMasterPlayerId { get; }
        /// <summary>
        /// <para>The id of the joined client.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnClientJoined"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint JoinedPlayerId { get; }
        /// <summary>
        /// <para>The id of the left client.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnClientLeft"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint LeftPlayerId { get; }
        /// <summary>
        /// <para>The id of the client which is beginning to catch up or has caught up.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnClientBeginCatchUp"/> and
        /// <see cref="LockstepEventType.OnClientCaughtUp"/>.</para>
        /// <para>Game state safe (but keep in mind that <see cref="LockstepEventType.OnClientBeginCatchUp"/>
        /// is a game state safe event. <see cref="LockstepEventType.OnClientCaughtUp"/> Is however).</para>
        /// </summary>
        public abstract uint CatchingUpPlayerId { get; }
        /// <summary>
        /// <para>The player id of client which sent the input action. It is guaranteed to be an id for which
        /// the <see cref="LockstepEventType.OnClientJoined"/> event has been raised, and the
        /// <see cref="LockstepEventType.OnClientLeft"/> event has not been raised.</para>
        /// <para>Usable inside of input actions.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint SendingPlayerId { get; }
        /// <summary>
        /// <para>The unique id of the input action that is currently running, which is the same unique id
        /// returned by <see cref="SendInputAction(uint)"/>, <see cref="SendSingletonInputAction(uint)"/>
        /// and its overload.</para>
        /// <para>Never 0uL, since that is an invalid unique id.</para>
        /// <para>The intended purpose is making implementations of latency state and latency hiding easier.
        /// </para>
        /// <para>Usable inside of input actions.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract ulong SendingUniqueId { get; }
        /// <summary>
        /// <para>Enables easily checking if <see cref="SendInputAction(uint)"/>,
        /// <see cref="SendSingletonInputAction(uint)"/> and its overload would be able to actually send input
        /// actions.</para>
        /// <para>It will be <see langword="true"/> as soon as <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> get raised.</para>
        /// <para>Usable any time.</para>
        /// <para>Not game state safe.</para>
        /// </summary>
        public abstract bool CanSendInputActions { get; }
        /// <summary>
        /// <para>Send an input action from one client which, since it is an input action, will then run on
        /// all clients in the same tick in the same order.</para>
        /// <para>To pass data from the sending client to the input action, use <c>Write</c> or any of the
        /// overloads before calling <see cref="SendInputAction(uint)"/>. Then on the receiving side call
        /// <c>Read</c> functions with matching types and in matching order.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="inputActionId">The id associated with a method with the
        /// <see cref="LockstepInputActionAttribute"/> to be sent.</param>
        /// <returns>The unique id of the input action that got sent. If <see cref="CanSendInputActions"/> is
        /// <see langword="false"/> then 0uL - an invalid id - indicating that it did not get sent will be
        /// returned.</returns>
        public abstract ulong SendInputAction(uint inputActionId);
        /// <summary>
        /// <para>Send an input action from one client which, since it is an input action, will then run on
        /// all clients in the same tick in the same order.</para>
        /// <para>To pass data from the sending client to the input action, use <c>Write</c> or any of the
        /// overloads before calling <see cref="SendInputAction(uint)"/>. Then on the receiving side call
        /// <c>Read</c> functions with matching types and in matching order.</para>
        /// <para>The major difference is that <see cref="SendInputAction(uint)"/> simply sends an input
        /// action, meaning if multiple players call it, multiple input actions will be sent. This may be
        /// undesirable when inside of a game state safe event since that runs on every client, however there
        /// may be some non game state data that must be input into the game state. This is what the
        /// <see cref="SendSingletonInputAction(uint)"/> and
        /// <see cref="SendSingletonInputAction(uint, uint)"/> are for. They guarantee that the input action
        /// is sent exactly once, even if the initial responsible player leaves immediately after sending the
        /// input action, in which case a different client becomes responsible for sending it.</para>
        /// <para>Usable inside of game state safe events.</para>
        /// </summary>
        /// <param name="inputActionId">The id associated with a method with the
        /// <see cref="LockstepInputActionAttribute"/> to be sent.</param>
        /// <returns>The unique id of the input action that got sent. If <see cref="CanSendInputActions"/> is
        /// <see langword="false"/> then 0uL - an invalid id - indicating that it did not get sent will be
        /// returned. The unique id is only returned on the initial responsible client.</returns>
        public abstract ulong SendSingletonInputAction(uint inputActionId);
        /// <inheritdoc cref="SendSingletonInputAction(uint)"/>
        /// <param name="responsiblePlayerId">The player id of the client which is takes the initial
        /// responsibility of sending the input action.</param>
        public abstract ulong SendSingletonInputAction(uint inputActionId, uint responsiblePlayerId);
        /// <summary>
        /// <para>The display names are saved in an internal lockstep game state. They are available starting
        /// from within <see cref="LockstepEventType.OnClientJoined"/>, and no longer available in
        /// <see cref="LockstepEventType.OnClientLeft"/>.</para>
        /// <para>Since they are saved and handled as an internal game state, the display names are the same
        /// on all clients and unchanging.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="playerId">The <see cref="VRCPlayerApi.playerId"/> (obtained through any means) of a
        /// client in lockstep's internal game state.</param>
        /// <returns>Display name for the given <paramref name="playerId"/>, or <see langword="null"/> if the
        /// given <paramref name="playerId"/> is not in the lockstep internal game state.</returns>
        public abstract string GetDisplayName(uint playerId);

        /// <summary>
        /// <para>Export the given <paramref name="gameStates"/> into a base 64 encoded string intended for
        /// users to copy and save externally such that the exported string can be passed to
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string)"/> at a future point in time,
        /// including in a future/new instance of the world.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="gameStates">The list of game states to export. Ignores any game states given where
        /// <see cref="LockstepGameState.GameStateSupportsImportExport"/> is <see langword="false"/>. If the
        /// total amount of given game states (which also support exporting) is 0, the function returns
        /// <see langword="null"/>. Must not contain <see langword="null"/>.</param>
        /// <param name="exportName">The name to save inside of the exported string which can be read back
        /// when importing in the future. <see langword="null"/> is a valid value.</param>
        /// <returns>A base 64 encoded string containing a bit of metadata such as which game states have been
        /// exported, their version, the current UTC date and time and then of course exported data retrieved
        /// from <see cref="LockstepGameState.SerializeGameState(bool)"/>. Returns <see langword="null"/> if
        /// called at an invalid time or with invalid <paramref name="gameStates"/>.</returns>
        public abstract string Export(LockstepGameState[] gameStates, string exportName);
        /// <summary>
        /// <para>Load and validate a given base 64 exported string, converting it into an array of objects
        /// containing all export information within the given string into an actually usable format.</para>
        /// <para>This data can be processed using utilities in the <see cref="LockstepImportedGS"/> class if
        /// desired.</para>
        /// <para>This is the data which can then be passed to
        /// <see cref="StartImport(System.DateTime, string, object[][])"/>. It is valid to create a new array
        /// which only contains some of the imported game states obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string)"/>, however modification of
        /// the <see cref="LockstepImportedGS"/> data structures is forbidden.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        /// <param name="exportedString">The base 64 encoded string originally obtained from
        /// <see cref="Export(LockstepGameState[], string)"/>. Can originate from previous instances or even
        /// previous versions of the world or even completely different worlds.</param>
        /// <param name="exportedDate">The UTC date and time at which the
        /// <see cref="Export(LockstepGameState[], string)"/> call was made.</param>
        /// <param name="exportName">The name which was passed to
        /// <see cref="Export(LockstepGameState[], string)"/> at the time of exporting, which can be
        /// <see langword="null"/>.</param>
        /// <returns><see cref="LockstepImportedGS"/>[] importedGameStates, or <see langword="null"/> in case
        /// the given <paramref name="exportedString"/> was invalid.</returns>
        public abstract object[][] ImportPreProcess(
            string exportedString,
            out System.DateTime exportedDate,
            out string exportName);
        /// <summary>
        /// <para>Start importing game states using data obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string)"/>. This requires sending of
        /// input actions. It is also only allowed to be called if <see cref="IsImporting"/> is
        /// <see langword="false"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="exportDate">The UTC date and time obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string)"/>.</param>
        /// <param name="exportName">The name obtained from
        /// <see cref="ImportPreProcess(string, out System.DateTime, out string)"/>.</param>
        /// <param name="importedGameStates">An array containing <see cref="LockstepImportedGS"/> objects
        /// obtained from <see cref="ImportPreProcess(string, out System.DateTime, out string)"/>.</param>
        public abstract void StartImport(
            System.DateTime exportDate,
            string exportName,
            object[][] importedGameStates);
        /// <summary>
        /// <para>Is an import of game states currently in progress? If yes, other properties with
        /// <c>Import</c> in the name can be used to obtain more information about the ongoing import.</para>
        /// <para>Set to <see langword="true"/> right before <see cref="LockstepEventType.OnImportStart"/>
        /// and set to <see langword="false"/> right before <see cref="LockstepEventType.OnImportFinished"/>.
        /// </para>
        /// <para>Usable any time.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract bool IsImporting { get; }
        /// <summary>
        /// <para>The player id of the client which initiated the import and has provided the import data.
        /// </para>
        /// <para>Usable if <see cref="IsImporting"/> is true, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract uint ImportingPlayerId { get; }
        /// <summary>
        /// <para>The UTC time of when the currently being imported data was initially exported.</para>
        /// <para>Usable if <see cref="IsImporting"/> is true, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract System.DateTime ImportingFromDate { get; }
        /// <summary>
        /// <para>The name that was set during the export of the currently being imported data.</para>
        /// <para>Can be <see langword="null"/>.</para>
        /// <para>Usable if <see cref="IsImporting"/> is true, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract string ImportingFromName { get; }
        /// <summary>
        /// <para>The game state that has just been imported.</para>
        /// <para>Usable inside of <see cref="LockstepEventType.OnImportedGameState"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract LockstepGameState ImportedGameState { get; }
        /// <summary>
        /// <para>This returns a new copy of the array every time it is accessed.</para>
        /// <para>The game stats which are about to be imported, but have not been imported yet.</para>
        /// <para>Inside of <see cref="LockstepEventType.OnImportedGameState"/>, the
        /// <see cref="ImportedGameState"/> is no longer in this list.</para>
        /// <para>Inside of <see cref="LockstepEventType.OnImportFinished"/> this list may not actually be
        /// empty, indicating that an import was aborted. For example if the importing client instantly left
        /// after initiating an import.</para>
        /// <para>Usable if <see cref="IsImporting"/> is true, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract LockstepGameState[] GameStatesWaitingForImport { get; }
        /// <summary>
        /// <para>Returns just the length of <see cref="GameStatesWaitingForImport"/> such that when all
        /// that's needed is the length there isn't an entire array being constructed and copied just to be
        /// thrown away again immediately afterwards.</para>
        /// <para>Usable if <see cref="IsImporting"/> is true, or inside of
        /// <see cref="LockstepEventType.OnImportFinished"/>.</para>
        /// <para>Game state safe.</para>
        /// </summary>
        public abstract int GameStatesWaitingForImportCount { get; }

        /// <summary>
        /// <para>When data has already been written to the internal write stream using any of the
        /// <c>Write</c> functions, however the call to <see cref="SendInputAction(uint)"/>,
        /// <see cref="SendSingletonInputAction(uint)"/> or its overload ends up not happening due to an early
        /// return, it is required to call <see cref="ResetWriteStream"/>.</para>
        /// <para>This cleans up the write stream such that future sent input actions do not get this
        /// unfinished input action data prepended to them, ultimately breaking them.</para>
        /// </summary>
        public abstract void ResetWriteStream();
        /// <summary>
        /// <para>When using <see cref="SendInputAction(uint)"/>, <see cref="SendSingletonInputAction(uint)"/>
        /// or its overload or <see cref="LockstepGameState.SerializeGameState(bool)"/>, in order to pass data
        /// to the input action or <see cref="LockstepGameState.DeserializeGameState(bool)"/> use this
        /// function to write data to an internal binary stream which is used by lockstep to perform syncing.
        /// </para>
        /// <para>On the note of <see cref="LockstepGameState.SerializeGameState(bool)"/> when exporting the
        /// same serialization method is used.</para>
        /// <para>Usable any time (technically).</para>
        /// </summary>
        /// <param name="value">The value to be serialized and written to the byte stream.</param>
        public abstract void Write(sbyte value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(byte value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(short value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(ushort value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(int value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(uint value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(long value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(ulong value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(float value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(double value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(Vector2 value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(Vector3 value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(Vector4 value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(Quaternion value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(char value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(string value);
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void Write(System.DateTime value);
        /// <inheritdoc cref="Write(sbyte)"/>
        /// <param name="bytes">The raw bytes to be written to the byte stream. This does not add any length
        /// information to the binary stream, it just takes these bytes as they are.</param>
        public abstract void Write(byte[] bytes);
        /// <summary>
        /// <para>When using <see cref="SendInputAction(uint)"/>, <see cref="SendSingletonInputAction(uint)"/>
        /// or its overload or <see cref="LockstepGameState.SerializeGameState(bool)"/>, in order to pass data
        /// to the input action or <see cref="LockstepGameState.DeserializeGameState(bool)"/> use this
        /// function to write data to an internal binary stream which is used by lockstep to perform syncing.
        /// </para>
        /// <para>On the note of <see cref="LockstepGameState.SerializeGameState(bool)"/> when exporting the
        /// same serialization method is used.</para>
        /// <para>The <p>WriteSmall</p> variants of these serialization functions use fewer bytes to
        /// serialize given values if the given value is small enough. For the signed variants, small signed
        /// values are also supported and will use fewer bytes, however unsigned variants are slightly more
        /// efficient, both in terms of speed and size.</para>
        /// <para>Usable any time (technically).</para>
        /// </summary>
        /// <inheritdoc cref="Write(sbyte)"/>
        public abstract void WriteSmall(short value);
        /// <inheritdoc cref="WriteSmall(short)"/>
        public abstract void WriteSmall(ushort value);
        /// <inheritdoc cref="WriteSmall(short)"/>
        public abstract void WriteSmall(int value);
        /// <inheritdoc cref="WriteSmall(short)"/>
        public abstract void WriteSmall(uint value);
        /// <inheritdoc cref="WriteSmall(short)"/>
        public abstract void WriteSmall(long value);
        /// <inheritdoc cref="WriteSmall(short)"/>
        public abstract void WriteSmall(ulong value);

        /// <summary>
        /// <para>Inside of input actions or <see cref="LockstepGameState.DeserializeGameState(bool)"/> in
        /// order to retrieve the data that was initially written to an internal binary stream, these
        /// <c>Read</c> functions shall be used to read from an internal read stream (a different stream than
        /// the write stream).</para>
        /// <para>The calls to the <c>Read</c> functions must match the data type and the order in which the
        /// values were initially written to the write stream on the sending side.</para>
        /// </summary>
        /// <returns>The deserialized value read from the internal read stream.</returns>
        public abstract sbyte ReadSByte();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract byte ReadByte();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract short ReadShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ushort ReadUShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract int ReadInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract uint ReadUInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract long ReadLong();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ulong ReadULong();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract float ReadFloat();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract double ReadDouble();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Vector2 ReadVector2();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Vector3 ReadVector3();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Vector4 ReadVector4();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract Quaternion ReadQuaternion();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract char ReadChar();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract string ReadString();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract System.DateTime ReadDateTime();
        /// <inheritdoc cref="ReadSByte"/>
        /// <param name="byteCount">The amount of raw bytes to read from the read stream. Very most likely
        /// used in conjunction with <see cref="Write(byte[])"/>, but again said write function does not write
        /// any length information to the write stream, therefore it is up to the caller to know the length of
        /// the data to be read.</param>
        public abstract byte[] ReadBytes(int byteCount);
        /// <inheritdoc cref="ReadSByte"/>
        public abstract short ReadSmallShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ushort ReadSmallUShort();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract int ReadSmallInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract uint ReadSmallUInt();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract long ReadSmallLong();
        /// <inheritdoc cref="ReadSByte"/>
        public abstract ulong ReadSmallULong();
    }
}
