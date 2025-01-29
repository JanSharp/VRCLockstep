using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;
using VRC.SDK3.Data;

namespace JanSharp.Internal
{
    #if !LockstepDebug
    [AddComponentMenu("")]
    #endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepGameStatesUI : UdonSharpBehaviour
    {
        [SerializeField] [HideInInspector] [SingletonReference] private LockstepAPI lockstep;

        [SerializeField] private GameObject mainGSEntryPrefab;

        [SerializeField] private Transform mainGSList;
        [SerializeField] private GameObject dimBackground;

        [SerializeField] private Slider autosaveTimerSlider;
        [SerializeField] private TextMeshProUGUI autosaveTimerText;

        [SerializeField] private GameObject importWindow;
        [SerializeField] private TMP_InputField serializedInputField;
        [SerializeField] private LockstepOptionsEditorUI importOptionsUI;
        [SerializeField] private Button confirmImportButton;
        [SerializeField] private TextMeshProUGUI confirmImportButtonText;

        [SerializeField] private GameObject exportWindow;
        [SerializeField] private LockstepOptionsEditorUI exportOptionsUI;
        [SerializeField] private Toggle autosaveToggle;
        [SerializeField] private TMP_InputField autosaveIntervalField;
        [SerializeField] private Slider autosaveIntervalSlider;
        [SerializeField] private TMP_InputField exportNameField;
        [SerializeField] private Button confirmExportButton;
        [SerializeField] private TextMeshProUGUI confirmExportButtonText;

        [SerializeField] private GameObject exportedDataWindow;
        [SerializeField] private TMP_InputField serializedOutputField;

        [SerializeField] private float minAutosaveInterval;
        [SerializeField] private float defaultAutosaveInterval;
        [SerializeField] private float autosaveInterval;

        [SerializeField] private LockstepMainGSEntry[] mainGSEntries;

        private bool isImportInitialized = false;
        private bool isExportInitialized = false;

        /// <summary>
        /// <para><see cref="LockstepImportedGS"/>[]</para>
        /// </summary>
        private object[][] importedGameStates;
        private System.DateTime exportDate;
        private string exportWorldName;
        private string exportName;

        private LockstepGameStateOptionsData[] exportOptions;
        private DataDictionary importOptions;
        private bool anyImportedGSHasNoErrors;

        private VRCPlayerApi localPlayer;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            exportOptionsUI.Init();
            importOptionsUI.Init();
            exportOptions = lockstep.GetNewExportOptions();
            importOptions = lockstep.GetNewImportOptions();
        }

        private void Enable()
        {
            if (isExportInitialized)
                InstantAutosaveTimerUpdateLoop();
        }

        public void Disable()
        {
            autosaveTimerUpdateLoopCounter = 0;
            if (applyAutosaveIntervalDelayedCounter != 0)
            {
                applyAutosaveIntervalDelayedCounter = 1;
                ApplyAutosaveIntervalDelayed();
            }
        }

        public void OpenImportWindow()
        {
            if (importWindow.activeSelf)
                return;
            dimBackground.SetActive(true);
            importWindow.SetActive(true);
            ResetImport();
            importOptionsUI.Info.AddChild((LabelWidgetData)importOptionsUI.Editor.NewLabel(
                "Paste text obtained from a previous export into the text field above.").StdMove());
            importOptionsUI.Draw();
        }

        public void OpenExportWindow()
        {
            exportOptionsUI.Clear();
            lockstep.ShowExportOptionsEditor(exportOptionsUI, exportOptions);
            exportOptionsUI.Draw();
            dimBackground.SetActive(true);
            exportWindow.SetActive(true);
        }

        public void OpenExportedDataWindow()
        {
            dimBackground.SetActive(true);
            exportedDataWindow.SetActive(true);
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
            lockstep.HideExportOptionsEditor(exportOptionsUI, exportOptions);
        }

        public void CloseExportedDataWindow()
        {
            dimBackground.SetActive(false);
            exportedDataWindow.SetActive(false);
            serializedOutputField.text = "";
        }

        public void CloseOpenWindow()
        {
            if (importWindow.activeSelf)
                CloseImportWindow();
            if (exportWindow.activeSelf)
                CloseExportWindow();
            if (exportedDataWindow.activeSelf)
                CloseExportedDataWindow();
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
            importedGameStates = lockstep.ImportPreProcess(
                importString,
                out exportDate,
                out exportWorldName,
                out exportName);
            if (importedGameStates == null)
            {
                importOptionsUI.Info.AddChild(
                    (LabelWidgetData)importOptionsUI.Editor.NewLabel("Malformed or invalid data.").StdMove());
                importOptionsUI.Draw();
                return;
            }

            LabelWidgetData mainInfoLabel = importOptionsUI.Info.AddChild(importOptionsUI.Editor.NewLabel(""));

            int canImportCount = 0;
            foreach (object[] importedGS in importedGameStates)
            {
                string errorMsg = LockstepImportedGS.GetErrorMsg(importedGS);
                if (errorMsg == null)
                {
                    canImportCount++;
                    continue;
                }
                string displayName = LockstepImportedGS.GetDisplayName(importedGS);
                importOptionsUI.Info.AddChild(
                    (LabelWidgetData)importOptionsUI.Editor.NewLabel($"{displayName} - {errorMsg}").StdMove());
            }
            anyImportedGSHasNoErrors = canImportCount != 0;

            // foreach (LockstepImportGSEntry entry in importGSEntries)
            //     if (!entry.mainToggle.isOn)
            //         entry.infoLabel.text = "not in imported data, unchanged";

            int cannotImportCount = importedGameStates.Length - canImportCount;
            mainInfoLabel.Label = $"Can import {(cannotImportCount == 0 ? "all " : "")}{canImportCount}"
                + (cannotImportCount == 0 ? "" : $", cannot import {cannotImportCount}")
                + $"\n<nobr><size=90%>{exportName ?? "<i>unnamed</i>"} "
                + $"<size=60%>(from <size=75%>{exportWorldName}<size=60%>, "
                + $"{exportDate.ToLocalTime():yyyy-MM-dd HH:mm})</nobr>";
            mainInfoLabel.DecrementRefsCount();

            lockstep.AssociateImportOptionsWithImportedGameStates(importedGameStates, importOptions);
            lockstep.ShowImportOptionsEditor(importOptionsUI, importedGameStates);
            importOptionsUI.Draw();

            UpdateImportButton();
        }

        private bool CanImport() => isImportInitialized && anyImportedGSHasNoErrors && !lockstep.IsImporting;

        private void UpdateImportButton()
        {
            confirmImportButton.interactable = CanImport();
            confirmImportButtonText.text = !isImportInitialized ? "Loading..."
                : lockstep.IsImporting ? "Importing..."
                : "Import";
        }

        public void ConfirmImport()
        {
            if (!CanImport())
            {
                Debug.LogError("[Lockstep] Through means meant to be impossible the import button has been "
                    + "pressed when it cannot actually import. Someone messed with something.");
                return;
            }
            lockstep.UpdateAllCurrentImportOptionsFromWidgets(importedGameStates);
            lockstep.StartImport(importedGameStates, exportDate, exportWorldName, exportName);
            CloseImportWindow();
        }

        private void ResetImport(bool leaveInputFieldUntouched = false)
        {
            if (!leaveInputFieldUntouched)
                serializedInputField.SetTextWithoutNotify("");

            if (importedGameStates != null)
                lockstep.HideImportOptionsEditor(importOptionsUI, importedGameStates);
            importedGameStates = null; // Free for GC.
            importOptionsUI.Clear();
            importOptionsUI.Info.FoldedOut = true;
            anyImportedGSHasNoErrors = false;
            UpdateImportButton();
        }

        public void SetAutosaveSelected()
        {
            // LockstepGameState[] toAutosave = new LockstepGameState[exportGSEntries.Length];
            // int i = 0;
            // foreach (LockstepExportGSEntry entry in exportGSEntries)
            //     if (entry.gameState.GameStateSupportsImportExport && entry.mainToggle.isOn)
            //         toAutosave[i++] = entry.gameState;
            // // The rest are null and that's fine, GameStatesToAutosave will shorten the array when copying.
            // lockstep.GameStatesToAutosave = toAutosave; // Raises OnGameStatesToAutosaveChanged.
        }

        private void UpdateAutosaveInfoLabelsReadingFromLockstep()
        {
            // LockstepGameState[] toAutosave = lockstep.GameStatesToAutosave;
            // DataDictionary lut = new DataDictionary();
            // foreach (LockstepGameState gs in toAutosave)
            //     lut.Add(gs, true);
            // for (int i = 0; i < exportGSEntries.Length; i++)
            // {
            //     LockstepExportGSEntry entry = exportGSEntries[i];
            //     if (!entry.gameState.GameStateSupportsImportExport)
            //         continue;
            //     bool doAutosave = lut.ContainsKey(entry.gameState);
            //     entry.doAutosave = doAutosave;
            //     entry.infoLabel.gameObject.SetActive(doAutosave);
            //     mainGSEntries[i].autosaveText.gameObject.SetActive(doAutosave);
            // }
        }

        public void OnAutosaveIntervalFieldEndEdit()
        {
            if (int.TryParse(autosaveIntervalField.text, out int autosaveIntervalMinutes))
                autosaveInterval = (float)autosaveIntervalMinutes;
            else
                autosaveInterval = defaultAutosaveInterval;
            if (autosaveInterval < minAutosaveInterval)
            {
                autosaveInterval = minAutosaveInterval;
            // SetTextWithoutNotify is not exposed for TextMeshProUGUI, but this only raises the text changed
            // event, not the end edit, so it's fine.
                autosaveIntervalField.text = ((int)autosaveInterval).ToString();
            }
            autosaveIntervalSlider.SetValueWithoutNotify(autosaveInterval);
            SendApplyAutosaveIntervalDelayed();
        }

        public void OnAutosaveIntervalSliderChanged()
        {
            autosaveInterval = autosaveIntervalSlider.value;
            // SetTextWithoutNotify is not exposed for TextMeshProUGUI, but this only raises the text changed
            // event, not the end edit, so it's fine.
            autosaveIntervalField.text = ((int)autosaveInterval).ToString();
            SendApplyAutosaveIntervalDelayed();
        }

        private void SendApplyAutosaveIntervalDelayed()
        {
            applyAutosaveIntervalDelayedCounter++;
            SendCustomEventDelayedSeconds(nameof(ApplyAutosaveIntervalDelayed), 2f);
            if (applyAutosaveIntervalDelayedCounter == 1)
                lockstep.StartScopedAutosavePause();
        }

        private int applyAutosaveIntervalDelayedCounter = 0;
        public void ApplyAutosaveIntervalDelayed()
        {
            if (applyAutosaveIntervalDelayedCounter == 0 || (--applyAutosaveIntervalDelayedCounter) != 0)
                return;
            lockstep.StopScopedAutosavePause();
            lockstep.AutosaveIntervalSeconds = autosaveInterval * 60f; // Raises OnAutosaveIntervalSecondsChanged.
        }

        private bool CanExport()
        {
            return isExportInitialized && !lockstep.IsImporting;
        }

        private void UpdateExportButton()
        {
            confirmExportButton.interactable = CanExport();
            confirmExportButtonText.text = !isExportInitialized ? "Loading..."
                : lockstep.IsImporting ? "Importing..."
                : "Export";
        }

        public void ConfirmExport()
        {
            if (!CanExport())
            {
                Debug.LogError("[Lockstep] Through means meant to be impossible the export button has been "
                    + "pressed when it cannot actually export. Someone messed with something.");
                return;
            }
            // It is a single line field so I would expect newlines to be impossible, however since I cannot
            // trust it, just in case they do exist they get removed, because according to lockstep's api they
            // are invalid.
            string exportName = exportNameField.text.Trim().Replace('\n', ' ').Replace('\r', ' ');
            if (exportName == "")
                exportName = null;
            serializedOutputField.text = lockstep.Export(exportName, lockstep.GetAllCurrentExportOptions());
            CloseOpenWindow();
            OpenExportedDataWindow();
        }

        private void InstantAutosaveTimerUpdateLoop()
        {
            autosaveTimerUpdateLoopCounter = 1; // Intentional set instead of increment to force an instant update.
            AutosaveTimerUpdateLoop();
        }

        private int autosaveTimerUpdateLoopCounter = 0;
        private float nextPlayerDistanceCheckTime = 0f;
        public void AutosaveTimerUpdateLoop()
        {
            if (autosaveTimerUpdateLoopCounter == 0 || (--autosaveTimerUpdateLoopCounter) != 0)
                return;

            if (lockstep.ExportOptionsForAutosave == null)
            {
                autosaveTimerSlider.gameObject.SetActive(false);
                return;
            }
            if (!localPlayer.IsValid())
                return;

            float seconds = lockstep.SecondsUntilNextAutosave;
            float interval = lockstep.AutosaveIntervalSeconds + 0.0625f; // Prevent division by 0.
            autosaveTimerSlider.value = (interval - seconds) / interval;
            int hours = (int)(seconds / 3600f);
            seconds -= hours * 3600;
            int minutes = (int)(seconds / 60f);
            seconds -= minutes * 60;
            if (hours != 0)
                autosaveTimerText.text = $"autosave in {hours}h {minutes + (seconds > 0f ? 1 : 0)}m";
            else if (minutes != 0)
                autosaveTimerText.text = $"autosave in {minutes + (seconds > 0f ? 1 : 0)}m";
            else
                autosaveTimerText.text = $"autosave in {(int)seconds}s";
            autosaveTimerSlider.gameObject.SetActive(true);

            float delay;
            if (Time.time >= nextPlayerDistanceCheckTime
                && Vector3.Distance(this.transform.position, localPlayer.GetPosition()) > 16f)
            {
                nextPlayerDistanceCheckTime = Time.time + 7.5f;
                delay = 10f;
            }
            else
                delay = Mathf.Clamp(interval / 300f, 0.125f, 4f);

            autosaveTimerUpdateLoopCounter++;
            SendCustomEventDelayedSeconds(nameof(AutosaveTimerUpdateLoop), delay);
        }

        private void OnInitialized()
        {
            isImportInitialized = true;
            isExportInitialized = true;
            UpdateImportButton();
            UpdateExportButton();
            autosaveTimerUpdateLoopCounter++;
            AutosaveTimerUpdateLoop();
        }

        [LockstepEvent(LockstepEventType.OnInit)]
        public void OnInit() => OnInitialized();

        [LockstepEvent(LockstepEventType.OnClientBeginCatchUp)]
        public void OnClientBeginCatchUp() => OnInitialized();

        [LockstepEvent(LockstepEventType.OnImportStart)]
        public void OnImportStart()
        {
            UpdateImportButton();
            UpdateExportButton();
        }

        [LockstepEvent(LockstepEventType.OnImportFinished)]
        public void OnImportFinished()
        {
            UpdateImportButton();
            UpdateExportButton();
        }

        [LockstepEvent(LockstepEventType.OnExportOptionsForAutosaveChanged)]
        public void OnExportOptionsForAutosaveChanged()
        {
            UpdateAutosaveInfoLabelsReadingFromLockstep();
            InstantAutosaveTimerUpdateLoop();
        }

        [LockstepEvent(LockstepEventType.OnAutosaveIntervalSecondsChanged)]
        public void OnAutosaveIntervalSecondsChanged()
        {
            autosaveInterval = Mathf.Floor(lockstep.AutosaveIntervalSeconds / 60f);
            // SetTextWithoutNotify is not exposed for TextMeshProUGUI, but this only raises the text changed
            // event, not the end edit, so it's fine.
            autosaveIntervalField.text = ((int)autosaveInterval).ToString();
            autosaveIntervalSlider.SetValueWithoutNotify(autosaveInterval);
            InstantAutosaveTimerUpdateLoop();
        }
    }
}
