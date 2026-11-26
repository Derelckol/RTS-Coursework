using UnityEditor;
using UnityEngine;

namespace Rts.Editor
{
    [CustomEditor(typeof(OpponentDirector))]
    internal sealed class OpponentDirectorInspector : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (Application.isPlaying)
            {
                string report = ((OpponentDirector)target).DebugRoster;
                if (!string.IsNullOrEmpty(report))
                {
                    EditorGUILayout.HelpBox(report, MessageType.None);
                }
            }
        }
    }
}
