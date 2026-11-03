using UnityEngine;

namespace MagicCampus.Presentation.Cutscene
{
    /// <summary>
    /// Supplied by the caller. Must return the presentation child Transform of an actor (never the logical/saved
    /// position root). Returns false for unknown actors.
    /// </summary>
    public interface IActorVisualResolver
    {
        bool TryResolveVisual(string actorId, out Transform visual);
    }

    /// <summary>Local pose of a presentation child captured before the first clip of an owner touches it.</summary>
    public readonly struct VisualPose
    {
        public Vector3 LocalPosition { get; }
        public Quaternion LocalRotation { get; }
        public Vector3 LocalScale { get; }

        public VisualPose(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            LocalScale = localScale;
        }

        public static VisualPose Capture(Transform t)
        {
            return new VisualPose(t.localPosition, t.localRotation, t.localScale);
        }

        public void ApplyTo(Transform t)
        {
            t.localPosition = LocalPosition;
            t.localRotation = LocalRotation;
            t.localScale = LocalScale;
        }
    }

    /// <summary>
    /// Explicitly registered clip. normalized runs 0..1 (0 is called when the clip starts, 1 when it finishes).
    /// The callback may only change the given presentation Transform, expressed relative to origin.
    /// </summary>
    public delegate void VisualClipCallback(Transform visual, VisualPose origin, float normalized);
}
