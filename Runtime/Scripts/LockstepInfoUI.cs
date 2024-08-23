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

        public void SetLockstepMaster(string masterName)
        {
            lockstepMasterText.text = masterName ?? "";
            lockstepMasterNoValueObj.SetActive(masterName == null);
        }

        public void SetLocalClientState(ClientState clientState)
        {
            localClientStateText.text = lockstep.ClientStateToString(clientState);
        }

        public void SetClientCount(int count)
        {
            clientCountText.text = count.ToString();
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
            entry.clientStateText.text = lockstep.ClientStateToString(clientState);
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
            entry.clientStateText.text = lockstep.ClientStateToString(clientState);
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
