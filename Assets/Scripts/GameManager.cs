using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameState { Start, Playing, Celebrating }

/// <summary>
/// The brain: level flow, ping budget, fail timer, streak + star scoring, rubber-banding,
/// the twists (moving exit, decoys), near-miss exit glow, and the whole level-clear
/// celebration (hitstop -> burst -> flash -> punch-zoom -> rolling score -> star pops ->
/// auto-advance). Everything else is handed to it by GameBootstrap.
/// </summary>
public class GameManager : MonoBehaviour
{
    public GameState State { get; private set; } = GameState.Start;

    private Camera _cam;
    private PlayerController _player;
    private SonarManager _sonar;
    private UIManager _ui;
    private ProceduralAudio _audio;
    private WallShaderController _wallCtrl;
    private FxManager _fx;
    private SpriteRenderer _exitSR;
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
    private bool _hintPending;
    private bool _hintMoved, _hintPinged;
    private float _hintTimer;

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
    }

    public void StartGame()
    {
        _level = 1; _score = 0; _streak = 0; _streakMul = 1f; _failStreak = 0;
        BuildLevel(_level);
        State = GameState.Start;
        _ui.ShowStart(SaveData.BestScore, SaveData.BestStreak);
    }

    private void BuildLevel(int level)
    {
        _profile = GameConfig.GetDifficulty(level, _failStreak);
        _maze = MazeGenerator.Generate(_profile.mazeSize, GameConfig.CellSize, Random.Range(1, int.MaxValue));

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

        _movingExit = _profile.movingExit;
        _exitMoveTimer = _profile.exitMoveInterval;

        PlaceDecoys(_profile.decoyCount);

        _pingsStart = _profile.pings;
        _pings = _profile.pings;
        _ui.BuildPingDots(_pingsStart);
        _ui.SetPingsRemaining(_pings);
        _ui.SetLevel(level);
        _ui.SetScoreImmediate(_score);
        _ui.SetStreak(_streak, _streakMul);

        _levelTimer = _profile.timeLimit;
        _pingReadyTime = 0f;
        _lastTickSecond = int.MaxValue;
        FitCamera(_maze);

        _hintPending = level == 1 && !SaveData.HintSeen;
        _hintMoved = _hintPinged = false;
        _hintTimer = 0f;
        if (_hintPending) { _ui.ShowHint(); RefreshHint(); } else _ui.HideHint();
    }

    private void UpdateHint()
    {
        if (!_hintPending) return;
        _hintTimer += Time.deltaTime;
        if (!_hintMoved && _player.Moving) { _hintMoved = true; RefreshHint(); }
        // Dismiss once they've done both, or after a timeout so it never nags forever.
        if ((_hintMoved && _hintPinged) || _hintTimer > GameConfig.HintMaxSeconds)
        {
            _hintPending = false;
            _ui.HideHint();
            SaveData.MarkHintSeen();
        }
    }

    private void RefreshHint()
    {
        if (!_hintPending) return;
        if (_hintPinged && !_hintMoved) _ui.SetHintText("nice!   now DRAG to reach the exit");
        else if (_hintMoved && !_hintPinged) _ui.SetHintText("TAP anywhere to ping & reveal walls");
        else _ui.SetHintText("DRAG to move   •   TAP to ping");
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

    public void RequestPing()
    {
        if (State != GameState.Playing) return;
        if (Time.time < _pingReadyTime) return;  // cooldown: can't spam — wait for the reveal to finish
        if (_pings <= 0) return;                  // out of pings, but you can still move blind

        if (_hintPending && !_hintPinged) { _hintPinged = true; RefreshHint(); }
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
                if (EchoInput.PointerDown || EchoInput.PingKeyDown)
                {
                    State = GameState.Playing;
                    _ui.HideStart();
                }
                return;

            case GameState.Celebrating:
                return; // fully automatic

            case GameState.Playing:
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
                if (secs == GameConfig.TimerWarnAt) _audio.PlayTimeWarning();
                else if (secs >= 1 && secs <= GameConfig.TimerTickFrom) _audio.PlayCountdownTick(secs);
                _lastTickSecond = secs;
            }

            if (_levelTimer <= 0f) { FailLevel(); return; }
        }
        else _ui.SetTimer(-1);

        UpdateHint();
        UpdateMovingExit();
        UpdateDecoys();
        PulseExit();

        if (Vector2.Distance(_player.transform.position, _exitWorld) < GameConfig.CellSize * 0.4f)
            StartCoroutine(WinRoutine());
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
        Vector2 pp = _player.transform.position;
        float hitRadiusSqr = (GameConfig.CellSize * 0.4f) * (GameConfig.CellSize * 0.4f);

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

            // Unchanged: only bites while the ball is actually visible.
            if (!blackedOut && vis > GameConfig.DecoyVisibleHit &&
                (pp - _decoyPos[i]).sqrMagnitude < hitRadiusSqr)
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
        Haptics.Medium();
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

        int gained = Mathf.RoundToInt((GameConfig.ScoreBaseClear + _pings * GameConfig.ScorePerPing + timeBonus) * _streakMul);
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

        // ---- Hitstop ----
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(GameConfig.HitstopTime);
        Time.timeScale = 1f;

        // ---- Celebration panel ----
        _ui.ShowCelebration(_level);
        _ui.SetStreak(_streak, _streakMul);
        _score = newScore;
        _ui.RollScoreTo(newScore);
        _ui.SetCelebrationScoreLine(_streakMul > 1f ? "+" + gained + "   x" + _streakMul.ToString("0.0") : "+" + gained);

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

    private void FailLevel()
    {
        _failStreak++;
        _streak = 0; _streakMul = 1f;       // streak breaks on a timer fail
        _ui.SetStreak(0, 1f);
        _ui.Flash(0.4f);
        Haptics.Heavy();
        _audio.PlayLose();
        BuildLevel(_level);                  // rebuild same level (rubber-banding via _failStreak)
        State = GameState.Playing;
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
