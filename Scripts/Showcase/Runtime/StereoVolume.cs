using KmaxXR;
using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// The display's comfortable depth budget, derived from the Kmax SDK rather than typed in.
    ///
    /// Two numbers decide the shape of every scene in this suite, and both come out of the SDK:
    /// <see cref="StereoCamera.DefaultDistance"/> puts the stereo camera half a metre from the
    /// screen plane, and <c>XRRig.DrawFrustum</c> draws its comfort-zone gizmo at 0.37 and 0.80
    /// from that camera. Subtracting gives <b>0.13 m of pop-out</b> in front of the glass and
    /// <b>0.30 m of depth</b> behind it, and all of it multiplies by <see cref="XRRig.ViewScale"/>,
    /// which is settable at runtime.
    ///
    /// That is why this is a type rather than a constant. A 0.13 copied into four scenes is wrong
    /// the first time anyone changes the view scale.
    ///
    /// <para>Coordinates: the volume's own space is the rig transform's. <b>+Z points away from the
    /// viewer, into the screen.</b> So a negative Z is pop-out and a positive Z is depth, which
    /// matches the sign convention in <see cref="DepthOf"/> throughout.</para>
    ///
    /// <para>Everything here reads the live rig, so nothing needs to be wired up in a scene. The
    /// SDK's own static is initialised from <c>XRRig.Awake</c> at execution order -500 in play
    /// mode, and from <c>XRRig.OnValidate</c> in the editor, so it is set before any ordinary
    /// component's <c>Awake</c> runs.</para>
    /// </summary>
    public static class StereoVolume {
        /// <summary>
        /// Near edge of the comfort zone, measured from the stereo camera. Lifted from
        /// <c>XRRig.DrawFrustum</c>, where it is a literal in the gizmo drawing code and not
        /// exposed as API. If the SDK is updated, check that this still matches.
        /// </summary>
        private const float ComfortNearFromCamera = 0.37f;

        /// <summary>
        /// Far edge of the comfort zone, measured from the stereo camera. Same source and same
        /// caveat as <see cref="ComfortNearFromCamera"/>.
        /// </summary>
        private const float ComfortFarFromCamera = 0.80f;

        private static XRRig _rig;
        private static Camera _centerCamera;
        private static int _lastResolveFrame = -1;

        /// <summary>
        /// True when a rig is present and the budget can be read. False in a scene with no
        /// <see cref="XRRig"/>, where every other member returns a harmless default.
        /// </summary>
        public static bool IsReady {
            get {
                if (_rig != null) {
                    return true;
                }
                if (_lastResolveFrame == Time.frameCount && Application.isPlaying) {
                    return false;
                }
                _lastResolveFrame = Time.frameCount;
                _rig = Object.FindFirstObjectByType<XRRig>();
                return _rig != null;
            }
        }

        /// <summary>
        /// The screen plane. Its origin is the centre of the glass and its +Z runs away from the
        /// viewer.
        /// </summary>
        public static Transform ScreenTransform {
            get {
                if (!IsReady) {
                    return null;
                }
                return _rig.transform;
            }
        }

        /// <summary>
        /// Current view scale. Every distance on this type is already multiplied by it.
        /// </summary>
        public static float ViewScale {
            get {
                if (!IsReady) {
                    return 1f;
                }
                return XRRig.ViewScale;
            }
        }

        /// <summary>
        /// Width and height of the window in world units - the physical glass, scaled.
        /// </summary>
        public static Vector2 Window {
            get {
                if (!IsReady) {
                    return Vector2.zero;
                }
                return XRRig.ViewSize;
            }
        }

        /// <summary>
        /// How far in front of the glass content may come before it stops being comfortable.
        /// 0.13 m at a view scale of 1.
        /// </summary>
        public static float PopOutLimit {
            get {
                return (StereoCamera.DefaultDistance - ComfortNearFromCamera) * ViewScale;
            }
        }

        /// <summary>
        /// How far behind the glass content may sit. 0.30 m at a view scale of 1.
        /// </summary>
        public static float DepthLimit {
            get {
                return (ComfortFarFromCamera - StereoCamera.DefaultDistance) * ViewScale;
            }
        }

        /// <summary>
        /// Nominal viewer distance from the glass. The head tracker moves the real eyes around
        /// this, which is what <see cref="ViolatesWindow(Bounds)"/> cannot account for.
        /// </summary>
        public static float EyeDistance {
            get {
                return StereoCamera.DefaultDistance * ViewScale;
            }
        }

        /// <summary>
        /// The rig's centre camera.
        ///
        /// Here because <b>this project has no <c>Camera.main</c></b> - <c>VRRenderer</c> creates
        /// its left and right cameras at runtime and neither is tagged, so the usual lookup
        /// returns null and every world-space canvas needs an event camera passed to it
        /// explicitly. Anything needing a camera for a screen-space conversion should come here
        /// rather than rediscover that.
        /// </summary>
        public static Camera CenterCamera {
            get {
                if (_centerCamera != null) {
                    return _centerCamera;
                }
                StereoCamera stereo = Object.FindFirstObjectByType<StereoCamera>();
                if (stereo == null) {
                    return null;
                }
                _centerCamera = stereo.CenterCamera;
                return _centerCamera;
            }
        }

        /// <summary>
        /// The whole comfort volume in the rig's local space, centred on the midpoint of the
        /// depth budget rather than on the glass - the budget is not symmetrical.
        /// </summary>
        public static Bounds LocalComfortBounds {
            get {
                Vector2 window = Window;
                float depth = PopOutLimit + DepthLimit;
                Vector3 centre = new Vector3(0f, 0f, (DepthLimit - PopOutLimit) * 0.5f);
                return new Bounds(centre, new Vector3(window.x, window.y, depth));
            }
        }

        /// <summary>
        /// Converts a world point into the volume's own space, where Z is parallax depth.
        /// </summary>
        public static Vector3 ToVolumeSpace(Vector3 worldPoint) {
            Transform plane = ScreenTransform;
            if (plane == null) {
                return worldPoint;
            }
            return plane.InverseTransformPoint(worldPoint);
        }

        /// <summary>
        /// Converts a point in the volume's own space back to world space.
        /// </summary>
        public static Vector3 ToWorld(Vector3 volumePoint) {
            Transform plane = ScreenTransform;
            if (plane == null) {
                return volumePoint;
            }
            return plane.TransformPoint(volumePoint);
        }

        /// <summary>
        /// Signed parallax depth of a world point. <b>Negative is pop-out</b>, in front of the
        /// glass; positive is behind it; zero is exactly on the screen plane, which is the most
        /// comfortable and most precise place on the display.
        /// </summary>
        public static float DepthOf(Vector3 worldPoint) {
            return ToVolumeSpace(worldPoint).z;
        }

        /// <summary>
        /// True when a world point sits inside the comfort volume on all three axes.
        /// </summary>
        public static bool Contains(Vector3 worldPoint) {
            if (!IsReady) {
                return true;
            }
            Vector3 local = ToVolumeSpace(worldPoint);
            Vector2 half = Window * 0.5f;
            if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y) {
                return false;
            }
            return local.z >= -PopOutLimit && local.z <= DepthLimit;
        }

        /// <summary>
        /// The nearest point inside the comfort volume. Useful for holding a grabbed object at a
        /// legible depth without letting the viewer drag it into their own face.
        /// </summary>
        public static Vector3 ClampToComfort(Vector3 worldPoint) {
            if (!IsReady) {
                return worldPoint;
            }
            Vector3 local = ToVolumeSpace(worldPoint);
            Vector2 half = Window * 0.5f;
            local.x = Mathf.Clamp(local.x, -half.x, half.x);
            local.y = Mathf.Clamp(local.y, -half.y, half.y);
            local.z = Mathf.Clamp(local.z, -PopOutLimit, DepthLimit);
            return ToWorld(local);
        }

        /// <summary>
        /// Clamps only the parallax depth, leaving the point where it is across the glass.
        /// </summary>
        public static Vector3 ClampDepth(Vector3 worldPoint) {
            if (!IsReady) {
                return worldPoint;
            }
            Vector3 local = ToVolumeSpace(worldPoint);
            local.z = Mathf.Clamp(local.z, -PopOutLimit, DepthLimit);
            return ToWorld(local);
        }

        /// <summary>
        /// True when bounds sit outside the depth budget at either end.
        /// </summary>
        public static bool ExceedsComfortDepth(Bounds worldBounds, out float nearest, out float furthest) {
            nearest = 0f;
            furthest = 0f;
            if (!IsReady) {
                return false;
            }
            bool first = true;
            for (int i = 0; i < 8; i++) {
                float z = ToVolumeSpace(CornerOf(worldBounds, i)).z;
                if (first) {
                    nearest = z;
                    furthest = z;
                    first = false;
                    continue;
                }
                if (z < nearest) {
                    nearest = z;
                }
                if (z > furthest) {
                    furthest = z;
                }
            }
            return nearest < -PopOutLimit || furthest > DepthLimit;
        }

        /// <summary>
        /// How far inside the window a point <b>appears</b>, in world units. Positive is inside,
        /// negative is off the edge, and zero is exactly on it.
        ///
        /// This is the same projection <see cref="ViolatesWindow(Bounds)"/> runs, exposed as a
        /// continuous number rather than a yes or no, because content that has to stay clear of
        /// the frame wants to fade out as it approaches rather than vanish when it crosses. Bloom
        /// uses it to retire a mote before it can break the window; Probe uses it to keep a
        /// generated path away from the edges.
        ///
        /// <para>Note that this shrinks as a point comes forward even if it does not move
        /// sideways at all - projection from the eye magnifies anything in front of the glass, so
        /// a mote drifting straight at the viewer runs out of margin on its own.</para>
        /// </summary>
        public static float ProjectedMargin(Vector3 worldPoint) {
            if (!IsReady) {
                return 0f;
            }
            Vector3 local = ToVolumeSpace(worldPoint);
            float eye = EyeDistance;
            float toEye = local.z + eye;
            if (toEye <= Mathf.Epsilon) {
                return float.NegativeInfinity;
            }
            float scale = eye / toEye;
            Vector2 half = Window * 0.5f;
            float marginX = half.x - Mathf.Abs(local.x * scale);
            float marginY = half.y - Mathf.Abs(local.y * scale);
            return Mathf.Min(marginX, marginY);
        }

        /// <summary>
        /// True when these bounds would break the stereo window.
        ///
        /// A window violation is the single most common way a pop-out effect fails, and it is
        /// nearly impossible to spot while authoring. An object in front of the glass that is
        /// clipped by the frame edge gives the viewer two contradictory depth cues at once -
        /// disparity says the object is nearer than the screen, occlusion says the screen's own
        /// bezel is in front of it - and the brain resolves that by discarding the stereo effect.
        /// It reads as the display being broken rather than as the object being badly placed.
        ///
        /// The test projects every corner that carries negative parallax from the nominal eye
        /// position onto the screen plane and asks whether the projection leaves the window.
        /// Content entirely at or behind the glass can never violate it, and returns false
        /// immediately.
        ///
        /// <para><b>This is a floor, not a ceiling.</b> It uses the nominal eye position, and the
        /// head tracker moves the real eyes around it - leaning sideways pushes content towards
        /// the edge it is leaning away from. Passing this test means a centred viewer is safe, not
        /// that every viewer is. Leave margin.</para>
        /// </summary>
        public static bool ViolatesWindow(Bounds worldBounds) {
            float overshoot;
            return ViolatesWindow(worldBounds, out overshoot);
        }

        /// <summary>
        /// As <see cref="ViolatesWindow(Bounds)"/>, and reports how far past the window edge the
        /// worst corner projects, in world units. Zero when there is no violation.
        /// </summary>
        public static bool ViolatesWindow(Bounds worldBounds, out float overshoot) {
            overshoot = 0f;
            if (!IsReady) {
                return false;
            }
            Transform plane = ScreenTransform;
            if (plane == null) {
                return false;
            }
            Vector2 half = Window * 0.5f;
            float eye = EyeDistance;
            for (int i = 0; i < 8; i++) {
                Vector3 local = plane.InverseTransformPoint(CornerOf(worldBounds, i));
                if (local.z >= 0f) {
                    continue;
                }
                float toEye = local.z + eye;
                if (toEye <= Mathf.Epsilon) {
                    // At or behind the viewer's own eye. Far past any comfort limit, and the
                    // projection below would divide by zero or flip, so call it the worst case.
                    overshoot = Mathf.Infinity;
                    return true;
                }
                float scale = eye / toEye;
                float projectedX = Mathf.Abs(local.x * scale) - half.x;
                float projectedY = Mathf.Abs(local.y * scale) - half.y;
                float worst = Mathf.Max(projectedX, projectedY);
                if (worst > overshoot) {
                    overshoot = worst;
                }
            }
            return overshoot > 0f;
        }

        /// <summary>
        /// One of the eight corners of a bounding box, indexed by the low three bits.
        /// </summary>
        private static Vector3 CornerOf(Bounds bounds, int index) {
            Vector3 extents = bounds.extents;
            return bounds.center + new Vector3(
                (index & 1) == 0 ? -extents.x : extents.x,
                (index & 2) == 0 ? -extents.y : extents.y,
                (index & 4) == 0 ? -extents.z : extents.z);
        }
    }
}
