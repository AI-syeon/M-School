using System;
using UnityEngine;

namespace MagicCampus.Game
{
    [ExecuteAlways]
    public sealed class CampusDaylight : MonoBehaviour
    {
        const int MaxCasters = 12;

        [Serializable]
        public struct Caster
        {
            public Transform anchor;
            public Vector2 offset;
            public float width, depth, height, opacity;
        }

        public readonly struct Sun
        {
            public readonly Vector2 Direction;
            public readonly float Altitude, Daylight, Warmth, DirectLight, LanternLight;

            public Vector4 NormalLight
            {
                get
                {
                    float radians = Altitude * Mathf.Deg2Rad;
                    float horizontal = Mathf.Cos(radians);
                    return new Vector4(Direction.x * horizontal, Direction.y * horizontal, Mathf.Sin(radians), DirectLight);
                }
            }

            public Sun(float hour)
            {
                float angle = (hour - 6f) * Mathf.PI / 12f;
                Direction = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle));
                float arc = Mathf.Sin(angle);
                Altitude = Mathf.Max(0, arc) * 65f;
                Daylight = Smooth(-0.25f, 0.47f, arc);
                DirectLight = Smooth(0.025f, 0.34f, arc);
                Warmth = (1f - Smooth(0.24f, 0.92f, arc)) * Smooth(-0.04f, 0.30f, arc);
                LanternLight = Mathf.Lerp(1.15f, 0.18f, Smooth(-0.15f, 0.56f, arc));
            }

            static float Smooth(float start, float end, float value) =>
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
        }

        [SerializeField, Range(0, 24)] float hour = 14.5f;
        [SerializeField, Min(0)] float previewSecondsPerDay = 96f;
        [SerializeField] bool showControls = true;
        [SerializeField] Renderer foliage;
        [SerializeField] Renderer normalReceiver;
        [SerializeField] Caster[] casters;

        readonly Vector4[] projected = new Vector4[MaxCasters];
        readonly Vector4[] footprints = new Vector4[MaxCasters];
        MaterialPropertyBlock properties;
        MaterialPropertyBlock groundProperties;
        bool scrubbed;

        public Sun CurrentSun => new Sun(hour);

        public void Configure(Renderer layer, Caster[] foliageCasters, float startHour, float secondsPerDay,
            Renderer ground = null)
        {
            foliage = layer;
            casters = foliageCasters;
            normalReceiver = ground;
            hour = startHour;
            previewSecondsPerDay = secondsPerDay;
            ApplyShadows();
        }

        public void SetGameMinute(int minute) => SetHour(minute / 60f);

        public void SetHour(float value)
        {
            scrubbed = true;
            hour = Mathf.Repeat(value, 24f);
            ApplyShadows();
        }

        void LateUpdate()
        {
            if (Application.isPlaying && !scrubbed && previewSecondsPerDay > 0)
                hour = Mathf.Repeat(hour + Time.deltaTime * 24f / previewSecondsPerDay, 24f);
            ApplyShadows();
        }

        void ApplyShadows()
        {
            var sun = CurrentSun;
            if (normalReceiver != null)
            {
                groundProperties ??= new MaterialPropertyBlock();
                normalReceiver.GetPropertyBlock(groundProperties);
                groundProperties.SetVector("_NormalLight", sun.NormalLight);
                normalReceiver.SetPropertyBlock(groundProperties);
            }
            if (foliage == null) return;
            var direction = -sun.Direction;
            float tangent = Mathf.Tan(Mathf.Max(8f, sun.Altitude) * Mathf.Deg2Rad);
            int count = Mathf.Min(casters?.Length ?? 0, MaxCasters);
            for (int i = 0; i < count; i++)
            {
                var caster = casters[i];
                Vector2 origin = caster.anchor == null ? caster.offset : (Vector2)caster.anchor.position + caster.offset;
                float reach = Mathf.Min(8f, caster.height / tangent);
                Vector2 center = origin + direction * reach;
                projected[i] = new Vector4(center.x, center.y, caster.opacity * sun.DirectLight, 0);
                float depth = caster.depth * (1f + Mathf.Min(1.25f, reach * 0.17f));
                footprints[i] = new Vector4(1f / caster.width, 1f / depth, 0, 0);
            }
            properties ??= new MaterialPropertyBlock();
            properties.SetVector("_ShadowDirection", new Vector4(direction.x, direction.y, 0, 0));
            properties.SetInt("_CasterCount", count);
            properties.SetVectorArray("_ProjectedCanopies", projected);
            properties.SetVectorArray("_Footprints", footprints);
            foliage.SetPropertyBlock(properties);
        }

        void OnGUI()
        {
            if (!Application.isPlaying || !showControls) return;
            int minutes = Mathf.FloorToInt(hour * 60f) % 1440;
            float width = Mathf.Min(470f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12, Screen.height - 64, width, 56), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{minutes / 60:00}:{minutes % 60:00}", GUILayout.Width(45));
            if (previewSecondsPerDay > 0 && GUILayout.Button(scrubbed ? "Run" : "Pause", GUILayout.Width(54)))
                scrubbed = !scrubbed;
            if (GUILayout.Button("Morning")) SetHour(8f);
            if (GUILayout.Button("Noon")) SetHour(12f);
            if (GUILayout.Button("Sunset")) SetHour(17f);
            if (GUILayout.Button("Night")) SetHour(22f);
            GUILayout.EndHorizontal();
            float adjusted = GUILayout.HorizontalSlider(hour, 0f, 24f);
            if (adjusted != hour) SetHour(adjusted);
            GUILayout.EndArea();
        }
    }
}
