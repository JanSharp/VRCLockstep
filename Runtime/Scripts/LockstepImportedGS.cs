using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// This file is generated from a definition file.
// When working on this repository, modify the definition file instead.

namespace JanSharp
{
    public static class LockstepImportedGS
    {
        ///<summary>string</summary>
        public const int InternalName = 0;
        ///<summary>string</summary>
        public const int DisplayName = 1;
        ///<summary>uint</summary>
        public const int DataVersion = 2;
        ///<summary>byte[]</summary>
        public const int BinaryData = 3;
        ///<summary>LockstepGameState</summary>
        public const int GameState = 4;
        ///<summary>int</summary>
        public const int GameStateIndex = 5;
        ///<summary>string</summary>
        public const int ErrorMsg = 6;
        ///<summary>LockstepGameStateOptionsData</summary>
        public const int ImportOptions = 7;
        ///<summary>byte[]</summary>
        public const int SerializedImportOptions = 8;
        public const int ObjectSize = 9;

        public static object[] New(
            string internalName = default,
            string displayName = default,
            uint dataVersion = default,
            byte[] binaryData = default,
            LockstepGameState gameState = default,
            int gameStateIndex = default,
            string errorMsg = default,
            LockstepGameStateOptionsData importOptions = default,
            byte[] serializedImportOptions = default)
        {
            object[] lockstepImportedGS = new object[ObjectSize];
            lockstepImportedGS[InternalName] = internalName;
            lockstepImportedGS[DisplayName] = displayName;
            lockstepImportedGS[DataVersion] = dataVersion;
            lockstepImportedGS[BinaryData] = binaryData;
            lockstepImportedGS[GameState] = gameState;
            lockstepImportedGS[GameStateIndex] = gameStateIndex;
            lockstepImportedGS[ErrorMsg] = errorMsg;
            lockstepImportedGS[ImportOptions] = importOptions;
            lockstepImportedGS[SerializedImportOptions] = serializedImportOptions;
            return lockstepImportedGS;
        }

        public static string GetInternalName(object[] lockstepImportedGS)
            => (string)lockstepImportedGS[InternalName];
        public static void SetInternalName(object[] lockstepImportedGS, string internalName)
            => lockstepImportedGS[InternalName] = internalName;
        public static string GetDisplayName(object[] lockstepImportedGS)
            => (string)lockstepImportedGS[DisplayName];
        public static void SetDisplayName(object[] lockstepImportedGS, string displayName)
            => lockstepImportedGS[DisplayName] = displayName;
        public static uint GetDataVersion(object[] lockstepImportedGS)
            => (uint)lockstepImportedGS[DataVersion];
        public static void SetDataVersion(object[] lockstepImportedGS, uint dataVersion)
            => lockstepImportedGS[DataVersion] = dataVersion;
        public static byte[] GetBinaryData(object[] lockstepImportedGS)
            => (byte[])lockstepImportedGS[BinaryData];
        public static void SetBinaryData(object[] lockstepImportedGS, byte[] binaryData)
            => lockstepImportedGS[BinaryData] = binaryData;
        public static LockstepGameState GetGameState(object[] lockstepImportedGS)
            => (LockstepGameState)lockstepImportedGS[GameState];
        public static void SetGameState(object[] lockstepImportedGS, LockstepGameState gameState)
            => lockstepImportedGS[GameState] = gameState;
        public static int GetGameStateIndex(object[] lockstepImportedGS)
            => (int)lockstepImportedGS[GameStateIndex];
        public static void SetGameStateIndex(object[] lockstepImportedGS, int gameStateIndex)
            => lockstepImportedGS[GameStateIndex] = gameStateIndex;
        public static string GetErrorMsg(object[] lockstepImportedGS)
            => (string)lockstepImportedGS[ErrorMsg];
        public static void SetErrorMsg(object[] lockstepImportedGS, string errorMsg)
            => lockstepImportedGS[ErrorMsg] = errorMsg;
        public static LockstepGameStateOptionsData GetImportOptions(object[] lockstepImportedGS)
            => (LockstepGameStateOptionsData)lockstepImportedGS[ImportOptions];
        public static void SetImportOptions(object[] lockstepImportedGS, LockstepGameStateOptionsData importOptions)
            => lockstepImportedGS[ImportOptions] = importOptions;
        public static byte[] GetSerializedImportOptions(object[] lockstepImportedGS)
            => (byte[])lockstepImportedGS[SerializedImportOptions];
        public static void SetSerializedImportOptions(object[] lockstepImportedGS, byte[] serializedImportOptions)
            => lockstepImportedGS[SerializedImportOptions] = serializedImportOptions;
    }
}
