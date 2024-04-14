using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// This file is generated from a definition file.
// When working on this repository, modify the definition file instead.

namespace JanSharp
{
    public static class LockStepImportedGS
    {
        ///<summary>string</summary>
        public const int InternalName = 0;
        ///<summary>string</summary>
        public const int DisplayName = 1;
        ///<summary>uint</summary>
        public const int DataVersion = 2;
        ///<summary>byte[]</summary>
        public const int BinaryData = 3;
        ///<summary>LockStepGameState</summary>
        public const int GameState = 4;
        ///<summary>int</summary>
        public const int GameStateIndex = 5;
        ///<summary>string</summary>
        public const int ErrorMsg = 6;
        public const int ObjectSize = 7;

        public static object[] New(
            string internalName = default,
            string displayName = default,
            uint dataVersion = default,
            byte[] binaryData = default,
            LockStepGameState gameState = default,
            int gameStateIndex = default,
            string errorMsg = default)
        {
            object[] lockStepImportedGS = new object[ObjectSize];
            lockStepImportedGS[InternalName] = internalName;
            lockStepImportedGS[DisplayName] = displayName;
            lockStepImportedGS[DataVersion] = dataVersion;
            lockStepImportedGS[BinaryData] = binaryData;
            lockStepImportedGS[GameState] = gameState;
            lockStepImportedGS[GameStateIndex] = gameStateIndex;
            lockStepImportedGS[ErrorMsg] = errorMsg;
            return lockStepImportedGS;
        }

        public static string GetInternalName(object[] lockStepImportedGS)
            => (string)lockStepImportedGS[InternalName];
        public static void SetInternalName(object[] lockStepImportedGS, string internalName)
            => lockStepImportedGS[InternalName] = internalName;
        public static string GetDisplayName(object[] lockStepImportedGS)
            => (string)lockStepImportedGS[DisplayName];
        public static void SetDisplayName(object[] lockStepImportedGS, string displayName)
            => lockStepImportedGS[DisplayName] = displayName;
        public static uint GetDataVersion(object[] lockStepImportedGS)
            => (uint)lockStepImportedGS[DataVersion];
        public static void SetDataVersion(object[] lockStepImportedGS, uint dataVersion)
            => lockStepImportedGS[DataVersion] = dataVersion;
        public static byte[] GetBinaryData(object[] lockStepImportedGS)
            => (byte[])lockStepImportedGS[BinaryData];
        public static void SetBinaryData(object[] lockStepImportedGS, byte[] binaryData)
            => lockStepImportedGS[BinaryData] = binaryData;
        public static LockStepGameState GetGameState(object[] lockStepImportedGS)
            => (LockStepGameState)lockStepImportedGS[GameState];
        public static void SetGameState(object[] lockStepImportedGS, LockStepGameState gameState)
            => lockStepImportedGS[GameState] = gameState;
        public static int GetGameStateIndex(object[] lockStepImportedGS)
            => (int)lockStepImportedGS[GameStateIndex];
        public static void SetGameStateIndex(object[] lockStepImportedGS, int gameStateIndex)
            => lockStepImportedGS[GameStateIndex] = gameStateIndex;
        public static string GetErrorMsg(object[] lockStepImportedGS)
            => (string)lockStepImportedGS[ErrorMsg];
        public static void SetErrorMsg(object[] lockStepImportedGS, string errorMsg)
            => lockStepImportedGS[ErrorMsg] = errorMsg;
    }
}
