using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameState { Start, Playing, Celebrating, Tutorial }

/// <summary>
/// The brain: level flow, ping budget, fail timer, streak + star scoring, rubber-banding,
/// the twists (moving exit, decoys), near-miss exit glow, and the whole level-clear
/// celebration (hitstop -> burst -> flash -> punch-zoom -> rolling score -> star pops ->
/// auto-advance). Everything else is handed to it by GameBootstrap.
/// </summary>
public class GameManager : MonoBehaviour
{
    public GameState State { get; private set; } = GameState.Start;

    /// <summary>Player may move/ping — true during normal play and while practising in the tutorial.</summary>
    public bool AcceptsInput => (State == GameState.Playing || State == GameState.Tutorial) && !_ui.SettingsOpen;

    /// <summary>A UI button was just pressed, so this tap must not also count as a gameplay tap.</summary>
    public bool UiJustPressed => _ui != null && _ui.UiJustPressed;

    private Camera _cam;
    private PlayerController _player;
    private SonarManager _sonar;
    private UIManager _ui;
    private ProceduralAudio _audio;
    private WallShaderController _wallCtrl;
    private FxManager _fx;
    private SpriteRenderer _exitSR;
    private SpriteRenderer _exitRingSR;
    private Transform _vignette;

    // Progress.
    private int _level = 1;
    private int _score;
    private int _pings, _pingsStart;
    private int _failStreak;   // consecutive fails on the current level (rubber-banding)
    private int _streak;       // consecutive clears with pings to spare
    private float _streakMul = 1f;

    // Level state.
    private MazeData _maze;
    private Difficulty _profile;
    private float _levelTimer;
    private float _pingReadyTime;   // ping cooldown gate
    private int _lastTickSecond;    // for timer audio cues

    // Moving exit.
    private bool _movingExit;
    private Vector2Int _exitCell;
    private Vector2 _exitWorld;
    private float _exitMoveTimer;
    private readonly List<Vector2Int> _nbrScratch = new List<Vector2Int>(4);

    // Decoys.
    private SpriteRenderer[] _decoySR;      // pulsing hazard ball
    private SpriteRenderer[] _decoyRingSR;  // hollow highlight ring, lit by the sonar reveal
    private Vector2[] _decoyPos;
    private float[] _decoyHideUntil;
    private float[] _decoyPhase;
    private int _decoyCount;

    // Bonus Echo orb (variable reward) — only visible while the sonar sweep is over it.
    private SpriteRenderer _orbSR;
    private Vector2 _orbPos;
    private bool _orbActive;

    // Daily streak (set once at StartGame).
    private int _dayStreak;
    private bool _isDaily;
    private TutorialController _tutorial;

    // Camera fx.
    private Vector3 _camBase;
    private float _baseOrthoSize;
    private float _shakeTimer;
    private float _punchTimer;

    public void Init(Camera cam, PlayerController player, SonarManager sonar, UIManager ui,
                     ProceduralAudio audio, WallShaderController wallCtrl, FxManager fx,
                     SpriteRenderer exitSR, Transform vignette)
    {
        _cam = cam; _player = player; _sonar = sonar; _ui = ui; _audio = audio;
        _wallCtrl = wallCtrl; _fx = fx; _exitSR = exitSR; _vignette = vignette;

        _tutorial = gameObject.AddComponent<TutorialController>();

        // Decoy pool.
        var decoyMat = new Material(Shader.Find("EchoMaze/Additive")) { name = "DecoyMat" };
        var ballSprite = VisualUtils.RadialGlow(); // the pulsing hazard
        var ringSprite = VisualUtils.HollowRing(); // the reveal highlight (clean circle outline)
        var container = new GameObject("Decoys").transform;
        container.SetParent(transform, false);
        _decoySR = new SpriteRenderer[GameConfig.MaxDecoys];
        _decoyRingSR = new SpriteRenderer[GameConfig.MaxDecoys];
        _decoyPos = new Vector2[GameConfig.MaxDecoys];
        _decoyHideUntil = new float[GameConfig.MaxDecoys];
        _decoyPhase = new float[GameConfig.MaxDecoys];
        for (int i = 0; i < GameConfig.MaxDecoys; i++)
        {
            var go = new GameObject("Decoy" + i);
            go.transform.SetParent(container, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ballSprite; sr.sharedMaterial = decoyMat;
            sr.color = GameConfig.DecoyColor; sr.sortingOrder = 28;
            go.transform.localScale = Vector3.one * (GameConfig.CellSize * 0.7f);
            go.SetActive(false);
            _decoySR[i] = sr;

            // Hollow highlight ring, drawn a touch larger, sitting around the ball.
            var rgo = new GameObject("DecoyRing" + i);
            rgo.transform.SetParent(container, false);
            var rsr = rgo.AddComponent<SpriteRenderer>();
            rsr.sprite = ringSprite; rsr.sharedMaterial = decoyMat;
            rsr.color = GameConfig.DecoyRingColor; rsr.sortingOrder = 29;
            // Bigger than the ball so the outline sits clearly AROUND it (ring ~0.86*scale across).
            rgo.transform.localScale = Vector3.one * (GameConfig.CellSize * 1.35f);
            rgo.SetActive(false);
            _decoyRingSR[i] = rsr;
        }

        // Bonus Echo orb — a golden reward hidden in a dead-end, findable only by pinging.
        var orbGO = new GameObject("BonusOrb");
        orbGO.transform.SetParent(transform, false);
        _orbSR = orbGO.AddComponent<SpriteRenderer>();
        _orbSR.sprite = ballSprite;
        _orbSR.sharedMaterial = decoyMat;
        _orbSR.color = GameConfig.BonusOrbColor;
        _orbSR.sortingOrder = 31;
        orbGO.SetActive(false);

        // Exit target ring — a pulsing green marker that makes the destination pop.
        var exitRingGO = new GameObject("ExitRing");
        exitRingGO.transform.SetParent(transform, false);
        _exitRingSR = exitRingGO.AddComponent<SpriteRenderer>();
        _exitRingSR.sprite = VisualUtils.HollowRing();
        _exitRingSR.sharedMaterial = decoyMat;
        _exitRingSR.color = GameConfig.ExitColor;
        _exitRingSR.sortingOrder = 29;
    }

    /// <summary>Show the main menu. Nothing runs until the player presses PLAY or DAILY.</summary>
    public void StartGame()
    {
        SaveData.ApplySettings();

        _ui.OnPlay = BeginEndlessRun;
        _ui.OnDaily = BeginDailyRun;
        _ui.OnDailyResultClosed = ReturnToMenu;
        _ui.OnProgressReset = OnProgressReset;

        ReturnToMenu();
    }

    /// <summary>
    /// Progress was wiped from the settings panel. Abandon whatever run is in flight and go back
    /// to a clean menu — continuing on level 14 with an empty save makes no sense.
    /// </summary>
    private void OnProgressReset()
    {
        StopAllCoroutines();      // cancel any celebration / fail sequence mid-flight
        Time.timeScale = 1f;      // ...which may have left time frozen
        _ui.HideCelebration();
        _ui.HideNewBest();
        _ui.SetCover(0f);
        ReturnToMenu();
    }

    private void ReturnToMenu()
    {
        if (_tutorial != null) _tutorial.Hide();   // never let the tutorial overlay sit on the menu
        _isDaily = false;
        _level = 1; _score = 0; _streak = 0; _streakMul = 1f; _failStreak = 0;
        _dayStreak = SaveData.DayStreak;

        BuildLevel(_level);                 // something alive behind the menu
        State = GameState.Start;
        _ui.ShowStart(SaveData.BestScore, SaveData.BestStreak, _dayStreak, SaveData.DailyDone);
    }

    /// <summary>Normal endless progression.</summary>
    private void BeginEndlessRun()
    {
        _isDaily = false;
        _level = 1; _score = 0; _streak = 0; _streakMul = 1f; _failStreak = 0;
        _dayStreak = SaveData.RegisterDailyVisit();   // opening the game counts toward the streak

        BuildLevel(_level);
        _ui.HideStart();
        State = _tutorial != null && _tutorial.ShouldRun ? GameState.Tutorial : GameState.Playing;
        if (State == GameState.Tutorial) _tutorial.Begin(this, _ui, _player);
    }

    /// <summary>
    /// The Daily Maze: one fixed layout per day, a single attempt, its own result screen.
    /// Deliberately a separate mode so the ritual is visible — a daily nobody knows about
    /// retains nobody.
    /// </summary>
    private void BeginDailyRun()
    {
        _isDaily = true;
        _level = 1; _score = 0; _streak = 0; _streakMul = 1f; _failStreak = 0;
        _dayStreak = SaveData.RegisterDailyVisit();

        BuildLevel(_level);
        _ui.HideStart();
        State = GameState.Playing;
        _ui.ShowBanner("DAILY MAZE", new Color(1f, 0.85f, 0.4f, 1f), 1.0f);
    }

    private void BuildLevel(int level)
    {
        _profile = GameConfig.GetDifficulty(level, _failStreak);

        // The daily run uses the date seed so every player worldwide gets the same layout today.
        int seed = _isDaily ? SaveData.DailySeed : Random.Range(1, int.MaxValue);
        _maze = MazeGenerator.Generate(_profile.mazeSize, GameConfig.CellSize, seed);

        // Per-sector palette: re-tint walls and the ping ring so each chapter reads differently.
        Color sectorColor = GameConfig.SectorWallColor(level);
        _wallCtrl.SetGlowColor(sectorColor);
        _sonar.SetRingColor(sectorColor);

        _wallCtrl.Build(_maze);
        _sonar.SetWalls(_maze.walls);
        _sonar.ApplyProfile(_profile);
        _sonar.SetMazeDiagonal(new Vector2(_maze.worldWidth, _maze.worldHeight).magnitude);
        _sonar.ResetPings();

        _player.PlaceAt(_maze.startPos);

        _exitCell = _maze.exitCell;
        _exitWorld = _maze.exitPos;
        _exitSR.transform.position = new Vector3(_exitWorld.x, _exitWorld.y, 0f);
        _exitSR.transform.localScale = Vector3.one * (GameConfig.CellSize * 0.8f);
        _exitRingSR.transform.position = new Vector3(_exitWorld.x, _exitWorld.y, 0f);

        _movingExit = _profile.movingExit;
        _exitMoveTimer = _profile.exitMoveInterval;

        PlaceDecoys(_profile.decoyCount);
        PlaceBonusOrb();

        // Daily-streak reward: extra reveals on your first level of the day.
        int dailyBonus = level == 1 ? Mathf.Min(GameConfig.DailyBonusPingsMax, Mathf.Max(0, _dayStreak - 1)) : 0;
        _pingsStart = _profile.pings + dailyBonus;
        _pings = _pingsStart;
        _ui.BuildPingDots(_pingsStart);
        _ui.SetPingsRemaining(_pings);
        _ui.SetLevel(level);
        _ui.SetSector(GameConfig.SectorName(level), GameConfig.LevelInSector(level),
                      GameConfig.LevelsPerSector, sectorColor);
        _ui.SetScoreImmediate(_score);
        _ui.SetStreak(_streak, _streakMul);

        // Announce a new sector as you enter it.
        if (GameConfig.LevelInSector(level) == 1 && level > 1)
            _ui.ShowBanner("SECTOR " + (GameConfig.SectorIndex(level) + 1) + "\n" + GameConfig.SectorName(level),
                           sectorColor, 1.3f);

        _levelTimer = _profile.timeLimit;
        _pingReadyTime = 0f;
        _lastTickSecond = int.MaxValue;
        FitCamera(_maze);

        _ui.HideHint(); // onboarding is handled by TutorialController now
    }

    /// <summary>
    /// Maybe hide a bonus orb in a dead-end. It only appears some of the time (uncertain rewards
    /// are far more compelling than guaranteed ones) and is invisible until a ping sweeps over it,
    /// so every ping carries a small "did I find gold?" thrill.
    /// </summary>
    private void PlaceBonusOrb()
    {
        _orbActive = false;
        _orbSR.gameObject.SetActive(false);

        var ends = _maze.deadEnds;
        if (ends == null || ends.Count == 0) return;
        if (Random.value > GameConfig.BonusOrbChance) return;

        // Prefer a dead-end away from the start so it's a real detour decision.
        float minDistSqr = (GameConfig.CellSize * 2.5f) * (GameConfig.CellSize * 2.5f);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var cell = ends[Random.Range(0, ends.Count)];
            Vector2 pos = _maze.CellCenter(cell.x, cell.y);
            if ((pos - _maze.startPos).sqrMagnitude < minDistSqr) continue;

            _orbPos = pos;
            _orbActive = true;
            _orbSR.transform.position = new Vector3(pos.x, pos.y, 0f);
            _orbSR.transform.localScale = Vector3.one * (GameConfig.CellSize * 0.5f);
            _orbSR.color = new Color(GameConfig.BonusOrbColor.r, GameConfig.BonusOrbColor.g, GameConfig.BonusOrbColor.b, 0f);
            _orbSR.gameObject.SetActive(true);
            return;
        }
    }

    private void UpdateBonusOrb()
    {
        if (!_orbActive) return;

        // Lit by the sonar exactly like the walls, with a gentle pulse on top.
        float reveal = _sonar.RevealAt(_orbPos);
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * GameConfig.BonusOrbPulseSpeed);
        var c = GameConfig.BonusOrbColor;
        c.a = Mathf.Clamp01(reveal * pulse * 1.3f);
        _orbSR.color = c;
        _orbSR.transform.localScale = Vector3.one * (GameConfig.CellSize * (0.45f + 0.15f * reveal));

        // Collect on contact (whether or not it happens to be lit at that instant).
        if (SweptHit(_orbPos, GameConfig.BonusOrbRadius))
            CollectBonusOrb();
    }

    private void CollectBonusOrb()
    {
        _orbActive = false;
        _orbSR.gameObject.SetActive(false);

        _score += GameConfig.BonusOrbScore;
        _ui.RollScoreTo(_score);
        _ui.ShowBanner("+" + GameConfig.BonusOrbScore + "  ECHO", GameConfig.BonusOrbColor, 0.5f);
        _ui.FlashColor(GameConfig.BonusOrbColor, 0.3f);
        _fx.PlayExitBurst(_orbPos);
        _audio.PlayStar(2);
        Haptics.Medium();
    }

    private void PlaceDecoys(int count)
    {
        _decoyCount = 0;
        for (int i = 0; i < GameConfig.MaxDecoys; i++)
        {
            _decoySR[i].gameObject.SetActive(false);
            _decoyRingSR[i].gameObject.SetActive(false);
        }

        var path = _maze.solutionPath;
        if (path == null || path.Count < 4 || count <= 0) return;

        // Place decoys ON the route the player has to take, spaced along it, avoiding the
        // first couple of cells (near start) and the exit cell.
        int lo = 2;
        int hi = path.Count - 2;               // exclusive of exit
        if (hi <= lo) return;
        int slots = Mathf.Min(count, GameConfig.MaxDecoys);

        int placed = 0;
        for (int k = 1; k <= slots; k++)
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(lo, hi, k / (float)(slots + 1))), lo, hi);
            var cell = path[idx];
            Vector2 pos = _maze.CellCenter(cell.x, cell.y);

            _decoyPos[placed] = pos;
            _decoyHideUntil[placed] = 0f;
            _decoyPhase[placed] = Random.Range(0f, Mathf.PI * 2f); // desync the blinking
            var transparent = new Color(GameConfig.DecoyColor.r, GameConfig.DecoyColor.g, GameConfig.DecoyColor.b, 0f);

            var sr = _decoySR[placed];
            sr.transform.position = new Vector3(pos.x, pos.y, 0f);
            sr.color = transparent;
            sr.gameObject.SetActive(true);

            var ring = _decoyRingSR[placed];
            ring.transform.position = new Vector3(pos.x, pos.y, 0f);
            ring.color = transparent;
            ring.gameObject.SetActive(true);
            placed++;
        }
        _decoyCount = placed;
    }

    /// <summary>Called by the tutorial when both steps are done — hands control back to play.</summary>
    public void OnTutorialComplete()
    {
        if (State == GameState.Tutorial) State = GameState.Playing;
    }

    public void RequestPing()
    {
        // During the tutorial only the ping STEP may fire one, so the drag lesson can't be skipped.
        if (State == GameState.Tutorial)
        {
            if (_tutorial == null || !_tutorial.PingAllowed) return;
            if (Time.time < _pingReadyTime || _pings <= 0) return;
            _pings--;
            _ui.SetPingsRemaining(_pings);
            _ui.DarkFlash();
            _sonar.EmitPing(_player.transform.position);
            _pingReadyTime = Time.time + GameConfig.PingCooldown;
            _tutorial.NotifyPinged();
            return;
        }

        if (State != GameState.Playing) return;
        if (_ui.SettingsOpen) return;
        if (Time.time < _pingReadyTime) return;  // cooldown: can't spam — wait for the reveal to finish
        if (_pings <= 0) return;                  // out of pings, but you can still move blind

        _pings--;
        _ui.SetPingsRemaining(_pings);
        _ui.DarkFlash();                 // brief darken so the ring burst reads as powerful
        _sonar.EmitPing(_player.transform.position);
        _pingReadyTime = Time.time + GameConfig.PingCooldown;
    }

    private void Update()
    {
        switch (State)
        {
            case GameState.Start:
                return; // menu buttons drive everything from here

            case GameState.Celebrating:
                return; // fully automatic

            case GameState.Tutorial:
                // The maze is live so the player can practise, but the level timer is paused and
                // the exit can't be completed until they've learned both controls.
                UpdateBonusOrb();
                PulseExit();
                return;

            case GameState.Playing:
                if (_ui.SettingsOpen) return;   // settings acts as a pause
                TickPlaying();
                return;
        }
    }

    private void TickPlaying()
    {
        if (_player.IsRewinding) return; // world is frozen mid-rewind

        if (_profile.timeLimit > 0f)
        {
            _levelTimer -= Time.deltaTime;
            int secs = Mathf.CeilToInt(Mathf.Max(0f, _levelTimer));
            _ui.SetTimer(secs);

            // Audio cues: one heads-up at 10s, then a rising tick each of the last 5 seconds.
            if (secs != _lastTickSecond)
            {
                // Haptics mirror the audio: the countdown is the one cue a player must not
                // miss, and it has to land even with the phone muted in a pocket.
                if (secs == GameConfig.TimerWarnAt) { _audio.PlayTimeWarning(); Haptics.Medium(); }
                else if (secs >= 1 && secs <= GameConfig.TimerTickFrom)
                {
                    _audio.PlayCountdownTick(secs);
                    if (secs <= 3) Haptics.Medium(); else Haptics.Light();
                }
                _lastTickSecond = secs;
            }

            if (_levelTimer <= 0f) { StartCoroutine(FailRoutine()); return; }
        }
        else _ui.SetTimer(-1);

        UpdateMovingExit();
        UpdateDecoys();
        UpdateBonusOrb();
        PulseExit();

        float exitR = GameConfig.CellSize * 0.4f;
        if (SweptHit(_exitWorld, exitR))
            StartCoroutine(WinRoutine());
    }

    /// <summary>
    /// Did the dot pass within <paramref name="radius"/> of <paramref name="target"/> at any point
    /// this frame? Tests the whole segment travelled, not just where the dot ended up — a fast drag
    /// applies its finger delta in one go and could otherwise skip clean over the exit.
    /// </summary>
    private bool SweptHit(Vector2 target, float radius)
    {
        Vector2 a = _player.PrevPosition;
        Vector2 b = _player.transform.position;
        Vector2 ab = b - a;
        float len2 = Vector2.Dot(ab, ab);
        Vector2 closest = len2 < 1e-8f
            ? a
            : a + ab * Mathf.Clamp01(Vector2.Dot(target - a, ab) / len2);
        return (target - closest).sqrMagnitude < radius * radius;
    }

    private void UpdateMovingExit()
    {
        if (!_movingExit) return;

        _exitMoveTimer -= Time.deltaTime;
        if (_exitMoveTimer <= 0f)
        {
            _exitMoveTimer = _profile.exitMoveInterval;
            OpenNeighbors(_exitCell, _nbrScratch);
            if (_nbrScratch.Count > 0)
                _exitCell = _nbrScratch[Random.Range(0, _nbrScratch.Count)];
        }

        Vector2 target = _maze.CellCenter(_exitCell.x, _exitCell.y);
        _exitWorld = Vector2.Lerp(_exitWorld, target, Time.deltaTime * GameConfig.ExitMoveLerp);
        _exitSR.transform.position = new Vector3(_exitWorld.x, _exitWorld.y, 0f);
    }

    private void OpenNeighbors(Vector2Int c, List<Vector2Int> outList)
    {
        outList.Clear();
        if (_maze.IsOpen(c.x, c.y, MazeGenerator.N)) outList.Add(new Vector2Int(c.x, c.y + 1));
        if (_maze.IsOpen(c.x, c.y, MazeGenerator.E)) outList.Add(new Vector2Int(c.x + 1, c.y));
        if (_maze.IsOpen(c.x, c.y, MazeGenerator.S)) outList.Add(new Vector2Int(c.x, c.y - 1));
        if (_maze.IsOpen(c.x, c.y, MazeGenerator.W)) outList.Add(new Vector2Int(c.x - 1, c.y));
    }

    private void UpdateDecoys()
    {
        float hitRadius = GameConfig.CellSize * 0.4f;

        for (int i = 0; i < _decoyCount; i++)
        {
            var sr = _decoySR[i];
            bool blackedOut = Time.time < _decoyHideUntil[i]; // just triggered a rewind, stays gone briefly

            // Pulsing orange ball (the hazard): fades in and out on its own phase — slip through
            // the cell while it's dark, and it only bites while it's visible.
            float wave = Mathf.Sin(Time.time * GameConfig.DecoyFadeSpeed + _decoyPhase[i]);
            float vis = Mathf.Clamp01(wave); vis *= vis;
            float alpha = blackedOut ? 0f : vis * GameConfig.DecoyMaxAlpha;
            var c = GameConfig.DecoyColor; c.a = alpha;
            sr.color = c;
            sr.transform.localScale = Vector3.one * (GameConfig.CellSize * (0.55f + 0.2f * vis));

            // Hollow highlight ring around it: lit by the SONAR as the ping front sweeps over the
            // decoy (same timing as the walls), so a ping also shows you where the decoys are.
            float reveal = blackedOut ? 0f : _sonar.RevealAt(_decoyPos[i]);
            var rc = GameConfig.DecoyRingColor; rc.a = Mathf.Clamp01(reveal * 1.25f);
            _decoyRingSR[i].color = rc;

            // Unchanged: only bites while the ball is actually visible. Swept like the exit, so
            // flicking through a lit decoy can't dodge the penalty either.
            if (!blackedOut && vis > GameConfig.DecoyVisibleHit &&
                SweptHit(_decoyPos[i], hitRadius))
            {
                HitDecoy(i);
                return;
            }
        }
    }

    private void HitDecoy(int i)
    {
        // Penalty: freeze time, wipe the reveal, and rewind the dot back its own path.
        // Kept gentle — soft flash, medium haptic, small shake — so it doesn't feel harsh.
        _audio.PlayWrong();                  // collision "thunk" the instant you touch it
        Haptics.Wrong();                     // the platform's "error" pattern: unmistakably bad
        _ui.FlashColor(new Color(0.4f, 0.8f, 1f, 1f), 0.25f);
        _ui.ShowRewind();
        _ui.PlayRewindEffect();
        _fx.PlayDecoyPop(_decoyPos[i]);
        _shakeTimer = Mathf.Max(_shakeTimer, 0.12f);
        _decoyHideUntil[i] = Time.time + 4f;
        _player.TriggerRewind();             // plays the rewind sound + retrace + reveal wipe
    }

    private void PulseExit()
    {
        float d = Vector2.Distance(_player.transform.position, _exitWorld);
        float near = Mathf.Clamp01(1f - d / GameConfig.ExitNearRadius);      // 1 when player is close
        float baseP = GameConfig.ExitPulseBaseAlpha +
                      GameConfig.ExitPulseAmp * (0.5f + 0.5f * Mathf.Sin(Time.time * GameConfig.ExitPulseSpeed));
        float alpha = baseP + GameConfig.ExitNearMaxBoost * near;             // "I'm close!" brightening
        var c = GameConfig.ExitColor; c.a = alpha;
        _exitSR.color = c;
        _exitSR.transform.localScale = Vector3.one * (GameConfig.CellSize * (0.8f + 0.25f * near));

        // Pulsing target ring around the exit, brighter as you approach — makes the goal pop.
        float ringPulse = 0.5f + 0.5f * Mathf.Sin(Time.time * GameConfig.ExitPulseSpeed * 1.3f);
        _exitRingSR.transform.position = _exitSR.transform.position;
        _exitRingSR.transform.localScale = Vector3.one * (GameConfig.CellSize * (1.25f + 0.12f * ringPulse));
        var rc = GameConfig.ExitColor; rc.a = 0.28f + 0.22f * ringPulse + 0.4f * near;
        _exitRingSR.color = rc;
    }

    private IEnumerator WinRoutine()
    {
        State = GameState.Celebrating;

        // ---- Score + records ----
        int timeBonus = _profile.timeLimit > 0f ? Mathf.RoundToInt(Mathf.Max(0f, _levelTimer) * GameConfig.ScorePerSecond) : 0;
        bool keptSpare = _pings >= GameConfig.StreakMinPingsSpare;
        if (keptSpare) _streak++;                    // build the streak (precious!)
        _streakMul = Mathf.Min(GameConfig.MaxStreakMultiplier, 1f + _streak * GameConfig.StreakStep);

        float frac = _pingsStart > 0 ? (float)_pings / _pingsStart : 0f;
        int stars = 1;
        if (frac >= GameConfig.Star2PingFrac) stars = 2;
        if (frac >= GameConfig.Star3PingFrac) stars = 3;

        // Clutch: finishing with almost no time left is the most exciting way to win, so it pays.
        bool clutch = _profile.timeLimit > 0f && _levelTimer <= GameConfig.ClutchSeconds;
        bool sectorDone = GameConfig.IsSectorFinale(_level);

        int extras = (clutch ? GameConfig.ClutchBonus : 0) + (sectorDone ? GameConfig.SectorClearBonus : 0);
        int gained = Mathf.RoundToInt((GameConfig.ScoreBaseClear + _pings * GameConfig.ScorePerPing + timeBonus + extras) * _streakMul);
        int newScore = _score + gained;

        bool newBestScore = SaveData.TrySetBestScore(newScore);
        bool newBestStreak = SaveData.TrySetBestStreak(_streak);
        SaveData.TrySetStars(_level, stars);

        // ---- Immediate impact ----
        _audio.PlayWin();
        Haptics.Success();
        _fx.PlayExitBurst(_exitWorld);
        _ui.Flash(0.7f);
        _shakeTimer = GameConfig.ShakeDuration;
        _punchTimer = GameConfig.PunchZoomTime;

        // Standout finishes are shown INSIDE the celebration panel (title + score line) rather than
        // as a floating banner, which used to overlap the panel's own title.

        // ---- Hitstop ----
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(GameConfig.HitstopTime);
        Time.timeScale = 1f;

        // ---- Celebration panel ----
        _ui.ShowCelebration(_level);
        if (sectorDone)
            _ui.SetCelebrationTitle("SECTOR CLEAR\n" + GameConfig.SectorName(_level), GameConfig.SectorWallColor(_level));
        else if (clutch)
            _ui.SetCelebrationTitle("CLUTCH CLEAR!", new Color(1f, 0.85f, 0.3f, 1f));

        _ui.SetStreak(_streak, _streakMul);
        _score = newScore;
        _ui.RollScoreTo(newScore);

        // Score line spells out the bonuses that made up this clear.
        string line = "+" + gained;
        if (_streakMul > 1f) line += "   x" + _streakMul.ToString("0.0");
        if (clutch) line += "\nCLUTCH +" + GameConfig.ClutchBonus;
        if (sectorDone) line += "\nSECTOR +" + GameConfig.SectorClearBonus;
        _ui.SetCelebrationScoreLine(line);

        // Stars pop in one-by-one with a haptic tap each.
        for (int i = 0; i < stars; i++)
        {
            yield return new WaitForSecondsRealtime(0.22f);
            _ui.PopStar(i);
            _audio.PlayStar(i);
            Haptics.Medium();
        }

        if (keptSpare) _audio.PlayStreak(_streak);
        if (newBestScore || newBestStreak) _ui.ShowNewBest();

        // The daily is a single maze, not a run — finish it and show its own result screen.
        if (_isDaily)
        {
            yield return new WaitForSecondsRealtime(GameConfig.CelebrationTime);
            bool dailyBest = SaveData.CompleteDaily(newScore);
            _ui.HideCelebration();
            _ui.HideNewBest();
            _ui.ShowDailyResult(true, newScore, _dayStreak, dailyBest);
            State = GameState.Start;   // menu buttons take over again via OnDailyResultClosed
            yield break;
        }

        // ---- One-more-level: auto-advance behind a quick fade so the swap isn't a hard cut ----
        yield return new WaitForSecondsRealtime(GameConfig.CelebrationTime);

        _ui.SetCover(1f);                                   // fade to black
        yield return new WaitForSecondsRealtime(0.22f);

        _failStreak = 0;
        _level++;
        BuildLevel(_level);                                 // camera + player swap, hidden by the cover
        _ui.HideCelebration();
        _ui.HideNewBest();
        State = GameState.Playing;

        _ui.SetCover(0f);                                   // fade back in on the fresh level
    }

    /// <summary>
    /// Timeout. Rather than cutting straight to a retry, the whole maze lights up and we show how
    /// close the exit actually was. Seeing "you were 2 tiles away" is what turns a failure into an
    /// instant retry instead of a quit — a near miss fires the same reward circuitry as a win.
    /// </summary>
    private IEnumerator FailRoutine()
    {
        State = GameState.Celebrating;      // reuse the "cutscene" state: input + ticking are off

        _failStreak++;
        _streak = 0; _streakMul = 1f;       // streak breaks on a timer fail
        _ui.SetStreak(0, 1f);
        _ui.Flash(0.35f);
        Haptics.Heavy();
        _audio.PlayLose();

        // Light the place up so the player can see the route they missed.
        Vector2 playerPos = _player.transform.position;
        _sonar.RevealAll(playerPos, GameConfig.FailRevealTime);

        int tiles = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(playerPos, _exitWorld) / GameConfig.CellSize));
        string line = tiles <= 2 ? "SO CLOSE!\n" + tiles + (tiles == 1 ? " tile away" : " tiles away")
                                 : "OUT OF TIME\n" + tiles + " tiles away";
        _ui.ShowBanner(line, new Color(1f, 0.55f, 0.5f, 1f), GameConfig.FailRevealTime * 0.6f);

        yield return new WaitForSecondsRealtime(GameConfig.FailRevealTime);

        // The daily allows one attempt — running out of time ends it.
        if (_isDaily)
        {
            SaveData.CompleteDaily(_score);
            _ui.ShowDailyResult(false, _score, _dayStreak, false);
            State = GameState.Start;
            yield break;
        }

        _ui.SetCover(1f);
        yield return new WaitForSecondsRealtime(0.22f);

        BuildLevel(_level);                 // retry same level (rubber-banding via _failStreak)
        State = GameState.Playing;
        _ui.SetCover(0f);
    }

    private void FitCamera(MazeData maze)
    {
        _cam.orthographic = true;
        float aspect = _cam.aspect;
        float halfH = maze.worldHeight * 0.5f + GameConfig.CameraPadding;
        float halfW = maze.worldWidth * 0.5f + GameConfig.CameraPadding;
        _baseOrthoSize = Mathf.Max(halfH, halfW / aspect); // fits any aspect (portrait -> width bound)
        _cam.orthographicSize = _baseOrthoSize;
        _camBase = new Vector3(maze.worldCenter.x, maze.worldCenter.y, -10f);
        _cam.transform.position = _camBase;
    }

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime; // survive hitstop (timeScale 0)

        // Punch-zoom: snap in, ease back out.
        float size = _baseOrthoSize;
        if (_punchTimer > 0f)
        {
            _punchTimer -= dt;
            float t = 1f - Mathf.Clamp01(_punchTimer / GameConfig.PunchZoomTime);
            size = Mathf.Lerp(_baseOrthoSize * (1f - GameConfig.PunchZoom), _baseOrthoSize, Easing.OutCubic(t));
        }
        _cam.orthographicSize = size;

        // Shake.
        if (_shakeTimer > 0f)
        {
            _shakeTimer -= dt;
            float amt = GameConfig.ShakeMagnitude * Mathf.Clamp01(_shakeTimer / GameConfig.ShakeDuration);
            Vector2 off = Random.insideUnitCircle * amt;
            _cam.transform.position = _camBase + new Vector3(off.x, off.y, 0f);
        }
        else _cam.transform.position = _camBase;

        // Vignette covers the (possibly punched) viewport.
        if (_vignette != null)
        {
            float h = size * 2f;
            float w = h * _cam.aspect;
            _vignette.localScale = new Vector3(w * 1.08f, h * 1.08f, 1f);
        }
    }

    private void OnApplicationPause(bool paused)
    {
        AudioListener.pause = paused; // mute cleanly when backgrounded; timer naturally halts
    }
}
