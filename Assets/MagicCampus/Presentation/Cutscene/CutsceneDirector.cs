using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using UnityEngine;

namespace MagicCampus.Presentation.Cutscene
{
    /// <summary>
    /// Owner-scoped camera interpolation and registered visual clips. Visual-only: it never writes logical or saved
    /// positions. A finished camera move stays at its target until the owner is closed. Time is advanced by the caller
    /// through Advance, so the pause policy is applied outside this class.
    /// </summary>
    public sealed class CutsceneDirector
    {
        sealed class CameraTween
        {
            public IHandleController<bool> Ctl;
            public string Owner;
            public Vector3 From;
            public Vector3 To;
            public float Duration;
            public float Elapsed;
        }

        sealed class AnimTween
        {
            public IHandleController<bool> Ctl;
            public string Owner;
            public Transform Visual;
            public VisualClipCallback Clip;
            public VisualPose Origin;
            public float Duration;
            public float Elapsed;
        }

        sealed class VisualHold
        {
            public string Owner;
            public VisualPose Pose;
        }

        readonly IHandleFactory handles;
        readonly IMapPort map;
        readonly IActorVisualResolver actors;
        readonly Dictionary<string, VisualClipCallback> clips = new Dictionary<string, VisualClipCallback>(StringComparer.Ordinal);
        readonly List<AnimTween> anims = new List<AnimTween>();
        readonly Dictionary<Transform, VisualHold> holds = new Dictionary<Transform, VisualHold>();
        CameraTween camera;
        string cameraOwner;
        Camera cameraHeld;
        Vector3 cameraOrigPos;
        Quaternion cameraOrigRot;
        int serial;

        /// <param name="handles">Required handle factory.</param>
        /// <param name="map">Optional. When given, camera targets must be on its active map.</param>
        /// <param name="actors">Optional. Without it every Animate fails with REF_NOT_FOUND.</param>
        public CutsceneDirector(IHandleFactory handles, IMapPort map, IActorVisualResolver actors)
        {
            this.handles = handles ?? throw new ArgumentNullException(nameof(handles));
            this.map = map;
            this.actors = actors;
        }

        public Camera Camera { get; set; }
        public float WorldUnitsPerTile { get; set; } = 1f;
        /// <summary>Optional tile-to-world mapping; only x and y of the result are used, the camera keeps its depth.</summary>
        public Func<Position, Vector3> TileToWorld { get; set; }

        public string CameraOwner { get { return cameraOwner; } }
        /// <summary>Resources still held (camera hold plus held visuals). Zero means no leak.</summary>
        public int HeldCount { get { return (cameraOwner != null ? 1 : 0) + holds.Count; } }
        public int ActiveCount { get { return (camera != null ? 1 : 0) + anims.Count; } }

        public void RegisterClip(string clipName, VisualClipCallback callback)
        {
            if (!PresentationHandles.ValidId(clipName)) throw new ArgumentException("invalid clip name", nameof(clipName));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (clips.ContainsKey(clipName)) throw new ArgumentException("clip already registered", nameof(clipName));
            clips.Add(clipName, callback);
        }

        public IHandle MoveCamera(Destination target, double seconds, string ownerToken)
        {
            var id = NextId("camera");
            if (target == null) return Refuse(id, ResultCodes.ContentInvalid, "camera destination is required", "destination", "null");
            if (!PresentationHandles.ValidId(ownerToken)) return Refuse(id, ResultCodes.ContentInvalid, "owner token is invalid", "valid owner token", ownerToken ?? "null");
            if (!IsDuration(seconds)) return Refuse(id, ResultCodes.ContentInvalid, "seconds must be finite and not negative", ">= 0", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var cam = Camera;
            if (cam == null) return Refuse(id, ResultCodes.Unregistered, "no presentation camera is configured", "camera", "none");
            if (map != null)
            {
                var active = map.ActiveMapId;
                if (!string.IsNullOrEmpty(active) && !string.Equals(active, target.Target.MapId, StringComparison.Ordinal))
                    return Refuse(id, ResultCodes.ContentInvalid, "camera target is not on the active map", active, target.Target.MapId);
            }
            if (camera != null) return Refuse(id, ResultCodes.ResourceBusy, "a camera move is already running", "idle camera", "running");
            if (cameraOwner != null && (!string.Equals(cameraOwner, ownerToken, StringComparison.Ordinal) || !ReferenceEquals(cameraHeld, cam)))
                return Refuse(id, ResultCodes.ResourceBusy, "the camera is held by another owner", "free camera", "held");

            var from = cam.transform.position;
            var mapper = TileToWorld;
            var w = mapper != null
                ? mapper(target.Target)
                : new Vector3((float)(target.Target.X * WorldUnitsPerTile), (float)(target.Target.Y * WorldUnitsPerTile), 0f);
            if (!IsFinite(w)) return Refuse(id, ResultCodes.ContentInvalid, "camera target is not finite in world space", "finite", "non-finite");
            var to = new Vector3(w.x, w.y, from.z);

            var ctl = handles.Create<bool>(id);
            if (cameraOwner == null)
            {
                cameraOwner = ownerToken;
                cameraHeld = cam;
                cameraOrigPos = cam.transform.position;
                cameraOrigRot = cam.transform.rotation;
            }
            var tween = new CameraTween { Ctl = ctl, Owner = ownerToken, From = from, To = to, Duration = (float)seconds, Elapsed = 0f };
            camera = tween;
            ctl.SetCancellationHandler(reason => CancelCamera(tween, reason));
            ctl.Start();
            return ctl;
        }

        public IHandle Animate(string actorId, string clip, double seconds, string ownerToken)
        {
            var id = NextId("animate");
            if (!PresentationHandles.ValidId(actorId)) return Refuse(id, ResultCodes.ContentInvalid, "actor id is invalid", "valid actor id", actorId ?? "null");
            if (!PresentationHandles.ValidId(clip)) return Refuse(id, ResultCodes.ContentInvalid, "clip name is invalid", "valid clip name", clip ?? "null");
            if (!PresentationHandles.ValidId(ownerToken)) return Refuse(id, ResultCodes.ContentInvalid, "owner token is invalid", "valid owner token", ownerToken ?? "null");
            if (!IsDuration(seconds)) return Refuse(id, ResultCodes.ContentInvalid, "seconds must be finite and not negative", ">= 0", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            VisualClipCallback cb;
            if (!clips.TryGetValue(clip, out cb)) return Refuse(id, ResultCodes.Unregistered, "clip is not registered", "registered clip", clip);
            Transform visual = null;
            if (actors == null || !actors.TryResolveVisual(actorId, out visual) || visual == null)
                return Refuse(id, ResultCodes.RefNotFound, "actor visual could not be resolved", "known actor", actorId);
            if (visual.parent == null)
                return Refuse(id, ResultCodes.ContentInvalid, "clips may only drive a presentation child, not the actor root", "child transform", "root transform");
            foreach (var a in anims)
                if (ReferenceEquals(a.Visual, visual)) return Refuse(id, ResultCodes.ResourceBusy, "actor is already animating", "idle actor", "animating");

            VisualHold hold;
            var newHold = false;
            if (holds.TryGetValue(visual, out hold))
            {
                if (!string.Equals(hold.Owner, ownerToken, StringComparison.Ordinal))
                    return Refuse(id, ResultCodes.ResourceBusy, "actor visual is held by another owner", "free visual", "held");
            }
            else
            {
                hold = new VisualHold { Owner = ownerToken, Pose = VisualPose.Capture(visual) };
                holds.Add(visual, hold);
                newHold = true;
            }

            try
            {
                cb(visual, hold.Pose, 0f);
            }
            catch (Exception ex)
            {
                if (newHold)
                {
                    hold.Pose.ApplyTo(visual);
                    holds.Remove(visual);
                }
                return Refuse(id, PresentationCodes.ClipFailed, "clip callback threw: " + ex.Message, "clip completes", ex.GetType().Name);
            }

            var ctl = handles.Create<bool>(id);
            var tween = new AnimTween { Ctl = ctl, Owner = ownerToken, Visual = visual, Clip = cb, Origin = hold.Pose, Duration = (float)seconds, Elapsed = 0f };
            anims.Add(tween);
            ctl.SetCancellationHandler(reason => CancelAnim(tween, reason));
            ctl.Start();
            return ctl;
        }

        /// <summary>Advances every running tween by dt seconds. The caller decides whether the Scene channel is open.</summary>
        public void Advance(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) dt = 0f;

            var t = camera;
            if (t != null)
            {
                if (cameraHeld == null)
                {
                    camera = null;
                    cameraOwner = null;
                    t.Ctl.Fail(ResultCodes.Unregistered, 0, new[] { PresentationHandles.Diag(ResultCodes.Unregistered, "presentation camera was destroyed", "camera", "destroyed") });
                }
                else
                {
                    t.Elapsed += dt;
                    var n = t.Duration <= 0f ? 1f : Mathf.Clamp01(t.Elapsed / t.Duration);
                    var eased = n * n * (3f - 2f * n);
                    cameraHeld.transform.position = Vector3.LerpUnclamped(t.From, t.To, eased);
                    if (n >= 1f)
                    {
                        camera = null;
                        t.Ctl.Succeed(true, 0);
                    }
                }
            }

            var copy = anims.ToArray();
            for (var i = 0; i < copy.Length; i++)
            {
                var a = copy[i];
                if (!anims.Contains(a)) continue;
                if (a.Visual == null)
                {
                    anims.Remove(a);
                    holds.Remove(a.Visual);
                    a.Ctl.Fail(ResultCodes.RefNotFound, 0, new[] { PresentationHandles.Diag(ResultCodes.RefNotFound, "actor visual was destroyed", "visual", "destroyed") });
                    continue;
                }
                a.Elapsed += dt;
                var n = a.Duration <= 0f ? 1f : Mathf.Clamp01(a.Elapsed / a.Duration);
                try
                {
                    a.Clip(a.Visual, a.Origin, n);
                }
                catch (Exception ex)
                {
                    anims.Remove(a);
                    RestoreHold(a.Visual);
                    a.Ctl.Fail(PresentationCodes.ClipFailed, 0, new[] { PresentationHandles.Diag(PresentationCodes.ClipFailed, "clip callback threw: " + ex.Message, "clip completes", ex.GetType().Name) });
                    continue;
                }
                if (n >= 1f)
                {
                    anims.Remove(a);
                    a.Ctl.Succeed(true, 0);
                }
            }
        }

        /// <summary>Restores camera and visuals of the owner first, then publishes Cancelled for its running operations.</summary>
        public void CloseOwner(string ownerToken)
        {
            if (!PresentationHandles.ValidId(ownerToken)) return;
            CloseMatching(o => string.Equals(o, ownerToken, StringComparison.Ordinal), "owner closed");
        }

        public void CloseAll(string reason)
        {
            CloseMatching(o => true, reason);
        }

        void CloseMatching(Func<string, bool> match, string reason)
        {
            var pending = new List<IHandleController<bool>>();
            if (camera != null && match(camera.Owner))
            {
                pending.Add(camera.Ctl);
                camera = null;
            }
            if (cameraOwner != null && match(cameraOwner)) RestoreCamera();
            foreach (var a in anims.ToArray())
            {
                if (!match(a.Owner)) continue;
                anims.Remove(a);
                pending.Add(a.Ctl);
            }
            var keys = new List<Transform>(holds.Keys);
            foreach (var k in keys)
                if (match(holds[k].Owner)) RestoreHold(k);
            foreach (var c in pending) c.Cancelled(reason ?? string.Empty, 0);
        }

        void CancelCamera(CameraTween t, string reason)
        {
            if (ReferenceEquals(camera, t))
            {
                camera = null;
                RestoreCamera();
            }
            t.Ctl.Cancelled(reason ?? string.Empty, 0);
        }

        void CancelAnim(AnimTween a, string reason)
        {
            if (anims.Remove(a)) RestoreHold(a.Visual);
            a.Ctl.Cancelled(reason ?? string.Empty, 0);
        }

        void RestoreCamera()
        {
            if (cameraHeld != null)
            {
                cameraHeld.transform.position = cameraOrigPos;
                cameraHeld.transform.rotation = cameraOrigRot;
            }
            cameraHeld = null;
            cameraOwner = null;
        }

        void RestoreHold(Transform visual)
        {
            VisualHold h;
            if (!holds.TryGetValue(visual, out h)) return;
            if (visual != null) h.Pose.ApplyTo(visual);
            holds.Remove(visual);
        }

        string NextId(string kind)
        {
            serial++;
            return "cutscene." + kind + "." + serial.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        IHandle Refuse(string id, string code, string message, string expected, string actual)
        {
            return PresentationHandles.Failed<bool>(handles, id, code, message, expected, actual);
        }

        static bool IsDuration(double s)
        {
            return !double.IsNaN(s) && !double.IsInfinity(s) && s >= 0.0;
        }

        static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y));
        }
    }
}
