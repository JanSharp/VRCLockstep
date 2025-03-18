using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    /// <summary>
    /// <para>Just a container for <see cref="WidgetData"/> custom <see cref="LockstepGameStateOptionsUI"/>
    /// can add to, as well as a reference to the <see cref="GenericValueEditor"/> that's being drawn
    /// to.</para>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepOptionsEditorUI : UdonSharpBehaviour
    {
        [HideInInspector] [SerializeField] [SingletonReference] private WidgetManager widgetManager;
        [SerializeField] private GenericValueEditor editor;
        private GroupingWidgetData root;
        private FoldOutWidgetData general;
        private FoldOutWidgetData info;
        /// <summary>
        /// <para>Simply a reference to the widget manager singleton which is used to create new widget
        /// data. Though when using it heavily it likely makes sense to add <c>[HideInInspector]
        /// [SerializeField] [SingletonReference] private WidgetManager widgetManager;</c> to the custom
        /// script itself. (so long as the script instance exists in the scene at build time - aka is not
        /// instantiated at runtime. It's likely static the vast majority of the time, but good to keep in
        /// mind when doing unusual things.)</para>
        /// </summary>
        public WidgetManager WidgetManager => widgetManager;
        /// <summary>
        /// <para>The generic value editor instance used for this custom options UI.</para>
        /// <para>Likely hardly used directly, since the rest of the api already expose enough functionality
        /// for the vast majority of use cases. The reset of the api being <see cref="Root"/>,
        /// <see cref="Info"/>, <see cref="General"/>, notably <see cref="WidgetManager"/>,
        /// <see cref="Clear"/> and <see cref="Draw"/>.</para>
        /// </summary>
        public GenericValueEditor Editor => editor;
        /// <summary>
        /// <para>While <see cref="GenericValueEditor"/>s already have a root object with a list of children,
        /// that root is not itself a widget. So for the purposes of these options editor UIs there is simply
        /// single widget - this root widget - in the root of the <see cref="GenericValueEditor"/>, and all
        /// other systems can add do this widget whatever custom widgets they wish. Preferably appending
        /// because the <see cref="Info"/> and <see cref="General"/> widgets usually make the most sense being
        /// at the top.</para>
        /// <para>For consistency it is best to make each child of the root widget being a
        /// <see cref="FoldOutWidgetData"/>. For example for game states with several options it would have
        /// its own fold out presumably with the game state name as the title and all its options inside of
        /// the fold out widget.</para>
        /// </summary>
        public GroupingWidgetData Root => root;
        /// <summary>
        /// <para>Generally the first child of <see cref="Root"/>. It should contain static information about
        /// what is to be exported or imported.</para>
        /// <para>If it ultimately ends up having zero children by the time the editor gets
        /// <see cref="Draw"/>n, <see cref="WidgetData.IsVisible"/> gets set to <see langword="false"/>
        /// (otherwise it gets set to <see langword="true"/>).</para>
        /// </summary>
        public FoldOutWidgetData Info => info;
        /// <summary>
        /// <para>Generally the second child of <see cref="Root"/>. It should contain either some simple
        /// options like for example just a toggle to choose if a game state should be exported or imported,
        /// or otherwise options which have no clear or direct association with a specific game state.</para>
        /// <para>If it ultimately ends up having zero children by the time the
        /// editor gets <see cref="Draw"/>n, <see cref="WidgetData.IsVisible"/> gets set to
        /// <see langword="false"/> (otherwise it gets set to <see langword="true"/>).</para>
        /// </summary>
        public FoldOutWidgetData General => general;

        /// <summary>
        /// <para>To be called only once. Like in unity's Start event.</para>
        /// <para>Merely creates the widget data for <see cref="Root"/>, <see cref="Info"/> and
        /// <see cref="General"/>.</para>
        /// </summary>
        public void Init()
        {
            if (root != null)
                return;
            root = widgetManager.NewGrouping();
            info = widgetManager.NewFoldOutScope("Info", true);
            general = widgetManager.NewFoldOutScope("General Options", true);
        }

        /// <summary>
        /// <para>Clears <see cref="Root"/>, <see cref="Info"/> and <see cref="General"/> and immediately adds
        /// <see cref="Info"/> and <see cref="General"/> to the <see cref="Root"/> widget again.</para>
        /// <para>Likely best used after closing/hiding an options editor, followed by a <see cref="Draw"/>
        /// call to return all the cleared widgets to the widget pool in the
        /// <see cref="WidgetManager"/>.</para>
        /// <para>Probably good to call after all calls to
        /// <see cref="LockstepGameStateOptionsUI.HideOptionsEditor"/> rather than before.</para>
        /// <para>Likely also good to be called before an options editor is opened/shown, before calling
        /// <see cref="LockstepGameStateOptionsUI.ShowOptionsEditor(LockstepOptionsEditorUI,
        /// LockstepGameStateOptionsData)"/>.</para>
        /// </summary>
        public void Clear()
        {
            root.ClearChildren();
            info.ClearChildren();
            general.ClearChildren();
            root.AddChild(info);
            root.AddChild(general);
        }

        /// <summary>
        /// <para>Sets <see cref="WidgetData.IsVisible"/> of <see cref="Info"/> and <see cref="General"/>
        /// depending on if they have any children - <see langword="false"/> when no children.</para>
        /// <para>Then calls <see cref="GenericValueEditor.Draw(WidgetData[], int)"/> on <see cref="Editor"/>
        /// with an array containing a single widget, the <see cref="Root"/> widget.</para>
        /// <para>Call this whenever widgets have been added or removed to any of the custom widgets managed
        /// by any <see cref="LockstepGameStateOptionsUI"/>, naturally and preferably only when
        /// <see cref="LockstepGameStateOptionsUI.CurrentlyShown"/> is <see langword="true"/> for the given
        /// custom options UI in question.</para>
        /// </summary>
        public void Draw()
        {
            info.IsVisible = info.childWidgetsCount != 0;
            general.IsVisible = general.childWidgetsCount != 0;
            editor.Draw(new WidgetData[] { root });
        }
    }
}
