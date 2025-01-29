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
        private TestGSExportOptions options;

        private ToggleFieldWidgetData shouldExportWidget;

        public override LockstepGameStateOptionsData NewOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  NewOptions");
            #endif
            return wannaBeClasses.New<TestGSExportOptions>(nameof(TestGSExportOptions));
        }

        public override void ValidateOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  ValidateOptions");
            #endif
        }

        public override void InitWidgetData(GenericValueEditor dummyEditor)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  InitWidgetData");
            #endif
            shouldExportWidget = dummyEditor.NewToggleField("Test GS", false);
        }

        public override void UpdateCurrentOptionsFromWidgets()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  UpdateCurrentOptionsFromWidgets");
            #endif
            currentOptions.shouldExport = shouldExportWidget.Value;
        }

        public override void OnOptionsEditorShow(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  OnOptionsEditorShow");
            #endif
            shouldExportWidget.Value = options.shouldExport;
            ui.General.AddChild(shouldExportWidget);
        }

        public override void OnOptionsEditorHide(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSExportUI  OnOptionsEditorHide");
            #endif
        }
    }
}
