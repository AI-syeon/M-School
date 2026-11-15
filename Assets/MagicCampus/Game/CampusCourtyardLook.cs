using UnityEngine;

namespace MagicCampus.Game
{
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class CampusCourtyardLook : MonoBehaviour
    {
        [SerializeField] Shader grading;
        [SerializeField] Vector4 frame;
        [SerializeField] Vector4[] lanterns;
        [SerializeField] CampusDaylight daylight;
        Camera view;
        Material material;

        public void Configure(Shader shader, Vector4 bounds, Vector4[] lights, CampusDaylight sun)
        {
            grading = shader;
            frame = bounds;
            lanterns = lights;
            daylight = sun;
        }

        void OnPreCull()
        {
            if (frame.w <= 0) return;
            if (view == null) view = GetComponent<Camera>();
            var target = view.targetTexture;
            float width = target != null ? target.width : Screen.width;
            float height = target != null ? target.height : Screen.height;
            if (height <= 0 || width <= 0) return;
            float aspect = frame.z / frame.w;
            float fittedWidth = Mathf.Min(1, height * aspect / width);
            float fittedHeight = Mathf.Min(1, width / (height * aspect));
            view.rect = new Rect((1 - fittedWidth) / 2, (1 - fittedHeight) / 2, fittedWidth, fittedHeight);
            view.aspect = aspect;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (grading == null)
            {
                Graphics.Blit(source, destination);
                return;
            }
            if (material == null) material = new Material(grading) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_WorldFrame", frame);
            var sun = daylight != null ? daylight.CurrentSun : new CampusDaylight.Sun(12f);
            material.SetVector("_Solar", new Vector4(sun.Daylight, sun.Warmth, sun.DirectLight,
                daylight != null ? sun.LanternLight : 1f));
            material.SetVector("_SunDirection", new Vector4(sun.Direction.x, sun.Direction.y, 0, 0));
            int count = Mathf.Min(lanterns?.Length ?? 0, 8);
            material.SetInt("_LanternCount", count);
            if (count > 0) material.SetVectorArray("_Lanterns", lanterns);
            Graphics.Blit(source, destination, material);
        }

        void OnDisable()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }
    }
}
