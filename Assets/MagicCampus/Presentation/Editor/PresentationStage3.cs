#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicCampus.Contracts;
using MagicCampus.Presentation.Dialogue;
using UnityEditor;
using UnityEngine;

namespace MagicCampus.Presentation.Editor
{
    /// <summary>
    /// Engine-level checks for the dialogue/cutscene adapter. They exercise the real component and real Unity
    /// Transform/Camera objects with local fake ports; they are not screenshots and do not judge how the UI looks.
    /// </summary>
    public static class PresentationStage3
    {
        const string PrefabDir = "Assets/MagicCampus/Presentation/Resources/MagicCampus";
        const string PrefabPath = PrefabDir + "/Dialogue.prefab";

        public static void BuildAndTest()
        {
            var check = new Check();
            try
            {
                var prefab = BuildPrefab(check);
                if (prefab != null)
                {
                    Guard(check, "say", () => SayScenario(check, prefab));
                    Guard(check, "choose", () => ChooseScenario(check, prefab));
                    Guard(check, "cancel", () => CancelScenario(check, prefab));
                    Guard(check, "validation", () => ValidationScenario(check, prefab));
                    Guard(check, "missing", () => MissingScenario(check, prefab));
                    Guard(check, "camera", () => CameraScenario(check, prefab));
                    Guard(check, "animation", () => AnimationScenario(check, prefab));
                    Guard(check, "cancel-camera", () => CameraCancelScenario(check, prefab));
                    Guard(check, "owner-close", () => OwnerCloseScenario(check, prefab));
                    Guard(check, "input", () => InputScenario(check, prefab));
                    Guard(check, "focus", () => FocusScenario(check, prefab));
                }
            }
            catch (Exception ex)
            {
                check.Fail("suite.exception", ex.ToString());
            }
            WriteReport(check);
            if (check.FailedCount > 0)
                throw new InvalidOperationException("Stage3 presentation checks failed: " + check.FailedCount + " of " + check.Total);
        }

        static void Guard(Check c, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { c.Fail(name + ".exception", ex.ToString()); }
        }

        // ------------------------------------------------------------------ prefab

        static GameObject BuildPrefab(Check c)
        {
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", PrefabDir)));
            var go = new GameObject("MagicCampusDialogue");
            try
            {
                go.AddComponent<DialoguePresentation>();
                bool ok;
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out ok);
                c.True(ok, "prefab.saved");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            AssetDatabase.ImportAsset(PrefabPath);
            var loaded = UnityEngine.Resources.Load<GameObject>(DialoguePresentation.ResourcePath);
            c.True(loaded != null, "prefab.loads-from-resources");
            c.True(loaded != null && loaded.GetComponent<DialoguePresentation>() != null, "prefab.has-component");
            return loaded;
        }

        // ------------------------------------------------------------------ rig

        sealed class Rig : IDisposable
        {
            public readonly FakeQueue Queue = new FakeQueue();
            public readonly FakeHandleFactory Factory;
            public readonly FakePause Pause = new FakePause();
            public readonly FakeActors Actors = new FakeActors();
            public readonly GameObject Root, CamGo, ActorGo;
            public readonly Camera Cam;
            public readonly Transform Visual;
            public readonly DialoguePresentation Dlg;

            public Rig(GameObject prefab, bool withCamera, IMapPort map)
            {
                Factory = new FakeHandleFactory(Queue);
                Root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Root.hideFlags = HideFlags.HideAndDontSave;
                Dlg = Root.GetComponent<DialoguePresentation>();
                Dlg.PollHardwareInput = false;

                CamGo = new GameObject("TestCamera");
                CamGo.hideFlags = HideFlags.HideAndDontSave;
                Cam = CamGo.AddComponent<Camera>();
                CamGo.transform.position = new Vector3(0f, 0f, -10f);

                ActorGo = new GameObject("TestActor");
                ActorGo.hideFlags = HideFlags.HideAndDontSave;
                ActorGo.transform.position = new Vector3(3f, 4f, 0f);
                var vis = new GameObject("Visual");
                vis.transform.SetParent(ActorGo.transform, false);
                vis.transform.localPosition = new Vector3(0.1f, 0.2f, 0f);
                Visual = vis.transform;
                Actors.Map["npc.a"] = Visual;
                Actors.Map["npc.root"] = ActorGo.transform;

                Dlg.Configure(Factory, Pause, map, Actors);
                if (withCamera) Dlg.SetCamera(Cam);
                Dlg.RegisterClip("bob", (v, o, t) => v.localPosition = o.LocalPosition + new Vector3(0f, t, 0f));
                Dlg.RegisterClip("boom", (v, o, t) => { throw new InvalidOperationException("clip exploded"); });
            }

            public void Tick(double unpaused) { Dlg.Tick(0.0, unpaused); }

            public void Dispose()
            {
                Dlg.CloseAll("test teardown");
                UnityEngine.Object.DestroyImmediate(Root);
                UnityEngine.Object.DestroyImmediate(CamGo);
                UnityEngine.Object.DestroyImmediate(ActorGo);
            }
        }

        sealed class Probe
        {
            public int Calls;
            public Result Last;
        }

        static Probe Watch(IHandle h)
        {
            var p = new Probe();
            h.OnTerminal(r => { p.Calls++; p.Last = r; });
            return p;
        }

        static Destination Dest(string map, double x, double y)
        {
            return new Destination(string.Empty, new Position(map, x, y), 0.1);
        }

        static void NoLeak(Check c, Rig r, string name)
        {
            c.Eq(0, r.Dlg.OutstandingLocks, name + ".no-outstanding-locks");
            c.Eq(0, r.Pause.Live, name + ".no-live-pause-tokens");
            c.True(!r.Dlg.UiCaptured, name + ".ui-released");
        }

        static string LongKorean()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 24; i++)
                sb.Append("마법 캠퍼스의 새 학기가 시작되었습니다. 도서관 열람실에서 오래된 지도를 발견했는데, 그 가장자리에는 아무도 읽지 못한 문장이 적혀 있었습니다. ");
            return sb.ToString().Trim();
        }

        static IReadOnlyList<ChoiceOption> ThreeOptions()
        {
            return new[]
            {
                new ChoiceOption("fork.study", "도서관에서 오래된 지도의 비밀을 조용히 끝까지 조사해 보겠습니다"),
                new ChoiceOption("fork.ask", "교수님께 먼저 여쭤보겠습니다"),
                new ChoiceOption("fork.leave", "지금은 돌아가겠습니다")
            };
        }

        // ------------------------------------------------------------------ scenarios

        static void SayScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, true, null))
            {
                var text = LongKorean();
                var h = r.Dlg.Say("교수", text, "owner.say");
                var probe = Watch(h);
                c.True(h.Terminal == null && h.Status == OperationStatus.Running, "say.pending-while-open");
                c.Eq(DialogueWindowKind.Say, r.Dlg.ActiveWindow, "say.window-kind");
                c.Eq(text, r.Dlg.CurrentText, "say.keeps-full-long-korean-text");
                c.Eq("교수", r.Dlg.CurrentSpeaker, "say.speaker");
                c.Eq(1, r.Pause.Live, "say.holds-exactly-one-pause-token");
                var eff = r.Pause.Effective;
                c.True(eff.Player && !eff.Scene && eff.Calendar, "say.pauses-player-and-calendar");
                c.True(r.Dlg.UiCaptured, "say.ui-captured");

                var busy = r.Dlg.Say("x", "다른 문장", "owner.other");
                r.Queue.Drain();
                c.Eq(OperationStatus.Failed, busy.Status, "say.reentrant-fails");
                c.Eq(ResultCodes.ResourceBusy, busy.Terminal == null ? "" : busy.Terminal.Code, "say.reentrant-resource-busy");
                c.True(h.Terminal == null && r.Dlg.CurrentText == text, "say.old-handle-still-valid-after-reentrant");

                c.True(r.Dlg.Navigate(NavDirection.Down), "say.navigate-down-scrolls");
                c.True(r.Dlg.TextScrollLines > 0, "say.scroll-lines-increase");
                c.True(r.Dlg.Navigate(NavDirection.Up), "say.navigate-up-scrolls");
                c.False(r.Dlg.Navigate(NavDirection.Left), "say.left-not-consumed");

                c.True(r.Dlg.Confirm(), "say.first-confirm-consumed");
                c.False(r.Dlg.Confirm(), "say.duplicate-confirm-ignored");
                c.Eq(0, probe.Calls, "say.terminal-callback-not-inline");
                r.Queue.Drain();
                c.Eq(1, probe.Calls, "say.terminal-callback-exactly-once");
                c.Eq(OperationStatus.Succeeded, h.Status, "say.succeeded");
                NoLeak(c, r, "say.after-confirm");
            }
        }

        static void ChooseScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, false, null))
            {
                var opts = ThreeOptions();
                var h = r.Dlg.Choose("무엇을 하시겠습니까?", opts, "owner.choose");
                var probe = Watch(h);
                c.Eq(DialogueWindowKind.Choose, r.Dlg.ActiveWindow, "choose.window-kind");
                c.Eq(3, r.Dlg.OptionCount, "choose.shows-all-options");
                for (var i = 0; i < 3; i++) c.Eq(opts[i].Text, r.Dlg.OptionLabel(i), "choose.label-" + i);
                c.Eq(0, r.Dlg.FocusIndex, "choose.initial-focus");

                var busy = r.Dlg.Choose("다시", opts, "owner.other");
                c.Eq(ResultCodes.ResourceBusy, busy.Terminal == null ? "" : busy.Terminal.Code, "choose.reentrant-resource-busy");
                c.True(h.Terminal == null, "choose.old-handle-valid");

                r.Dlg.Navigate(NavDirection.Down); c.Eq(1, r.Dlg.FocusIndex, "choose.down-1");
                r.Dlg.Navigate(NavDirection.Down); c.Eq(2, r.Dlg.FocusIndex, "choose.down-2");
                r.Dlg.Navigate(NavDirection.Down); c.Eq(0, r.Dlg.FocusIndex, "choose.down-wraps");
                r.Dlg.Navigate(NavDirection.Up); c.Eq(2, r.Dlg.FocusIndex, "choose.up-wraps");
                r.Dlg.Navigate(NavDirection.Left); c.Eq(1, r.Dlg.FocusIndex, "choose.left-previous");
                r.Dlg.Navigate(NavDirection.Right); c.Eq(2, r.Dlg.FocusIndex, "choose.right-next");
                r.Dlg.Navigate(NavDirection.Left); c.Eq(1, r.Dlg.FocusIndex, "choose.focus-back-to-1");

                c.True(r.Dlg.Confirm(), "choose.confirm-consumed");
                c.False(r.Dlg.Confirm(), "choose.duplicate-confirm-ignored");
                c.Eq(0, probe.Calls, "choose.terminal-not-inline");
                r.Queue.Drain();
                c.Eq(1, probe.Calls, "choose.terminal-exactly-once");
                c.Eq(OperationStatus.Succeeded, h.Status, "choose.succeeded");
                c.Eq("fork.ask", h.Value == null ? "" : h.Value.Key, "choose.returns-selected-key");
                c.Eq(1, h.Value == null ? -1 : h.Value.Index, "choose.returns-selected-index");
                NoLeak(c, r, "choose.after-confirm");
            }
        }

        static void CancelScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, false, null))
            {
                var h = r.Dlg.Choose("선택", ThreeOptions(), "owner.cancel");
                c.True(r.Dlg.Cancel(), "cancel.choose-consumed");
                r.Queue.Drain();
                c.Eq(OperationStatus.Failed, h.Status, "cancel.choose-fails");
                c.Eq(ResultCodes.Cancelled, h.Terminal == null ? "" : h.Terminal.Code, "cancel.choose-code");
                c.False(r.Dlg.Confirm(), "cancel.confirm-after-cancel-ignored");
                NoLeak(c, r, "cancel.after-choose-cancel");

                var say = r.Dlg.Say("교수", "잠시만요.", "owner.cancel");
                c.True(r.Dlg.Cancel(), "cancel.say-consumed");
                c.Eq(DialogueWindowKind.None, r.Dlg.ActiveWindow, "cancel.say-closes");
                say.Cancel("external");
                r.Queue.Drain();
                c.Eq(OperationStatus.Cancelled, say.Status, "cancel.handle-cancel-finalizes-cancelled");
                NoLeak(c, r, "cancel.after-handle-cancel");
            }
        }

        static void ValidationScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, false, null))
            {
                var empty = r.Dlg.Choose("p", new ChoiceOption[0], "owner.v");
                c.Eq(ResultCodes.ContentInvalid, empty.Terminal == null ? "" : empty.Terminal.Code, "validation.empty-options");
                var dup = r.Dlg.Choose("p", new[] { new ChoiceOption("a", "하나"), new ChoiceOption("a", "둘") }, "owner.v");
                c.Eq(ResultCodes.ContentInvalid, dup.Terminal == null ? "" : dup.Terminal.Code, "validation.duplicate-keys");
                var blank = r.Dlg.Choose("p", new[] { new ChoiceOption("a", "") }, "owner.v");
                c.Eq(ResultCodes.ContentInvalid, blank.Terminal == null ? "" : blank.Terminal.Code, "validation.blank-label");
                var nullText = r.Dlg.Say("s", null, "owner.v");
                c.Eq(ResultCodes.ContentInvalid, nullText.Terminal == null ? "" : nullText.Terminal.Code, "validation.null-text");
                var badOwner = r.Dlg.Say("s", "문장", "bad owner");
                c.Eq(ResultCodes.ContentInvalid, badOwner.Terminal == null ? "" : badOwner.Terminal.Code, "validation.owner-with-space");
                r.Queue.Drain();
                NoLeak(c, r, "validation.after-refusals");
            }
        }

        static void MissingScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, true, null))
            {
                var clip = r.Dlg.Animate("npc.a", "no-such-clip", 1.0, "owner.m");
                c.Eq(ResultCodes.Unregistered, clip.Terminal == null ? "" : clip.Terminal.Code, "missing.clip-unregistered");
                var actor = r.Dlg.Animate("ghost", "bob", 1.0, "owner.m");
                c.Eq(ResultCodes.RefNotFound, actor.Terminal == null ? "" : actor.Terminal.Code, "missing.actor-ref-not-found");
                var root = r.Dlg.Animate("npc.root", "bob", 1.0, "owner.m");
                c.Eq(ResultCodes.ContentInvalid, root.Terminal == null ? "" : root.Terminal.Code, "missing.actor-root-refused");
                var boom = r.Dlg.Animate("npc.a", "boom", 1.0, "owner.m");
                c.Eq(PresentationCodes.ClipFailed, boom.Terminal == null ? "" : boom.Terminal.Code, "missing.throwing-clip-fails");
                c.Near(0.2, r.Visual.localPosition.y, 1e-5, "missing.visual-untouched-after-failures");
                NoLeak(c, r, "missing.after-refusals");
            }
            using (var r = new Rig(prefab, false, null))
            {
                var cam = r.Dlg.MoveCamera(Dest("m", 1, 1), 1.0, "owner.m");
                c.Eq(ResultCodes.Unregistered, cam.Terminal == null ? "" : cam.Terminal.Code, "missing.camera-unregistered");
                NoLeak(c, r, "missing.camera-none");
            }
            using (var r = new Rig(prefab, true, new FakeMap("m")))
            {
                var off = r.Dlg.MoveCamera(Dest("other", 1, 1), 1.0, "owner.m");
                c.Eq(ResultCodes.ContentInvalid, off.Terminal == null ? "" : off.Terminal.Code, "missing.camera-off-map-refused");
                var on = r.Dlg.MoveCamera(Dest("m", 1, 1), 1.0, "owner.m");
                c.True(on.Terminal == null, "missing.camera-on-map-accepted");
                r.Dlg.CloseOwner("owner.m");
                NoLeak(c, r, "missing.camera-map-case");
            }
        }

        static void CameraScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, true, null))
            {
                var h = r.Dlg.MoveCamera(Dest("m", 10, 4), 2.0, "owner.cam");
                c.True(h.Terminal == null, "camera.pending");
                c.Eq("owner.cam", r.Dlg.CameraOwner, "camera.owner-recorded");

                var cal = r.Pause.Acquire("calendar", new PauseSet(true, false, false, false, false));
                r.Dlg.Tick(0.0, 1.0);
                var p = r.CamGo.transform.position;
                c.Near(5.0, p.x, 1e-3, "camera.moves-with-calendar-paused-x");
                c.Near(2.0, p.y, 1e-3, "camera.moves-with-calendar-paused-y");
                c.Near(-10.0, p.z, 1e-5, "camera.depth-preserved");

                var menu = r.Pause.Acquire("menu", PauseSet.All);
                r.Dlg.Tick(0.0, 0.5);
                c.Near(5.0, r.CamGo.transform.position.x, 1e-3, "camera.frozen-under-menu-all");
                r.Pause.Release(menu);
                r.Pause.Release(cal);

                r.Dlg.Tick(0.0, 1.0);
                r.Queue.Drain();
                c.Eq(OperationStatus.Succeeded, h.Status, "camera.completes-after-resume");
                c.Near(10.0, r.CamGo.transform.position.x, 1e-4, "camera.rests-at-target");
                r.Dlg.Tick(0.0, 3.0);
                c.Near(10.0, r.CamGo.transform.position.x, 1e-4, "camera.stays-at-target");
                c.Eq("owner.cam", r.Dlg.CameraOwner, "camera.still-held-after-finish");

                var other = r.Dlg.MoveCamera(Dest("m", 1, 1), 1.0, "owner.b");
                c.Eq(ResultCodes.ResourceBusy, other.Terminal == null ? "" : other.Terminal.Code, "camera.other-owner-busy-while-held");

                r.Dlg.CloseOwner("owner.cam");
                c.Near(0.0, r.CamGo.transform.position.x, 1e-5, "camera.restored-x-on-owner-close");
                c.Near(-10.0, r.CamGo.transform.position.z, 1e-5, "camera.restored-z-on-owner-close");
                c.True(r.Dlg.CameraOwner == null, "camera.hold-released");
                NoLeak(c, r, "camera.after-owner-close");
            }
        }

        static void AnimationScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, false, null))
            {
                var root0 = r.ActorGo.transform.position;
                var h = r.Dlg.Animate("npc.a", "bob", 1.0, "owner.anim");
                c.True(h.Terminal == null, "anim.pending");
                c.Near(0.2, r.Visual.localPosition.y, 1e-5, "anim.starts-at-origin");

                var again = r.Dlg.Animate("npc.a", "bob", 1.0, "owner.anim");
                c.Eq(ResultCodes.ResourceBusy, again.Terminal == null ? "" : again.Terminal.Code, "anim.same-actor-busy");

                var cal = r.Pause.Acquire("calendar", new PauseSet(true, false, false, false, false));
                r.Dlg.Tick(0.0, 0.5);
                c.Near(0.7, r.Visual.localPosition.y, 1e-4, "anim.advances-with-calendar-paused");
                r.Pause.Release(cal);

                var menu = r.Pause.Acquire("menu", PauseSet.All);
                r.Dlg.Tick(0.0, 0.25);
                c.Near(0.7, r.Visual.localPosition.y, 1e-4, "anim.frozen-under-menu-all");
                r.Pause.Release(menu);

                r.Dlg.Tick(0.0, 0.5);
                r.Queue.Drain();
                c.Eq(OperationStatus.Succeeded, h.Status, "anim.completes");
                c.Near(1.2, r.Visual.localPosition.y, 1e-4, "anim.final-pose");
                c.True(r.ActorGo.transform.position == root0, "anim.logical-root-position-unchanged");

                r.Dlg.CloseOwner("owner.anim");
                c.Near(0.2, r.Visual.localPosition.y, 1e-5, "anim.visual-restored-y");
                c.Near(0.1, r.Visual.localPosition.x, 1e-5, "anim.visual-restored-x");
                NoLeak(c, r, "anim.after-owner-close");
            }
        }

        static void CameraCancelScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, true, null))
            {
                var h = r.Dlg.MoveCamera(Dest("m", 10, 4), 2.0, "owner.cc");
                r.Dlg.Tick(0.0, 1.0);
                c.True(r.CamGo.transform.position.x > 1.0, "cancel-camera.moved-before-cancel");
                h.Cancel("scene skipped");
                r.Queue.Drain();
                c.Eq(OperationStatus.Cancelled, h.Status, "cancel-camera.status");
                c.Near(0.0, r.CamGo.transform.position.x, 1e-5, "cancel-camera.restored");
                r.Dlg.Tick(0.0, 5.0);
                c.Near(0.0, r.CamGo.transform.position.x, 1e-5, "cancel-camera.no-further-movement");
                NoLeak(c, r, "cancel-camera.after");
                var next = r.Dlg.MoveCamera(Dest("m", 2, 2), 1.0, "owner.next");
                c.True(next.Terminal == null, "cancel-camera.camera-free-for-next-owner");
                r.Dlg.CloseOwner("owner.next");
                NoLeak(c, r, "cancel-camera.after-next");
            }
        }

        static void OwnerCloseScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, true, null))
            {
                var say = r.Dlg.Say("교수", "연출 중 대사입니다.", "owner.x");
                var cam = r.Dlg.MoveCamera(Dest("m", 8, 8), 2.0, "owner.x");
                var anim = r.Dlg.Animate("npc.a", "bob", 1.0, "owner.x");
                r.Dlg.Tick(0.0, 0.4);
                c.True(r.CamGo.transform.position.x > 0.1f, "owner-close.camera-moved");
                c.True(r.Visual.localPosition.y > 0.25f, "owner-close.visual-moved");

                r.Dlg.CloseOwner("owner.y");
                c.True(say.Terminal == null && cam.Terminal == null && anim.Terminal == null, "owner-close.unrelated-owner-leaves-others");

                r.Dlg.CloseOwner("owner.x");
                r.Queue.Drain();
                c.Eq(OperationStatus.Cancelled, say.Status, "owner-close.say-cancelled");
                c.Eq(OperationStatus.Cancelled, cam.Status, "owner-close.camera-cancelled");
                c.Eq(OperationStatus.Cancelled, anim.Status, "owner-close.anim-cancelled");
                c.Near(0.0, r.CamGo.transform.position.x, 1e-5, "owner-close.camera-restored");
                c.Near(0.2, r.Visual.localPosition.y, 1e-5, "owner-close.visual-restored");
                c.True(r.Dlg.CameraOwner == null, "owner-close.camera-hold-released");
                NoLeak(c, r, "owner-close.after");
                r.Dlg.Tick(0.0, 5.0);
                c.Near(0.0, r.CamGo.transform.position.x, 1e-5, "owner-close.no-late-camera-movement");
            }
        }

        static void InputScenario(Check c, GameObject prefab)
        {
            var frame = new InputFrame(5, 9, 0.6, 0.8, false, new[] { InputActions.Interact, InputActions.Confirm }, new[] { InputActions.Cast, InputActions.Cancel });
            var s = DialogueInputAdapter.SuppressWorld(frame);
            c.Near(0.0, s.MoveX, 1e-9, "input.suppress-move-x");
            c.Near(0.0, s.MoveY, 1e-9, "input.suppress-move-y");
            c.True(s.UiCaptured, "input.suppress-marks-captured");
            c.Eq(1, s.Pressed.Count, "input.suppress-pressed-count");
            c.True(s.Pressed.Count == 1 && s.Pressed[0] == InputActions.Confirm, "input.suppress-keeps-confirm-only");
            c.True(s.Held.Count == 1 && s.Held[0] == InputActions.Cancel, "input.suppress-held-keeps-cancel-only");
            c.Eq(5L, s.Tick, "input.suppress-keeps-tick");
            c.Eq(9L, s.InputSequence, "input.suppress-keeps-sequence");

            using (var r = new Rig(prefab, false, null))
            {
                var inner = new FakeInputPort { Next = frame };
                var port = new UiCapturingInputPort(inner, () => r.Dlg.UiCaptured);
                var open = port.Poll(5);
                c.Near(0.6, open.MoveX, 1e-9, "input.passthrough-when-no-window");
                c.True(open.Pressed.Contains(InputActions.Interact), "input.interact-passes-when-no-window");

                r.Dlg.Say("s", "문장", "owner.in");
                var closed = port.Poll(5);
                c.Near(0.0, closed.MoveX, 1e-9, "input.window-suppresses-move");
                c.False(closed.Pressed.Contains(InputActions.Interact), "input.window-blocks-interact-leak");
                c.True(closed.UiCaptured, "input.window-reports-captured");
                r.Dlg.Confirm();
                var again = port.Poll(5);
                c.Near(0.6, again.MoveX, 1e-9, "input.passthrough-restored-after-close");
            }
        }

        static void FocusScenario(Check c, GameObject prefab)
        {
            using (var r = new Rig(prefab, false, null))
            {
                var captured = 0;
                var restored = new List<object>();
                r.Dlg.FocusCapture = () => { captured++; return "focus-token-" + captured; };
                r.Dlg.FocusRestore = o => restored.Add(o);

                r.Dlg.Say("s", "첫 문장", "owner.f");
                c.Eq(1, captured, "focus.captured-on-open");
                r.Dlg.Confirm();
                c.Eq(1, restored.Count, "focus.restored-on-confirm");
                c.Eq("focus-token-1", restored.Count > 0 ? restored[0] : null, "focus.restores-captured-value");

                r.Dlg.Choose("선택", ThreeOptions(), "owner.f");
                r.Dlg.CloseOwner("owner.f");
                c.Eq(2, restored.Count, "focus.restored-on-owner-close");
                c.Eq("focus-token-2", restored.Count > 1 ? restored[1] : null, "focus.second-token-restored");
                NoLeak(c, r, "focus.after");
            }
        }

        // ------------------------------------------------------------------ report

        sealed class Row
        {
            public string Name;
            public bool Ok;
            public string Detail;
        }

        sealed class Check
        {
            public readonly List<Row> Rows = new List<Row>();
            public int Total { get { return Rows.Count; } }
            public int FailedCount { get { var n = 0; foreach (var r in Rows) if (!r.Ok) n++; return n; } }

            void Add(string name, bool ok, string detail) { Rows.Add(new Row { Name = name, Ok = ok, Detail = detail ?? string.Empty }); }
            public void True(bool cond, string name) { Add(name, cond, cond ? string.Empty : "expected true"); }
            public void False(bool cond, string name) { Add(name, !cond, cond ? "expected false" : string.Empty); }
            public void Fail(string name, string detail) { Add(name, false, detail); }

            public void Eq(object expected, object actual, string name)
            {
                var ok = Equals(expected, actual);
                Add(name, ok, ok ? string.Empty : "expected [" + expected + "] actual [" + actual + "]");
            }

            public void Near(double expected, double actual, double tol, string name)
            {
                var ok = Math.Abs(expected - actual) <= tol;
                Add(name, ok, ok ? string.Empty : "expected " + expected + " actual " + actual);
            }
        }

        static void Str(StringBuilder sb, string v)
        {
            sb.Append('"');
            foreach (var ch in v ?? string.Empty)
            {
                if (ch == '"' || ch == (char)92) sb.Append((char)92).Append(ch);
                else if (ch < ' ') sb.Append((char)92).Append('u').Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            sb.Append('"');
        }

        static void WriteReport(Check c)
        {
            var path = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Tests", "Results", "stage3-presentation.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"suite\":"); Str(sb, "stage3-presentation");
            sb.Append(",\"kind\":"); Str(sb, "unity-editor-engine-no-screenshots");
            sb.Append(",\"total\":").Append(c.Total);
            sb.Append(",\"passed\":").Append(c.Total - c.FailedCount);
            sb.Append(",\"failed\":").Append(c.FailedCount);
            sb.Append(",\"assertions\":[");
            for (var i = 0; i < c.Rows.Count; i++)
            {
                var row = c.Rows[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":"); Str(sb, row.Name);
                sb.Append(",\"ok\":").Append(row.Ok ? "true" : "false");
                sb.Append(",\"detail\":"); Str(sb, row.Detail);
                sb.Append('}');
            }
            sb.Append("]}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
#endif
