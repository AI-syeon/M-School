using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace MagicCampus.Game.Editor
{
    public static class CampusGridImport
    {
        const string Root = "Assets/MagicCampus/CampusGrid";
        const string Output = Root + "/Generated";
        const string ScenePath = Output + "/CampusCourtyard.unity";
        const int TileSize = 32;

        [Serializable] sealed class Cell { public int x, y; public Vector3Int Position => new Vector3Int(x, y, 0); }
        [Serializable] sealed class TileCell { public int x, y; public string tile; public Vector3Int Position => new Vector3Int(x, y, 0); }
        [Serializable] sealed class TileSpec { public string id; public string[] frames; public float fps; }
        [Serializable] sealed class Prop { public string id, image; public FrameRect[] rectangles; public float fps; public Cell[] blocked; }
        [Serializable] sealed class FrameRect { public int x, y, w, h; }
        [Serializable]
        sealed class Placement
        {
            public string id;
            public int x, y, order;
            public bool decorative;
        }

        [Serializable]
        sealed class Glow
        {
            public float x, y, radius, power;
            public Vector4 Value => new Vector4(x, y, radius, power);
        }

        [Serializable]
        sealed class Shadow
        {
            public float x, y, width, height, angle, opacity;
        }

        [Serializable]
        sealed class CanopySpec
        {
            public string prop;
            public float offsetX, offsetY, width, depth, height, opacity;

            public CampusDaylight.Caster Caster(Transform anchor, Vector2 offset) => new CampusDaylight.Caster
            {
                anchor = anchor, offset = offset, width = width, depth = depth, height = height, opacity = opacity
            };
        }

        [Serializable] sealed class Anchor { public float x, y; }

        [Serializable]
        sealed class DaylightSpec
        {
            public float startHour, secondsPerDay;
            public CanopySpec canopy;
            public Anchor[] offscreen;
        }

        [Serializable]
        sealed class Layout
        {
            public int schema, tileSize, width, height;
            public int[] camera;
            public Glow[] glows;
            public Shadow[] shadows;
            public DaylightSpec daylight;
            public string packedAtlas;
            public string groundNormal;
            public TileSpec[] tiles;
            public TileCell[] ground;
            public Prop[] props;
            public Placement[] placements;
        }

        [MenuItem("MagicCampus/Art/Rebuild Campus Courtyard")]
        public static void Rebuild()
        {
            var openDemo = SceneManager.GetSceneByPath(ScenePath);
            var previous = SceneManager.GetActiveScene();
            var batch = Application.isBatchMode;
            if (!batch && Enumerable.Range(0, SceneManager.sceneCount).Any(i => string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)))
                throw new InvalidOperationException("Save or close the untitled scene before rebuilding CampusCourtyard.");
            if (openDemo.isLoaded && openDemo.isDirty)
                throw new InvalidOperationException("Save or close CampusCourtyard before rebuilding its generated scene.");
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Root + "/layout.json"));
            if (layout.schema != 1 || layout.tileSize != TileSize)
                throw new InvalidDataException("Campus grid layout must use schema 1 and 32 px tiles.");

            Directory.CreateDirectory(Output + "/Tiles");
            Directory.CreateDirectory(Output + "/Props");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (layout.daylight != null) ImportCanopyMask();
            if (!string.IsNullOrEmpty(layout.groundNormal)) ImportGroundNormal(layout.groundNormal);
            var sprites = layout.tiles.SelectMany(tile => tile.frames).Distinct().ToDictionary(file => file, ImportSprite);
            var tiles = layout.tiles.ToDictionary(spec => spec.id, spec =>
            {
                var tile = LoadOrCreate<CampusGridTile>(Output + "/Tiles/" + spec.id + ".asset");
                tile.SetFrames(spec.frames.Select(file => sprites[file]).ToArray(), spec.fps);
                EditorUtility.SetDirty(tile);
                return tile;
            });
            var blocker = LoadOrCreate<Tile>(Output + "/Tiles/collision.asset");
            blocker.colliderType = Tile.ColliderType.Grid;
            EditorUtility.SetDirty(blocker);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                int packedCount = 0;
                if (!string.IsNullOrEmpty(layout.packedAtlas))
                {
                    // Opening a scene can unload unreferenced atlas sub-assets.
                    var packed = AssetDatabase.LoadAllAssetsAtPath(Root + "/" + layout.packedAtlas).OfType<CampusGridTile>().ToArray();
                    if (packed.Length == 0) throw new InvalidDataException("No tiles imported from " + layout.packedAtlas);
                    foreach (var tile in packed) tiles.Add(tile.name, tile);
                    packedCount = packed.Length;
                }
                var prefabs = layout.props.ToDictionary(prop => prop.id, prop =>
                {
                    var root = new GameObject(prop.id, typeof(Grid));
                    AddPropSprite(root, prop);
                    if (prop.blocked.Length != 0)
                    {
                        var collision = Layer("Collision", root.transform, false);
                        foreach (var cell in prop.blocked)
                            collision.SetTile(cell.Position, blocker);
                        collision.gameObject.AddComponent<TilemapCollider2D>();
                    }
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, Output + "/Props/" + prop.id + ".prefab");
                    UnityEngine.Object.DestroyImmediate(root);
                    if (prefab == null) throw new InvalidOperationException("Could not save prop " + prop.id);
                    return prefab;
                });

                var grid = new GameObject("Courtyard Grid", typeof(Grid));
                var ground = Layer("Ground", grid.transform, true);
                foreach (var cell in layout.ground)
                    ground.SetTile(cell.Position, tiles[cell.tile]);
                if (layout.ground.Any(cell => !ground.HasTile(cell.Position)))
                    throw new InvalidDataException("One or more ground tiles were lost during scene creation.");
                var normalReceiver = BindGroundNormal(layout, ground);
                AddShadows(layout.shadows, grid.transform);
                var casters = new List<CampusDaylight.Caster>();
                var canopy = layout.daylight?.canopy;
                foreach (var entry in layout.placements)
                {
                    var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[entry.id], grid.transform);
                    prop.transform.localPosition = grid.GetComponent<Grid>().CellToLocal(new Vector3Int(entry.x, entry.y, 0));
                    prop.GetComponentInChildren<SpriteRenderer>().sortingOrder = entry.order > 0 ? entry.order : 10;
                    var collision = prop.transform.Find("Collision");
                    if (entry.decorative && collision != null) collision.gameObject.SetActive(false);
                    if (canopy != null && canopy.prop == entry.id)
                        casters.Add(canopy.Caster(prop.transform, new Vector2(canopy.offsetX, canopy.offsetY)));
                }

                var daylight = AddDaylight(layout, grid.transform, casters, normalReceiver);
                AddCamera(layout, daylight);

                if (openDemo.isLoaded) EditorSceneManager.CloseScene(openDemo, true);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new IOException("Could not save " + ScenePath);
                foreach (var path in Directory.GetFiles(Output + "/Tiles", "prop-*.asset"))
                    AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
                AssetDatabase.SaveAssets();
                Debug.Log($"Campus grid imported {sprites.Count} standalone sprites, {packedCount} packed tiles, {layout.ground.Length} ground cells and {layout.placements.Length} prop placements.");
            }
            finally
            {
                if (!batch && previous.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        static void AddPropSprite(GameObject root, Prop prop)
        {
            var path = Root + "/" + prop.image;
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            if (sprites.Length != prop.rectangles.Length)
                throw new InvalidDataException($"Expected {prop.rectangles.Length} sprites from {path}, imported {sprites.Length}");
            for (var i = 0; i < sprites.Length; i++)
            {
                var frame = prop.rectangles[i];
                var expected = new Rect(frame.x, sprites[i].texture.height - frame.y - frame.h, frame.w, frame.h);
                if (sprites[i].rect != expected || sprites[i].pixelsPerUnit != TileSize)
                    throw new InvalidDataException($"Incorrect sprite rectangle or PPU for {path} frame {i}");
            }
            var visual = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            visual.transform.SetParent(root.transform, false);
            visual.sprite = sprites[0];
            visual.transform.localPosition = sprites[0].bounds.size / 2f;
            visual.sortingOrder = 10;
            if (sprites.Length == 1) return;

            var basePath = Output + "/Props/" + prop.id;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(basePath + ".anim");
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, basePath + ".anim");
            }
            clip.frameRate = prop.fps;
            var keys = sprites.Select((sprite, i) => new ObjectReferenceKeyframe { time = i / prop.fps, value = sprite });
            // The final key gives the last frame its full duration before the loop restarts.
            var end = new ObjectReferenceKeyframe { time = sprites.Length / prop.fps, value = sprites[0] };
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("Sprite", typeof(SpriteRenderer), "m_Sprite"), keys.Append(end).ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(basePath + ".controller");
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(basePath + ".controller", clip);
            root.AddComponent<Animator>().runtimeAnimatorController = controller;
        }

        static void AddCamera(Layout layout, CampusDaylight daylight)
        {
            var frame = layout.camera ?? new[] { 0, 0, layout.width, layout.height };
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = frame[3] / 2f;
            camera.aspect = (float)frame[2] / frame[3];
            camera.transform.position = new Vector3(frame[0] + frame[2] / 2f, frame[1] + frame[3] / 2f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(26, 24, 28, 255);
            camera.allowMSAA = false;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/CourtyardGrade.shader");
            var lights = layout.glows?.Select(glow => glow.Value).ToArray() ?? Array.Empty<Vector4>();
            camera.gameObject.AddComponent<CampusCourtyardLook>().Configure(shader,
                new Vector4(frame[0], frame[1], frame[2], frame[3]), lights, daylight);
        }

        const string CanopyTexture = Root + "/CanopyShadow.png";

        static void ImportGroundNormal(string file)
        {
            var importer = AssetImporter.GetAtPath(Root + "/" + file) as TextureImporter
                ?? throw new FileNotFoundException("Missing ground normal map", file);
            if (importer.textureType == TextureImporterType.Default && !importer.sRGBTexture &&
                importer.filterMode == FilterMode.Point && !importer.mipmapEnabled &&
                importer.wrapMode == TextureWrapMode.Clamp && importer.npotScale == TextureImporterNPOTScale.None &&
                importer.textureCompression == TextureImporterCompression.Uncompressed) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static Renderer BindGroundNormal(Layout layout, Tilemap ground)
        {
            if (string.IsNullOrEmpty(layout.groundNormal)) return null;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + layout.groundNormal);
            if (texture == null || texture.width != layout.width * TileSize || texture.height != layout.height * TileSize)
                throw new InvalidDataException("Ground normal map must align with the complete tile grid.");
            const string path = Output + "/CourtyardGround.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/CourtyardGround.shader"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_GroundNormal", texture);
            material.SetVector("_GroundSize", new Vector4(layout.width, layout.height, 0, 0));
            EditorUtility.SetDirty(material);
            var renderer = ground.GetComponent<TilemapRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static void ImportCanopyMask()
        {
            var importer = AssetImporter.GetAtPath(CanopyTexture) as TextureImporter
                ?? throw new FileNotFoundException("Missing daylight mask", CanopyTexture);
            if (importer.filterMode == FilterMode.Point && !importer.mipmapEnabled &&
                importer.wrapMode == TextureWrapMode.Clamp &&
                importer.textureCompression == TextureImporterCompression.Uncompressed) return;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static CampusDaylight AddDaylight(Layout layout, Transform parent, List<CampusDaylight.Caster> casters, Renderer ground)
        {
            var spec = layout.daylight;
            if (spec == null) return null;
            foreach (var anchor in spec.offscreen ?? Array.Empty<Anchor>())
                casters.Add(spec.canopy.Caster(null, new Vector2(anchor.x, anchor.y)));

            const string meshPath = Output + "/CourtyardCanopy.asset";
            const string materialPath = Output + "/CourtyardCanopy.mat";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Courtyard canopy projection" };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            mesh.Clear();
            mesh.vertices = new[] { Vector3.zero, new Vector3(layout.width, 0), new Vector3(layout.width, layout.height), new Vector3(0, layout.height) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            EditorUtility.SetDirty(mesh);

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/CourtyardCanopy.shader"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(CanopyTexture);
            EditorUtility.SetDirty(material);

            var layer = new GameObject("Daylight", typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(parent, false);
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 1;
            var daylight = layer.AddComponent<CampusDaylight>();
            daylight.Configure(renderer, casters.ToArray(), spec.startHour, spec.secondsPerDay, ground);
            return daylight;
        }

        static void AddShadows(Shadow[] shadows, Transform parent)
        {
            if (shadows == null || shadows.Length == 0) return;
            const string meshPath = Output + "/CourtyardShadows.asset";
            const string materialPath = Output + "/CourtyardShadows.mat";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Courtyard contact shadows" };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/CourtyardShadow.shader"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var vertices = new List<Vector3>(shadows.Length * 4);
            var colors = new List<Color>(shadows.Length * 4);
            var uv = new List<Vector2>(shadows.Length * 4);
            var triangles = new List<int>(shadows.Length * 6);
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            foreach (var shadow in shadows)
            {
                int first = vertices.Count;
                var rotation = Quaternion.Euler(0, 0, shadow.angle);
                foreach (var corner in corners)
                {
                    var offset = new Vector3(corner.x * shadow.width / 2, corner.y * shadow.height / 2);
                    vertices.Add(new Vector3(shadow.x, shadow.y) + rotation * offset);
                    colors.Add(new Color(1, 1, 1, shadow.opacity));
                    uv.Add(corner);
                }
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            EditorUtility.SetDirty(mesh);

            var layer = new GameObject("Contact Shadows", typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(parent, false);
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 2;
        }

        static Tilemap Layer(string name, Transform parent, bool visible)
        {
            var layer = new GameObject(name, typeof(Tilemap));
            layer.transform.SetParent(parent, false);
            var tilemap = layer.GetComponent<Tilemap>();
            tilemap.animationFrameRate = 1f;
            if (visible) layer.AddComponent<TilemapRenderer>();
            return tilemap;
        }

        static Sprite ImportSprite(string file)
        {
            var path = Root + "/Textures/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter
                ?? throw new FileNotFoundException("Missing tile frame", path);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
                importer.spritePixelsPerUnit != TileSize || importer.filterMode != FilterMode.Point ||
                importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = TileSize;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null || sprite.rect.size != new Vector2(TileSize, TileSize))
                throw new InvalidDataException("Tile frame must be a 32×32 Sprite: " + path);
            return sprite;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
