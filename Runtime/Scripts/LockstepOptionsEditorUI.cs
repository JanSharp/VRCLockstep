using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LockstepOptionsEditorUI : UdonSharpBehaviour
    {
        [SerializeField] private GenericValueEditor editor;
        private GroupingWidgetData root;
        private FoldOutWidgetData general;
        private FoldOutWidgetData info;
        public GenericValueEditor Editor => editor;
        public GroupingWidgetData Root => root;
        public FoldOutWidgetData Info => info;
        public FoldOutWidgetData General => general;

        public void Init()
        {
            root = editor.NewGrouping();
            info = editor.NewFoldOutScope("Info", true);
            general = editor.NewFoldOutScope("General Options", true);
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
