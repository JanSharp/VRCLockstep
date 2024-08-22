﻿using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Data;
using TMPro;

///cSpell:ignore Raycasts

namespace JanSharp.Internal
{
    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepInfoUI : UdonSharpBehaviour
    {
        // Used for easier editor scripting.
        [SerializeField] [HideInInspector] private LockstepAPI lockstep;

        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject notificationEntryPrefab;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject clientStateEntryPrefab;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI localClientStateText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI clientCountText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject localMasterPreferenceObject;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI localMasterPreferenceText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Slider localMasterPreferenceSlider;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Toggle notificationLogTabToggle;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private CanvasGroup notificationLogCanvasGroup;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Transform notificationLogContent;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private ScrollRect notificationLogScrollRect;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Toggle clientStatesTabToggle;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private CanvasGroup clientStatesCanvasGroup;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Transform clientStatesContent;

        /// <summary>
        /// <para><see cref="uint"/> clientId => <see cref="LockstepClientStateEntry"/> entry</para>
        /// </summary>
        private DataDictionary clientStateEntries = new DataDictionary();
        private LockstepClientStateEntry[] unusedClientStateEntries = new LockstepClientStateEntry[ArrList.MinCapacity];
        private int unusedClientStateEntriesCount = 0;

        private string ClientStateToString(ClientState clientState)
        {
            if (clientState == ClientState.Master)
                return "Master";
            else if (clientState == ClientState.WaitingForLateJoinerSync)
                return "WaitingForLateJoinerSync";
            else if (clientState == ClientState.CatchingUp)
                return "CatchingUp";
            else if (clientState == ClientState.Normal)
                return "Normal";
            else
                return "None";
        }

        public void SetLocalClientState(ClientState clientState)
        {
            localClientStateText.text = $"Local Client State: {ClientStateToString(clientState)}";
        }

        public void SetClientCount(int count)
        {
            clientCountText.text = $"Client Count: {count}";
        }

        private void UpdateCanvasGroupVisibility(CanvasGroup canvasGroup, bool isVisible)
        {
            canvasGroup.alpha = isVisible ? 1.0f : 0.0f;
            canvasGroup.blocksRaycasts = isVisible;
        }

        public void OnTabToggleValueChanged()
        {
            UpdateCanvasGroupVisibility(notificationLogCanvasGroup, notificationLogTabToggle.isOn);
            UpdateCanvasGroupVisibility(clientStatesCanvasGroup, clientStatesTabToggle.isOn);
        }

        private LockstepClientStateEntry GetOrCreateEntry()
        {
            if (unusedClientStateEntriesCount != 0)
            {
                LockstepClientStateEntry entry = ArrList.RemoveAt(
                    ref unusedClientStateEntries,
                    ref unusedClientStateEntriesCount,
                    unusedClientStateEntriesCount - 1);
                entry.transform.SetAsLastSibling();
                entry.gameObject.SetActive(true);
                return entry;
            }
            GameObject entryGo = Instantiate(clientStateEntryPrefab);
            entryGo.transform.SetParent(clientStatesContent, worldPositionStays: false);
            return entryGo.GetComponent<LockstepClientStateEntry>();
        }

        public void AddClient(uint clientId, string displayName, ClientState clientState)
        {
            LockstepClientStateEntry entry = GetOrCreateEntry();
            clientStateEntries.Add(clientId, entry);
            entry.clientId = clientId;
            entry.clientDisplayNameText.text = displayName;
            entry.clientStateText.text = ClientStateToString(clientState);
        }

        public void RemoveClient(uint clientId)
        {
            clientStateEntries.Remove(clientId, out DataToken entryToken);
            DisableClientEntry((LockstepClientStateEntry)entryToken.Reference);
        }

        private void DisableClientEntry(LockstepClientStateEntry entry)
        {
            entry.gameObject.SetActive(false);
            entry.transform.SetAsLastSibling();
            ArrList.Add(ref unusedClientStateEntries, ref unusedClientStateEntriesCount, entry);
        }

        public void ClearClients()
        {
            DataList entryTokens = clientStateEntries.GetValues();
            int count = entryTokens.Count;
            for (int i = 0; i < count; i++)
                DisableClientEntry((LockstepClientStateEntry)entryTokens[i].Reference);
            clientStateEntries.Clear();
        }

        public void SetClientState(uint clientId , ClientState clientState)
        {
            LockstepClientStateEntry entry = (LockstepClientStateEntry)clientStateEntries[clientId].Reference;
            entry.clientStateText.text = ClientStateToString(clientState);
        }

        public void SendNotification(string message)
        {
            GameObject entryGo = Instantiate(notificationEntryPrefab);
            entryGo.transform.SetParent(notificationLogContent, worldPositionStays: false);
            entryGo.transform.SetAsFirstSibling();
            entryGo.GetComponentInChildren<TextMeshProUGUI>().text = message;
        }
    }
}
