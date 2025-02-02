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
        private TestGSImportOptions options;

        private ToggleFieldWidgetData shouldImportWidget;

        public override LockstepGameStateOptionsData NewOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  NewOptions");
            #endif
            return wannaBeClasses.New<TestGSImportOptions>(nameof(TestGSImportOptions));
        }

        public override void ValidateOptions()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  ValidateOptions");
            #endif
        }

        public override void InitWidgetData(GenericValueEditor dummyEditor)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  InitWidgetData");
            #endif
            shouldImportWidget = dummyEditor.NewToggleField("Test GS", false);
        }

        public override void UpdateCurrentOptionsFromWidgets()
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  UpdateCurrentOptionsFromWidgets");
            #endif
            currentOptions.shouldImport = shouldImportWidget.Value;
        }

        public override void OnOptionsEditorShow(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  OnOptionsEditorShow");
            #endif
            if (!testGameState.HasImportData())
                return;
            shouldImportWidget.Value = options.shouldImport;
            ui.General.AddChild(shouldImportWidget);
        }

        public override void OnOptionsEditorHide(LockstepOptionsEditorUI ui)
        {
            #if LockstepDebug
            Debug.Log($"[LockstepDebug] TestGSImportUI  OnOptionsEditorHide");
            #endif
        }
    }
}
