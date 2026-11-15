using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>
    /// A character's face for the map screen, cut from the head of one sprite frame. The head band comes from the opaque
    /// pixels: it runs from the top row down to the neck, the narrowest row in the upper half of the figure; a figure whose
    /// hair runs on into the shoulders has no neck, so the head is then a fixed share of the figure's height.
    /// </summary>
    public static class CharacterFace
    {
        /// <summary>A neck must be at most this share of the head's widest row, else the figure has no neck.</summary>
        const float NeckRatio = 0.8f;
        /// <summary>Head height as a share of the figure's height when there is no neck.</summary>
        const float HeadShare = 0.4f;

        /// <summary>The head's bounding rectangle in a frame of the given size, row 0 on top; an empty rect when nothing is opaque.</summary>
        public static RectInt HeadRect(bool[] opaque, int width, int height)
        {
            var widths = new int[height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (opaque[y * width + x]) widths[y]++;
            int top = System.Array.FindIndex(widths, w => w > 0), bottom = System.Array.FindLastIndex(widths, w => w > 0);
            if (top < 0) return new RectInt();
            var figure = bottom - top + 1;
            int lo = top + figure / 4, hi = top + figure / 2, neck = lo;
            for (var y = lo; y <= hi; y++)
                if (widths[y] < widths[neck]) neck = y;
            var headMax = 0;
            for (var y = top; y <= neck; y++) headMax = Mathf.Max(headMax, widths[y]);
            if (widths[neck] > NeckRatio * headMax) neck = top + Mathf.RoundToInt(HeadShare * figure) - 1;
            int left = width, right = -1;
            for (var y = top; y <= neck; y++)
                for (var x = 0; x < width; x++)
                    if (opaque[y * width + x])
                    {
                        left = Mathf.Min(left, x);
                        right = Mathf.Max(right, x);
                    }
            return new RectInt(left, top, right - left + 1, neck - top + 1);
        }

        /// <summary>The head of the frame at the given top-left pixel rectangle of the sheet, as its own point-filtered texture.</summary>
        public static Texture2D Crop(Texture2D sheet, int x, int y, int w, int h)
        {
            var pixels = sheet.GetPixels32();
            var opaque = new bool[w * h];
            // Texture rows run bottom to top; the manifest's rectangles run top to bottom.
            int Source(int px, int py) => (sheet.height - 1 - (y + py)) * sheet.width + x + px;
            for (var py = 0; py < h; py++)
                for (var px = 0; px < w; px++)
                    opaque[py * w + px] = pixels[Source(px, py)].a > 0;
            var head = HeadRect(opaque, w, h);
            if (head.width <= 0) return null;
            var face = new Texture2D(head.width, head.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "face" };
            for (var py = 0; py < head.height; py++)
                for (var px = 0; px < head.width; px++)
                    face.SetPixel(px, head.height - 1 - py, pixels[Source(head.x + px, head.y + py)]);
            face.Apply();
            return face;
        }
    }
}
