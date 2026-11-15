using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>
    /// Scene-held references the runtime world needs from the project (GameBuild fills them): the CampusGrid shaders and canopy
    /// mask, so the player build includes them, and an inactive CampusDaylight whose preview controls are switched off.
    /// </summary>
    public sealed class WorldArtAssets : MonoBehaviour
    {
        public Shader CanopyShader, ContactShadowShader, GradeShader, NormalShader;
        public Texture2D CanopyMask;
        public CampusDaylight DaylightTemplate;

        readonly Dictionary<Texture2D, Material> normalMaterials = new Dictionary<Texture2D, Material>();
        static readonly Vector4 IndoorLight = new Vector4(-0.424264f, -0.424264f, 0.8f, 0.55f);
        Vector4 light = IndoorLight;

        public Material NormalMaterial(Texture2D texture, Vector2 groundSize = default)
        {
            if (texture == null || NormalShader == null) return null;
            if (normalMaterials.TryGetValue(texture, out var material)) return material;
            material = new Material(NormalShader);
            material.SetTexture("_GroundNormal", texture);
            material.SetVector("_NormalLight", light);
            if (groundSize != Vector2.zero)
                material.SetVector("_GroundSize", new Vector4(groundSize.x, groundSize.y));
            else
                material.SetFloat("_UseSpriteUV", 1);
            normalMaterials.Add(texture, material);
            return material;
        }

        public void SetNormalLight(CampusDaylight daylight)
        {
            var next = daylight != null ? daylight.CurrentSun.NormalLight : IndoorLight;
            if (light == next) return;
            light = next;
            foreach (var material in normalMaterials.Values)
                material.SetVector("_NormalLight", light);
        }

        public static Texture2D LoadNormal(string path, int width, int height)
        {
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                name = Path.GetFileName(path),
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (texture.LoadImage(File.ReadAllBytes(path), true) && texture.width == width && texture.height == height)
                return texture;
            Destroy(texture);
            throw new InvalidDataException("Normal map must match the color image dimensions: " + path);
        }

        void OnDestroy()
        {
            foreach (var material in normalMaterials.Values) Destroy(material);
        }
    }
}
