using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;

namespace JanSharp
{
    public abstract class LockStepGameState : UdonSharpBehaviour
    {
        public abstract string GameStateDisplayName { get; }
        public abstract DataList SerializeGameState();
        public abstract string DeserializeGameState(DataList stream);
    }
}
