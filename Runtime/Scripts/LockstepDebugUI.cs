using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace JanSharp.Internal
{
#if !LockstepDebug
    [AddComponentMenu("")]
#endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepDebugUI : UdonSharpBehaviour
    {
        [SerializeField][HideInInspector][SingletonReference] private LockstepAPI lockstep;

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private Transform flagsParent;
        private string[] flagFieldNames = new string[]
        {
            "isTickPaused",
            "isMaster",
            "ignoreLocalInputActions",
            "stillAllowLocalClientJoinedIA",
            "ignoreIncomingInputActions",
            "isWaitingToSendClientJoinedIA",
            "sendLateJoinerDataAtStartOfTick",
            "isCatchingUp",
            "isInitialCatchUp",
            "isSinglePlayer",
            "currentlyNoMaster",
            "checkMasterChangeAfterProcessingLJGameStates",
            "lockstepIsInitialized",
            "isExporting",
            "isImporting",
            "isAskingForMasterCandidates",
            "someoneIsAskingForMasterCandidates",
            "acceptForcedCandidate",
            "masterChangeRequestInProgress",
            "sendMasterChangeConfirmationInFirstMutableTick",
            "finishMasterChangeProcessAtStartOfTick",
            "flaggedToContinueNextFrame",
            "flaggedToContinueInsideOfGSImport",
            "isContinuationFromPrevFrame",
            "suspendedInInputActionsToRunNextFrame",
            "suspendedInStandaloneIA",
            "suspendedInLJSerialization",
            "suspendedInExportPreparation",
            "suspendedInExport",
            "suspendedInImportOptionsDeserialization",
        };
        private Toggle[] flagToggles;
        private TextMeshProUGUI[] flagLabels;

        private const float MinMaxTimeFrame = 5f;
        private int lastFullSecond = int.MinValue;

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI lockstepPerformanceText;
        private const string lockstepLastUpdateSWFieldName = "lastUpdateSW";
        private double averageUpdateMS;
        private double minUpdateMS = double.MaxValue;
        private double maxUpdateMS = double.MinValue;
        private string formattedMaxAndMax;

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI debugUIPerformanceText;
        private System.Diagnostics.Stopwatch debugLastUpdateSW = new System.Diagnostics.Stopwatch();
        private double debugAverageUpdateMS;
        private double debugMinUpdateMS = double.MaxValue;
        private double debugMaxUpdateMS = double.MinValue;
        private string debugFormattedMaxAndMax;

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private Transform numbersParent;
        private string[] numbersFieldNames = new string[]
        {
            "currentTick",
            "lastRunnableTick",
            "firstMutableTick",
            "tickStartTime",
            "tickStartTimeShift",
            "byteCountForLatestLJSync",
            "unprocessedLJSerializedGSCount",
            "nextLJGameStateToProcess",
            "requestedMasterClientId",
            "unrecoverableStateDueToUniqueId",
            "clientIdAskingForCandidates",
            "acceptingCandidatesCount",
            "acceptForcedCandidateFromPlayerId",
            "suspendedInputActionId",
            "suspendedSingletonInputActionId",
            "suspendedExportGSSizePosition",
            "suspendedIndexInArray",
            "suspendedGSIndexInExport",
            "currentIncomingGSDataIndex",
        };
        private TextMeshProUGUI[] numbersValues;
        private TextMeshProUGUI[] numbersLabels;

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private RectTransform clientStatesParent;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI clientStatesCountText;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private float clientStatesElemHeight;
        private object[] clientStatesListObj;
        private uint[] allClientIds;
        private ClientState[] allClientStates;
        private string[] allClientNames;
        private int allClientStatesCount;
        private const string ClientIdsFieldName = "allClientIds";
        private const string ClientStatesFieldName = "allClientStates";
        private const string ClientNamesFieldName = "allClientNames";
        private const string ClientStatesCountFieldName = "allClientStatesCount";

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private RectTransform leftClientsParent;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI leftClientsCountText;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private float leftClientsElemHeight;
        private object[] leftClientsListObj;
        private uint[] leftClients;
        private const string LeftClientsFieldName = "leftClients";
        private const string LeftClientsCountFieldName = "leftClientsCount";

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private RectTransform inputActionsByUniqueIdParent;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI inputActionsByUniqueIdCountText;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private float inputActionsByUniqueIdElemHeight;
        private object[] inputActionsByUniqueIdListObj;
        private DataDictionary inputActionsByUniqueId;
        private DataList inputActionsByUniqueIdKeys;
        private string[] inputActionHandlerEventNames;
        private const string InputActionsByUniqueIdsFieldName = "inputActionsByUniqueId";
        private const string InputActionHandlerEventNamesFieldName = "inputActionHandlerEventNames";

#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private RectTransform uniqueIdsByTickParent;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI uniqueIdsByTickCountText;
#if !LockstepDebug
        [HideInInspector]
#endif
        [SerializeField] private float uniqueIdsByTickElemHeight;
        private object[] uniqueIdsByTickListObj;
        private DataDictionary uniqueIdsByTick;
        private ulong[][] uniqueIdsByTickUnrolled = new ulong[ArrList.MinCapacity][];
        ///cSpell:ignore uibtu
        private int uibtuCount = 0;
        private const string UniqueIdsByTickFieldName = "uniqueIdsByTick";

        // NOTE: Probably also list of registered input actions, but that's for later

        void Start()
        {
            long freq = System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"[LockstepDebug] StopWatch IsHighResolution: {System.Diagnostics.Stopwatch.IsHighResolution}, "
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
                flagToggles[i].SetIsOnWithoutNotify((bool)lockstep.GetProgramVariable(flagFieldNames[i]));
        }

        private void UpdatePerformance()
        {
            System.Diagnostics.Stopwatch lastUpdateSW
                = (System.Diagnostics.Stopwatch)lockstep.GetProgramVariable(lockstepLastUpdateSWFieldName);
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
            lockstepPerformanceText.text = averageUpdateMS.ToString("f3") + formattedMaxAndMax;

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
                numbersValues[i].text = $"<mspace=0.55em>{lockstep.GetProgramVariable(numbersFieldNames[i])}";
        }

        private string FormatPlayerId(uint playerId, string displayName = null)
        {
            if (displayName != null)
                return $"{playerId} - {displayName}";
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
            // Always fetch new because it can be reset in some special cases.
            allClientIds = (uint[])lockstep.GetProgramVariable(ClientIdsFieldName);
            allClientStates = (ClientState[])lockstep.GetProgramVariable(ClientStatesFieldName);
            allClientNames = (string[])lockstep.GetProgramVariable(ClientNamesFieldName);
            allClientStatesCount = (int)lockstep.GetProgramVariable(ClientStatesCountFieldName);

            UpdateList(
                clientStatesListObj,
                allClientStates == null,
                allClientStates == null ? 0 : allClientStatesCount,
                nameof(CreateValueLabelListElemObj),
                nameof(UpdateClientStateListElemObj)
            );
        }

        // (object[] listObj, object[] listElemObj, int elemIndex) => void;
        public void UpdateClientStateListElemObj()
        {
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text
                = lockstep.ClientStateToString(allClientStates[elemIndex]);
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Label]).text
                = FormatPlayerId(allClientIds[elemIndex], allClientNames[elemIndex]);
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
            leftClients = (uint[])lockstep.GetProgramVariable(LeftClientsFieldName);
            int count = (int)lockstep.GetProgramVariable(LeftClientsCountFieldName);

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
                inputActionsByUniqueId = (DataDictionary)lockstep.GetProgramVariable(InputActionsByUniqueIdsFieldName);
            inputActionHandlerEventNames = (string[])lockstep.GetProgramVariable(InputActionHandlerEventNamesFieldName);

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
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text = $"<mspace=0.55em>0x{uniqueIdToken.ULong:x16}";
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
                uniqueIdsByTick = (DataDictionary)lockstep.GetProgramVariable(UniqueIdsByTickFieldName);

            if (uniqueIdsByTick != null)
            {
                ArrList.Clear(ref uniqueIdsByTickUnrolled, ref uibtuCount);
                DataList keys = uniqueIdsByTick.GetKeys();
                for (int i = 0; i < keys.Count; i++)
                {
                    DataToken keyToken = keys[i];
                    uint tick = keyToken.UInt;
                    foreach (ulong uniqueId in (ulong[])uniqueIdsByTick[keyToken].Reference)
                        ArrList.Add(ref uniqueIdsByTickUnrolled, ref uibtuCount, new ulong[] { tick, uniqueId });
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
            ulong[] pair = uniqueIdsByTickUnrolled[elemIndex];
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Value]).text = pair[0].ToString();
            ((TextMeshProUGUI)listElemObj[ValueLabel_ListElemObj_Label]).text = $"<mspace=0.55em>0x{pair[1]:x16}";
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
#if LockstepDebug
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
#endif
                elemGameObj = Instantiate(
                    (GameObject)listObj[ListObj_ElemPrefab],
                    (RectTransform)listObj[ListObj_Parent]
                );
#if LockstepDebug
                Debug.Log($"[LockstepDebug] [sw] LockstepDebugUI  UpdateList (inner) - instantiateMs: {sw.Elapsed.TotalMilliseconds}");
#endif
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
