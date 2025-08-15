using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

///cSpell:ignore Raycasts

namespace JanSharp.Internal
{
#if !LOCKSTEP_DEBUG
    [AddComponentMenu("")]
#endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepInfoUI : UdonSharpBehaviour
    {
        [SerializeField][HideInInspector][SingletonReference] private LockstepAPI lockstep;
        [SerializeField][HideInInspector][SingletonReference(Optional = true)] private LockstepMasterPreference masterPreference;
        private bool MasterPreferenceExists => masterPreference != null;
        private const string LocalMasterPreferenceText = "Your Master Preference: ";
        private const string EntryMasterPreferenceText = "Master Preference: ";

#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private GameObject notificationEntryPrefab;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private GameObject clientStateEntryPrefab;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI lockstepMasterText;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private GameObject lockstepMasterNoValueObj;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI vrcMasterText;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private GameObject vrcMasterNoValueObj;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI localClientStateText;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private Button becomeMasterButton;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI clientCountText;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private GameObject localMasterPreferenceObject;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private TextMeshProUGUI localMasterPreferenceText;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private Slider localMasterPreferenceSlider;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private Toggle notificationLogTabToggle;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private CanvasGroup notificationLogCanvasGroup;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private Transform notificationLogContent;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private ScrollRect notificationLogScrollRect;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private Toggle clientStatesTabToggle;
#if !LOCKSTEP_DEBUG
        [HideInInspector]
#endif
        [SerializeField] private CanvasGroup clientStatesCanvasGroup;
#if !LOCKSTEP_DEBUG
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
        private string[] displayNames = new string[ArrList.MinCapacity];
        private int displayNamesCount = 0;
        private LockstepClientStateEntry[] unusedClientStateEntries = new LockstepClientStateEntry[ArrList.MinCapacity];
        private int unusedClientStateEntriesCount = 0;

        private bool isCheckVRCMasterLoopRunning = false;
        private const float CheckVRCMasterLoopFrequency = 127f;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            if (MasterPreferenceExists)
                localMasterPreferenceObject.SetActive(true);
            SetVRCMaster(Networking.Master);

            // We probably won't get the OnMasterTransferred event when this script is disabled in hierarchy,
            // so continuously update the VRChat master.
            if (isCheckVRCMasterLoopRunning)
                return;
            isCheckVRCMasterLoopRunning = true;
            SendCustomEventDelayedSeconds(nameof(CheckVRCMasterLoop), CheckVRCMasterLoopFrequency);
        }

        public override void OnMasterTransferred(VRCPlayerApi newMaster)
        {
            SetVRCMaster(newMaster);
        }

        public void CheckVRCMasterLoop()
        {
            SetVRCMaster(Networking.Master);
            SendCustomEventDelayedSeconds(nameof(CheckVRCMasterLoop), CheckVRCMasterLoopFrequency);
        }

        private void SetVRCMaster(VRCPlayerApi newMaster)
        {
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
            LockstepClientStateEntry entry = (LockstepClientStateEntry)clientStateEntries[localPlayerId].Reference;
            UpdatePreferenceUIForEntry(entry, preference);
            entry.WaitBeforeApplyingPreferenceChange();
        }

        public void OnPreferenceSliderValueChanged(LockstepClientStateEntry entry)
        {
            if (!MasterPreferenceExists)
                return;
            int preference = (int)entry.masterPreferenceSlider.value;
            entry.masterPreferenceText.text = EntryMasterPreferenceText + preference.ToString();
            if (entry.playerId == localPlayerId)
                UpdateLocalPreferenceUI(preference);
            entry.WaitBeforeApplyingPreferenceChange();
        }

        public void ApplyMasterPreferenceChange(LockstepClientStateEntry entry)
        {
            int preference = (int)entry.masterPreferenceSlider.value;
            masterPreference.SetPreference(entry.playerId, preference);
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
            if (!MasterPreferenceExists)
                return;
            int highestPreference = masterPreference.GetHighestLatencyHiddenPreference();
            int preference = masterPreference.GetLatencyHiddenPreference(playerId);
            if (preference < highestPreference)
                masterPreference.SetPreference(playerId, highestPreference);
        }

        private void UpdateBecomeAndMakeMasterButtons()
        {
            becomeMasterButton.interactable = !IsChangingMaster
                && LocalClientState == ClientState.Normal
                && lockstep.IsInitialized;

            DataList entries = clientStateEntries.GetValues();
            int count = entries.Count;
            for (int i = 0; i < count; i++)
                UpdateMakeMasterButton((LockstepClientStateEntry)entries[i].Reference);
        }

        private void UpdateMakeMasterButton(LockstepClientStateEntry entry)
        {
            entry.makeMasterButton.interactable = !IsChangingMaster
                && lockstep.GetClientState(entry.playerId) == ClientState.Normal;
            // Does not need the lockstep.IsInitialized check because entries won't exist yet while that
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
            UpdateClientState(lockstep.JoinedPlayerId);
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
            if (lockstep.MasterPlayerId == desiredNewMasterPlayerId
                || (MasterPreferenceExists && masterPreference.GetLatencyHiddenPreference(desiredNewMasterPlayerId) < masterPreference.GetHighestLatencyHiddenPreference()))
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
#if LOCKSTEP_DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            GameObject entryGo = Instantiate(clientStateEntryPrefab);
#if LOCKSTEP_DEBUG
            Debug.Log($"[LockstepDebug] [sw] LockstepInfoUI  GetOrCreateEntry (inner) - instantiateMs: {sw.Elapsed.TotalMilliseconds}");
#endif
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
            string displayName = lockstep.GetDisplayName(clientId);
            string comparableName = displayName.ToLower().Trim();
            int index = ArrList.BinarySearch(ref displayNames, ref displayNamesCount, comparableName);
            if (index < 0)
                index = ~index;
            ArrList.Insert(ref displayNames, ref displayNamesCount, comparableName, index);
            LockstepClientStateEntry entry = GetOrCreateEntry();
            clientStateEntries.Add(clientId, entry);
            entry.transform.SetSiblingIndex(index);
            entry.playerId = clientId;
            entry.clientDisplayNameText.text = displayName;
            entry.clientStateText.text = lockstep.ClientStateToString(clientState);
            UpdateMakeMasterButton(entry);
            UpdateClientCount();
            if (MasterPreferenceExists)
                UpdatePreferenceUIForEntry(entry, masterPreference.GetLatencyHiddenPreference(clientId));
        }

        public void RemoveClient(uint clientId)
        {
            clientStateEntries.Remove(clientId, out DataToken entryToken);
            LockstepClientStateEntry entry = (LockstepClientStateEntry)entryToken.Reference;
            ArrList.RemoveAt(ref displayNames, ref displayNamesCount, entry.transform.GetSiblingIndex());
            DisableClientEntry(entry);
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
#if LOCKSTEP_DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            GameObject entryGo = Instantiate(notificationEntryPrefab);
#if LOCKSTEP_DEBUG
            Debug.Log($"[LockstepDebug] [sw] LockstepInfoUI  SendNotification (inner) - instantiateMs: {sw.Elapsed.TotalMilliseconds}");
#endif
            entryGo.transform.SetParent(notificationLogContent, worldPositionStays: false);
            entryGo.transform.SetAsFirstSibling();
            entryGo.GetComponentInChildren<TextMeshProUGUI>().text = message;
            notificationLogTabToggle.isOn = true;
        }
    }
}
