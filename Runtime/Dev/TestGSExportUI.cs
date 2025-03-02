using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSExportUI : LockstepGameStateOptionsUI
    {
        public override string OptionsClassName => nameof(TestGSExportOptions);
        private TestGSExportOptions currentOptions;

        private ToggleFieldWidgetData shouldExportWidget;

        public override LockstepGameStateOptionsData NewOptions()
        {
            Debug.Log($"[LockstepTest] TestGSExportUI  NewOptions");
            return wannaBeClasses.New<TestGSExportOptions>(nameof(TestGSExportOptions));
        }

        private TestGSExportOptions optionsToValidate;
        protected override void ValidateOptionsImpl()
        {
            Debug.Log($"[LockstepTest] TestGSExportUI  ValidateOptions");
        }

        protected override void InitWidgetData()
        {
            Debug.Log($"[LockstepTest] TestGSExportUI  InitWidgetData");
            shouldExportWidget = widgetManager.NewToggleField("Test GS", false);
        }

        protected override void UpdateCurrentOptionsFromWidgetsImpl()
        {
            Debug.Log($"[LockstepTest] TestGSExportUI  UpdateCurrentOptionsFromWidgets");
            currentOptions.shouldExport = shouldExportWidget.Value;
        }

        protected override void OnOptionsEditorShow(LockstepOptionsEditorUI ui)
        {
            Debug.Log($"[LockstepTest] TestGSExportUI  OnOptionsEditorShow");
            shouldExportWidget.Value = currentOptions.shouldExport;
            ui.General.AddChild(shouldExportWidget);
        }

        protected override void OnOptionsEditorHide(LockstepOptionsEditorUI ui)
        {
            Debug.Log($"[LockstepTest] TestGSExportUI  OnOptionsEditorHide");
        }
    }
}
