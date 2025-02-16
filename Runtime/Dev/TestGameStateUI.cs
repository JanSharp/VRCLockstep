using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using VRC.SDK3.Data;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGameStateUI : UdonSharpBehaviour
    {
        [SerializeField] private TestGameState gameState;
        [SerializeField] private Transform elemsParent;
        [SerializeField] private GameObject elemPrefab;
        private TestGameStateUIElem[] elems = new TestGameStateUIElem[ArrList.MinCapacity];
        private int elemsCount = 0;
        private int activeCount = 0;

        private void SetActiveCount(int count)
        {
            Debug.Log($"<dlt> TestGameStateUI  SetCount - count: {count}");
            if (activeCount == count)
                return;
            activeCount = count;

            for (int i = 0; i < elemsCount; i++)
                elems[i].gameObject.SetActive(i < count);

            for (int i = elemsCount; i < count; i++)
            {
                #if LockstepDebug
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                #endif
                GameObject elemObj = Instantiate(elemPrefab, elemsParent);
                #if LockstepDebug
                Debug.Log($"[LockstepDebug] [sw] TestGameStateUI  SetActiveCount (inner) - instantiateMs: {sw.Elapsed.TotalMilliseconds}");
                #endif
                TestGameStateUIElem elem = elemObj.GetComponent<TestGameStateUIElem>();
                elem.gameState = gameState;
                ArrList.Add(ref elems, ref elemsCount, elem);
            }
        }

        public void UpdateUI()
        {
            Debug.Log($"<dlt> TestGameStateUI  UpdateUI");
            DataList values = gameState.allPlayerData.GetValues();
            SetActiveCount(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                object[] playerData = (object[])values[i].Reference;
                TestGameStateUIElem elem = elems[i];
                uint playerId = (uint)playerData[TestGameState.PlayerData_PlayerId];
                elem.playerId = playerId;
                elem.header.text = $"{playerId} - {(string)playerData[TestGameState.PlayerData_DisplayName]}";
                elem.descriptionField.text = (string)playerData[TestGameState.PlayerData_Description];
            }
        }
    }
}
