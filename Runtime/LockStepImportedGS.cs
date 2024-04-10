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
        ///<summary>int</summary>
        public const int DataSize = 3;
        ///<summary>int</summary>
        public const int DataPosition = 4;
        ///<summary>LockStepGameState</summary>
        public const int GameState = 5;
        ///<summary>string</summary>
        public const int ErrorMsg = 6;
        public const int ObjectSize = 7;

        public static object[] New(
            string internalName = default,
            string displayName = default,
            uint dataVersion = default,
            int dataSize = default,
            int dataPosition = default,
            LockStepGameState gameState = default,
            string errorMsg = default)
        {
            object[] lockStepExportedGS = new object[ObjectSize];
            lockStepExportedGS[InternalName] = internalName;
            lockStepExportedGS[DisplayName] = displayName;
            lockStepExportedGS[DataVersion] = dataVersion;
            lockStepExportedGS[DataSize] = dataSize;
            lockStepExportedGS[DataPosition] = dataPosition;
            lockStepExportedGS[GameState] = gameState;
            lockStepExportedGS[ErrorMsg] = errorMsg;
            return lockStepExportedGS;
        }

        public static string GetInternalName(object[] lockStepExportedGS)
            => (string)lockStepExportedGS[InternalName];
        public static void SetInternalName(object[] lockStepExportedGS, string internalName)
            => lockStepExportedGS[InternalName] = internalName;
        public static string GetDisplayName(object[] lockStepExportedGS)
            => (string)lockStepExportedGS[DisplayName];
        public static void SetDisplayName(object[] lockStepExportedGS, string displayName)
            => lockStepExportedGS[DisplayName] = displayName;
        public static uint GetDataVersion(object[] lockStepExportedGS)
            => (uint)lockStepExportedGS[DataVersion];
        public static void SetDataVersion(object[] lockStepExportedGS, uint dataVersion)
            => lockStepExportedGS[DataVersion] = dataVersion;
        public static int GetDataSize(object[] lockStepExportedGS)
            => (int)lockStepExportedGS[DataSize];
        public static void SetDataSize(object[] lockStepExportedGS, int dataSize)
            => lockStepExportedGS[DataSize] = dataSize;
        public static int GetDataPosition(object[] lockStepExportedGS)
            => (int)lockStepExportedGS[DataPosition];
        public static void SetDataPosition(object[] lockStepExportedGS, int dataPosition)
            => lockStepExportedGS[DataPosition] = dataPosition;
        public static LockStepGameState GetGameState(object[] lockStepExportedGS)
            => (LockStepGameState)lockStepExportedGS[GameState];
        public static void SetGameState(object[] lockStepExportedGS, LockStepGameState gameState)
            => lockStepExportedGS[GameState] = gameState;
        public static string GetErrorMsg(object[] lockStepExportedGS)
            => (string)lockStepExportedGS[ErrorMsg];
        public static void SetErrorMsg(object[] lockStepExportedGS, string errorMsg)
            => lockStepExportedGS[ErrorMsg] = errorMsg;
    }
}
