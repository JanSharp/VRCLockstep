using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGameState : UdonSharpBehaviour
    {
        private int lockStepPlayerId;

        [LockStepEvent(LockStepEventType.OnInit)]
        public void OnInit()
        {
            Debug.Log($"<dlt> TestGameState  OnInit");
        }

        [LockStepEvent(LockStepEventType.OnClientJoined)]
        public void OnClientJoined()
        {
            Debug.Log($"<dlt> TestGameState  OnClientJoined - {lockStepPlayerId}");
        }

        [LockStepEvent(LockStepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientBeginCatchUp - {lockStepPlayerId}");
        }

        [LockStepEvent(LockStepEventType.OnClientCaughtUp)]
        public void OnClientCaughtUp()
        {
            Debug.Log($"<dlt> TestGameState  OnClientCaughtUp - {lockStepPlayerId}");
        }

        [LockStepEvent(LockStepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
            Debug.Log($"<dlt> TestGameState  OnClientLeft - {lockStepPlayerId}");
        }

        [LockStepEvent(LockStepEventType.OnTick)]
        public void OnTick()
        {
            // Debug.Log("<dlt> TestGameState  OnTick");
        }
    }
}
