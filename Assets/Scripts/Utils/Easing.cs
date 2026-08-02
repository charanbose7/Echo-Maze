using UnityEngine;

/// <summary>
/// Code easing curves. Juice moments never use linear lerps — they use these.
/// All take a normalized t in [0,1] and return an eased value (usually [0,1],
/// OutBack/OutElastic overshoot past 1 on purpose).
/// </summary>
public static class Easing
{
    public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);

    public static float OutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }

    public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;

    /// <summary>Overshoots then settles — great for pops and star reveals.</summary>
    public static float OutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>Springy bounce — used for the strongest pops.</summary>
    public static float OutElastic(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        const float c4 = (2f * Mathf.PI) / 3f;
        return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
    }

    /// <summary>A 0->1->0 pulse (peaks at the middle). Handy for flashes.</summary>
    public static float Spike(float t) => 1f - Mathf.Abs(2f * Mathf.Clamp01(t) - 1f);
}
