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
        [SerializeField] [HideInInspector] private LockStep lockStep;

        public Transform flagsParent;
        private string[] flagFieldNames = new string[]
        {
            "isTickPaused",
            "isMaster",
            "ignoreLocalInputActions",
            "stillAllowLocalClientJoinedIA",
            "ignoreIncomingInputActions",
            "isWaitingToSendClientJoinedIA",
            "sendLateJoinerDataAtEndOfTick",
            "isCatchingUp",
            "isSinglePlayer",
            "currentlyNoMaster",
            "checkMasterChangeAfterProcessingLJGameStates",
        };
        private Toggle[] flagToggles;
        private TextMeshProUGUI[] flagLabels;

        private const float MinMaxTimeFrame = 5f;
        private int lastFullSecond = int.MinValue;

        public TextMeshProUGUI lockStepPerformanceText;
        private const string lockStepLastUpdateSWFieldName = "lastUpdateSW";
        private double averageUpdateMS;
        private double minUpdateMS = double.MaxValue;
        private double maxUpdateMS = double.MinValue;
        private string formattedMaxAndMax;

        public TextMeshProUGUI debugUIPerformanceText;
        private System.Diagnostics.Stopwatch debugLastUpdateSW = new System.Diagnostics.Stopwatch();
        private double debugAverageUpdateMS;
        private double debugMinUpdateMS = double.MaxValue;
        private double debugMaxUpdateMS = double.MinValue;
        private string debugFormattedMaxAndMax;

        public Transform numbersParent;
        private string[] numbersFieldNames = new string[]
        {
            "currentTick",
            "waitTick",
            "firstMutableTick",
            "startTick",
            "tickStartTime",
            "syncCountForLatestLJSync",
            "unprocessedLJSerializedGSCount",
            "nextLJGameStateToProcess",
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
            "CatchingUp",
            "Normal",
        };

        public RectTransform leftClientsParent;
        public TextMeshProUGUI leftClientsCountText;
        public float leftClientsElemHeight;
        private object[] leftClientsListObj;
        private uint[] leftClients;
        private const string LeftClientsFieldName = "leftClients";
        private const string LeftClientsCountFieldName = "leftClientsCount";

        public RectTransform inputActionsByUniqueIdParent;
        public TextMeshProUGUI inputActionsByUniqueIdCountText;
        public float inputActionsByUniqueIdElemHeight;
        private object[] inputActionsByUniqueIdListObj;
        private DataDictionary inputActionsByUniqueId;
        private DataList inputActionsByUniqueIdKeys;
        private string[] inputActionHandlerEventNames;
        private const string InputActionsByUniqueIdsFieldName = "inputActionsByUniqueId";
        private const string InputActionHandlerEventNamesFieldName = "inputActionHandlerEventNames";

        public RectTransform uniqueIdsByTickParent;
        public TextMeshProUGUI uniqueIdsByTickCountText;
        public float uniqueIdsByTickElemHeight;
        private object[] uniqueIdsByTickListObj;
        private DataDictionary uniqueIdsByTick;
        private uint[][] uniqueIdsByTickUnrolled = new uint[ArrList.MinCapacity][];
        ///cSpell:ignore uibtu
        private int uibtuCount = 0;
        private const string UniqueIdsByTickFieldName = "uniqueIdsByTick";

        // NOTE: Probably also list of registered input actions, but that's for later
        // NOTE: Probably also some visualization of game states, but that's for later

        void Start()
        {
            long freq = System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"[LockStepDebug] StopWatch IsHighResolution: {System.Diagnostics.Stopwatch.IsHighResolution}, "
                + $"ticks per second: {freq}, "
                + $"nano seconds per tick: {1_000_000_000L / freq}.");

            InitializeFlags();
            InitializeNumbers();
            InitializeClientStates();
            InitializeLeftClients();
            InitializeInputActionsByUniqueIds();
            InitializeUniqueIdsByTick();
            Update();
        }

        void Update()
        {
            debugLastUpdateSW.Reset();
            debugLastUpdateSW.Start();
            UpdateFlags();
            UpdatePerformance();
            UpdateNumbers();
            UpdateClientStates();
            UpdateLeftClients();
            UpdateInputActionsByUniqueIds();
            UpdateUniqueIdsByTick();
            debugLastUpdateSW.Stop();
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
            System.Diagnostics.Stopwatch lastUpdateSW
                = (System.Diagnostics.Stopwatch)lockStep.GetProgramVariable(lockStepLastUpdateSWFieldName);
            double lastUpdateMS = lastUpdateSW.Elapsed.TotalMilliseconds;
            double debugLastUpdateMS = debugLastUpdateSW.Elapsed.TotalMilliseconds;

            maxUpdateMS = System.Math.Max(maxUpdateMS, lastUpdateMS);
            minUpdateMS = System.Math.Min(minUpdateMS, lastUpdateMS);
            debugMaxUpdateMS = System.Math.Max(debugMaxUpdateMS, debugLastUpdateMS);
            debugMinUpdateMS = System.Math.Min(debugMinUpdateMS, debugLastUpdateMS);

            int currentFullSecond = (int)(Time.realtimeSinceStartup / MinMaxTimeFrame);
            if (currentFullSecond != lastFullSecond)
            {
                lastFullSecond = currentFullSecond;

                formattedMaxAndMax = $" | {minUpdateMS:f3} | {maxUpdateMS:f3}";
                maxUpdateMS = float.MinValue;
                minUpdateMS = float.MaxValue;

                debugFormattedMaxAndMax = $" | {debugMinUpdateMS:f3} | {debugMaxUpdateMS:f3}";
                debugMaxUpdateMS = float.MinValue;
                debugMinUpdateMS = float.MaxValue;
            }

            averageUpdateMS = averageUpdateMS * 0.9375 + lastUpdateMS * 0.0625; // 1/16
            lockStepPerformanceText.text = averageUpdateMS.ToString("f3") + formattedMaxAndMax;

            debugAverageUpdateMS = debugAverageUpdateMS * 0.9375 + debugLastUpdateMS * 0.0625; // 1/16
            debugUIPerformanceText.text = debugAverageUpdateMS.ToString("f3") + debugFormattedMaxAndMax;
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

        ///cSpell:ignore mspace

        private void UpdateNumbers()
        {
            for (int i = 0; i < numbersFieldNames.Length; i++)
                numbersValues[i].text = $"<mspace=0.55em>{lockStep.GetProgramVariable(numbersFieldNames[i])}";
        }

        private string FormatPlayerId(uint playerId)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById((int)playerId);
            return player == null ? playerId.ToString() : $"{playerId} - {player.displayName}";
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

        // (object[] listObj, object[] listElemObj, int elemIndex) => void;
        public void UpdateClientStateListElemObj()
        {
            DataToken playerIdToken = clientStatesKeys[elemIndex];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text
                = clientStateNameLut[clientStates[playerIdToken].Byte];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Label]).text
                = FormatPlayerId(playerIdToken.UInt);
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
            leftClients = (uint[])lockStep.GetProgramVariable(LeftClientsFieldName);
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
            uint playerId = leftClients[elemIndex];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text = FormatPlayerId(playerId);
        }

        private void InitializeInputActionsByUniqueIds()
        {
            inputActionsByUniqueIdListObj = NewListObj(
                null,
                inputActionsByUniqueIdElemHeight,
                inputActionsByUniqueIdParent,
                inputActionsByUniqueIdCountText
            );
        }

        private void UpdateInputActionsByUniqueIds()
        {
            if (inputActionsByUniqueId == null)
                inputActionsByUniqueId = (DataDictionary)lockStep.GetProgramVariable(InputActionsByUniqueIdsFieldName);
            inputActionHandlerEventNames = (string[])lockStep.GetProgramVariable(InputActionHandlerEventNamesFieldName);

            if (inputActionsByUniqueId != null)
                inputActionsByUniqueIdKeys = inputActionsByUniqueId.GetKeys();

            UpdateList(
                inputActionsByUniqueIdListObj,
                inputActionsByUniqueId == null,
                inputActionsByUniqueId == null ? 0 : inputActionsByUniqueId.Count,
                nameof(CreateValueLabelListElemObj),
                nameof(UpdateInputActionsByUniqueIdsElemObj)
            );
        }

        // (object[] listObj, object[] listElemObj, int elemIndex) => void;
        public void UpdateInputActionsByUniqueIdsElemObj()
        {
            DataToken uniqueIdToken = inputActionsByUniqueIdKeys[elemIndex];
            object[] iaData = (object[])inputActionsByUniqueId[uniqueIdToken].Reference;
            uint inputActionId = (uint)iaData[0];
            string inputActionEventName = inputActionHandlerEventNames[inputActionId];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text = $"<mspace=0.55em>0x{uniqueIdToken.UInt:x8}";
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Label]).text = $"<mspace=0.55em>{inputActionId}</mspace> {inputActionEventName}";
        }

        private void InitializeUniqueIdsByTick()
        {
            uniqueIdsByTickListObj = NewListObj(
                null,
                uniqueIdsByTickElemHeight,
                uniqueIdsByTickParent,
                uniqueIdsByTickCountText
            );
        }

        private void UpdateUniqueIdsByTick()
        {
            if (uniqueIdsByTick == null)
                uniqueIdsByTick = (DataDictionary)lockStep.GetProgramVariable(UniqueIdsByTickFieldName);

            if (uniqueIdsByTick != null)
            {
                ArrList.Clear(ref uniqueIdsByTickUnrolled, ref uibtuCount);
                DataList keys = uniqueIdsByTick.GetKeys();
                for (int i = 0; i < keys.Count; i ++)
                {
                    DataToken keyToken = keys[i];
                    uint tick = keyToken.UInt;
                    foreach (uint uniqueId in (uint[])uniqueIdsByTick[keyToken].Reference)
                        ArrList.Add(ref uniqueIdsByTickUnrolled, ref uibtuCount, new uint[] { tick, uniqueId });
                }
            }

            UpdateList(
                uniqueIdsByTickListObj,
                uniqueIdsByTick == null,
                uniqueIdsByTick == null ? 0 : uibtuCount,
                nameof(CreateValueLabelListElemObj),
                nameof(UpdateUniqueIdsByTickListElemObj)
            );
        }

        // (object[] listObj, object[] listElemObj, int elemIndex) => void;
        public void UpdateUniqueIdsByTickListElemObj()
        {
            uint[] pair = uniqueIdsByTickUnrolled[elemIndex];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text = pair[0].ToString();
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Label]).text = $"<mspace=0.55em>0x{pair[1]:x8}";
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
