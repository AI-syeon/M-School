using System;
using System.IO;
using System.Linq;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MagicCampus.Game.Editor
{
    [ScriptedImporter(1, "campusatlas")]
    public sealed class CampusAtlasImporter : ScriptedImporter
    {
        [Serializable]
        sealed class Frame
        {
            public string name;
            public int x, y, w, h;
        }

        [Serializable]
        sealed class TileSpec
        {
            public string id;
            public string[] frames;
            public float fps;
        }

        [Serializable]
        sealed class Atlas
        {
            public int tileSize;
            public string png;
            public Frame[] frames;
            public TileSpec[] tiles;
        }

        public override void OnImportAsset(AssetImportContext context)
        {
            var atlas = JsonUtility.FromJson<Atlas>(File.ReadAllText(context.assetPath));
            if (atlas.tileSize != 32) throw new InvalidDataException("Campus atlas must contain 32 px cells.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = Path.GetFileNameWithoutExtension(context.assetPath),
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(atlas.png)))
                throw new InvalidDataException("Could not decode the campus atlas PNG.");
            context.AddObjectToAsset("texture", texture);
            context.SetMainObject(texture);

            var sprites = atlas.frames.ToDictionary(frame => frame.name, frame =>
            {
                var rect = new Rect(frame.x, texture.height - frame.y - frame.h, frame.w, frame.h);
                var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), atlas.tileSize, 0, SpriteMeshType.FullRect);
                sprite.name = Path.GetFileNameWithoutExtension(frame.name);
                context.AddObjectToAsset("sprite/" + frame.name, sprite);
                return sprite;
            });
            foreach (var spec in atlas.tiles)
            {
                var tile = ScriptableObject.CreateInstance<CampusGridTile>();
                tile.name = spec.id;
                tile.SetFrames(spec.frames.Select(name => sprites[name]).ToArray(), spec.fps);
                context.AddObjectToAsset("tile/" + spec.id, tile);
            }
        }
    }
}
