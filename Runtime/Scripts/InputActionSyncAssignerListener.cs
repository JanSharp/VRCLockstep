﻿using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Cyan.PlayerObjectPool;

namespace JanSharp.Internal
{
    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class InputActionSyncAssignerListener : CyanPlayerObjectPoolEventListener
    {
        public Lockstep lockstep;

        // This event is called when the local player's pool object has been assigned.
        public override void _OnLocalPlayerAssigned() { }

        // This event is called when any player is assigned a pool object.
        public override void _OnPlayerAssigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
        {
            InputActionSync inputActionSync = (InputActionSync)(Component)poolObject;
            inputActionSync.lockstep = lockstep;
            inputActionSync.shiftedPlayerId = ((ulong)player.playerId) << Lockstep.PlayerIdKeyShift;
            inputActionSync.ownerPlayerId = (uint)player.playerId;
            // New players always restart at 1u, but 1u was not sent yet so the last received one is 0u.
            inputActionSync.latestInputActionIndex = 0u;

            lockstep.OnInputActionSyncPlayerAssigned(player, inputActionSync);
        }

        // This event is called when any player's object has been unassigned.
        public override void _OnPlayerUnassigned(VRCPlayerApi player, int poolIndex, UdonBehaviour poolObject)
        {
            InputActionSync inputActionSync = (InputActionSync)(Component)poolObject;
            lockstep.OnInputActionSyncPlayerUnassigned(player, inputActionSync);
        }
    }
}
