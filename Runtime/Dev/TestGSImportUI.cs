using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSImportUI : LockstepGameStateOptionsUI
    {
        private TestGSImportOptions currentOptions;
        private TestGSImportOptions options;

        private ToggleFieldWidgetData shouldImportWidget;

        public override LockstepGameStateOptionsData NewOptions() => wannaBeClasses.New<TestGSImportOptions>(nameof(TestGSImportOptions));

        public override void ValidateOptions() { }

        public override void InitWidgetData(GenericValueEditor dummyEditor)
        {
            shouldImportWidget = dummyEditor.NewToggleField("Test GS", false);
        }

        public override void UpdateCurrentOptionsFromWidgets()
        {
            options.shouldImport = shouldImportWidget.Value;
        }

        public override void OnOptionsEditorShow(LockstepOptionsEditorUI ui)
        {
            shouldImportWidget.Value = options.shouldImport;
            ui.General.AddChild(shouldImportWidget);
        }

        public override void OnOptionsEditorHide(LockstepOptionsEditorUI ui) { }
    }
}
