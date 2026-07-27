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

    /// <summary>
    /// Build the difficulty profile for a given level. <paramref name="failStreak"/> is the
    /// number of consecutive fails on THIS level, used for rubber-banding.
    /// </summary>
    public static Difficulty GetDifficulty(int level, int failStreak)
    {
        var d = new Difficulty();

        d.mazeSize = Mathf.Clamp(StartMazeSize + (level - 1) / LevelsPerSizeStep,
                                 StartMazeSize, MaxMazeSize);

        int earlyBonus = Mathf.Max(0, TutorialPingBonus - (level - 1));
        int rubber = failStreak >= RubberBandFails ? RubberBandPings : 0;
        d.pings = d.mazeSize + earlyBonus + rubber;

        // 0 at level 1, 1 at the top of the ramp.
        float ramp = Mathf.Clamp01((level - 1) / FadeRampLevels);
        d.fade      = Mathf.Lerp(FadeStart, FadeEnd, ramp);
        d.ringSpeed = Mathf.Lerp(RingSpeedStart, RingSpeedEnd, ramp);
        d.band      = Mathf.Lerp(BandStart, BandEnd, ramp);

        d.timeLimit = LevelTimeLimit;

        d.movingExit = level >= MovingExitLevel;
        d.exitMoveInterval = ExitMoveInterval;

        d.decoyCount = level >= DecoyStartLevel
            ? Mathf.Min(MaxDecoys, 1 + (level - DecoyStartLevel) / DecoyEveryLevels)
            : 0;

        return d;
    }
}
