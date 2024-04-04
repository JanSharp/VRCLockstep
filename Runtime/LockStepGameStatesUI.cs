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

        [SerializeField] [HideInInspector] private LockStepMainGSEntry[] mainGSEntries;
        [SerializeField] [HideInInspector] private LockStepImportGSEntry[] importGSEntries;
        [SerializeField] [HideInInspector] private LockStepExportGSEntry[] exportGSEntries;

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

        }

        public void ExportSelectNone()
        {

        }

        public void SetAutosaveSelected()
        {

        }

        public void OnAutosaveIntervalFieldChanged()
        {

        }

        public void OnAutosaveIntervalSliderChanged()
        {

        }

        public void ConfirmExport()
        {

        }
    }
}
