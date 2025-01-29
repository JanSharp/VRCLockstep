using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// TODO: docs

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class LockstepGameStateOptionsUI : UdonSharpBehaviour
    {
        [HideInInspector] [SingletonReference] public WannaBeClassesManager wannaBeClasses;
        public abstract string OptionsClassName { get; }

        // HACK: I hate that you have to make a field for this in the deriving class with the correct type.
        public LockstepGameStateOptionsData CurrentOptionsInternal
        {
            get => (LockstepGameStateOptionsData)GetProgramVariable("currentOptions");
            set => SetProgramVariable("currentOptions", value);
        }

        public abstract LockstepGameStateOptionsData NewOptions();
        public abstract void ValidateOptions();
        public abstract void InitWidgetData(GenericValueEditor dummyEditor);
        public abstract void UpdateCurrentOptionsFromWidgets();
        public abstract void OnOptionsEditorShow(LockstepOptionsEditorUI ui);
        public abstract void OnOptionsEditorHide(LockstepOptionsEditorUI ui);

        public LockstepGameStateOptionsData NewOptionsInternal() => NewOptions();
        public void ValidateOptionsInternal(LockstepGameStateOptionsData options)
        {
            // HACK: I hate that you have to make a field for this in the deriving class with the correct type.
            SetProgramVariable("options", options);
            ValidateOptions();
        }

        public void OnOptionsEditorShowInternal(LockstepOptionsEditorUI ui, LockstepGameStateOptionsData options)
        {
            SetProgramVariable("options", options);
            OnOptionsEditorShow(ui);
        }

        public void OnOptionsEditorHideInternal(LockstepOptionsEditorUI ui, LockstepGameStateOptionsData options)
        {
            SetProgramVariable("options", options);
            OnOptionsEditorHide(ui);
        }
    }
}
