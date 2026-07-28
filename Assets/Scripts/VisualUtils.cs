using UnityEngine;

/// <summary>
/// Procedural texture / sprite factory. Everything the game draws is generated
/// here at runtime with Texture2D — no imported art. All sprites are built with a
/// centered pivot and a pixels-per-unit chosen so that a localScale of 1 == 1 world unit.
/// </summary>
public static class VisualUtils
{
    /// <summary>1x1 opaque white pixel. Used for wall quads (color comes from the shader).</summary>
    public static Sprite WhiteSquare()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        // PPU == width so the sprite is exactly 1 world unit before scaling.
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }

    /// <summary>Soft radial glow: bright at the center, fading to transparent at the edge.</summary>
    public static Sprite RadialGlow(int size = 128)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0 center -> 1 edge
            float a = Mathf.Clamp01(1f - d);
            a = a * a;                       // tighten the falloff for a hotter core
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>Thin glowing annulus, used for the expanding ping ring. 1 unit diameter at scale 1.</summary>
    public static Sprite Ring(int size = 256, float thickness = 0.06f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0..1, ring lives near 0.5
            float edge = Mathf.Abs(d - 0.5f);                                // distance from the ring line
            float a = Mathf.Clamp01(1f - edge / thickness);
            a = a * a;
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>Solid soft-edged disc, used for UI ping dots.</summary>
    public static Sprite Disc(int size = 64)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01((1f - d) * 6f); // solid disc with a 1px soft edge
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>
    /// Vignette: transparent in the center, opaque near-black toward the edges.
    /// Rendered on top (alpha blended) to darken the screen borders.
    /// </summary>
    public static Sprite Vignette(int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Use the larger axis distance so corners darken like a real vignette.
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.55f) / 0.6f)) * 0.9f;
            px[y * size + x] = new Color(0.01f, 0.015f, 0.03f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>
    /// Crisp hollow-circle OUTLINE near the sprite edge (for the decoy highlight). Because the
    /// ring sits at ~0.86 of the radius, a localScale of S gives a circle ~0.86*S world units
    /// across — so scaling it a bit above the ball's size draws the ring cleanly AROUND the ball.
    /// </summary>
    public static Sprite HollowRing(int size = 256, float radiusFrac = 0.86f, float thickness = 0.07f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / c, dy = (y - c) / c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);   // 0 center .. 1 at edge midpoints
            float ring = Mathf.Abs(d - radiusFrac);    // distance from the ring line
            float a = Mathf.Clamp01(1f - ring / thickness);
            a *= a;                                    // slightly crisp edge
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>
    /// Rounded rectangle for UI buttons/panels, returned as a 9-sliced sprite so it scales to any
    /// size without distorting the corners. Use with Image.type = Sliced.
    /// </summary>
    public static Sprite RoundedRect(int size = 64, int radius = 20)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Distance outside the rounded-rect body, measured only in the corner regions.
            float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
            float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(radius - d + 0.5f);   // 1 inside, soft 1px edge
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size,
                             0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    /// <summary>
    /// Hollow rounded-rect OUTLINE, 9-sliced. This is the core of the sonar-HUD button look:
    /// a bright thin stroke over a dark translucent fill, rather than a flat solid button.
    /// </summary>
    public static Sprite RoundedRectOutline(int size = 64, int radius = 20, float thickness = 3.5f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Signed distance to a rounded box: negative inside, 0 on the edge.
            float pxc = x + 0.5f - h, pyc = y + 0.5f - h;
            float qx = Mathf.Abs(pxc) - (h - radius);
            float qy = Mathf.Abs(pyc) - (h - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float sdf = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

            float a = Mathf.Clamp01(thickness * 0.5f - Mathf.Abs(sdf) + 0.5f);
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size,
                             0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    /// <summary>Simple gear glyph for the settings button.</summary>
    public static Sprite Gear(int size = 96)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        const int teeth = 8;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - c, dy = y - c;
            float r = Mathf.Sqrt(dx * dx + dy * dy) / c;      // 0..1
            float ang = Mathf.Atan2(dy, dx);
            // Outer edge wobbles between the tooth tip and root to form the cog.
            float tooth = Mathf.Cos(ang * teeth);
            float outer = 0.72f + 0.16f * Mathf.Sign(tooth);
            float a = (r < outer && r > 0.30f) ? 1f : 0f;      // ring body with a hollow centre
            a *= Mathf.Clamp01((outer - r) * 12f);             // soften the outer edge
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>Filled 5-point star (for the level-clear star rating).</summary>
    public static Sprite Star(int size = 96)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        float outer = c * 0.95f;
        float inner = outer * 0.42f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - c, dy = y - c;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            // Angle from top, folded into one 72-degree wedge, then compare against the
            // straight edge between an outer point and the adjacent inner vertex.
            float ang = Mathf.Atan2(dx, dy);                 // 0 at top, clockwise
            float wedge = Mathf.Repeat(ang, Mathf.PI * 2f / 5f);
            float half = Mathf.PI / 5f;
            float f = Mathf.Abs(wedge - half) / half;        // 0 at a point, 1 at a valley
            float edge = Mathf.Lerp(outer, inner, f);
            float a = Mathf.Clamp01((edge - r) * 4f + 0.5f); // soft 1px edge
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
