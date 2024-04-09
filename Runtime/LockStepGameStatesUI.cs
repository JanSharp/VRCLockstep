using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

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

        private int selectedCount = 0;

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

        public void OnImportSerializedTextChanged()
        {

        }

        public void ConfirmImport()
        {

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
