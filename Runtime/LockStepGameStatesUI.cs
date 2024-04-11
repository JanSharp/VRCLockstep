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
    public class LockStepGameStatesUI : UdonSharpBehaviour
    {
        [SerializeField] [HideInInspector] private LockStep lockStep;

        [SerializeField] private GameObject mainGSEntryPrefab;
        [SerializeField] private GameObject importGSEntryPrefab;
        [SerializeField] private GameObject exportGSEntryPrefab;

        [SerializeField] private Transform mainGSList;
        [SerializeField] private GameObject dimBackground;

        [SerializeField] private GameObject importWindow;
        [SerializeField] private InputField serializedInputField;
        [SerializeField] private TextMeshProUGUI importInfoText;
        [SerializeField] private Transform importGSList;
        [SerializeField] private Button confirmImportButton;

        [SerializeField] private GameObject exportWindow;
        [SerializeField] private TextMeshProUGUI exportSelectedText;
        [SerializeField] private Transform exportGSList;
        [SerializeField] private InputField autosaveIntervalField;
        [SerializeField] private Slider autosaveIntervalSlider;
        [SerializeField] private InputField serializedOutputField;
        [SerializeField] private Button confirmExportButton;

        [SerializeField] [HideInInspector] private int minAutosaveInterval;
        [SerializeField] [HideInInspector] private int defaultAutosaveInterval;
        [SerializeField] [HideInInspector] private int autosaveInterval;

        [SerializeField] [HideInInspector] private LockStepMainGSEntry[] mainGSEntries;
        [SerializeField] [HideInInspector] private LockStepImportGSEntry[] importGSEntries;
        [SerializeField] [HideInInspector] private LockStepExportGSEntry[] exportGSEntries;

        private LockStepImportGSEntry[] extraImportGSEntries = new LockStepImportGSEntry[ArrList.MinCapacity];
        private int extraImportGSEntriesCount = 0;
        private int extraImportGSEntriesUsedCount = 0;

        ///<summary>string internalName => LockStepImportGSEntry entry</summary>
        private DataDictionary importEntriesByInternalName = new DataDictionary();
        ///<summary>LockStepImportedGS[]</summary>
        object[][] importedGameStates = null;

        private int selectedCount = 0;

        private void Start()
        {
            foreach (LockStepImportGSEntry entry in importGSEntries)
                importEntriesByInternalName.Add(entry.gameState.GameStateInternalName, entry);
        }

        public void OpenImportWindow()
        {
            dimBackground.SetActive(true);
            importWindow.SetActive(true);
        }

        public void OpenExportWindow()
        {
            dimBackground.SetActive(true);
            exportWindow.SetActive(true);
        }

        public void CloseImportWindow()
        {
            dimBackground.SetActive(false);
            importWindow.SetActive(false);
            ResetImport();
        }

        public void CloseExportWindow()
        {
            dimBackground.SetActive(false);
            exportWindow.SetActive(false);
        }

        public void CloseOpenWindow()
        {
            if (importWindow.activeSelf)
                CloseImportWindow();
            if (exportWindow.activeSelf)
                CloseExportWindow();
        }

        private LockStepImportGSEntry GetImportGSEntry(string internalName, string displayName)
        {
            if (importEntriesByInternalName.TryGetValue(internalName, out DataToken entryToken))
                return (LockStepImportGSEntry)entryToken.Reference;
            LockStepImportGSEntry entry;
            if (extraImportGSEntriesUsedCount < extraImportGSEntriesCount)
            {
                entry = extraImportGSEntries[extraImportGSEntriesUsedCount++];
                entry.displayNameText.text = displayName;
                entry.gameObject.SetActive(true);
                return entry;
            }
            GameObject entryObj = GameObject.Instantiate(importGSEntryPrefab);
            entryObj.transform.SetParent(importGSList, worldPositionStays: false);
            entry = entryObj.GetComponent<LockStepImportGSEntry>();
            entry.displayNameText.text = displayName;
            ArrList.Add(ref extraImportGSEntries, ref extraImportGSEntriesCount, entry);
            extraImportGSEntriesUsedCount++;
            return entry;
        }

        // Cannot use TextChanged because pasting text when testing in editor raises text changed for each
        // character in the string. It may not do that in VRChat itself, but it doing it in editor is enough
        // to use EndEdit instead. Because all that logging, decoding and crc calculating ends up lagging.
        public void OnImportSerializedTextEndEdit()
        {
            // Reset regardless, because 2 consecutive valid yet different imports could be pasted in.
            ResetImport(leaveInputFieldUntouched: true);

            string importString = serializedInputField.text;
            if (importString == "")
                return;
            importedGameStates = lockStep.ImportPreProcess(importString);
            if (importedGameStates == null)
            {
                importInfoText.text = "Malformed or invalid data.";
                return;
            }

            int canImportCount = 0;
            foreach (object[] importedGS in importedGameStates)
            {
                string errorMsg = LockStepImportedGS.GetErrorMsg(importedGS);
                LockStepImportGSEntry entry = GetImportGSEntry(
                    LockStepImportedGS.GetInternalName(importedGS),
                    LockStepImportedGS.GetDisplayName(importedGS));
                if (errorMsg == null)
                {
                    canImportCount++;
                    entry.infoLabel.text = "can import";
                    entry.toggledImage.color = entry.goodColor;
                }
                else
                {
                    entry.infoLabel.text = errorMsg;
                    entry.toggledImage.color = entry.badColor;
                }
                entry.mainToggle.isOn = true;
            }

            foreach (LockStepImportGSEntry entry in importGSEntries)
                if (!entry.mainToggle.isOn)
                    entry.infoLabel.text = "not in imported data, unchanged";

            int cannotImportCount = importedGameStates.Length - canImportCount;
            importInfoText.text = $"Can import " + (cannotImportCount == 0 ? "all " : "") + canImportCount.ToString()
                + (cannotImportCount == 0 ? "" : $", cannot import {cannotImportCount}");
            confirmImportButton.interactable = canImportCount != 0;
        }

        public void ConfirmImport()
        {
            // TODO: actually initiate import for importedGameStates
        }

        private void ResetImport(bool leaveInputFieldUntouched = false)
        {
            if (!leaveInputFieldUntouched)
            {
                // Does not raise OnImportSerializedTextEndEdit, because that's end edit, not text changed.
                serializedInputField.text = "";
            }

            foreach (LockStepImportGSEntry entry in importGSEntries)
            {
                if (!entry.gameState.GameStateSupportsImportExport)
                {
                    entry.infoLabel.text = "does not support import/export";
                    continue;
                }
                entry.infoLabel.text = "";
                entry.mainToggle.isOn = false;
            }
            for (int i = 0; i < extraImportGSEntriesUsedCount; i++)
            {
                LockStepImportGSEntry entry = extraImportGSEntries[i];
                entry.gameObject.SetActive(false);
                entry.mainToggle.isOn = false;
                entry.infoLabel.text = "";
            }
            extraImportGSEntriesUsedCount = 0;
            importInfoText.text = "";
            confirmImportButton.interactable = false;
        }

        public void ExportSelectAll()
        {
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport)
                    entry.mainToggle.isOn = true;
        }

        public void ExportSelectNone()
        {
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport)
                    entry.mainToggle.isOn = false;
        }

        public void SetAutosaveSelected()
        {
            for (int i = 0; i < exportGSEntries.Length; i++)
            {
                LockStepExportGSEntry entry = exportGSEntries[i];
                if (!entry.gameState.GameStateSupportsImportExport)
                    continue;
                bool doAutosave = entry.mainToggle.isOn;
                entry.doAutosave = doAutosave;
                entry.infoLabel.gameObject.SetActive(doAutosave);
                mainGSEntries[i].autosaveText.gameObject.SetActive(doAutosave);
            }
        }

        public void OnAutosaveIntervalFieldChanged()
        {
            if (!int.TryParse(autosaveIntervalField.text, out autosaveInterval))
                autosaveInterval = defaultAutosaveInterval;
            if (autosaveInterval < minAutosaveInterval)
            {
                autosaveInterval = minAutosaveInterval;
                // SetTextWithoutNotify is not exposed for TextMeshProUGUI, but the setter for text doesn't
                // seem to raise the changed event... Udon what is going on?
                autosaveIntervalField.text = autosaveInterval.ToString();
            }
            autosaveIntervalSlider.SetValueWithoutNotify(autosaveInterval);
        }

        public void OnAutosaveIntervalSliderChanged()
        {
            autosaveInterval = (int)autosaveIntervalSlider.value;
                // SetTextWithoutNotify is not exposed for TextMeshProUGUI, but the setter for text doesn't
                // seem to raise the changed event... Udon what is going on?
            autosaveIntervalField.text = autosaveInterval.ToString();
        }

        public void OnExportEntryToggled()
        {
            selectedCount = 0;
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport && entry.mainToggle.isOn)
                    selectedCount++;
            confirmExportButton.interactable = selectedCount != 0;
            exportSelectedText.text = $"selected: {selectedCount}";
        }

        public void ConfirmExport()
        {
            LockStepGameState[] gameStates = new LockStepGameState[selectedCount];
            int i = 0;
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport && entry.mainToggle.isOn)
                    gameStates[i++] = entry.gameState;
            serializedOutputField.text = lockStep.Export(gameStates);
        }
    }
}
