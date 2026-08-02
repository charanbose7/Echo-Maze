using UnityEngine;

/// <summary>
/// Per-level difficulty, computed by <see cref="GameConfig.GetDifficulty"/>.
/// Everything that changes as you progress lives here so the ramp is data-driven.
/// </summary>
public struct Difficulty
{
    public int mazeSize;
    public int pings;
    public float fade;        // seconds a revealed wall stays lit
    public float ringSpeed;   // sonar expansion speed
    public float band;        // front white-flash band (seconds) — thinner = harder to read
    public float timeLimit;   // fail timer
    public bool movingExit;
    public float exitMoveInterval;
    public int decoyCount;
    public float decoyFadeSpeed;   // how fast decoys blink — faster is harder to time a crossing
}

/// <summary>
/// Central tuning block. Every knob for maze size, movement, sonar, juice, streaks,
/// stars and the difficulty ramp lives here. No ScriptableObjects.
/// </summary>
public static class GameConfig
{
    // ---- Maze / session length (aim: 20-60s per level) ----
    public const int   StartMazeSize     = 6;    // level 1 grid
    public const int   MaxMazeSize        = 12;  // cap — we grow difficulty other ways past this
    public const int   LevelsPerSizeStep  = 2;   // grid grows +1 every N levels
    public const float CellSize           = 1.0f;
    public const float WallThickness      = 0.12f;

    // ---- Player ----
    public const float PlayerMoveSpeed    = 1.2f;
    public const float PlayerRadius       = 0.24f; // keep < CellSize/2
    public const float PlayerGlowScale    = 0.7f;
    public const float SquashAmount       = 0.28f; // stretch toward motion at full speed
    public const float SquashLerp         = 12f;
    public const float BreathAmplitude    = 0.06f; // idle "breathing" pulse
    public const float BreathSpeed        = 2.4f;
    public const float TrailTime          = 0.12f; // short so fast moves don't leave a long sharp streak
    public const float TrailWidth         = 0.11f;
    public const float SpawnPopTime       = 0.4f;  // dot "materializes" instead of snapping in

    // ---- Touch / input (direct finger-drag, frame-rate, collide-and-slide) ----
    public const float MoveEngagePx      = 8f;    // finger travel before the dot starts following (small = responsive)
    public const float MoveSensitivity   = .5f;  // 1.0 = the dot tracks the finger 1:1 in world space
    public const float PlayerCollideSkin = 0.02f; // gap kept from walls when sliding
    public const float TapMaxDuration    = 0.22f; // quick touch that never engaged movement = a ping
    public const float MoveAudioMaxVol   = 0.16f; // loudest the movement drone gets
    public const float HintMaxSeconds    = 12f;   // level-1 tutorial auto-dismiss

    // ---- Rewind penalty (touching a decoy) ----
    public const float RewindSeconds   = 2f;    // how far back the dot is sent
    public const float RewindDuration  = 1.0f;  // unscaled length of the whole rewind sequence
    public const float PathSampleStep  = 0.05f; // how often the dot's position is recorded

    // ---- Timer audio cues ----
    public const int   TimerWarnAt     = 10;    // one warning cue when this many seconds remain
    public const int   TimerTickFrom   = 5;     // tick every second at/under this many remaining

    // ---- Sonar (ranges lerped across the ramp) ----
    public const int   MaxPings           = 4;     // must match MAX_PINGS in SonarWall.shader
    public const float PingCooldown       = 1.1f;  // can't fire another ping until the last reveal finishes
    public const float RingSpeedStart     = 6.5f;
    public const float RingSpeedEnd       = 8.0f;
    public const float FadeStart          = 2.2f;  // easy: walls linger
    public const float FadeEnd            = 1.0f;  // hard: blink and it's gone
    public const float BandStart          = 0.18f; // easy: fat readable flash
    public const float BandEnd            = 0.09f; // hard: thin flash
    public const float FlashBoost         = 1.7f;  // white bloom strength at the ring front
    public const float FadeRampLevels     = 12f;   // levels to reach the hardest settings
    public const float RingLife           = 1.1f;  // ring visual lifetime
    public const float OriginFlashTime    = 0.22f; // bright burst at the ping origin
    public const float OriginFlashScale   = 1.5f;
    public const float PingPitchJitter    = 0.06f;
    public const float TickThrottle       = 0.028f;
    public const float TickPitchJitter    = 0.18f;
    public const float NearRevealRadius   = 1.8f;  // wall revealed within this of player = medium haptic
    public const float NearHapticThrottle = 0.09f;
    public const float PingDarkenAmount   = 0.14f; // brief screen darken as the ring bursts
    public const float PingDarkenTime     = 0.18f;

    // ---- Pings budget / scoring ----
    public const int   TutorialPingBonus  = 3;     // +3,+2,+1 on levels 1..3
    public const int   RubberBandFails    = 2;     // after this many fails on a level...
    public const int   RubberBandPings    = 2;     // ...quietly grant this many extra pings
    public const int   ScorePerPing       = 100;
    public const float ScorePerSecond     = 8f;
    public const int   ScoreBaseClear     = 250;   // flat reward per clear

    // ---- Streak (the retention hook) ----
    public const float StreakStep         = 0.5f;  // multiplier = 1 + streak*StreakStep
    public const float MaxStreakMultiplier= 6f;
    public const int   StreakMinPingsSpare= 1;     // must finish with >= this many pings to keep streak

    // ---- Stars (fraction of starting pings still in hand at clear) ----
    public const float Star3PingFrac      = 0.5f;
    public const float Star2PingFrac      = 0.22f;

    // ---- Loop timing ----
    public const float LevelTimeLimit     = 45f;   // <=0 disables the fail timer
    public const float CelebrationTime    = 1.5f;  // auto-advance delay after a clear
    public const float HitstopTime        = 0.15f; // freeze on exit reached

    // ---- Twists (introduced as you climb) ----
    public const int   DecoyStartLevel    = 5;
    public const int   DecoyEveryLevels   = 4;
    public const int   MaxDecoys          = 3;
    public const int   MovingExitLevel    = 8;
    public const float ExitMoveInterval   = 2.6f;
    public const float ExitMoveLerp       = 2.2f;
    public const float DecoyFadeSpeed     = 2.1f;  // fade in/out rate; slower = easier to time a crossing
    public const float DecoyMaxAlpha      = 0.95f; // peak visibility
    public const float DecoyVisibleHit    = 0.5f;  // only collidable once this visible (time your run!)
    public const float DecoyHitBlackout   = 1.0f;  // after taking a ping it blinks out so you can pass
    public const int   DecoyPingCost      = 1;     // "search taps" lost on a visible hit

    // ---- Camera / presentation ----
    public const float CameraPadding      = 0.9f;
    public const float ShakeMagnitude     = 0.28f;
    public const float ShakeDuration      = 0.35f;
    public const float PunchZoom          = 0.12f; // fraction to zoom in on clear
    public const float PunchZoomTime      = 0.45f;

    // ---- Exit near-miss glow ----
    public const float ExitPulseBaseAlpha = 0.24f;
    public const float ExitPulseAmp       = 0.28f;
    public const float ExitPulseSpeed     = 2.0f;
    public const float ExitNearRadius     = 4.0f;  // brighten the exit as player gets within this
    public const float ExitNearMaxBoost   = 0.55f;

    // ---- Sectors (chapters) ----
    // Progression needs a big beat every ~10-15 min, not just the per-level one. Levels are grouped
    // into named sectors that re-tint the whole maze and pay a bonus on completion.
    public const int   LevelsPerSector    = 5;
    public const int   SectorClearBonus   = 600;
    public static readonly string[] SectorNames =
        { "SHALLOWS", "THE DEEP", "DRIFT", "THE HOLLOW", "ECHO CORE",
          "LONG DARK", "STILLWATER", "THE LATTICE" };
    public static readonly Color[] SectorWallColors =
    {
        new Color(0.55f, 0.80f, 1.00f, 1f), // blue-white
        new Color(0.45f, 1.00f, 0.85f, 1f), // teal
        new Color(0.80f, 0.65f, 1.00f, 1f), // violet
        new Color(1.00f, 0.70f, 0.45f, 1f), // amber
        new Color(1.00f, 0.45f, 0.55f, 1f), // rose
        new Color(0.60f, 0.70f, 1.00f, 1f), // indigo
        new Color(0.70f, 0.95f, 0.80f, 1f), // sea green
        new Color(0.95f, 0.85f, 0.60f, 1f), // sand
    };

    // ---- Deep progression (past the authored ramp) ----
    // The run is endless, so difficulty must be too. Levels 1..DeepStartLevel keep the hand-tuned
    // ramp untouched; past it every knob keeps moving but converges on a floor, so the game gets
    // relentlessly harder without ever becoming unwinnable.
    public const int   DeepStartLevel     = 13;   // where the authored ramp tops out
    // Difficulty deliberately CONVERGES rather than rising forever — past this ramp every knob has
    // reached its floor and the run stays hard-but-winnable indefinitely. Escalating without limit
    // would just produce an unwinnable wall. What stays endless is the run itself: fresh mazes,
    // climbing level count, and sector names that keep advancing (SHALLOWS -> SHALLOWS II -> ...).
    public const float DeepRampLevels     = 60f;  // levels taken to reach full deep pressure
    public const int   DeepMaxMazeSize    = 16;   // hard cap: past this, cells are too small to read
    public const int   DeepSizeStep       = 8;    // +1 grid row every N levels in the deep phase
    public const float SecondsPerExtraRow = 3.5f; // bigger grids buy time so they stay fair...
    public const float DeepTimeSqueeze    = 0.72f;// ...and the deep ramp claws it back
    public const float DeepPingSqueeze    = 0.78f;
    public const int   MinPings           = 5;    // never leave the player without a way to look
    public const float DeepExitInterval   = 1.7f; // moving exit gets twitchier
    public const float DeepDecoyFade      = 3.0f; // decoys blink faster = harder to time

    // ---- Bonus Echo orb (the variable reward) ----
    // Uncertain rewards hit harder than guaranteed ones, so this only appears some of the time and
    // only the sonar can find it — every ping becomes a small "did I hit gold?" pull.
    public const float BonusOrbChance     = 0.4f;   // fraction of levels that contain one
    public const int   BonusOrbScore      = 300;
    public const float BonusOrbRadius     = 0.34f;  // pickup radius
    public const float BonusOrbPulseSpeed = 3.2f;

    // ---- Near-miss / clutch ----
    public const float FailRevealTime     = 1.8f;   // maze lights up on a timeout so you see how close
    public const float ClutchSeconds      = 3f;     // clearing with less than this left = CLUTCH
    public const int   ClutchBonus        = 400;

    // ---- Daily streak ----
    public const int   DailyBonusPingsMax = 3;      // extra pings on your first level of the day

    // ---- Tutorial ----
    public const float TutorialDragDistance = 1.6f; // world units the player must drag to pass step 1

    // ---- Performance ----
    public const int   TargetFrameRate    = 60;

    // ---- Colors ----
    public static readonly Color BackgroundColor = new Color(0.015f, 0.02f, 0.035f, 1f);
    public static readonly Color WallGlowColor   = new Color(0.55f, 0.8f, 1.0f, 1f);
    public static readonly Color PlayerColor     = new Color(0.6f, 0.9f, 1.0f, 1f);
    public static readonly Color ExitColor       = new Color(0.4f, 1.0f, 0.7f, 1f);
    public static readonly Color RingColor       = new Color(0.6f, 0.85f, 1.0f, 1f);
    public static readonly Color DecoyColor      = new Color(1.0f, 0.5f, 0.35f, 1f); // warm = "not the exit"
    public static readonly Color DecoyRingColor  = new Color(1.0f, 0.22f, 0.22f, 1f); // red highlight outline
    public static readonly Color StreakColor     = new Color(1.0f, 0.6f, 0.15f, 1f); // flame
    public static readonly Color BonusOrbColor   = new Color(1.0f, 0.85f, 0.25f, 1f); // gold reward

    // ---- Sector helpers ----
    public static int SectorIndex(int level) => Mathf.Max(0, (level - 1) / LevelsPerSector);

    /// <summary>
    /// Sector label. The name list cycles, but each full lap appends a numeral ("SHALLOWS II"),
    /// so a player 200 levels deep still sees a name they have never seen before rather than
    /// silently looping back to level 1's.
    /// </summary>
    public static string SectorName(int level)
    {
        int idx = SectorIndex(level);
        string name = SectorNames[idx % SectorNames.Length];
        int lap = idx / SectorNames.Length;
        return lap == 0 ? name : name + " " + Numeral(lap + 1);
    }

    /// <summary>Roman numerals for the sector lap; falls back to digits well past any real run.</summary>
    private static string Numeral(int n)
    {
        switch (n)
        {
            case 2:  return "II";
            case 3:  return "III";
            case 4:  return "IV";
            case 5:  return "V";
            case 6:  return "VI";
            case 7:  return "VII";
            case 8:  return "VIII";
            case 9:  return "IX";
            case 10: return "X";
            default: return n.ToString();
        }
    }

    public static Color SectorWallColor(int level) => SectorWallColors[SectorIndex(level) % SectorWallColors.Length];
    /// <summary>True on the last level of a sector (i.e. clearing it completes the sector).</summary>
    public static bool IsSectorFinale(int level) => level % LevelsPerSector == 0;
    /// <summary>1-based position within the current sector, for "3/5" style readouts.</summary>
    public static int LevelInSector(int level) => ((level - 1) % LevelsPerSector) + 1;

    /// <summary>
    /// Build the difficulty profile for a given level. <paramref name="failStreak"/> is the
    /// number of consecutive fails on THIS level, used for rubber-banding.
    /// </summary>
    public static Difficulty GetDifficulty(int level, int failStreak)
    {
        var d = new Difficulty();

        // 0 until the authored ramp is done, then 0..1 across DeepRampLevels. Every deep knob
        // below is a Lerp on this, so they all converge instead of running away.
        float deepT = Mathf.Clamp01((level - DeepStartLevel) / DeepRampLevels);

        // ---- Grid ----
        d.mazeSize = Mathf.Clamp(StartMazeSize + (level - 1) / LevelsPerSizeStep,
                                 StartMazeSize, MaxMazeSize);
        if (level > DeepStartLevel)
        {
            // Keeps growing past the authored cap, but far more slowly and to a hard ceiling —
            // beyond DeepMaxMazeSize the cells are too small to read on a phone.
            d.mazeSize = Mathf.Min(DeepMaxMazeSize,
                                   d.mazeSize + (level - DeepStartLevel) / DeepSizeStep);
        }

        // ---- Reveals ----
        int earlyBonus = Mathf.Max(0, TutorialPingBonus - (level - 1));
        int rubber = failStreak >= RubberBandFails ? RubberBandPings : 0;
        d.pings = d.mazeSize + earlyBonus + rubber;
        if (level > DeepStartLevel)
        {
            // Squeezed relative to grid size, so a bigger maze does NOT hand out proportionally
            // more sight — the deeper you go, the less of the maze one run can afford to light up.
            d.pings = Mathf.Max(MinPings, Mathf.RoundToInt(d.pings * Mathf.Lerp(1f, DeepPingSqueeze, deepT)));
        }

        // 0 at level 1, 1 at the top of the authored ramp.
        float ramp = Mathf.Clamp01((level - 1) / FadeRampLevels);
        d.fade      = Mathf.Lerp(FadeStart, FadeEnd, ramp);
        d.ringSpeed = Mathf.Lerp(RingSpeedStart, RingSpeedEnd, ramp);
        d.band      = Mathf.Lerp(BandStart, BandEnd, ramp);

        // ---- Clock ----
        d.timeLimit = LevelTimeLimit;
        if (level > DeepStartLevel)
        {
            // Extra grid rows buy seconds (a 16x16 in 45s would be unwinnable, not hard), and the
            // deep ramp then takes a cut back off the top.
            float sizeBonus = (d.mazeSize - MaxMazeSize) * SecondsPerExtraRow;
            d.timeLimit = (LevelTimeLimit + sizeBonus) * Mathf.Lerp(1f, DeepTimeSqueeze, deepT);
        }

        // ---- Twists ----
        d.movingExit = level >= MovingExitLevel;
        d.exitMoveInterval = Mathf.Lerp(ExitMoveInterval, DeepExitInterval, deepT);

        d.decoyCount = level >= DecoyStartLevel
            ? Mathf.Min(MaxDecoys, 1 + (level - DecoyStartLevel) / DecoyEveryLevels)
            : 0;
        d.decoyFadeSpeed = Mathf.Lerp(DecoyFadeSpeed, DeepDecoyFade, deepT);

        return d;
    }
}
