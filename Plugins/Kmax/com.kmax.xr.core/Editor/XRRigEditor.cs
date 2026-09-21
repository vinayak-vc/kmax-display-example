using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace KmaxXR
{
    [CustomEditor(typeof(XRRig))]
    public class XRRigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.HelpBox($"Screen Size: {XRRig.Screen.Size:F3}\nView Size: {XRRig.ViewSize:F3}", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            bool mono = XRRig.MonoDisplayMode;
            if (GUILayout.Button(mono ? "Switch to Side By Side" : "Switch to Mono"))
                XRRig.MonoDisplayMode = !mono;
            EditorGUILayout.EndHorizontal();
        }
    }

}
