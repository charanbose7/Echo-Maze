using UnityEngine;

/// <summary>
/// Thin PlayerPrefs wrapper for persistence: best score, best streak, per-level star
/// records, and the one-time "how to play" hint flag. Setters return true when a new
/// record was set, so the UI can fire a "NEW BEST!" callout.
/// </summary>
public static class SaveData
{
    private const string KBestScore  = "em_best_score";
    private const string KBestStreak = "em_best_streak";
    private const string KHintSeen   = "em_hint_seen";
    private const string KStarPrefix = "em_stars_"; // + level

    public static int BestScore => PlayerPrefs.GetInt(KBestScore, 0);
    public static int BestStreak => PlayerPrefs.GetInt(KBestStreak, 0);

    /// <summary>Record a score. Returns true if it beat the previous best.</summary>
    public static bool TrySetBestScore(int score)
    {
        if (score <= BestScore) return false;
        PlayerPrefs.SetInt(KBestScore, score);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>Record a streak length. Returns true if it beat the previous best.</summary>
    public static bool TrySetBestStreak(int streak)
    {
        if (streak <= BestStreak) return false;
        PlayerPrefs.SetInt(KBestStreak, streak);
        PlayerPrefs.Save();
        return true;
    }

    public static int GetStars(int level) => PlayerPrefs.GetInt(KStarPrefix + level, 0);

    /// <summary>Record stars for a level. Returns true if it improved the record.</summary>
    public static bool TrySetStars(int level, int stars)
    {
        if (stars <= GetStars(level)) return false;
        PlayerPrefs.SetInt(KStarPrefix + level, stars);
        PlayerPrefs.Save();
        return true;
    }

    public static bool HintSeen => PlayerPrefs.GetInt(KHintSeen, 0) == 1;
    public static void MarkHintSeen()
    {
        PlayerPrefs.SetInt(KHintSeen, 1);
        PlayerPrefs.Save();
    }
}
