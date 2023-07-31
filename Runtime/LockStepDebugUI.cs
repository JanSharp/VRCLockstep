using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;
using VRC.SDK3.Data;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockStepDebugUI : UdonSharpBehaviour
    {
        public LockStep lockStep;

        public Transform flagsParent;
        private string[] flagFieldNames = new string[]
        {
            "isTickPaused",
            "isMaster",
            "ignoreLocalInputActions",
            "stillAllowLocalClientJoinedIA",
            "ignoreIncomingInputActions",
            "isWaitingForLateJoinerSync",
            "sendLateJoinerDataAtEndOfTick",
            "isCatchingUp",
            "isSinglePlayer",
            "currentlyNoMaster",
        };
        private Toggle[] flagToggles;
        private TextMeshProUGUI[] flagLabels;

        private const float MinMaxTimeFrame = 5f;
        private int lastFullSecond = int.MinValue;

        public TextMeshProUGUI lockStepPerformanceText;
        private const string lockStepLastUpdateTimeFieldName = "lastUpdateTime";
        private float averageUpdateTime;
        private float minUpdateTime = float.MaxValue;
        private float maxUpdateTime = float.MinValue;
        private string formattedMaxAndMax;

        public TextMeshProUGUI debugUIPerformanceText;
        private float debugLastUpdateTime;
        private float debugAverageUpdateTime;
        private float debugMinUpdateTime = float.MaxValue;
        private float debugMaxUpdateTime = float.MinValue;
        private string debugFormattedMaxAndMax;

        public Transform numbersParent;
        private string[] numbersFieldNames = new string[]
        {
            "currentTick",
            "waitTick",
            "startTick",
            "tickStartTime",
            "unrecoverableStateDueToUniqueId",
        };
        private TextMeshProUGUI[] numbersValues;
        private TextMeshProUGUI[] numbersLabels;

        public RectTransform clientStatesParent;
        public TextMeshProUGUI clientStatesCountText;
        public float clientStatesElemHeight;
        private object[] clientStatesListObj;
        private DataDictionary clientStates;
        private DataList clientStatesKeys;
        private const string ClientStatesFieldName = "clientStates";
        private string[] clientStateNameLut = new string[]
        {
            "Master",
            "WaitingForLateJoinerSync",
            "Normal",
        };

        public RectTransform leftClientsParent;
        public TextMeshProUGUI leftClientsCountText;
        public float leftClientsElemHeight;
        private object[] leftClientsListObj;
        private int[] leftClients;
        private const string LeftClientsFieldName = "leftClients";
        private const string LeftClientsCountFieldName = "leftClientsCount";

        // TODO: pending input actions
        // TODO: queued input actions
        // NOTE: Probably also list of registered input actions, but that's for later
        // NOTE: Probably also some visualization of game states, but that's for later

        void Start()
        {
            InitializeFlags();
            InitializeNumbers();
            InitializeClientStates();
            InitializeLeftClients();
            Update();
        }

        void Update()
        {
            float startTime = Time.realtimeSinceStartup;
            UpdateFlags();
            UpdatePerformance();
            UpdateNumbers();
            UpdateClientStates();
            UpdateLeftClients();
            debugLastUpdateTime = Time.realtimeSinceStartup - startTime;
        }

        private void InitializeFlags()
        {
            int count = flagFieldNames.Length;
            flagToggles = new Toggle[count];
            flagLabels = new TextMeshProUGUI[count];
            for (int i = 0; i < count; i++)
            {
                Transform child = flagsParent.GetChild(i);
                flagToggles[i] = child.GetComponentInChildren<Toggle>();
                flagLabels[i] = child.GetComponentInChildren<TextMeshProUGUI>();
                flagLabels[i].text = flagFieldNames[i];
            }
        }

        private void UpdateFlags()
        {
            for (int i = 0; i < flagFieldNames.Length; i++)
                flagToggles[i].SetIsOnWithoutNotify((bool)lockStep.GetProgramVariable(flagFieldNames[i]));
        }

        private void UpdatePerformance()
        {
            float lastUpdateTime = (float)lockStep.GetProgramVariable(lockStepLastUpdateTimeFieldName);
            maxUpdateTime = Mathf.Max(maxUpdateTime, lastUpdateTime);
            minUpdateTime = Mathf.Min(minUpdateTime, lastUpdateTime);
            debugMaxUpdateTime = Mathf.Max(debugMaxUpdateTime, debugLastUpdateTime);
            debugMinUpdateTime = Mathf.Min(debugMinUpdateTime, debugLastUpdateTime);

            int currentFullSecond = (int)(Time.realtimeSinceStartup / MinMaxTimeFrame);
            if (currentFullSecond != lastFullSecond)
            {
                lastFullSecond = currentFullSecond;

                formattedMaxAndMax = $" | {(minUpdateTime * 1000f):f3} | {(maxUpdateTime * 1000f):f3}";
                maxUpdateTime = float.MinValue;
                minUpdateTime = float.MaxValue;

                debugFormattedMaxAndMax = $" | {(debugMinUpdateTime * 1000f):f3} | {(debugMaxUpdateTime * 1000f):f3}";
                debugMaxUpdateTime = float.MinValue;
                debugMinUpdateTime = float.MaxValue;
            }

            averageUpdateTime = averageUpdateTime * 0.875f + lastUpdateTime * 0.125f;
            lockStepPerformanceText.text = (averageUpdateTime * 1000f).ToString("f3") + formattedMaxAndMax;

            debugAverageUpdateTime = debugAverageUpdateTime * 0.875f + debugLastUpdateTime * 0.125f;
            debugUIPerformanceText.text = (debugAverageUpdateTime * 1000f).ToString("f3") + debugFormattedMaxAndMax;
        }

        private void InitializeNumbers()
        {
            int count = numbersFieldNames.Length;
            numbersValues = new TextMeshProUGUI[count];
            numbersLabels = new TextMeshProUGUI[count];
            for (int i = 0; i < count; i++)
            {
                Transform child = numbersParent.GetChild(i);
                TextMeshProUGUI[] texts = child.GetComponentsInChildren<TextMeshProUGUI>();
                numbersValues[i] = texts[0];
                numbersLabels[i] = texts[1];
                numbersLabels[i].text = numbersFieldNames[i];
            }
        }

        private void UpdateNumbers()
        {
            for (int i = 0; i < numbersFieldNames.Length; i++)
                numbersValues[i].text = lockStep.GetProgramVariable(numbersFieldNames[i]).ToString();
        }

        private void InitializeClientStates()
        {
            clientStatesListObj = NewListObj(
                null,
                clientStatesElemHeight,
                clientStatesParent,
                clientStatesCountText
            );
        }

        private void UpdateClientStates()
        {
            if (clientStates == null)
                clientStates = (DataDictionary)lockStep.GetProgramVariable(ClientStatesFieldName);

            if (clientStates != null)
                clientStatesKeys = clientStates.GetKeys();

            UpdateList(
                clientStatesListObj,
                clientStates == null,
                clientStates == null ? 0 : clientStates.Count,
                nameof(CreateValueLabelListElemObj),
                nameof(UpdateClientStateListElemObj)
            );
        }

        // (GameObject elemGameObj) => object[] listElemObj;
        public void CreateValueLabelListElemObj()
        {
            TextMeshProUGUI[] texts = elemGameObj.GetComponentsInChildren<TextMeshProUGUI>();
            listElemObj = new object[ValueLabel_ListElemObj_Size];
            listElemObj[ListObjElem_GameObj] = elemGameObj;
            listElemObj[ValueLabel_ListElemObj_Value] = texts[0];
            listElemObj[ValueLabel_ListElemObj_Label] = texts.Length >= 2 ? texts[1] : null;
        }

        private string FormatPlayerId(int playerId)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            return player == null ? playerId.ToString() : $"{playerId} - {player.displayName}";
        }

        // (object[] listObj, object[] listElemObj, int elemIndex) => void;
        public void UpdateClientStateListElemObj()
        {
            DataToken playerIdToken = clientStatesKeys[elemIndex];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text
                = clientStateNameLut[clientStates[playerIdToken].Byte];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Label]).text
                = FormatPlayerId(playerIdToken.Int);
        }

        private void InitializeLeftClients()
        {
            leftClientsListObj = NewListObj(
                null,
                leftClientsElemHeight,
                leftClientsParent,
                leftClientsCountText
            );
        }

        private void UpdateLeftClients()
        {
            leftClients = (int[])lockStep.GetProgramVariable(LeftClientsFieldName);
            int count = (int)lockStep.GetProgramVariable(LeftClientsCountFieldName);

            UpdateList(
                leftClientsListObj,
                false,
                count,
                nameof(CreateValueLabelListElemObj),
                nameof(UpdateLeftClientsListElemObj)
            );
        }

        // (object[] listObj, object[] listElemObj, int elemIndex) => void;
        public void UpdateLeftClientsListElemObj()
        {
            int playerId = leftClients[elemIndex];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text = FormatPlayerId(playerId);
        }

        // I'm intentionally breaking the naming convention here, because defining "classes" like this is
        // not something you'd ever do in normal C#, so coming up with a new convention for it seems reasonable.

        private const int ListObj_BackingData = 0; // object
        private const int ListObj_Elems = 1; // object[][] (ListObjElem[])
        private const int ListObj_ElemCount = 2; // int
        private const int ListObj_ElemHeight = 3; // float
        private const int ListObj_ElemPrefab = 4; // GameObject
        private const int ListObj_StateText = 5; // TextMeshProUGUI
        private const int ListObj_InitialPanelSize = 6; // Vector2
        private const int ListObj_InitialParentSize = 7; // Vector2
        private const int ListObj_Panel = 8; // RectTransform
        private const int ListObj_Parent = 9; // RectTransform
        private const int ListObj_Size = 10;

        private const int ListObjElem_GameObj = 0; // GameObject
        private const int ListObjElem_Size = 1;

        // This is what inheritance looks like
        private const int ValueLabel_ListElemObj_Value = ListObjElem_Size + 0; // TextMeshProUGUI
        private const int ValueLabel_ListElemObj_Label = ListObjElem_Size + 1; // TextMeshProUGUI
        private const int ValueLabel_ListElemObj_Size = ListObjElem_Size + 2;

        private object[] NewListObj(
            object backingData,
            float elemHeight,
            RectTransform elemsParent,
            TextMeshProUGUI stateText)
        {
            object[] listObj = new object[ListObj_Size];

            RectTransform panel = (RectTransform)elemsParent.parent;

            listObj[ListObj_BackingData] = backingData;
            listObj[ListObj_Elems] = new object[ArrList.MinCapacity][];
            listObj[ListObj_ElemCount] = 0;
            listObj[ListObj_ElemHeight] = elemHeight;
            listObj[ListObj_ElemPrefab] = elemsParent.GetChild(0).gameObject;
            listObj[ListObj_StateText] = stateText;
            listObj[ListObj_InitialPanelSize] = panel.sizeDelta;
            listObj[ListObj_InitialParentSize] = elemsParent.sizeDelta;
            listObj[ListObj_Panel] = panel;
            listObj[ListObj_Parent] = elemsParent;

            return listObj;
        }

        private object[] listObj;
        private GameObject elemGameObj;
        private object[] listElemObj;
        private int elemIndex;

        private void UpdateList(
            object[] listObj,
            bool isBackingDataNull,
            int backingDataCount,
            // (GameObject elemGameObj) => object[] listElemObj;
            string createListObjElemEventName,
            // (object[] listObj, object[] listElemObj, int elemIndex) => void;
            string updateListObjElemEventName)
        {
            int elemCount = (int)listObj[ListObj_ElemCount];
            object[][] elems = (object[][])listObj[ListObj_Elems];

            if (isBackingDataNull)
            {
                for (int i = 0; i < elemCount; i++)
                    ((GameObject)elems[i][ListObjElem_GameObj]).SetActive(false);
                ((TextMeshProUGUI)listObj[ListObj_StateText]).text = "null";
                return;
            }

            ((TextMeshProUGUI)listObj[ListObj_StateText]).text = backingDataCount.ToString();

            float elemHeight = (float)listObj[ListObj_ElemHeight];

            Vector2 panelSize = (Vector2)listObj[ListObj_InitialPanelSize];
            panelSize.y += backingDataCount * elemHeight;
            ((RectTransform)listObj[ListObj_Panel]).sizeDelta = panelSize;

            Vector2 parentSize = (Vector2)listObj[ListObj_InitialParentSize];
            parentSize.y += backingDataCount * elemHeight;
            ((RectTransform)listObj[ListObj_Parent]).sizeDelta = parentSize;

            for (int i = elemCount; i < backingDataCount; i++)
            {
                elemGameObj = Instantiate(
                    (GameObject)listObj[ListObj_ElemPrefab],
                    (RectTransform)listObj[ListObj_Parent]
                );
                SendCustomEvent(createListObjElemEventName);
                ArrList.Add(ref elems, ref elemCount, listElemObj);
            }
            listObj[ListObj_ElemCount] = elemCount;
            listObj[ListObj_Elems] = elems;

            this.listObj = listObj;
            for (int i = 0; i < elemCount; i++)
            {
                if (i >= backingDataCount)
                {
                    ((GameObject)elems[i][ListObjElem_GameObj]).SetActive(false);
                    continue;
                }
                ((GameObject)elems[i][ListObjElem_GameObj]).SetActive(true);

                listElemObj = elems[i];
                elemIndex = i;
                SendCustomEvent(updateListObjElemEventName);
            }
        }
    }
}
