using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSImportUI : LockstepGameStateOptionsUI
    {
        [HideInInspector][SerializeField][SingletonReference] private TestGameState testGameState;

        public override string OptionsClassName => nameof(TestGSImportOptions);
        private TestGSImportOptions currentOptions;

        private ToggleFieldWidgetData shouldImportWidget;

        protected override LockstepGameStateOptionsData NewOptionsImpl()
        {
            Debug.Log($"[LockstepTest] TestGSImportUI  NewOptionsImpl");
            return wannaBeClasses.New<TestGSImportOptions>(nameof(TestGSImportOptions));
        }

        private TestGSImportOptions optionsToValidate;
        protected override void ValidateOptionsImpl()
        {
            Debug.Log($"[LockstepTest] TestGSImportUI  ValidateOptions");
        }

        protected override void InitWidgetData()
        {
            Debug.Log($"[LockstepTest] TestGSImportUI  InitWidgetData");
            shouldImportWidget = widgetManager.NewToggleField("Test GS", false);
        }

        protected override void UpdateCurrentOptionsFromWidgetsImpl()
        {
            Debug.Log($"[LockstepTest] TestGSImportUI  UpdateCurrentOptionsFromWidgets");
            currentOptions.shouldImport = shouldImportWidget.Value;
        }

        protected override void OnOptionsEditorShow(LockstepOptionsEditorUI ui, uint importedDataVersion)
        {
            Debug.Log($"[LockstepTest] TestGSImportUI  OnOptionsEditorShow");
            if (!testGameState.HasImportData())
                return;
            shouldImportWidget.Value = currentOptions.shouldImport;
            ui.General.AddChild(shouldImportWidget);
        }

        protected override void OnOptionsEditorHide(LockstepOptionsEditorUI ui)
        {
            Debug.Log($"[LockstepTest] TestGSImportUI  OnOptionsEditorHide");
        }
    }
}
