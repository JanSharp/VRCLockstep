using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using UnityEngine.UI;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGameStateUIElem : UdonSharpBehaviour
    {
        [System.NonSerialized] public TestGameState gameState;
        [System.NonSerialized] public int playerId;
        public TextMeshProUGUI header;
        public InputField descriptionField;

        public void OnDescriptionEndEdit()
        {
            Debug.Log($"<dlt> TestGameStateUIElem  OnDescriptionEndEdit - playerId: {playerId}");
            if (!this.gameObject.activeSelf) // Just to make sure.
                return;
            gameState.SetDescription(playerId, descriptionField.text);
        }
    }
}
