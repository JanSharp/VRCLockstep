using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGSImportUI : LockstepGameStateOptionsUI
    {
        [SerializeField] [HideInInspector] [SingletonReference] private TestGameState testGameState;

        public override string OptionsClassName => nameof(TestGSImportOptions);
        private TestGSImportOptions currentOptions;

        private ToggleFieldWidgetData shouldImportWidget;

        public override LockstepGameStateOptionsData NewOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  NewOptions");
            #endif
            return wannaBeClasses.New<TestGSImportOptions>(nameof(TestGSImportOptions));
        }

        private TestGSImportOptions optionsToValidate;
        protected override void ValidateOptionsImpl()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  ValidateOptions");
            #endif
        }

        protected override void InitWidgetData(GenericValueEditor dummyEditor)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  InitWidgetData");
            #endif
            shouldImportWidget = dummyEditor.NewToggleField("Test GS", false);
        }

        protected override void UpdateCurrentOptionsFromWidgetsImpl()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  UpdateCurrentOptionsFromWidgets");
            #endif
            currentOptions.shouldImport = shouldImportWidget.Value;
        }

        protected override void OnOptionsEditorShow(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  OnOptionsEditorShow");
            #endif
            if (!testGameState.HasImportData())
                return;
            shouldImportWidget.Value = currentOptions.shouldImport;
            ui.General.AddChild(shouldImportWidget);
        }

        protected override void OnOptionsEditorHide(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  OnOptionsEditorHide");
            #endif
        }
    }
}
