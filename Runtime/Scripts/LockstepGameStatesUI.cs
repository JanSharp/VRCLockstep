using System.Text;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace JanSharp.Internal
{
#if !LOCKSTEP_DEBUG
    [AddComponentMenu("")]
#endif
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepGameStatesUI : UdonSharpBehaviour
    {
        [SerializeField][HideInInspector][SingletonReference] private LockstepAPI lockstep;

        [SerializeField] private GameObject mainGSEntryPrefab; // Used by editor scripting.

        [SerializeField] private Transform mainGSList; // Used by editor scripting.
        [SerializeField] private GameObject dimBackground;

        [SerializeField] private Slider autosaveTimerSlider;
        [SerializeField] private TextMeshProUGUI autosaveTimerText;

        [SerializeField] private Button openImportWindowButton;
        [SerializeField] private Button openExportWindowButton;
        [SerializeField] private Button openAutosaveWindowButton;

        [SerializeField] private GameObject importWindow;
        [SerializeField] private TMP_InputField serializedInputField;
        [SerializeField] private LockstepOptionsEditorUI importOptionsUI;
        [SerializeField] private Button confirmImportButton;
        [SerializeField] private TextMeshProUGUI confirmImportButtonText;

        [SerializeField] private GameObject exportWindow;
        [SerializeField] private RectTransform exportOptionsEditorContainer;
        [SerializeField] private RectTransform exportOptionsEditorTransform;
        [SerializeField] private LockstepOptionsEditorUI exportOptionsUI;
        [SerializeField] private TMP_InputField exportNameField;
        [SerializeField] private Button confirmExportButton;
        [SerializeField] private TextMeshProUGUI confirmExportButtonText;

        [SerializeField] private GameObject exportedDataWindow;
        [SerializeField] private TextMeshProUGUI exportedDataSizeText;
        [SerializeField] private TMP_InputField serializedOutputField;

        [SerializeField] private GameObject autosaveWindow;
        [SerializeField] private RectTransform autosaveOptionsEditorContainer;
        [SerializeField] private Toggle autosaveUsesExportOptionsToggle;
        [SerializeField] private TMP_InputField autosaveIntervalField;
        [SerializeField] private Slider autosaveIntervalSlider;
        [SerializeField] private Toggle autosaveToggle;
        [SerializeField] private TextMeshProUGUI autosaveToggleText;

        [SerializeField] private float minAutosaveInterval;
        [SerializeField] private float defaultAutosaveInterval;
        [SerializeField] private float autosaveInterval;

        [SerializeField] private LockstepMainGSEntry[] mainGSEntries; // Used by editor scripting.

        private bool isInitialized = false;

        /// <summary>
        /// <para><see cref="LockstepImportedGS"/>[]</para>
        /// </summary>
        private object[][] importedGameStates;
        private System.DateTime exportDate;
        private string exportWorldName;
        private string exportName;
        private bool anySupportImportExport;
        private bool waitingForExportToFinish = false;

        /// <summary>
        /// <para>Keys: <see cref="string"/> <see cref="LockstepGameState.GameStateInternalName"/>,<br/>
        /// Values: <see cref="LockstepGameStateOptionsData"/> <c>importOptions</c>.</para>
        /// </summary>
        private DataDictionary importOptions;
        private bool anyImportedGSHasNoErrors;
        private LockstepGameStateOptionsData[] exportOptions;
        private LockstepGameStateOptionsData[] autosaveOptions;
        private bool AutosaveUsesExportOptions => autosaveOptions == exportOptions;

        private VRCPlayerApi localPlayer;

        private void OnEnable()
        {
            TryStartAutosaveTimerUpdateLoop();
        }

        public void OpenImportWindow()
        {
            if (!isInitialized || importWindow.activeSelf)
                return;
            dimBackground.SetActive(true);
            importWindow.SetActive(true);
            ResetImport();
            importOptionsUI.Info.AddChild(importOptionsUI.WidgetManager.NewLabel(
                "Paste text obtained from a previous export into the text field above.").StdMoveWidget());
            importOptionsUI.Draw();
        }

        public void OpenExportWindow()
        {
            if (!isInitialized || exportWindow.activeSelf)
                return;
            exportOptionsEditorTransform.SetParent(exportOptionsEditorContainer, worldPositionStays: false);
            exportOptionsUI.Clear();
            AddGameStatesToExportToInfoWidget();
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

        public void OpenAutosaveWindow()
        {
            if (!isInitialized || autosaveWindow.activeSelf)
                return;
            exportOptionsEditorTransform.SetParent(autosaveOptionsEditorContainer, worldPositionStays: false);
            ShowAutosaveOptionsEditor();
            dimBackground.SetActive(true);
            autosaveWindow.SetActive(true);
        }

        public void CloseImportWindow()
        {
            dimBackground.SetActive(false);
            importWindow.SetActive(false);
            ResetImport();
            importOptionsUI.Draw(); // Return widgets to the pool.
        }

        public void CloseExportWindow()
        {
            if (!exportWindow.activeSelf)
                return;
            dimBackground.SetActive(false);
            exportWindow.SetActive(false);
            lockstep.UpdateAllCurrentExportOptionsFromWidgets();
            lockstep.HideExportOptionsEditor();
            if (!AutosaveUsesExportOptions && autosaveToggle.isOn)
                lockstep.ExportOptionsForAutosave = exportOptions; // exportOptions == autosaveOptions
            exportOptionsUI.Clear();
            exportOptionsUI.Draw(); // Return widgets to the pool.
        }

        public void CloseExportedDataWindow()
        {
            dimBackground.SetActive(false);
            exportedDataWindow.SetActive(false);
            SetExportedDataSizeText(0, 0);
            serializedOutputField.text = "";
        }

        public void CloseAutosaveWindow()
        {
            if (!autosaveWindow.activeSelf)
                return;
            dimBackground.SetActive(false);
            autosaveWindow.SetActive(false);
            HideAutosaveOptionsEditor();
            exportOptionsUI.Clear();
            exportOptionsUI.Draw(); // Return widgets to the pool.
        }

        public void CloseOpenWindow()
        {
            if (importWindow.activeSelf)
                CloseImportWindow();
            if (exportWindow.activeSelf)
                CloseExportWindow();
            if (exportedDataWindow.activeSelf)
                CloseExportedDataWindow();
            if (autosaveWindow.activeSelf)
                CloseAutosaveWindow();
        }

        // Cannot use EndEdit because that simply does not get raised in VRChat. We only get value changed.
        // Which is oh so very great in the editor because value changed gets raised for every character that
        // gets pasted in, while in VRChat it'll only get raised once.
        // This handler is expensive, so rerunning it for every character causes exponentially long lag
        // spikes. In the editor. You know, the thing we use for testing.
        // https://vrchat.canny.io/sdk-bug-reports/p/worlds-316-vrcinputfield-inputfield-no-longer-sends-onendedit-event
        public void OnImportSerializedTextValueChanged()
        {
            if (onImportSerializedTextValueChangedDelayedQueued)
                return;
            onImportSerializedTextValueChangedDelayedQueued = true;
            SendCustomEventDelayedFrames(nameof(OnImportSerializedTextValueChangedDelayed), 1);
        }

        private bool onImportSerializedTextValueChangedDelayedQueued = false;
        public void OnImportSerializedTextValueChangedDelayed()
        {
            onImportSerializedTextValueChangedDelayedQueued = false;
            if (!isInitialized)
                return;
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
                importOptionsUI.Info.AddChild(importOptionsUI.WidgetManager.NewLabel(
                    "Malformed or invalid data.").StdMoveWidget());
                importOptionsUI.Draw();
                return;
            }

            LabelWidgetData mainInfoLabel = importOptionsUI.Info.AddChild(importOptionsUI.WidgetManager.NewLabel(""));

            string importedGameStatesMsg = BuildImportedGameStatesMsg(out int canImportCount, out bool anyWarnings);
            if (anyWarnings)
                importOptionsUI.Info.FoldedOut = true; // Else retain state, don't set to false.

            FoldOutWidgetData gsFoldOut = importOptionsUI.Info.AddChild(
                importOptionsUI.WidgetManager.NewFoldOutScope("Game States", foldedOut: anyWarnings));
            gsFoldOut.AddChild(importOptionsUI.WidgetManager.NewLabel(importedGameStatesMsg).StdMoveWidget());
            gsFoldOut.DecrementRefsCount();
            anyImportedGSHasNoErrors = canImportCount != 0;

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

        private string BuildImportedGameStatesMsg(out int canImportCount, out bool anyWarnings)
        {
            DataDictionary importedGSByInternalName = new DataDictionary();
            foreach (object[] importedGS in importedGameStates)
                importedGSByInternalName.Add(LockstepImportedGS.GetInternalName(importedGS), new DataToken(importedGS));

            canImportCount = 0;
            anyWarnings = false;

            StringBuilder sb = new StringBuilder();
            sb.Append("<size=80%>");
            bool isFirstLine = true;

            foreach (LockstepGameState gameState in lockstep.AllGameStates)
            {
                if (isFirstLine)
                    isFirstLine = false;
                else
                    sb.Append('\n'); // AppendLine could add \r\n. \r is outdated. \n is the future.

                sb.Append(gameState.GameStateDisplayName);
                if (!importedGSByInternalName.TryGetValue(gameState.GameStateInternalName, out DataToken importedGSToken))
                {
                    if (!gameState.GameStateSupportsImportExport)
                        sb.Append(" - <color=#888888>does not support import</color>");
                    else
                    {
                        sb.Append(" - <color=#ffff99>not in imported data</color>");
                        anyWarnings = true;
                    }
                }
                else
                {
                    object[] importedGS = (object[])importedGSToken.Reference;
                    string errorMsg = LockstepImportedGS.GetErrorMsg(importedGS);
                    if (errorMsg != null)
                    {
                        sb.Append(" - <color=#ffaaaa>");
                        sb.Append(errorMsg);
                        sb.Append("</color>");
                        anyWarnings = true;
                    }
                    else
                    {
                        canImportCount++;
                        sb.Append(" - <color=#99ccff>supports import</color>");
                    }
                }
            }

            foreach (object[] importedGS in importedGameStates)
            {
                LockstepGameState gameState = LockstepImportedGS.GetGameState(importedGS);
                if (gameState != null)
                    continue;

                if (isFirstLine)
                    isFirstLine = false;
                else
                    sb.Append('\n'); // AppendLine could add \r\n. \r is outdated. \n is the future.

                sb.Append(LockstepImportedGS.GetDisplayName(importedGS));
                sb.Append(" - <color=#ffaaaa>");
                sb.Append(LockstepImportedGS.GetErrorMsg(importedGS));
                sb.Append("</color>");
                anyWarnings = true;
            }

            return sb.ToString();
        }

        private string BuildGameStatesToExportMsg()
        {
            if (lockstep.AllGameStatesCount == 0)
                return "<size=80%>There are no game states in this world";

            StringBuilder sb = new StringBuilder();
            sb.Append("<size=80%>");
            bool isFirstLine = true;

            foreach (LockstepGameState gameState in lockstep.AllGameStates)
            {
                if (isFirstLine)
                    isFirstLine = false;
                else
                    sb.Append('\n'); // AppendLine could add \r\n. \r is outdated. \n is the future.

                sb.Append(gameState.GameStateDisplayName);
                sb.Append(gameState.GameStateSupportsImportExport
                    ? " - <color=#99ccff>supports export</color>"
                    : " - <color=#888888>does not support export</color>");
            }

            return sb.ToString();
        }

        private void AddGameStatesToExportToInfoWidget()
        {
            FoldOutWidgetData gsFoldOut = exportOptionsUI.Info.AddChild(exportOptionsUI.WidgetManager.NewFoldOutScope("Game States", true));
            gsFoldOut.AddChild(exportOptionsUI.WidgetManager.NewLabel(BuildGameStatesToExportMsg()).StdMoveWidget());
            gsFoldOut.DecrementRefsCount();
        }

        private bool CanImport() => isInitialized && anySupportImportExport && anyImportedGSHasNoErrors && !lockstep.IsImporting;

        private void UpdateImportButton()
        {
            confirmImportButton.interactable = CanImport();
            confirmImportButtonText.text = !isInitialized ? "Loading..."
                : !anySupportImportExport ? "None Support Importing"
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
            lockstep.UpdateAllCurrentImportOptionsFromWidgets();
            lockstep.StartImport(importedGameStates, exportDate, exportWorldName, exportName);
            CloseImportWindow();
        }

        private void ResetImport(bool leaveInputFieldUntouched = false)
        {
            if (!leaveInputFieldUntouched)
                serializedInputField.SetTextWithoutNotify("");

            if (importedGameStates != null)
            {
                lockstep.HideImportOptionsEditor();
                lockstep.CleanupImportedGameStatesData(importedGameStates);
            }
            importedGameStates = null; // Free for GC.
            importOptionsUI.Clear();
            importOptionsUI.Info.FoldedOut = true;
            anyImportedGSHasNoErrors = false;
            UpdateImportButton();
        }

        public void OnAutosaveUsesExportOptionsToggleValueChanged()
        {
            if (!isInitialized)
                return;
            if (AutosaveUsesExportOptions == autosaveUsesExportOptionsToggle.isOn)
                return;
            if (AutosaveUsesExportOptions)
                autosaveOptions = lockstep.CloneAllOptions(exportOptions);
            else
            {
                foreach (LockstepGameStateOptionsData options in autosaveOptions)
                    if (options != null)
                        options.DecrementRefsCount();
                autosaveOptions = exportOptions;
            }
            HideAutosaveOptionsEditor();
            ShowAutosaveOptionsEditor();
        }

        private LabelWidgetData autosaveInfoLabel;
        private LabelWidgetData autosaveUsingExportInfoLabel;

        private void ShowAutosaveOptionsEditor()
        {
            exportOptionsUI.Clear();
            autosaveInfoLabel = autosaveInfoLabel ?? exportOptionsUI.WidgetManager.NewLabel(
                "Autosaves periodically write exported data to the VRChat log file. Export and autosave log "
                    + "messages use the prefix '[Lockstep] Export:'.");
            exportOptionsUI.Info.AddChild(autosaveInfoLabel);
            AddGameStatesToExportToInfoWidget();
            lockstep.ShowExportOptionsEditor(exportOptionsUI, autosaveOptions);
            if (autosaveUsesExportOptionsToggle.isOn)
            {
                exportOptionsUI.Root.Interactable = false;
                autosaveUsingExportInfoLabel = autosaveUsingExportInfoLabel ?? exportOptionsUI.WidgetManager.NewLabel(
                    "Currently using export options for autosaves. Modifying is disabled to prevent "
                        + "accidentally confusing exports vs autosaves.");
                exportOptionsUI.Info.AddChild(autosaveUsingExportInfoLabel);
            }
            exportOptionsUI.Draw();
        }

        private void HideAutosaveOptionsEditor()
        {
            if (!AutosaveUsesExportOptions)
                lockstep.UpdateAllCurrentExportOptionsFromWidgets();
            lockstep.HideExportOptionsEditor();
            exportOptionsUI.Root.Interactable = true;
            if (autosaveToggle.isOn)
                lockstep.ExportOptionsForAutosave = autosaveOptions;
        }

        public void OnAutosaveToggleValueChanged()
        {
            if (!isInitialized)
                return;
            lockstep.ExportOptionsForAutosave = autosaveToggle.isOn
                ? lockstep.GetAllCurrentExportOptions(weakReferences: true)
                : null;
        }

        public void OnAutosaveIntervalFieldValueChanged()
        {
            if (int.TryParse(autosaveIntervalField.text, out int autosaveIntervalMinutes))
                autosaveInterval = (float)autosaveIntervalMinutes;
            else
                autosaveInterval = defaultAutosaveInterval;
            if (autosaveInterval < minAutosaveInterval)
            {
                autosaveInterval = minAutosaveInterval;
                autosaveIntervalField.SetTextWithoutNotify(((int)autosaveInterval).ToString());
            }
            autosaveIntervalSlider.SetValueWithoutNotify(autosaveInterval);
            SendApplyAutosaveIntervalDelayed();
        }

        public void OnAutosaveIntervalSliderChanged()
        {
            autosaveInterval = autosaveIntervalSlider.value;
            autosaveIntervalField.SetTextWithoutNotify(((int)autosaveInterval).ToString());
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

        private bool CanExport() => isInitialized && anySupportImportExport && !lockstep.IsExporting && !lockstep.IsImporting;

        private void UpdateExportButton()
        {
            confirmExportButton.interactable = CanExport();
            confirmExportButtonText.text = !isInitialized ? "Loading..."
                : !anySupportImportExport ? "None Support Exporting"
                : lockstep.IsExporting ? "Exporting..."
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
            if (lockstep.StartExport(exportName, lockstep.GetAllCurrentExportOptions(weakReferences: true)))
                waitingForExportToFinish = true;
            else
                Debug.LogError("[Lockstep] Export failed to start, this is supposed to be impossible.");
        }

        private bool autosaveUpdateTImerLoopIsRunning = false;
        private void TryStartAutosaveTimerUpdateLoop()
        {
            if (!isInitialized || autosaveUpdateTImerLoopIsRunning)
                return;
            autosaveUpdateTImerLoopIsRunning = true;
            AutosaveTimerUpdateLoop();
        }

        private float nextPlayerDistanceCheckTime = 0f;
        public void AutosaveTimerUpdateLoop()
        {
            if (!autosaveUpdateTImerLoopIsRunning)
                return;

            if (!lockstep.HasExportOptionsForAutosave)
            {
                autosaveTimerSlider.gameObject.SetActive(false);
                autosaveUpdateTImerLoopIsRunning = false;
                return;
            }
            if (!localPlayer.IsValid() || !this.gameObject.activeInHierarchy)
            {
                autosaveUpdateTImerLoopIsRunning = false;
                return;
            }

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

            SendCustomEventDelayedSeconds(nameof(AutosaveTimerUpdateLoop), delay);
        }

        private bool CanAutosave() => isInitialized && anySupportImportExport && !lockstep.IsImporting;

        private void UpdateAutosaveToggle()
        {
            autosaveToggle.interactable = CanAutosave();
            autosaveToggleText.text = !isInitialized ? "Loading..."
                : !anySupportImportExport ? "None Support Exporting"
                : lockstep.IsExporting ? "Exporting..."
                : lockstep.IsImporting ? "Importing..."
                : "Autosave";
        }

        private void UpdateAllConfirmButtons()
        {
            UpdateImportButton();
            UpdateExportButton();
            UpdateAutosaveToggle();
        }

        private void OnInitialized()
        {
            isInitialized = true;
            localPlayer = Networking.LocalPlayer;
            exportOptionsUI.Init();
            importOptionsUI.Init();
            exportOptions = lockstep.GetNewExportOptions();
            autosaveOptions = exportOptions;
            importOptions = lockstep.GetNewImportOptions();
            anySupportImportExport = lockstep.GameStatesSupportingImportExportCount != 0;
            openImportWindowButton.interactable = true;
            openExportWindowButton.interactable = true;
            openAutosaveWindowButton.interactable = true;
            UpdateAllConfirmButtons();
            TryStartAutosaveTimerUpdateLoop();
            // Things that other scripts could have already modified but were ignored because lockstep was not
            // initialized yet.
            OnAutosaveToggleValueChanged();
            OnAutosaveUsesExportOptionsToggleValueChanged();
            OnImportSerializedTextValueChanged();
        }

        [LockstepEvent(LockstepEventType.OnInitFinished)]
        public void OnInitFinished() => OnInitialized();

        [LockstepEvent(LockstepEventType.OnPostClientBeginCatchUp)]
        public void OnPostClientBeginCatchUp() => OnInitialized();

        [LockstepEvent(LockstepEventType.OnExportStart)]
        public void OnExportStart()
        {
            UpdateExportButton();
            UpdateAutosaveToggle();
        }

        [LockstepEvent(LockstepEventType.OnExportFinished)]
        public void OnExportFinished()
        {
            UpdateExportButton();
            UpdateAutosaveToggle();
            if (!waitingForExportToFinish)
                return;

            waitingForExportToFinish = false;
            string result = lockstep.ExportResult;
            SetExportedDataSizeText(lockstep.ExportByteCount, result.Length);
            serializedOutputField.text = result;
            CloseOpenWindow();
            OpenExportedDataWindow();
        }

        private void SetExportedDataSizeText(int byteCount, int characterCount)
        {
            exportedDataSizeText.text = $"Data: {StringUtil.FormatNumberWithSpaces(byteCount)} bytes, "
                + $"Base64: {StringUtil.FormatNumberWithSpaces(characterCount)} characters";
        }

        [LockstepEvent(LockstepEventType.OnImportStart)]
        public void OnImportStart()
        {
            UpdateAllConfirmButtons();
        }

        [LockstepEvent(LockstepEventType.OnImportFinished)]
        public void OnImportFinished()
        {
            UpdateAllConfirmButtons();
        }

        [LockstepEvent(LockstepEventType.OnExportOptionsForAutosaveChanged)]
        public void OnExportOptionsForAutosaveChanged()
        {
            TryStartAutosaveTimerUpdateLoop();
        }

        [LockstepEvent(LockstepEventType.OnAutosaveIntervalSecondsChanged)]
        public void OnAutosaveIntervalSecondsChanged()
        {
            autosaveInterval = Mathf.Floor(lockstep.AutosaveIntervalSeconds / 60f);
            autosaveIntervalField.SetTextWithoutNotify(((int)autosaveInterval).ToString());
            autosaveIntervalSlider.SetValueWithoutNotify(autosaveInterval);
            TryStartAutosaveTimerUpdateLoop();
        }
    }
}
