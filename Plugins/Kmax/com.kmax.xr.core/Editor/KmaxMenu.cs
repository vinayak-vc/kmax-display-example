using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEngine.Rendering;

namespace KmaxXR
{
    /// <summary>
    /// 编辑器菜单
    /// </summary>
    public class KmaxMenu
    {
        public const string COMPANY_NAME = "Kmax";
        const string GAMEOBJECT_EXT = "GameObject/" + COMPANY_NAME;
        public const string XR_DISPLAY_ENABLE = "EnableStereoDisplay";
        public const string MENU_XR_ENABLE = COMPANY_NAME + "/Enable Stereo Display";

        [MenuItem(GAMEOBJECT_EXT + "/Add XRRig", false, 10)]
        static void AddXRRig()
        {
            CreateGameObjectFromPrefab<XRRig>(nameof(XRRig));
        }

        [MenuItem(GAMEOBJECT_EXT + "/Add XRRig Unpacked", false, 10)]
        static void AddXRRigUnpacked()
        {
            CreateGameObjectFromPrefab<XRRig>(nameof(XRRig), true, true);
        }

        [MenuItem(GAMEOBJECT_EXT + "/Add XRRig", true)]
        [MenuItem(GAMEOBJECT_EXT + "/Add XRRig Unpacked", true)]
        static bool AddXRRigValid()
        {
#if UNITY_2022_3_OR_NEWER
            return GameObject.FindFirstObjectByType<XRRig>() == null;
#else
            return GameObject.FindObjectOfType<XRRig>() == null;
#endif
        }

        [MenuItem(GAMEOBJECT_EXT + "/Convert to KmaxInputModule", false)]
        static void ConvertInputModule()
        {
            var cs = Selection.activeGameObject.GetComponents<BaseInputModule>();
            foreach (var item in cs)
            {
                Undo.DestroyObjectImmediate(item);
            }
            Undo.AddComponent<KmaxInputModule>(Selection.activeGameObject);
        }

        [MenuItem(GAMEOBJECT_EXT + "/Convert to KmaxInputModule", true)]
        static bool ConvertInputModuleValid()
        {
            return Selection.activeGameObject != null &&
                Selection.activeGameObject.GetComponent<EventSystem>() != null;
        }

        [MenuItem(GAMEOBJECT_EXT + "/Fix Canvas", false)]
        public static void FixCanvas()
        {
            if (AddXRRigValid())
            {
                Debug.LogError("XRRig not found");
                return;
            }
            var caster = Selection.activeGameObject.GetComponent<KmaxUIRaycaster>();
            if (caster == null)
            {
                caster = Undo.AddComponent<KmaxUIRaycaster>(Selection.activeGameObject);
            }
            var fix = Selection.activeGameObject.GetComponent<UIScaler>();
            if (fix == null)
            {
                fix = Undo.AddComponent<UIScaler>(Selection.activeGameObject);
            }
            Undo.RegisterCompleteObjectUndo(Selection.activeTransform, "Fix Canvas");
            var canvas = Selection.activeGameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            fix.FixSize(XRRig.ViewSize);
            fix.FixPose(XRRig.ScreenTrans);
            EditorUtility.SetDirty(Selection.activeTransform);
        }

        [MenuItem(GAMEOBJECT_EXT + "/Fix Canvas", true)]
        static bool FixCanvasValid()
        {
            if (Selection.activeGameObject == null) return false;
            var canvas = Selection.activeGameObject.GetComponent<Canvas>();
            if (canvas == null) return false;
            return true;
        }

        [MenuItem(MENU_XR_ENABLE)]
        static void ToggleStereoDisplay()
        {
            bool enable = EditorPrefs.GetBool(XR_DISPLAY_ENABLE);
            EditorPrefs.SetBool(XR_DISPLAY_ENABLE, !enable);
            Menu.SetChecked(MENU_XR_ENABLE, !enable);
        }

        [MenuItem(MENU_XR_ENABLE, true)]
        static bool StereoDisplayValid()
        {
#if UNITY_EDITOR_WIN
            bool valid = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11;
            bool enable = EditorPrefs.GetBool(XR_DISPLAY_ENABLE);
            Menu.SetChecked(MENU_XR_ENABLE, enable && valid);
            return valid;
#else
            return false;
#endif
        }

        private static T CreateGameObject<T>(
            string name, bool setSelected = true, Transform parent = null)
            where T : Component
        {
            // Create the game object.
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent);
            gameObject.transform.SetAsLastSibling();

            // Register this operation with the Unity Editor's undo stack.
            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");

            // Determine whether to select the newly created Game Object
            // in the Unity Inspector window.
            if (setSelected)
            {
                Selection.activeGameObject = gameObject;
            }

            // Create the specified component.
            T component = gameObject.AddComponent<T>();

            return component;
        }

        const string PrefabAssetRelativePath = "Editor Resources";
        private static T CreateGameObjectFromPrefab<T>(
            string name, bool setSelected = true, bool unpack = false, Transform parent = null)
            where T : Component
        {
            // Attempt to find a reference to the prefab asset.
            GameObject prefab = FindAsset<GameObject>(
                $"{name} t:prefab", PrefabAssetRelativePath);

            if (prefab == null)
            {
                Debug.LogError($"Failed to create instance of {name}. Prefab not found.");
                return null;
            }

            // Create an instance of the prefab.
            var obj = PrefabUtility.InstantiatePrefab(prefab);
            GameObject gameObject = obj as GameObject;
            if (gameObject == null) return null;
            //GameObject gameObject = GameObject.Instantiate(prefab);
            gameObject.transform.SetParent(parent);
            gameObject.transform.SetAsLastSibling();
            //gameObject.name = name;

            // Register the operation with the Unity Editor's undo stack.
            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");

            // Determine whether to select the newly created prefab instance
            // in the Unity Inspector window.
            if (setSelected)
            {
                Selection.activeGameObject = gameObject;
            }

            // 解除对预制体的引用
            if (unpack)
            {
                PrefabUtility.UnpackPrefabInstance(gameObject,
                    PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            }

            return gameObject.GetComponent<T>();
        }

        private static T FindAsset<T>(string filter, string relativePath = null)
            where T : Object
        {
            string[] guids = AssetDatabase.FindAssets(filter);

            for (int i = 0; i < guids.Length; ++i)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (string.IsNullOrEmpty(relativePath) ||
                    assetPath.Contains(relativePath))
                {
                    return (T)AssetDatabase.LoadAssetAtPath(assetPath, typeof(T));
                }
            }

            return null;
        }
    }

}
