using UdonSharp;
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
        [SerializeField] [HideInInspector] private LockstepAPI lockstep;
        public LockstepMasterPreference masterPreference;
        private bool MasterPreferenceExists => masterPreference != null;
        private const string LocalMasterPreferenceText = "Your Master Preference: ";
        private const string EntryMasterPreferenceText = "Master Preference: ";

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
        [SerializeField] private TextMeshProUGUI lockstepMasterText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject lockstepMasterNoValueObj;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI vrcMasterText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private GameObject vrcMasterNoValueObj;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private TextMeshProUGUI localClientStateText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        [SerializeField] private Button becomeMasterButton;
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

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;
        private bool isSetup;

        private uint desiredNewMasterPlayerId;
        private bool isChangingMaster = false;
        private bool IsChangingMaster
        {
            get => isChangingMaster;
            set
            {
                if (isChangingMaster == value)
                    return;
                isChangingMaster = value;
                UpdateBecomeAndMakeMasterButtons();
            }
        }
        private ClientState localClientState = ClientState.None;
        private ClientState LocalClientState
        {
            get => localClientState;
            set
            {
                if (localClientState == value)
                    return;
                localClientState = value;
                localClientStateText.text = lockstep.ClientStateToString(localClientState);
                UpdateBecomeAndMakeMasterButtons();
            }
        }

        /// <summary>
        /// <para><see cref="uint"/> clientId => <see cref="LockstepClientStateEntry"/> entry</para>
        /// </summary>
        private DataDictionary clientStateEntries = new DataDictionary();
        private LockstepClientStateEntry[] unusedClientStateEntries = new LockstepClientStateEntry[ArrList.MinCapacity];
        private int unusedClientStateEntriesCount = 0;

        private bool isCheckVRCMasterLoopRunning = false;
        private VRCPlayerApi currentVRCMaster = null;
        private bool restartMasterPlayerSearch = true;
        private VRCPlayerApi[] allPlayers = new VRCPlayerApi[82];
        private int allPlayersCount = 0;
        private int currentPlayerIndex = 0;
        private const float CheckVRCMasterLoopFrequency = 5f;
        private const float CheckVRCMasterLoopDelay = 120f;
        private float nextPlayerSearchStartTime = -1f;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            if (MasterPreferenceExists)
                localMasterPreferenceObject.SetActive(true);

            if (isCheckVRCMasterLoopRunning)
                return;
            isCheckVRCMasterLoopRunning = true;
            CheckVRCMasterLoop();
        }

        public void CheckVRCMasterLoop()
        {
            if (restartMasterPlayerSearch)
            {
                if (Time.time < nextPlayerSearchStartTime)
                {
                    SendCustomEventDelayedSeconds(nameof(CheckVRCMasterLoop), CheckVRCMasterLoopFrequency);
                    return;
                }
                restartMasterPlayerSearch = false;
                VRCPlayerApi.GetPlayers(allPlayers);
                allPlayersCount = VRCPlayerApi.GetPlayerCount();
                currentPlayerIndex = 0;
                SendCustomEventDelayedFrames(nameof(CheckVRCMasterLoop), 1);
                return;
            }
            if (currentPlayerIndex >= allPlayersCount)
            {
                SetVRCMaster(null);
                restartMasterPlayerSearch = true;
                nextPlayerSearchStartTime = Time.time + CheckVRCMasterLoopDelay;
                SendCustomEventDelayedSeconds(nameof(CheckVRCMasterLoop), CheckVRCMasterLoopFrequency);
                return;
            }
            VRCPlayerApi currentPlayer = allPlayers[currentPlayerIndex++];
            if (currentPlayer == null || !currentPlayer.IsValid() || !currentPlayer.isMaster)
            {
                SendCustomEventDelayedFrames(nameof(CheckVRCMasterLoop), 1);
                return;
            }
            SetVRCMaster(currentPlayer);
            restartMasterPlayerSearch = true;
            nextPlayerSearchStartTime = Time.time + CheckVRCMasterLoopDelay;
            SendCustomEventDelayedSeconds(nameof(CheckVRCMasterLoop), CheckVRCMasterLoopFrequency);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player != currentVRCMaster)
                return;
            // Delay by 1 second because we cannot trust that 'isMaster' is even true on any player at this
            // current point in time, or if the leaving player is still in the list and would still return
            // 'isMaster' less than 1 second later from now. We can't trust anything so just wait another
            // second to increase our chances of finding the new master.
            SendCustomEventDelayedSeconds(nameof(RestartPlayerSearchDelayed), 1f);
        }

        public void RestartPlayerSearchDelayed()
        {
            restartMasterPlayerSearch = true;
            nextPlayerSearchStartTime = -1f;
        }

        private void SetVRCMaster(VRCPlayerApi newMaster)
        {
            currentVRCMaster = newMaster;
            vrcMasterText.text = newMaster != null ? newMaster.displayName : "";
            vrcMasterNoValueObj.SetActive(newMaster == null);
        }

        public void UpdateLockstepMaster()
        {
            string masterName = lockstep.GetDisplayName(lockstep.MasterPlayerId);
            lockstepMasterText.text = masterName ?? "";
            lockstepMasterNoValueObj.SetActive(masterName == null);
        }

        public void UpdateClientCount()
        {
            clientCountText.text = lockstep.ClientStatesCount.ToString();
        }

        public void OnBecomeMasterClick()
        {
            SetDesiredMasterPlayerId(localPlayerId);
        }

        public void OnMakeMasterClick(LockstepClientStateEntry entry)
        {
            SetDesiredMasterPlayerId(entry.playerId);
        }

        public void OnLocalPreferenceSliderValueChanged()
        {
            if (!MasterPreferenceExists)
                return;
            int preference = (int)localMasterPreferenceSlider.value;
            localMasterPreferenceText.text = LocalMasterPreferenceText + preference.ToString();
            if (!isSetup)
                return;
            masterPreference.SetPreference(localPlayerId, preference);
            LockstepClientStateEntry entry = (LockstepClientStateEntry)clientStateEntries[localPlayerId].Reference;
            UpdatePreferenceUIForEntry(entry, preference);
        }

        public void OnPreferenceSliderValueChanged(LockstepClientStateEntry entry)
        {
            if (!MasterPreferenceExists)
                return;
            int preference = (int)entry.masterPreferenceSlider.value;
            entry.masterPreferenceText.text = EntryMasterPreferenceText + preference.ToString();
            masterPreference.SetPreference(entry.playerId, preference);
            if (entry.playerId == localPlayerId)
                UpdateLocalPreferenceUI(preference);
        }

        [LockstepMasterPreferenceEvent(LockstepMasterPreferenceEventType.OnLatencyHiddenMasterPreferenceChanged)]
        public void OnLatencyHiddenMasterPreferenceChanged()
        {
            if (!clientStateEntries.TryGetValue(masterPreference.ChangedPlayerId, out DataToken entryToken))
                return;
            int preference = masterPreference.GetLatencyHiddenPreference(masterPreference.ChangedPlayerId);
            LockstepClientStateEntry entry = (LockstepClientStateEntry)entryToken.Reference;
            UpdatePreferenceUIForEntry(entry, preference);
            if (entry.playerId == localPlayerId)
                UpdateLocalPreferenceUI(preference);
        }

        private void UpdatePreferenceUIForEntry(LockstepClientStateEntry entry, int preference)
        {
            entry.masterPreferenceSlider.SetValueWithoutNotify(preference);
            entry.masterPreferenceText.text = EntryMasterPreferenceText + preference.ToString();
        }

        private void UpdateLocalPreferenceUI(int preference)
        {
            localMasterPreferenceSlider.SetValueWithoutNotify(preference);
            localMasterPreferenceText.text = LocalMasterPreferenceText + preference.ToString();
        }

        private void SetDesiredMasterPlayerId(uint playerId)
        {
            desiredNewMasterPlayerId = playerId;
            if (IsChangingMaster)
                return;
            IsChangingMaster = lockstep.SendMasterChangeRequestIA(desiredNewMasterPlayerId);
        }

        private void UpdateBecomeAndMakeMasterButtons()
        {
            becomeMasterButton.interactable = !IsChangingMaster
                && LocalClientState == ClientState.Normal
                && lockstep.CanSendInputActions;

            DataList entries = clientStateEntries.GetValues();
            int count = entries.Count;
            for (int i = 0; i < count; i++)
                UpdateMakeMasterButton((LockstepClientStateEntry)entries[i].Reference);
        }

        private void UpdateMakeMasterButton(LockstepClientStateEntry entry)
        {
            entry.makeMasterButton.interactable = !IsChangingMaster
                && lockstep.GetClientState(entry.playerId) == ClientState.Normal;
            // Does not need the lockstep.CanSendInputActions check because entries won't exist yet while that
            // is still false.
        }

        private void Setup()
        {
            if (MasterPreferenceExists)
            {
                int preference = (int)localMasterPreferenceSlider.value;
                if (preference != 0) // Checking if the User hasn't already touched the slider - before Setup.
                    masterPreference.SetPreference(localPlayerId, preference);
                else
                    UpdateLocalPreferenceUI(masterPreference.GetLatencyHiddenPreference(localPlayerId));
            }

            foreach (uint clientId in lockstep.AllClientPlayerIds)
                AddClient(clientId);
            UpdateLockstepMaster();
            UpdateBecomeAndMakeMasterButtons();

            isSetup = true;
        }

        [LockstepEvent(LockstepEventType.OnInit)]
        public void OnInit()
        {
            Setup();
        }

        [LockstepEvent(LockstepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp()
        {
            Setup();
        }

        [LockstepEvent(LockstepEventType.OnPreClientJoined)]
        public void OnPreClientJoined()
        {
            AddClient(lockstep.JoinedPlayerId);
        }

        [LockstepEvent(LockstepEventType.OnClientJoined)]
        public void OnClientJoined()
        {
            uint joinedPlayerId = lockstep.JoinedPlayerId;
            if (clientStateEntries.ContainsKey(joinedPlayerId))
                UpdateClientState(joinedPlayerId);
            else
                AddClient(joinedPlayerId);
        }

        [LockstepEvent(LockstepEventType.OnClientCaughtUp)]
        public void OnClientCaughtUp()
        {
            UpdateClientState(lockstep.CatchingUpPlayerId);
        }

        [LockstepEvent(LockstepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
            RemoveClient(lockstep.LeftPlayerId);
        }

        [LockstepEvent(LockstepEventType.OnMasterClientChanged)]
        public void OnMasterClientChanged()
        {
            UpdateClientState(lockstep.OldMasterPlayerId);
            UpdateClientState(lockstep.MasterPlayerId);
            UpdateLockstepMaster();
            if (!IsChangingMaster)
                return;
            if (lockstep.MasterPlayerId == desiredNewMasterPlayerId)
            {
                IsChangingMaster = false;
                return;
            }
            lockstep.SendMasterChangeRequestIA(desiredNewMasterPlayerId); // Try again.
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
            LockstepClientStateEntry entry;
            if (unusedClientStateEntriesCount != 0)
            {
                entry = ArrList.RemoveAt(
                    ref unusedClientStateEntries,
                    ref unusedClientStateEntriesCount,
                    unusedClientStateEntriesCount - 1);
                entry.transform.SetAsLastSibling();
                entry.gameObject.SetActive(true);
                return entry;
            }
            GameObject entryGo = Instantiate(clientStateEntryPrefab);
            entryGo.transform.SetParent(clientStatesContent, worldPositionStays: false);
            entry = entryGo.GetComponent<LockstepClientStateEntry>();
            entry.infoUI = this;
            if (MasterPreferenceExists)
            {
                entry.masterPreferenceText.gameObject.SetActive(true);
                entry.masterPreferenceSlider.gameObject.SetActive(true);
            }
            return entry;
        }

        public void AddClient(uint clientId)
        {
            ClientState clientState = lockstep.GetClientState(clientId);
            if (clientId == localPlayerId)
                LocalClientState = clientState;
            LockstepClientStateEntry entry = GetOrCreateEntry();
            clientStateEntries.Add(clientId, entry);
            entry.playerId = clientId;
            entry.clientDisplayNameText.text = lockstep.GetDisplayName(clientId);
            entry.clientStateText.text = lockstep.ClientStateToString(clientState);
            UpdateMakeMasterButton(entry);
            UpdateClientCount();
            if (MasterPreferenceExists)
                UpdatePreferenceUIForEntry(entry, masterPreference.GetLatencyHiddenPreference(clientId));
        }

        public void RemoveClient(uint clientId)
        {
            clientStateEntries.Remove(clientId, out DataToken entryToken);
            DisableClientEntry((LockstepClientStateEntry)entryToken.Reference);
            UpdateClientCount();
        }

        private void DisableClientEntry(LockstepClientStateEntry entry)
        {
            entry.gameObject.SetActive(false);
            entry.transform.SetAsLastSibling();
            ArrList.Add(ref unusedClientStateEntries, ref unusedClientStateEntriesCount, entry);
        }

        public void UpdateClientState(uint clientId)
        {
            ClientState clientState = lockstep.GetClientState(clientId);
            if (clientId == localPlayerId)
                LocalClientState = clientState;
            LockstepClientStateEntry entry = (LockstepClientStateEntry)clientStateEntries[clientId].Reference;
            entry.clientStateText.text = lockstep.ClientStateToString(clientState);
            UpdateMakeMasterButton(entry);
        }

        [LockstepEvent(LockstepEventType.OnLockstepNotification)]
        public void OnLockstepNotification()
        {
            SendNotification(lockstep.NotificationMessage);
        }

        private void SendNotification(string message)
        {
            GameObject entryGo = Instantiate(notificationEntryPrefab);
            entryGo.transform.SetParent(notificationLogContent, worldPositionStays: false);
            entryGo.transform.SetAsFirstSibling();
            entryGo.GetComponentInChildren<TextMeshProUGUI>().text = message;
        }
    }
}
