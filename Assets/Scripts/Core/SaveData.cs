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
    private const string KDayStreak  = "em_day_streak";
    private const string KLastDay    = "em_last_day";   // UTC day number of the last session
    private const string KBestDay    = "em_best_day_streak";
    private const string KSound      = "em_sound";
    private const string KHaptics    = "em_haptics";
    private const string KDailyDone  = "em_daily_done";   // UTC day number of the last daily clear
    private const string KDailyBest  = "em_daily_best";   // best daily score
    private const string KRunFinished= "em_run_finished";  // player has completed one endless run

    public static int BestScore => PlayerPrefs.GetInt(KBestScore, 0);
    public static int BestStreak => PlayerPrefs.GetInt(KBestStreak, 0);

    // PlayerPrefs.Save() writes the file synchronously. The level-clear path used to call it
    // three times in a row, which is three blocking disk writes during the celebration — exactly
    // when the game should feel smoothest. Setters now only stage the value; Flush() commits once.
    /// <summary>Record a score. Returns true if it beat the previous best. Call Flush() after.</summary>
    public static bool TrySetBestScore(int score)
    {
        if (score <= BestScore) return false;
        PlayerPrefs.SetInt(KBestScore, score);
        return true;
    }

    /// <summary>Record a streak length. Returns true if it beat the previous best. Call Flush() after.</summary>
    public static bool TrySetBestStreak(int streak)
    {
        if (streak <= BestStreak) return false;
        PlayerPrefs.SetInt(KBestStreak, streak);
        return true;
    }

    /// <summary>Commit staged records to disk. One write instead of several.</summary>
    public static void Flush() => PlayerPrefs.Save();

    // ---- Daily streak ----------------------------------------------------------------
    // "Come back tomorrow" is the strongest retention hook we can add without a backend.
    // Everything is keyed off the UTC day number so it can't be gamed by timezone changes.

    /// <summary>Whole days since epoch, in UTC — the canonical "which day is it" value.</summary>
    public static int TodayNumber => (int)(System.DateTime.UtcNow.Date - new System.DateTime(1970, 1, 1)).TotalDays;

    public static int DayStreak => PlayerPrefs.GetInt(KDayStreak, 0);
    public static int BestDayStreak => PlayerPrefs.GetInt(KBestDay, 0);

    /// <summary>Seed for today's shared maze — same layout for every player on a given day.</summary>
    public static int DailySeed => TodayNumber * 7919 + 13;

    /// <summary>True if today's daily has already been played (so we only reward it once).</summary>
    public static bool PlayedToday => PlayerPrefs.GetInt(KLastDay, -1) == TodayNumber;

    /// <summary>
    /// Register a session for today. Consecutive days extend the streak, a skipped day resets it.
    /// Returns the streak length after the update. Safe to call repeatedly on the same day.
    /// </summary>
    public static int RegisterDailyVisit()
    {
        int today = TodayNumber;
        int last = PlayerPrefs.GetInt(KLastDay, -1);
        if (last == today) return DayStreak;             // already counted today

        int streak = last == today - 1 ? DayStreak + 1 : 1;
        PlayerPrefs.SetInt(KDayStreak, streak);
        PlayerPrefs.SetInt(KLastDay, today);
        if (streak > BestDayStreak) PlayerPrefs.SetInt(KBestDay, streak);
        PlayerPrefs.Save();
        return streak;
    }

    // ---- Daily Maze run ----
    /// <summary>Has today's daily maze already been completed? (One attempt per day.)</summary>
    public static bool DailyDone => PlayerPrefs.GetInt(KDailyDone, -1) == TodayNumber;

    public static int DailyBest => PlayerPrefs.GetInt(KDailyBest, 0);

    /// <summary>Mark today's daily finished. Returns true if it was also a personal daily best.</summary>
    public static bool CompleteDaily(int score)
    {
        PlayerPrefs.SetInt(KDailyDone, TodayNumber);
        bool best = score > DailyBest;
        if (best) PlayerPrefs.SetInt(KDailyBest, score);
        PlayerPrefs.Save();
        return best;
    }

    /// <summary>
    /// True once the player has played an endless run through to its end (i.e. lost once).
    /// The Daily Maze stays locked until then: it is a single high-pressure attempt with no
    /// retries, which is a terrible first experience for someone who has never pinged a wall.
    /// </summary>
    public static bool RunFinished => PlayerPrefs.GetInt(KRunFinished, 0) == 1;

    public static void MarkRunFinished()
    {
        if (RunFinished) return;
        PlayerPrefs.SetInt(KRunFinished, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Wipe onboarding state so the player is treated as brand new: the tutorial replays and the
    /// Daily Maze re-locks. Used when someone resets progress before they have played past the
    /// tutorial level — at that point they have learned nothing worth preserving.
    /// </summary>
    public static void ResetOnboarding()
    {
        PlayerPrefs.SetInt(KHintSeen, 0);
        PlayerPrefs.SetInt(KRunFinished, 0);
        PlayerPrefs.Save();
    }

    public static bool HintSeen => PlayerPrefs.GetInt(KHintSeen, 0) == 1;
    public static void MarkHintSeen()
    {
        PlayerPrefs.SetInt(KHintSeen, 1);
        PlayerPrefs.Save();
    }

    // ---- Settings --------------------------------------------------------------------
    public static bool SoundOn
    {
        get => PlayerPrefs.GetInt(KSound, 1) == 1;
        set { PlayerPrefs.SetInt(KSound, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static bool HapticsOn
    {
        get => PlayerPrefs.GetInt(KHaptics, 1) == 1;
        set { PlayerPrefs.SetInt(KHaptics, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>Push saved settings into the systems that consume them.</summary>
    public static void ApplySettings()
    {
        AudioListener.volume = SoundOn ? 1f : 0f;
        Haptics.Enabled = HapticsOn;
    }

    /// <summary>
    /// Clear per-level star records and the day-streak counter.
    ///
    /// Deliberately PRESERVED:
    ///  - best score / best streak — lifetime achievements the player earned
    ///  - the tutorial flag        — nobody wants to be re-taught the controls
    ///  - today's daily completion — otherwise resetting would let you re-farm the daily
    ///  - sound / vibration settings
    /// </summary>
    public static void ResetProgress()
    {
        // Snapshot everything that must survive.
        bool sound = SoundOn, haptics = HapticsOn, hint = HintSeen, runDone = RunFinished;
        int bestScore = BestScore, bestStreak = BestStreak;
        int dailyDone = PlayerPrefs.GetInt(KDailyDone, -1);
        int dailyBest = DailyBest;

        PlayerPrefs.DeleteAll();

        PlayerPrefs.SetInt(KSound, sound ? 1 : 0);
        PlayerPrefs.SetInt(KHaptics, haptics ? 1 : 0);
        PlayerPrefs.SetInt(KHintSeen, hint ? 1 : 0);
        PlayerPrefs.SetInt(KRunFinished, runDone ? 1 : 0);
        PlayerPrefs.SetInt(KBestScore, bestScore);
        PlayerPrefs.SetInt(KBestStreak, bestStreak);
        PlayerPrefs.SetInt(KDailyDone, dailyDone);
        PlayerPrefs.SetInt(KDailyBest, dailyBest);
        PlayerPrefs.Save();
        ApplySettings();
    }
}
