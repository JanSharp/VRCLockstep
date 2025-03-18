using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    /// <summary>
    /// <para>In order to have custom UI for options for the user to configure how a
    /// <see cref="LockstepGameState"/> should be exported or imported (through
    /// <see cref="LockstepAPI.Export(string, LockstepGameStateOptionsData[])"/> or
    /// <see cref="LockstepAPI.StartImport(object[][], System.DateTime, string, string)"/>), derive from this
    /// class and implement each abstract function as they describe themselves through annotations.<br/>
    /// Then create an instance of this custom class in the scene and reference it in the inspector for
    /// <see cref="LockstepGameState"/> for either <see cref="LockstepGameState.ExportUI"/> or
    /// <see cref="LockstepGameState.ImportUI"/>.</para>
    /// <para>A suggested naming convention is to use the name of the associated game state (potentially
    /// abbreviated if it's getting a bit long) plus either <c>ExportUI</c> or <c>ImportUI</c> as a
    /// postfix.</para>
    /// <para>As <see cref="OptionsClassName"/> mentions, there must also be a different class deriving from
    /// <see cref="LockstepGameStateOptionsData"/>. This is a <see cref="WannaBeClass"/>, do not create an
    /// instance of it in the scene, rather pretend like it is an actual custom class. Use the
    /// <see cref="WannaBeClassesManager"/> to create <c>New</c> instances of these classes. Use the already
    /// provided <see cref="wannaBeClasses"/> field as a reference to the
    /// <see cref="WannaBeClassesManager"/>.</para>
    /// <para>For game states with both export and import options and UIs there is ultimately going to be 4
    /// different classes. 2 deriving from <see cref="LockstepGameStateOptionsUI"/> and 2 deriving from
    /// <see cref="LockstepGameStateOptionsData"/>.</para>
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
        /// <para>Use <c>nameof(MyClassName)</c> to define this property.</para>
        /// <para>When deriving from <see cref="LockstepGameStateOptionsUI"/>, there must be
        /// <c>currentOptions</c> and <c>optionsToValidate</c> fields with the same type as specified by
        /// <see cref="OptionsClassName"/> defined in the deriving class.</para>
        /// <para>Game state safe.</para>
        /// <para>Usable any time.</para>
        /// </summary>
        public abstract string OptionsClassName { get; }

        private bool currentlyShown = false;
        /// <summary>
        /// <para>Whether this UI is currently shown to the user through the
        /// <see cref="LockstepOptionsEditorUI.Editor"/> or not. <see cref="CurrentlyShown"/> is modified
        /// through
        /// <see cref="ShowOptionsEditor(LockstepOptionsEditorUI, LockstepGameStateOptionsData, uint)"/> and
        /// <see cref="HideOptionsEditor"/>.</para>
        /// <para>Gets set to <see langword="true"/> right before
        /// <see cref="OnOptionsEditorShow(LockstepOptionsEditorUI, uint)"/> gets raised, gets set to
        /// <see langword="false"/> right before <see cref="OnOptionsEditorHide(LockstepOptionsEditorUI)"/>
        /// gets raised.</para>
        /// <para>While <see langword="true"/> <see cref="CurrentUI"/> is never <see langword="null"/>, while
        /// <see langword="false"/> <see cref="CurrentUI"/> is always <see langword="null"/>.</para>
        /// <para>While <see langword="true"/> <see cref="CurrentOptions"/> is never <see langword="null"/>,
        /// while <see langword="false"/> <see cref="CurrentOptions"/> is always
        /// <see langword="null"/>.</para>
        /// <para>Not game state safe.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public bool CurrentlyShown => currentlyShown;

        private LockstepOptionsEditorUI currentUI;
        /// <summary>
        /// <para>The UI instance which was passed to
        /// <see cref="ShowOptionsEditor(LockstepOptionsEditorUI, LockstepGameStateOptionsData, uint)"/> and
        /// <see cref="OnOptionsEditorShow(LockstepOptionsEditorUI, uint)"/>.</para>
        /// <para>Gets set to the UI instance right before
        /// <see cref="OnOptionsEditorShow(LockstepOptionsEditorUI, uint)"/> gets raised, set to
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
        /// <para>Generally not game state safe, unless <see cref="LockstepAPI.IsSerializingForExport"/> or
        /// <see cref="LockstepAPI.IsDeserializingForImport"/> is <see langword="true"/>, depending on if this
        /// is an <see cref="LockstepGameState.ExportUI"/> or <see cref="LockstepGameState.ImportUI"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public LockstepGameStateOptionsData CurrentOptions => (LockstepGameStateOptionsData)GetProgramVariable("currentOptions");

        private void SetCurrentOptions(LockstepGameStateOptionsData options)
        {
            LockstepGameStateOptionsData prev = (LockstepGameStateOptionsData)GetProgramVariable("currentOptions");
            if (prev != null)
                prev.DecrementRefsCount();
            if (options != null)
                options.IncrementRefsCount();
            SetProgramVariable("currentOptions", options);
        }

        /// <summary>
        /// <para>Create an new options instance which is valid in the context of the current state of the
        /// game state, would would make it already ready for
        /// <see cref="LockstepAPI.Export(string, LockstepGameStateOptionsData[])"/> or
        /// <see cref="LockstepAPI.StartImport(object[][], System.DateTime, string, string)"/>, depending on
        /// if this is an <see cref="LockstepGameState.ExportUI"/> or <see cref="LockstepGameState.ImportUI"/>
        /// accordingly.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <returns>A new instance of the <see cref="OptionsClassName"/> class.</returns>
        public abstract LockstepGameStateOptionsData NewOptions();
        /// <summary>
        /// <para>When deriving from <see cref="LockstepGameStateOptionsUI"/>, there must be an
        /// <c>optionsToValidate</c> field with the same type as specified by <see cref="OptionsClassName"/>
        /// defined in the deriving class.</para>
        /// <para>This function must update the <c>optionsToValidate</c> such that they are valid with the
        /// current game state, ready for potential use with
        /// <see cref="LockstepAPI.Export(string, LockstepGameStateOptionsData[])"/> or
        /// <see cref="LockstepAPI.StartImport(object[][], System.DateTime, string, string)"/>.</para>
        /// </summary>
        protected abstract void ValidateOptionsImpl();
        /// <summary>
        /// <para>This function gets called either after <see cref="LockstepEventType.OnInit"/> or immediately
        /// after
        /// <see cref="LockstepGameState.DeserializeGameState(bool, uint, LockstepGameStateOptionsData)"/> for
        /// late joiners. Meaning game states coming later in load order (the order of
        /// <see cref="LockstepAPI.AllGameStates"/>) are not yet deserialized and initialized. Use the
        /// <see cref="LockstepGameStateDependencyAttribute"/> in any scenario where cross game state
        /// interaction is required.</para>
        /// </summary>
        protected abstract void InitWidgetData();
        /// <summary>
        /// <para>This is not part of the public api, do not call.</para>
        /// </summary>
        public void InitWidgetDataInternal() => InitWidgetData();
        /// <summary>
        /// <para>Similar to <see cref="ValidateOptionsImpl"/> This function must update the
        /// <see cref="CurrentOptions"/> such that they are valid with the current game state, ready for
        /// potential use with <see cref="LockstepAPI.Export(string, LockstepGameStateOptionsData[])"/> or
        /// <see cref="LockstepAPI.StartImport(object[][], System.DateTime, string, string)"/>.</para>
        /// <para>Difference is however that this function only gets called while
        /// <see cref="CurrentlyShown"/> is <see langword="true"/> and it should use all the custom
        /// <see cref="WidgetData"/> to make <see cref="CurrentOptions"/> reflect the current state of the
        /// UI.</para>
        /// <para>Depending on implementation, this function may not even need to do anything, for example
        /// when <see cref="WidgetData.SetListener(UdonSharpBehaviour, string)"/> is used to update
        /// <see cref="CurrentOptions"/> immediately upon user interaction. Though for most simple options not
        /// using listeners and just updating whenever <see cref="UpdateCurrentOptionsFromWidgetsImpl"/> gets
        /// called is simpler.</para>
        /// </summary>
        protected abstract void UpdateCurrentOptionsFromWidgetsImpl();
        /// <summary>
        /// <para><see cref="CurrentUI"/> and <see cref="CurrentOptions"/> get populated and
        /// <see cref="CurrentlyShown"/> gets set to <see langword="true"/> all right before this event gets
        /// raised.</para>
        /// <para>It is expected for <see cref="LockstepOptionsEditorUI.Clear"/> to have been called before
        /// this event gets raised.</para>
        /// <para>This event is responsible for adding <see cref="WidgetData"/> to the given
        /// <paramref name="ui"/>/<see cref="CurrentUI"/>. It may create new <see cref="WidgetData"/>, it may
        /// use <see cref="WidgetData"/> created back in <see cref="InitWidgetData"/>.</para>
        /// <para>For information and suggestions about how to use the <see cref="LockstepOptionsEditorUI"/>
        /// api, see its documentation.</para>
        /// <para>For dynamic options, which get updated while the UI is shown even without the user actively
        /// interacting with the options UI, this event is the notification that these dynamic widgets should
        /// now start to be updated in real time. Generally dynamic widgets should only be updated while
        /// <see cref="CurrentlyShown"/> is <see langword="true"/>.</para>
        /// <para>For implementations intended for use with <see cref="LockstepGameState.ImportUI"/> this
        /// event can also use <c>Read</c> functions on the <see cref="LockstepAPI"/>. The read stream this is
        /// going to read from is the exported serialized game state data which is to be imported.
        /// Additionally The <paramref name="importedDataVersion"/> is what the
        /// <see cref="LockstepGameState.GameStateDataVersion"/> was at the time of the export.</para>
        /// </summary>
        /// <param name="ui">The same value as <see cref="CurrentUI"/>.</param>
        /// <param name="importedDataVersion">Only relevant for implementations intended for use as
        /// <see cref="LockstepGameState.ImportUI"/>.</param>
        protected abstract void OnOptionsEditorShow(LockstepOptionsEditorUI ui, uint importedDataVersion);
        /// <summary>
        /// <para><see cref="CurrentUI"/> and <see cref="CurrentOptions"/> get set to <see langword="null"/>
        /// right after this event gets raised, where as <see cref="CurrentlyShown"/> gets set to
        /// <see langword="false"/> right before this event gets raised.</para>
        /// <para>This event is mainly a notification for dynamic options, which get updated while the UI is
        /// shown even without the user actively interacting with the options UI, to stop being updated in
        /// real time. Generally dynamic widgets should only be updated while <see cref="CurrentlyShown"/> is
        /// <see langword="true"/>.</para>
        /// </summary>
        /// <param name="ui">The same value as <see cref="CurrentUI"/>.</param>
        protected abstract void OnOptionsEditorHide(LockstepOptionsEditorUI ui);

        /// <summary>
        /// <para>Updates/Validates the given <paramref name="options"/> instance ensuring that it can be used
        /// for an <see cref="LockstepAPI.Export(string, LockstepGameStateOptionsData[])"/> or
        /// <see cref="LockstepAPI.StartImport(object[][], System.DateTime, string, string)"/> for the current
        /// state the game state is in, depending on if this is an <see cref="LockstepGameState.ExportUI"/> or
        /// <see cref="LockstepGameState.ImportUI"/> accordingly.</para>
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
            if (!lockstep.InitializedEnoughForImportExport)
            {
                Debug.LogError($"[Lockstep] Attempt to call ValidateOptions before InitializedEnoughForImportExport is true.");
                return;
            }
            if (options == null)
            {
                Debug.LogError("[Lockstep] Attempt to call ValidateOptions with null options.");
                return;
            }
            options.IncrementRefsCount();
            SetProgramVariable("optionsToValidate", options);
            ValidateOptionsImpl();
            SetProgramVariable("optionsToValidate", null);
            options.DecrementRefsCount();
        }

        /// <summary>
        /// <para>Both updates the <see cref="CurrentOptions"/> to match the current state of active
        /// <see cref="WidgetData"/> and ensures that <see cref="CurrentOptions"/> is in a valid state for
        /// use with an <see cref="LockstepAPI.Export(string, LockstepGameStateOptionsData[])"/> or
        /// <see cref="LockstepAPI.StartImport(object[][], System.DateTime, string, string)"/>, depending on
        /// if this is an <see cref="LockstepGameState.ExportUI"/> or <see cref="LockstepGameState.ImportUI"/>
        /// accordingly.</para>
        /// <para>Must be called while <see cref="CurrentlyShown"/> is <see langword="false"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public void UpdateCurrentOptionsFromWidgets()
        {
            if (!currentlyShown)
            {
                Debug.LogError("[Lockstep] Attempt to call UpdateCurrentOptionsFromWidgets while CurrentlyShown is false.");
                return;
            }
            UpdateCurrentOptionsFromWidgetsImpl();
        }

        /// <summary>
        /// <para>Request for <see cref="WidgetData"/> to be added to the given <paramref name="ui"/>,
        /// representing user configurable options for an export or import, depending on if this is an
        /// <see cref="LockstepGameState.ExportUI"/> or <see cref="LockstepGameState.ImportUI"/>.</para>
        /// <para>Must be called while <see cref="CurrentlyShown"/> is <see langword="false"/>.</para>
        /// <para>Sets <see cref="CurrentlyShown"/> to <see langword="true"/>.</para>
        /// <para><see cref="LockstepOptionsEditorUI.Clear"/> should have been called since the last time this
        /// function gets called, otherwise the same <see cref="WidgetData"/> will most likely end up being
        /// used for multiple <see cref="Widget"/>s, which is not supported.</para>
        /// <para>When calling this for <see cref="LockstepGameState.ImportUI"/> instances, the associated
        /// serialized binary data must be set as the current read stream for lockstep. See
        /// <see cref="LockstepImportedGS.GetBinaryData(object[])"/> and
        /// <see cref="LockstepAPI.SetReadStream(byte[])"/>. This allows import options to read data from the
        /// associated serialized game state to populate dynamic options depending on the data to be
        /// imported.</para>
        /// <para>Holds a strong reference to <paramref name="options"/> - which is a
        /// <see cref="WannaBeClass"/> at the end of the day - so long as the UI is shown.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        /// <param name="ui">The ui to be used and to be assigned to <see cref="CurrentUI"/>.</param>
        /// <param name="options">The options to be used and to be assigned to
        /// <see cref="CurrentOptions"/>.</param>
        /// <param name="importedDataVersion">For <see cref="LockstepGameState.ImportUI"/> this parameter must
        /// be supplied with the data version matching the binary data which was set as the read stream, as
        /// described in the main summary.</param>
        public void ShowOptionsEditor(LockstepOptionsEditorUI ui, LockstepGameStateOptionsData options, uint importedDataVersion = 0u)
        {
            if (!lockstep.InitializedEnoughForImportExport)
            {
                Debug.LogError($"[Lockstep] Attempt to call ShowOptionsEditor before InitializedEnoughForImportExport is true.");
                return;
            }
            if (currentlyShown)
            {
                Debug.LogError("[Lockstep] Attempt to call ShowOptionsEditor while the UI is already shown.");
                return;
            }
            currentlyShown = true;
            currentUI = ui;
            SetCurrentOptions(options);
            OnOptionsEditorShow(ui, importedDataVersion);
        }

        /// <summary>
        /// <para>Inform this options UI that the <see cref="CurrentUI"/> has been or is being hidden, such
        /// that the options UI can stop keeping its <see cref="WidgetData"/> up to date.</para>
        /// <para>Must be called while <see cref="CurrentlyShown"/> is <see langword="true"/>.</para>
        /// <para>Sets <see cref="CurrentlyShown"/> to <see langword="false"/>.</para>
        /// <para>Releases its strong reference to <see cref="CurrentOptions"/> - which is a
        /// <see cref="WannaBeClass"/> at the end of the day - since the UI is no longer shown and
        /// <see cref="CurrentOptions"/> gets set to <see langword="null"/>.</para>
        /// <para><see cref="CurrentUI"/> also get set to <see langword="null"/>.</para>
        /// <para>Usable once <see cref="LockstepEventType.OnInit"/> or
        /// <see cref="LockstepEventType.OnClientBeginCatchUp"/> is raised.</para>
        /// </summary>
        public void HideOptionsEditor()
        {
            if (!currentlyShown)
            {
                Debug.LogError("[Lockstep] Attempt to call HideOptionsEditor while the UI is already hidden.");
                return;
            }
            currentlyShown = false;
            OnOptionsEditorHide(currentUI);
            currentUI = null;
            SetCurrentOptions(null); // After the event is raised because passing it as an argument would mean
            // the programmer has to type cast it inside of the event, which the currentOptions field is meant
            // to avoid entirely.
        }
    }
}
