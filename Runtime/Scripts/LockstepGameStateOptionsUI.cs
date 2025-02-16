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
        [HideInInspector] [SingletonReference] public LockstepAPI lockstep;
        [HideInInspector] [SingletonReference] public WannaBeClassesManager wannaBeClasses;
        [HideInInspector] [SingletonReference] public WidgetManager widgetManager;
        public abstract string OptionsClassName { get; }

        private bool currentlyShown = false;
        public bool CurrentlyShown => currentlyShown;
        private LockstepOptionsEditorUI currentUI;
        public LockstepOptionsEditorUI CurrentUI => currentUI;

        // HACK: I hate that you have to make a field for this in the deriving class with the correct type.
        public LockstepGameStateOptionsData CurrentOptions
        {
            get => (LockstepGameStateOptionsData)GetProgramVariable("currentOptions");
            set
            {
                if (currentlyShown)
                {
                    Debug.LogError("[Lockstep] While a LockstepGameStateOptionsUI is shown, its current "
                        + "options must not be set to a different options instance.");
                    return;
                }
                LockstepGameStateOptionsData prev = (LockstepGameStateOptionsData)GetProgramVariable("currentOptions");
                if (prev != null)
                    prev.DecrementRefsCount();
                if (value != null)
                    value.IncrementRefsCount();
                SetProgramVariable("currentOptions", value);
            }
        }

        public void SetCurrentOptionsAndHideShowIfNeeded(LockstepGameStateOptionsData options)
        {
            bool mustHideShow = currentlyShown;
            LockstepOptionsEditorUI ui = currentUI;
            if (mustHideShow)
                HideOptionsEditor();
            CurrentOptions = options;
            if (mustHideShow)
                OnOptionsEditorShow(ui);
        }

        public abstract LockstepGameStateOptionsData NewOptions();
        protected abstract void ValidateOptionsImpl();
        protected abstract void InitWidgetData();
        // The public function has an Internal prefix since it is not part of the pubic api.
        public void InitWidgetDataInternal() => InitWidgetData();
        protected abstract void UpdateCurrentOptionsFromWidgetsImpl();
        protected abstract void OnOptionsEditorShow(LockstepOptionsEditorUI ui);
        protected abstract void OnOptionsEditorHide(LockstepOptionsEditorUI ui);

        public void ValidateOptions(LockstepGameStateOptionsData options)
        {
            if (options == null)
            {
                Debug.LogError("[Lockstep] Attempt to call ValidateOptions with null options.");
                return;
            }
            // HACK: I hate that you have to make a field for this in the deriving class with the correct type.
            SetProgramVariable("optionsToValidate", options);
            ValidateOptionsImpl();
        }

        public void UpdateCurrentOptionsFromWidgets()
        {
            if (CurrentOptions == null)
            {
                Debug.LogError("[Lockstep] Attempt to UpdateCurrentOptionsFromWidgets while CurrentOptions is null.");
                return;
            }
            UpdateCurrentOptionsFromWidgetsImpl();
        }

        public void ShowOptionsEditor(LockstepOptionsEditorUI ui)
        {
            if (!lockstep.InitializedEnoughForImportExport)
            {
                Debug.LogError($"[Lockstep] Attempt to ShowOptionsEditor before InitializedEnoughForImportExport is true.");
                return;
            }
            if (currentlyShown)
            {
                Debug.LogError("[Lockstep] Attempt to ShowOptionsEditor while the UI is already shown.");
                return;
            }
            currentlyShown = true;
            currentUI = ui;
            OnOptionsEditorShow(ui);
        }

        public void HideOptionsEditor()
        {
            if (!currentlyShown)
            {
                Debug.LogError("[Lockstep] Attempt to HideOptionsEditor while the UI is already hidden.");
                return;
            }
            OnOptionsEditorHide(currentUI);
            currentUI = null;
            currentlyShown = false;
        }
    }
}
