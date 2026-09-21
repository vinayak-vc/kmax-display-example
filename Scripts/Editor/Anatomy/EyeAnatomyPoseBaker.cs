using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Bakes the eye's assembled and exploded poses out of the model's imported animation clips.
    ///
    /// The model ships 23 clips, one per part, and they are a continuous pulse rather than a
    /// one-shot explode - the parts collapse, spread again and collapse again over 2.53 s. So
    /// rather than replaying a clip, this walks the merged position curves, finds the sample
    /// times where the parts are most tightly packed and most spread out, and stores those two
    /// poses for the runtime to interpolate between.
    /// </summary>
    public static class EyeAnatomyPoseBaker {
        private const string ModelPath = "Assets/Games/kmax-display-example/Model/EyeAnatomy.glb";
        private const string PoseSetPath = "Assets/Games/kmax-display-example/Data/EyeExplodePoses.asset";
        private const int SampleCount = 240;

        [MenuItem("Kmax/Eye Anatomy/Bake Explode Poses")]
        public static void Bake() {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            if (assets == null || assets.Length == 0) {
                Debug.LogError($"{nameof(EyeAnatomyPoseBaker)}: no asset at '{ModelPath}'.");
                return;
            }

            Dictionary<string, AnimationCurve[]> curves = new Dictionary<string, AnimationCurve[]>();
            float clipLength = 0f;

            for (int i = 0; i < assets.Length; i++) {
                AnimationClip clip = assets[i] as AnimationClip;
                if (clip == null) {
                    continue;
                }

                clipLength = Mathf.Max(clipLength, clip.length);
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                for (int b = 0; b < bindings.Length; b++) {
                    int axis = GetPositionAxis(bindings[b].propertyName);
                    if (axis < 0) {
                        continue;
                    }

                    if (!curves.ContainsKey(bindings[b].path)) {
                        curves[bindings[b].path] = new AnimationCurve[3];
                    }

                    curves[bindings[b].path][axis] = AnimationUtility.GetEditorCurve(clip, bindings[b]);
                }
            }

            if (curves.Count == 0) {
                Debug.LogError($"{nameof(EyeAnatomyPoseBaker)}: '{ModelPath}' contains no position animation to bake.");
                return;
            }

            List<string> paths = new List<string>(curves.Keys);
            paths.Sort();

            float closedTime = 0f;
            float openTime = 0f;
            float smallestSpread = float.MaxValue;
            float largestSpread = float.MinValue;
            Vector3[] sample = new Vector3[paths.Count];

            for (int s = 0; s < SampleCount; s++) {
                float time = clipLength * s / (SampleCount - 1);
                EvaluateAt(paths, curves, time, sample);
                float spread = MeasureSpread(sample);

                if (spread < smallestSpread) {
                    smallestSpread = spread;
                    closedTime = time;
                }

                if (spread > largestSpread) {
                    largestSpread = spread;
                    openTime = time;
                }
            }

            Vector3[] closed = new Vector3[paths.Count];
            Vector3[] open = new Vector3[paths.Count];
            EvaluateAt(paths, curves, closedTime, closed);
            EvaluateAt(paths, curves, openTime, open);

            EyePartPose[] poses = new EyePartPose[paths.Count];
            for (int i = 0; i < paths.Count; i++) {
                poses[i] = new EyePartPose(paths[i], closed[i], open[i]);
            }

            EyeExplodePoseSet poseSet = AssetDatabase.LoadAssetAtPath<EyeExplodePoseSet>(PoseSetPath);
            bool created = false;
            if (poseSet == null) {
                poseSet = ScriptableObject.CreateInstance<EyeExplodePoseSet>();
                created = true;
            }

            poseSet.SetPoses(poses);

            if (created) {
                AssetDatabase.CreateAsset(poseSet, PoseSetPath);
            } else {
                EditorUtility.SetDirty(poseSet);
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"{nameof(EyeAnatomyPoseBaker)}: baked {poses.Length} parts into '{PoseSetPath}'. " +
                $"Closed pose at t={closedTime:F3}s (spread {smallestSpread:F1}), " +
                $"open pose at t={openTime:F3}s (spread {largestSpread:F1}).");
        }

        private static int GetPositionAxis(string propertyName) {
            if (!propertyName.StartsWith("m_LocalPosition")) {
                return -1;
            }

            if (propertyName.EndsWith(".x")) {
                return 0;
            }

            if (propertyName.EndsWith(".y")) {
                return 1;
            }

            if (propertyName.EndsWith(".z")) {
                return 2;
            }

            return -1;
        }

        private static void EvaluateAt(List<string> paths, Dictionary<string, AnimationCurve[]> curves, float time, Vector3[] result) {
            for (int i = 0; i < paths.Count; i++) {
                AnimationCurve[] axes = curves[paths[i]];
                result[i] = new Vector3(
                    axes[0] != null ? axes[0].Evaluate(time) : 0f,
                    axes[1] != null ? axes[1].Evaluate(time) : 0f,
                    axes[2] != null ? axes[2].Evaluate(time) : 0f);
            }
        }

        private static float MeasureSpread(Vector3[] positions) {
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < positions.Length; i++) {
                centre += positions[i];
            }

            centre /= positions.Length;

            float total = 0f;
            for (int i = 0; i < positions.Length; i++) {
                total += Vector3.Distance(positions[i], centre);
            }

            return total;
        }
    }
}