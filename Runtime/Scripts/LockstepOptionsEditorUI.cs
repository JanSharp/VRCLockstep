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
        public GenericValueEditor Editor => editor;
        public GroupingWidgetData Root => root;
        public FoldOutWidgetData General => general;

        public void Init()
        {
            root = editor.NewGrouping();
            general = root.AddChild(editor.NewFoldOutScope("General Options", true));
        }

        public void Draw()
        {
            editor.Draw(new WidgetData[] { root });
        }
    }
}
