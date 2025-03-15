using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// TODO: docs

namespace JanSharp
{
    /// <summary>
    /// <para>When deriving from <see cref="LockstepGameStateOptionsUI"/>, there must be
    /// <c>currentOptions</c> and <c>optionsToValidate</c> fields with the same type as specified by
    /// <see cref="OptionsClassName"/> defined in the deriving class.</para>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class LockstepGameStateOptionsUI : UdonSharpBehaviour
    {
        [HideInInspector] [SingletonReference] public LockstepAPI lockstep;
        [HideInInspector] [SingletonReference] public WannaBeClassesManager wannaBeClasses;
        [HideInInspector] [SingletonReference] public WidgetManager widgetManager;
        /// <summary>
        /// <para>The class name of a class deriving from <see cref="LockstepGameStateOptionsData"/> which is
        /// the class this options UI uses. <see cref="CurrentOptions"/>, <see cref="NewOptions"/> and
        /// <see cref="ValidateOptions(LockstepGameStateOptionsData)"/> all use this class.</para>
        /// <para>When deriving from <see cref="LockstepGameStateOptionsUI"/>, there must be
        /// <c>currentOptions</c> and <c>optionsToValidate</c> fields with the same type as specified by
        /// <see cref="OptionsClassName"/> defined in the deriving class.</para>
        /// <para>Game state safe.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        public abstract string OptionsClassName { get; }

        private bool currentlyShown = false;
        /// <summary>
        /// <para>Wether this UI is currently shown to the user or not, modified through
        /// <see cref="ShowOptionsEditor(LockstepOptionsEditorUI)"/> and
        /// <see cref="HideOptionsEditor"/>.</para>
        /// <para>Gets set to <see langword="true"/> right before
        /// <see cref="OnOptionsEditorShow(LockstepOptionsEditorUI)"/> gets raised, set to
        /// <see langword="false"/> right before <see cref="OnOptionsEditorHide(LockstepOptionsEditorUI)"/>
        /// gets raised.</para>
        /// <para>While <see langword="true"/> <see cref="CurrentUI"/> is never <see langword="null"/>, while
        /// <see langword="false"/> <see cref="CurrentUI"/> is always <see langword="null"/>.</para>
        /// <para>While <see langword="true"/> <see cref="CurrentOptions"/> is never <see langword="null"/>,
        /// while <see langword="false"/> <see cref="CurrentOptions"/> may or may not be
        /// <see langword="null"/>, though it should generally be <see langword="null"/>. Preferably it should
        /// only be non <see langword="null"/> while <see cref="CurrentlyShown"/> is <see langword="false"/>
        /// right before <see cref="ShowOptionsEditor(LockstepOptionsEditorUI)"/> gets called.</para>
        /// <para>Not game state safe.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public bool CurrentlyShown => currentlyShown;
        private LockstepOptionsEditorUI currentUI;
        /// <summary>
        /// <para>The UI instance which was passed to
        /// <see cref="ShowOptionsEditor(LockstepOptionsEditorUI)"/> and
        /// <see cref="OnOptionsEditorShow(LockstepOptionsEditorUI)"/>.</para>
        /// <para>Gets set to the UI instance right before
        /// <see cref="OnOptionsEditorShow(LockstepOptionsEditorUI)"/> gets raised, set to
        /// <see langword="null"/> right before <see cref="OnOptionsEditorHide(LockstepOptionsEditorUI)"/>
        /// gets raised.</para>
        /// <para><see langword="null"/> when <see cref="CurrentlyShown"/> is <see langword="false"/>.</para>
        /// <para></para>
        /// <para>Not game state safe.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public LockstepOptionsEditorUI CurrentUI => currentUI;

        /// <summary>
        /// <para>When deriving from this class, there must be a <c>currentOptions</c> field with the same
        /// type as specified by <see cref="OptionsClassName"/> defined in the deriving class. Said field is
        /// the backing field for this property.</para>
        /// <para>Holds a strong reference to the <see cref="LockstepGameStateOptionsData"/> which is a
        /// <see cref="WannaBeClass"/>, which is to say that assigning to this property calls
        /// <see cref="WannaBeClass.IncrementRefsCount"/> and <see cref="WannaBeClass.DecrementRefsCount"/>
        /// accordingly.</para>
        /// <para>Cannot be assigned to while <see cref="CurrentlyShown"/> is <see langword="true"/>.</para>
        /// <para>Generally not game state safe, unless <see cref="LockstepAPI.IsSerializingForExport"/> or
        /// <see cref="LockstepAPI.IsDeserializingForImport"/> is <see langword="true"/>, depending on if this
        /// is an <see cref="LockstepGameState.ExportUI"/> or <see cref="LockstepGameState.ImportUI"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
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

        /// <summary>
        /// <para>This function can be used at any time with any <paramref name="options"/>, including
        /// <see langword="null"/> to assign to <see cref="CurrentOptions"/> without throwing an error.</para>
        /// <para><see cref="CurrentOptions"/> cannot be assigned to while <see cref="CurrentlyShown"/> is
        /// <see langword="true"/>. This function checks if this is the case and calls
        /// <see cref="HideOptionsEditor"/> before assigning to <see cref="CurrentOptions"/> if required, and
        /// subsequently calls <see cref="ShowOptionsEditor(LockstepOptionsEditorUI)"/> afterwards, using the
        /// same <see cref="LockstepOptionsEditorUI"/> instance that was used previously.</para>
        /// <para>Does not call <see cref="ShowOptionsEditor(LockstepOptionsEditorUI)"/> if the given
        /// <paramref name="options"/> are <see langword="null"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="options">The value to assign to <see cref="CurrentOptions"/>.</param>
        public void SetCurrentOptionsAndHideShowIfNeeded(LockstepGameStateOptionsData options)
        {
            bool mustHideShow = currentlyShown;
            LockstepOptionsEditorUI ui = currentUI;
            if (mustHideShow)
                HideOptionsEditor();
            CurrentOptions = options;
            if (mustHideShow && options != null)
                ShowOptionsEditor(ui);
        }

        /// <summary>
        /// <para>Create an new options instance which must be valid in the context of the current state of
        /// the game state.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <returns>A new instance of the <see cref="OptionsClassName"/> class.</returns>
        public abstract LockstepGameStateOptionsData NewOptions();
        protected abstract void ValidateOptionsImpl();
        protected abstract void InitWidgetData();
        // The public function has an Internal prefix since it is not part of the pubic api.
        public void InitWidgetDataInternal() => InitWidgetData();
        protected abstract void UpdateCurrentOptionsFromWidgetsImpl();
        protected abstract void OnOptionsEditorShow(LockstepOptionsEditorUI ui);
        protected abstract void OnOptionsEditorHide(LockstepOptionsEditorUI ui);

        /// <summary>
        /// <para>Updates/Validates the given <paramref name="options"/> instance ensuring that it can be used
        /// for an export or import for the current state the game state is in.</para>
        /// <para>This is required whenever some options instance reference was held long enough such that the
        /// game state could have been mutated since the last time the options instance was known to be
        /// valid.</para>
        /// <para><see cref="NewOptions"/> returns valid options, and
        /// <see cref="UpdateCurrentOptionsFromWidgets"/> makes <see cref="CurrentOptions"/> known to be valid
        /// at the current point in time.</para>
        /// </summary>
        /// <param name="options">Must not be <see langword="null"/>.</param>
        public void ValidateOptions(LockstepGameStateOptionsData options)
        {
            if (options == null)
            {
                Debug.LogError("[Lockstep] Attempt to call ValidateOptions with null options.");
                return;
            }
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
            if (CurrentOptions == null)
            {
                Debug.LogError("[Lockstep] Attempt to ShowOptionsEditor while CurrentOptions are null.");
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
            currentlyShown = false;
            currentUI = null;
            OnOptionsEditorHide(currentUI);
        }
    }
}
