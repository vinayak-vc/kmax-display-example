using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KmaxXR
{
    [CustomEditor(typeof(KmaxStylus), true)]
    public class KmaxStylusEditor : Editor
    {
        private string[] tips;
        private GUIContent previewTitle;
        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            if (changed || tips == null)
            {
                var stylus = (KmaxStylus)target;
                tips = stylus.CheckPropertyValid();
            }
            for (int i = 0; i < tips.Length; i++)
            {
                EditorGUILayout.HelpBox(tips[i], MessageType.Warning);
            }
        }

        public override bool HasPreviewGUI()
        {
            return Application.isPlaying;
        }

        public override GUIContent GetPreviewTitle()
        {
            if (previewTitle == null)
            {
                previewTitle = new GUIContent(base.GetPreviewTitle());
                previewTitle.text = nameof(KmaxStylus);
            }
            return previewTitle;
        }

        private GUIStyle m_PreviewLabelStyle;

        protected GUIStyle previewLabelStyle
        {
            get
            {
                if (m_PreviewLabelStyle == null)
                {
                    m_PreviewLabelStyle = new GUIStyle("PreOverlayLabel")
                    {
                        richText = true,
                        alignment = TextAnchor.UpperLeft,
                        fontStyle = FontStyle.Normal
                    };
                }

                return m_PreviewLabelStyle;
            }
        }

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            var stylus = target as KmaxStylus;
            if (stylus == null)
                return;
            
            GUI.Label(rect, $"<b>Pointer Id:</b> {stylus.Id}\n\n<b>Pointer State:</b>\n{stylus.pointerState}", previewLabelStyle);
        }
    }
}
