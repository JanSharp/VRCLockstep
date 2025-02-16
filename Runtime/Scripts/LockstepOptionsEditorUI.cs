using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    /// <summary>
    /// <para>Just a container for the widgets custom option UIs can add to, as well as a reference to the
    /// editor that's being drawn to.</para>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepOptionsEditorUI : UdonSharpBehaviour
    {
        [HideInInspector] [SerializeField] [SingletonReference] private WidgetManager widgetManager;
        [SerializeField] private GenericValueEditor editor;
        private GroupingWidgetData root;
        private FoldOutWidgetData general;
        private FoldOutWidgetData info;
        public WidgetManager WidgetManager => widgetManager;
        public GenericValueEditor Editor => editor;
        public GroupingWidgetData Root => root;
        public FoldOutWidgetData Info => info;
        public FoldOutWidgetData General => general;

        public void Init()
        {
            root = widgetManager.NewGrouping();
            info = widgetManager.NewFoldOutScope("Info", true);
            general = widgetManager.NewFoldOutScope("General Options", true);
        }

        public void Clear()
        {
            root.ClearChildren();
            info.ClearChildren();
            general.ClearChildren();
            root.AddChild(info);
            root.AddChild(general);
        }

        public void Draw()
        {
            info.IsVisible = info.childWidgetsCount != 0;
            general.IsVisible = general.childWidgetsCount != 0;
            editor.Draw(new WidgetData[] { root });
        }
    }
}
