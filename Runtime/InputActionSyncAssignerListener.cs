using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Cyan.PlayerObjectPool;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class InputActionSyncAssignerListener : CyanPlayerObjectPoolEventListener
    {
        public LockStep lockStep;

        // This event is called when the local player's pool object has been assigned.
        public override void _OnLocalPlayerAssigned() { }

        // This event is called when any player is assigned a pool object.
        public override void _OnPlayerAssigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
        {
            InputActionSync inputActionSync = (InputActionSync)(Component)poolObject;
            inputActionSync.lockStep = lockStep;
            inputActionSync.shiftedPlayerId = ((ulong)player.playerId) << LockStep.PlayerIdKeyShift;

            lockStep.OnInputActionSyncPlayerAssigned(player, inputActionSync);
        }

        // This event is called when any player's object has been unassigned.
        public override void _OnPlayerUnassigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
        { }
    }
}
