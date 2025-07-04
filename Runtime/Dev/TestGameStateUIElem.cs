﻿using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGameStateUIElem : UdonSharpBehaviour
    {
        [System.NonSerialized] public TestGameState gameState;
        [System.NonSerialized] public uint playerId;
        public TextMeshProUGUI header;
        public InputField descriptionField;

        public void OnDescriptionEndEdit()
        {
            Debug.Log($"[LockstepTest] TestGameStateUIElem  OnDescriptionEndEdit - playerId: {playerId}");
            if (!this.gameObject.activeSelf) // Just to make sure.
                return;
            gameState.SetDescription(playerId, descriptionField.text);
        }
    }
}
