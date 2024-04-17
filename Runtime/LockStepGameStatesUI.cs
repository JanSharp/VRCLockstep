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
        [SerializeField] private TextMeshProUGUI importSelectedText;
        [SerializeField] private InputField serializedInputField;
        [SerializeField] private TextMeshProUGUI importInfoText;
        [SerializeField] private Transform importGSList;
        [SerializeField] private Button confirmImportButton;
        [SerializeField] private TextMeshProUGUI confirmImportButtonText;

        [SerializeField] private GameObject exportWindow;
        [SerializeField] private TextMeshProUGUI exportSelectedText;
        [SerializeField] private Transform exportGSList;
        [SerializeField] private InputField autosaveIntervalField;
        [SerializeField] private Slider autosaveIntervalSlider;
        [SerializeField] private InputField serializedOutputField;
        [SerializeField] private InputField exportNameField;
        [SerializeField] private Button confirmExportButton;
        [SerializeField] private TextMeshProUGUI confirmExportButtonText;

        [SerializeField] [HideInInspector] private int minAutosaveInterval;
        [SerializeField] [HideInInspector] private int defaultAutosaveInterval;
        [SerializeField] [HideInInspector] private int autosaveInterval;

        [SerializeField] [HideInInspector] private LockStepMainGSEntry[] mainGSEntries;
        [SerializeField] [HideInInspector] private LockStepImportGSEntry[] importGSEntries;
        [SerializeField] [HideInInspector] private LockStepExportGSEntry[] exportGSEntries;

        private bool isImportInitialized = false;
        private bool isExportInitialized = false;

        private LockStepImportGSEntry[] extraImportGSEntries = new LockStepImportGSEntry[ArrList.MinCapacity];
        private int extraImportGSEntriesCount = 0;
        private int extraImportGSEntriesUsedCount = 0;

        ///<summary>string internalName => LockStepImportGSEntry entry</summary>
        private DataDictionary importEntriesByInternalName = new DataDictionary();
        ///<summary>LockStepImportedGS[]</summary>
        object[][] importedGameStates = null;
        System.DateTime exportDate;
        string exportName;

        private int importSelectedCount = 0;
        [SerializeField] [HideInInspector] private int exportSelectedCount;

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
            ResetExport();
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
            entry.gameStatesUI = this;
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
            importedGameStates = lockStep.ImportPreProcess(
                importString,
                out exportDate,
                out exportName);
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
                    entry.mainToggle.interactable = true;
                    entry.canImport = true;
                }
                else
                {
                    entry.infoLabel.text = errorMsg;
                    entry.toggledImage.color = entry.badColor;
                }
                entry.importedGS = importedGS;
                entry.mainToggle.SetIsOnWithoutNotify(true);
            }

            foreach (LockStepImportGSEntry entry in importGSEntries)
                if (!entry.mainToggle.isOn)
                    entry.infoLabel.text = "not in imported data, unchanged";

            int cannotImportCount = importedGameStates.Length - canImportCount;
            importInfoText.text = $"Can import {(cannotImportCount == 0 ? "all " : "")}{canImportCount}"
                + (cannotImportCount == 0 ? "" : $", cannot import {cannotImportCount}")
                + $"\n<size=90%>{exportName ?? "<i>unnamed</i>"} "
                + $"<nobr><size=70%>(from {exportDate.ToLocalTime():yyyy-MM-dd HH:mm})</nobr>";
            importSelectedCount = canImportCount;
            UpdateImportSelectedCount();
        }

        public void ImportSelectAll()
        {
            importSelectedCount = 0;
            foreach (LockStepImportGSEntry entry in importGSEntries)
                if (entry.canImport)
                {
                    entry.mainToggle.SetIsOnWithoutNotify(true);
                    importSelectedCount++;
                }
            UpdateImportSelectedCount();
        }

        public void ImportSelectNone()
        {
            foreach (LockStepImportGSEntry entry in importGSEntries)
                if (entry.canImport)
                    entry.mainToggle.SetIsOnWithoutNotify(false);
            importSelectedCount = 0;
            UpdateImportSelectedCount();
        }

        public void OnImportEntryToggled()
        {
            importSelectedCount = 0;
            foreach (LockStepImportGSEntry entry in importGSEntries)
                if (entry.canImport && entry.mainToggle.isOn)
                    importSelectedCount++;
            UpdateImportSelectedCount();
        }

        private void UpdateImportSelectedCount()
        {
            UpdateImportButton();
            importSelectedText.text = $"selected: {importSelectedCount}";
        }

        private bool CanImport() => isImportInitialized && importSelectedCount != 0 && !lockStep.IsImporting;

        private void UpdateImportButton()
        {
            confirmImportButton.interactable = CanImport();
            confirmImportButtonText.text = !isImportInitialized ? "Loading..."
                : lockStep.IsImporting ? "Importing..."
                : "Import";
        }

        public void ConfirmImport()
        {
            if (!CanImport())
            {
                Debug.LogError("[LockStep] Through means meant to be impossible the import button has been "
                    + "pressed when it cannot actually import. Someone messed with something.");
                return;
            }
            object[][] gameStatesToImport = new object[importSelectedCount][];
            int i = 0;
            foreach (LockStepImportGSEntry entry in importGSEntries)
                if (entry.canImport && entry.mainToggle.isOn)
                    gameStatesToImport[i++] = entry.importedGS;
            lockStep.StartImport(exportDate, exportName, gameStatesToImport);
            CloseImportWindow();
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
                entry.mainToggle.SetIsOnWithoutNotify(false);
                entry.mainToggle.interactable = false;
                entry.canImport = false;
                entry.importedGS = null;
            }
            for (int i = 0; i < extraImportGSEntriesUsedCount; i++)
            {
                LockStepImportGSEntry entry = extraImportGSEntries[i];
                entry.gameObject.SetActive(false);
                entry.mainToggle.SetIsOnWithoutNotify(false);
                entry.infoLabel.text = "";
            }
            extraImportGSEntriesUsedCount = 0;
            importSelectedCount = 0;
            importSelectedText.text = "";
            importInfoText.text = "";
            UpdateImportButton();
        }

        public void ExportSelectAll()
        {
            exportSelectedCount = 0;
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport)
                {
                    entry.mainToggle.SetIsOnWithoutNotify(true);
                    exportSelectedCount++;
                }
            UpdateExportSelectedCount();
        }

        public void ExportSelectNone()
        {
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport)
                    entry.mainToggle.SetIsOnWithoutNotify(false);
            exportSelectedCount = 0;
            UpdateExportSelectedCount();
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
            exportSelectedCount = 0;
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport && entry.mainToggle.isOn)
                    exportSelectedCount++;
            UpdateExportSelectedCount();
        }

        private void UpdateExportSelectedCount()
        {
            UpdateExportButton();
            exportSelectedText.text = $"selected: {exportSelectedCount}";
            ResetExport();
        }

        private bool CanExport() => isExportInitialized && exportSelectedCount != 0 && !lockStep.IsImporting;

        private void UpdateExportButton()
        {
            confirmExportButton.interactable = CanExport();
            confirmExportButtonText.text = !isExportInitialized ? "Loading..."
                : lockStep.IsImporting ? "Importing..."
                : "Export";
        }

        public void ConfirmExport()
        {
            if (!CanExport())
            {
                Debug.LogError("[LockStep] Through means meant to be impossible the export button has been "
                    + "pressed when it cannot actually export. Someone messed with something.");
                return;
            }
            LockStepGameState[] gameStates = new LockStepGameState[exportSelectedCount];
            int i = 0;
            foreach (LockStepExportGSEntry entry in exportGSEntries)
                if (entry.gameState.GameStateSupportsImportExport && entry.mainToggle.isOn)
                    gameStates[i++] = entry.gameState;
            string exportName = exportNameField.text.Trim();
            if (exportName == "")
                exportName = null;
            serializedOutputField.text = lockStep.Export(gameStates, exportName);
        }

        private void ResetExport()
        {
            // Just clear the output field, leaving the selected state of all the export entries unchanged.
            // This way closing and reopening the window doesn't actually change anything, however it does
            // clear the output field as to not confuse the user what that data that's still in the output
            // field actually contains.
            // Also reset the output field whenever the selected state changes, for the same reason to not
            // confuse the user.
            serializedOutputField.text = "";
        }

        private void OnInitialized()
        {
            isImportInitialized = true;
            isExportInitialized = true;
            UpdateImportButton();
            UpdateExportButton();
        }

        [LockStepEvent(LockStepEventType.OnInit)]
        public void OnInit() => OnInitialized();

        [LockStepEvent(LockStepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp() => OnInitialized();

        [LockStepEvent(LockStepEventType.OnImportStart)]
        public void OnImportStart()
        {
            UpdateImportButton();
            UpdateExportButton();
        }

        [LockStepEvent(LockStepEventType.OnImportFinished)]
        public void OnImportFinished()
        {
            UpdateImportButton();
            UpdateExportButton();
        }

        // TODO: show import state in game states UI
    }
}
