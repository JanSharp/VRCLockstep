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
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  NewOptions");
            #endif
            return wannaBeClasses.New<TestGSExportOptions>(nameof(TestGSExportOptions));
        }

        private TestGSExportOptions optionsToValidate;
        protected override void ValidateOptionsImpl()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  ValidateOptions");
            #endif
        }

        protected override void InitWidgetData(GenericValueEditor dummyEditor)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  InitWidgetData");
            #endif
            shouldExportWidget = dummyEditor.NewToggleField("Test GS", false);
        }

        protected override void UpdateCurrentOptionsFromWidgetsImpl()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  UpdateCurrentOptionsFromWidgets");
            #endif
            currentOptions.shouldExport = shouldExportWidget.Value;
        }

        protected override void OnOptionsEditorShow(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  OnOptionsEditorShow");
            #endif
            shouldExportWidget.Value = currentOptions.shouldExport;
            ui.General.AddChild(shouldExportWidget);
        }

        protected override void OnOptionsEditorHide(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  OnOptionsEditorHide");
            #endif
        }
    }
}
