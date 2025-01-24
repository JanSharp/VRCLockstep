using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSExportUI : LockstepGameStateOptionsUI
    {
        private TestGSExportOptions currentOptions;
        private TestGSExportOptions options;

        private ToggleFieldWidgetData shouldExportWidget;

        public override LockstepGameStateOptionsData NewOptions() => wannaBeClasses.New<TestGSExportOptions>(nameof(TestGSExportOptions));

        public override void ValidateOptions() { }

        public override void InitWidgetData(GenericValueEditor dummyEditor)
        {
            shouldExportWidget = dummyEditor.NewToggleField("Test GS", false);
        }

        public override void UpdateCurrentOptionsFromWidgets()
        {
            options.shouldExport = shouldExportWidget.Value;
        }

        public override void OnOptionsEditorShow(LockstepOptionsEditorUI ui)
        {
            shouldExportWidget.Value = options.shouldExport;
            ui.General.AddChild(shouldExportWidget);
        }

        public override void OnOptionsEditorHide(LockstepOptionsEditorUI ui) { }
    }
}
