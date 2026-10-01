using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using ViitorCloud.KmaxShowcase;

namespace ViitorCloud.KmaxShowcase.Editor {
    /// <summary>
    /// Walks the open scene and reports everything that breaks the stereo window or leaves the
    /// depth budget.
    ///
    /// This exists because neither failure is visible while authoring. A window violation looks
    /// completely correct flat - the object is simply near the edge of the frame - and only falls
    /// apart in stereo, on the device, in front of a viewer. Content outside the depth budget
    /// looks fine too, and reads as eye strain twenty minutes later rather than as an obvious
    /// mistake. Both are cheap to detect from the geometry and nearly impossible to catch by eye,
    /// which is exactly the shape of thing that should be a tool rather than a discipline.
    ///
    /// <para>Run it before every build, and after any change to <c>XRRig.ViewScale</c> - the whole
    /// budget scales with it, so a scene that passed at one scale can fail at another without
    /// anything in it having moved.</para>
    /// </summary>
    public static class ShowcaseComfortValidator {
        private const string MenuPath = "Kmax/Showcase/Audit Comfort Volume";

        [MenuItem(MenuPath)]
        public static void Audit() {
            if (!StereoVolume.IsReady) {
                Debug.LogError("Comfort audit: no XRRig in the open scene, so there is no comfort " +
                    "volume to measure against. Add the Kmax rig first.");
                return;
            }

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            List<Object> offenders = new List<Object>();
            int windowBreaks = 0;
            int depthBreaks = 0;
            float worstOvershoot = 0f;

            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) {
                    continue;
                }
                if (renderer is ParticleSystemRenderer) {
                    // Particle bounds are whatever the last simulated frame produced, which in the
                    // editor is usually nothing. Judged at runtime by ComfortOverlay instead.
                    continue;
                }

                Bounds bounds = renderer.bounds;
                bool flagged = false;

                float overshoot;
                if (StereoVolume.ViolatesWindow(bounds, out overshoot)) {
                    windowBreaks++;
                    flagged = true;
                    if (overshoot > worstOvershoot) {
                        worstOvershoot = overshoot;
                    }
                    Debug.LogWarning($"Window violation: '{GetPath(renderer.transform)}' is in front " +
                        $"of the glass and projects {overshoot * 1000f:F1} mm past the frame edge. " +
                        "Disparity says it is nearer than the screen while the bezel occludes it, " +
                        "which reads as the display being broken.", renderer);
                }

                float nearest;
                float furthest;
                if (StereoVolume.ExceedsComfortDepth(bounds, out nearest, out furthest)) {
                    depthBreaks++;
                    flagged = true;
                    Debug.LogWarning($"Outside the depth budget: '{GetPath(renderer.transform)}' spans " +
                        $"{nearest * 1000f:F0} mm to {furthest * 1000f:F0} mm, where the budget is " +
                        $"{-StereoVolume.PopOutLimit * 1000f:F0} mm to " +
                        $"{StereoVolume.DepthLimit * 1000f:F0} mm.", renderer);
                }

                if (flagged) {
                    offenders.Add(renderer.gameObject);
                }
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Comfort audit");
            summary.AppendLine($"  renderers checked   {renderers.Length}");
            summary.AppendLine($"  window violations   {windowBreaks}");
            summary.AppendLine($"  outside depth       {depthBreaks}");
            summary.AppendLine($"  pop-out budget      {StereoVolume.PopOutLimit * 1000f:F0} mm");
            summary.AppendLine($"  depth budget        {StereoVolume.DepthLimit * 1000f:F0} mm");
            summary.AppendLine($"  window              {StereoVolume.Window.x * 1000f:F0} x " +
                $"{StereoVolume.Window.y * 1000f:F0} mm");
            summary.AppendLine($"  view scale          {StereoVolume.ViewScale:F2}");
            if (windowBreaks > 0) {
                summary.AppendLine($"  worst overshoot     {worstOvershoot * 1000f:F1} mm");
            }
            summary.Append("  The window test assumes a centred viewer. Head tracking moves the " +
                "real eyes around that, so leave margin.");

            if (offenders.Count > 0) {
                Selection.objects = offenders.ToArray();
                Debug.LogWarning(summary.ToString());
                return;
            }
            Debug.Log(summary.ToString());
        }

        private static string GetPath(Transform target) {
            StringBuilder path = new StringBuilder(target.name);
            Transform current = target.parent;
            while (current != null) {
                path.Insert(0, current.name + "/");
                current = current.parent;
            }
            return path.ToString();
        }
    }
}
