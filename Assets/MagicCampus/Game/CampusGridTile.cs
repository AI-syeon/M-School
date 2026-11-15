using UnityEngine;
using UnityEngine.Tilemaps;

namespace MagicCampus.Game
{
    public sealed class CampusGridTile : Tile
    {
        [SerializeField] Sprite[] frames;
        [SerializeField] float fps;

        public void SetFrames(Sprite[] sprites, float framesPerSecond)
        {
            sprite = sprites[0];
            frames = sprites;
            fps = framesPerSecond;
            colliderType = ColliderType.None;
        }

        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData data)
        {
            if (frames == null || frames.Length <= 1) return false;
            data.animatedSprites = frames;
            data.animationSpeed = fps;
            data.animationStartTime = 0f;
            data.flags = TileAnimationFlags.SyncAnimation;
            return true;
        }
    }
}
