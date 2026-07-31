using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds and animates the whole HUD in code (legacy uGUI Text so nothing needs
/// importing). Safe-area aware. Owns all the "readout" juice: rolling score, streak
/// flame, star pops, NEW BEST callout, ping flash/darken. GameManager tells it WHAT
/// happened; UIManager decides how it looks.
/// </summary>
public class UIManager : MonoBehaviour
{
    private TMP_FontAsset _tmpFont;      // HUD / body
    private TMP_FontAsset _displayFont;  // titles and headlines

    /// <summary>
    /// Build an SDF font asset from a TTF in Resources. Generated at runtime so the project keeps
    /// no baked font assets; TMP renders it from a signed-distance field either way, so it stays
    /// sharp at any size. Returns null if the font is missing, letting callers fall back.
    /// </summary>
    private static TMP_FontAsset LoadFont(string resourcePath)
    {
        var ttf = Resources.Load<Font>(resourcePath);
        if (ttf == null)
        {
            Debug.LogWarning("[EchoMaze] Font not found: Resources/" + resourcePath + " — falling back.");
            return TMP_Settings.defaultFontAsset;
        }
        return TMP_FontAsset.CreateFontAsset(ttf);
    }

    /// <summary>Promote a label to the display typeface (titles, headlines, callouts).</summary>
    private TMP_Text Display(TMP_Text t)
    {
        if (t != null && _displayFont != null) t.font = _displayFont;
        return t;
    }
    private Canvas _canvas;
    private RectTransform _safe;
    private CanvasGroup _hudGroup;

    private TMP_Text _levelText, _scoreText, _timerText, _streakText, _sectorText;
    private TMP_Text _pingCountText;   // numeric reveal counter ("10  o")
    private Image _pingIcon;       // single circle icon next to the count
    private Sprite _dotSprite;

    private Image _streakGlow;
    private Image _timerRing;      // bloom behind the clock; reddens as time runs out
    private TMP_Text _timerCaption;
    // Menu / settings / daily.
    public System.Action OnPlay, OnDaily, OnDailyResultClosed, OnProgressReset;

    /// <summary>Unscaled time of the most recent UI button press.</summary>
    public float LastUiPressTime { get; private set; } = -10f;
    /// <summary>True just after any UI button was pressed — gameplay should ignore that tap.</summary>
    public bool UiJustPressed => Time.unscaledTime - LastUiPressTime < 0.3f;
    private Sprite _roundRect;                    // panel cards only
    private Sprite _brackets;                     // the game's button frame
    private TMP_Text _playLabel;
    private RectTransform _playBtnRT;
    private Button _dailyBtn;
    private TMP_Text _dailyLabel;
    private Image _dailyFlame;
    private Image _dailyCheck;
    private GameObject _settingsPanel, _dailyResultPanel, _confirmRow;
    private TMP_Text _soundLabel, _hapticsLabel, _dailyResultText;
    private GameObject _gearInGame;

    private GameObject _startOverlay, _celebOverlay, _hint;
    private TMP_Text _hintText;
    private TMP_Text _startSub, _celebTitle, _celebScore;
    private Image[] _stars = new Image[3];
    private TMP_Text _newBest;
    private TMP_Text _rewindText;
    private float _rewindT = -1f;
    private TMP_Text _bannerText;          // sector intro / orb / near-miss callouts
    private float _bannerT = -1f, _bannerHold;
    private Color _bannerColor = Color.white;
    private float _dailyStreakGlow;

    private Image _flash, _dark, _cover;
    private Image _rewindOverlay, _scanBar;
    private float _rewindFxT = -1f;

    // Animation state (all mutated in Update, no per-frame allocation).
    private float _flashA, _darkA;
    private Color _flashColor = Color.white;
    private float _coverA, _coverTarget;
    private float _scoreDisplay; private int _scoreTarget, _scoreShown;
    private readonly float[] _starT = new float[3];
    private readonly bool[] _starShown = new bool[3];
    private int _streakCount; private float _streakMul; private bool _streakOn;
    private float _newBestT = -1f;
    private int _lastTimer = int.MinValue;
    private bool _timerUrgent;
    private float _pingFlashT = -1f;

    private Image _celebGlow;

    private static readonly Color PingLostCol = new Color(1f, 0.35f, 0.3f, 1f);

    private static readonly Color DotFull = new Color(0.6f, 0.9f, 1f, 1f);
    private static readonly Color DotUsed = new Color(0.6f, 0.9f, 1f, 0.16f);
    private static readonly Color TextCol = new Color(0.85f, 0.92f, 1f, 0.9f);

    public void BuildUI()
    {
        // Two typefaces, each doing the job it's good at:
        //  * Chakra Petch — HUD/body. Techy and angular but with clean, evenly-spaced numerals,
        //    which matters when the score and countdown change every frame.
        //  * Orbitron — display only. Wide geometric caps give the title and headlines their
        //    sci-fi identity; too wide for readouts, hence the split.
        _tmpFont = LoadFont("Fonts/ChakraPetch");
        _displayFont = LoadFont("Fonts/Orbitron") ?? _tmpFont;
        _dotSprite = VisualUtils.Disc();
        _roundRect = VisualUtils.RoundedRect();
        _brackets = VisualUtils.CornerBrackets();

        var canvasGO = new GameObject("HUD Canvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f); // portrait reference
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Safe-area container for the readouts.
        var safeGO = new GameObject("SafeArea");
        safeGO.transform.SetParent(_canvas.transform, false);
        _safe = safeGO.AddComponent<RectTransform>();
        _safe.anchorMin = Vector2.zero; _safe.anchorMax = Vector2.one;
        _safe.offsetMin = Vector2.zero; _safe.offsetMax = Vector2.zero;
        safeGO.AddComponent<SafeArea>();
        // Everything in the safe area is gameplay HUD, so one group toggles it all off for the menu.
        _hudGroup = safeGO.AddComponent<CanvasGroup>();

        // ---- Top-left: score, with the streak multiplier tucked under it ----
        // Score is a running tally you glance at; it belongs in a corner, not centre stage.
        _scoreText = Text_("Score", _safe, new Vector2(0, 1), new Vector2(34, -18), new Vector2(520, 70), 46, TextAnchor.UpperLeft, "0");
        _scoreText.color = new Color(0.9f, 0.97f, 1f, 0.95f);
        Neon(_scoreText, Accent, 0.45f);

        var glowGO = new GameObject("StreakGlow");
        glowGO.transform.SetParent(_safe, false);
        _streakGlow = glowGO.AddComponent<Image>();
        _streakGlow.sprite = VisualUtils.RadialGlow();
        _streakGlow.raycastTarget = false;
        _streakGlow.color = new Color(GameConfig.StreakColor.r, GameConfig.StreakColor.g, GameConfig.StreakColor.b, 0f);
        var grt = _streakGlow.rectTransform;
        grt.anchorMin = grt.anchorMax = new Vector2(0, 1); grt.pivot = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = new Vector2(78, -96); grt.sizeDelta = new Vector2(180, 180);
        _streakText = Text_("Streak", _safe, new Vector2(0, 1), new Vector2(34, -80), new Vector2(400, 46), 32, TextAnchor.UpperLeft, "");
        _streakText.color = new Color(1f, 0.8f, 0.4f, 0f);
        Neon(_streakText, GameConfig.StreakColor, 0f); // alpha rides the streak fade

        // ---- Top-centre: level, then the TIMER directly beneath it ----
        // The timer used to sit in a corner and playtesters simply never noticed there was a time
        // limit. Centre-stage under the level makes the pressure impossible to miss.
        var lvlGlowGO = new GameObject("LevelGlow");
        lvlGlowGO.transform.SetParent(_safe, false);
        var lvlGlow = lvlGlowGO.AddComponent<Image>();
        lvlGlow.sprite = VisualUtils.RadialGlow();
        lvlGlow.raycastTarget = false;
        lvlGlow.color = new Color(Accent.r, Accent.g, Accent.b, 0.10f);
        var lgrt = lvlGlow.rectTransform;
        lgrt.anchorMin = lgrt.anchorMax = new Vector2(0.5f, 1f); lgrt.pivot = new Vector2(0.5f, 1f);
        lgrt.anchoredPosition = new Vector2(0, 40); lgrt.sizeDelta = new Vector2(760, 320);

        _levelText = Display(Text_("Level", _safe, new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(880, 76), 50, TextAnchor.UpperCenter, Spaced("LEVEL 1")));
        _levelText.color = new Color(0.88f, 0.97f, 1f, 1f);
        Neon(_levelText, Accent, 0.7f);

        // Sector caption between the level and the clock.
        _sectorText = Text_("Sector", _safe, new Vector2(0.5f, 1), new Vector2(0, -74), new Vector2(700, 36), 24, TextAnchor.UpperCenter, "");
        Neon(_sectorText, Accent, 0.45f);

        // The clock itself — the biggest thing in the HUD.
        _timerRing = new GameObject("TimerGlow").AddComponent<Image>();
        _timerRing.transform.SetParent(_safe, false);
        _timerRing.sprite = VisualUtils.RadialGlow();
        _timerRing.raycastTarget = false;
        _timerRing.color = new Color(Accent.r, Accent.g, Accent.b, 0f);
        var trt = _timerRing.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f); trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0, -168); trt.sizeDelta = new Vector2(420, 300);

        _timerText = Display(Text_("Timer", _safe, new Vector2(0.5f, 1), new Vector2(0, -104), new Vector2(520, 130), 92, TextAnchor.UpperCenter, ""));
        Neon(_timerText, Accent, 0.55f);

        // A bare number under the level could read as anything; this names it as a countdown.
        _timerCaption = Text_("TimerCaption", _safe, new Vector2(0.5f, 1), new Vector2(0, -206), new Vector2(400, 34), 20, TextAnchor.UpperCenter, Spaced("SECONDS"));
        _timerCaption.color = new Color(0.6f, 0.78f, 0.92f, 0.55f);

        // Reveal counter (top-right): a number followed by a single circle icon, e.g. "10  o".
        _pingCountText = Text_("PingCount", _safe, new Vector2(1, 1), new Vector2(-82, -24), new Vector2(240, 74), 52, TextAnchor.UpperRight, "0");
        _pingCountText.color = DotFull;
        Neon(_pingCountText, Accent, 0.5f);
        var iconGO = new GameObject("PingIcon");
        iconGO.transform.SetParent(_safe, false);
        _pingIcon = iconGO.AddComponent<Image>();
        _pingIcon.sprite = _dotSprite; _pingIcon.color = DotFull; _pingIcon.raycastTarget = false;
        var irt = _pingIcon.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(1, 1); irt.pivot = new Vector2(1, 1);
        irt.anchoredPosition = new Vector2(-34, -36); irt.sizeDelta = new Vector2(40, 40);

        // Full-screen effect layers (outside safe area on purpose).
        _dark  = FullScreen("Dark",  new Color(0, 0, 0, 0));
        _flash = FullScreen("Flash", new Color(1, 1, 1, 0));

        // Rewind screen effect: a soft cyan tint + a scan bar that sweeps down.
        _rewindOverlay = FullScreen("RewindTint", new Color(0.3f, 0.7f, 1f, 0f));
        var barGO = new GameObject("ScanBar");
        barGO.transform.SetParent(_canvas.transform, false);
        _scanBar = barGO.AddComponent<Image>();
        _scanBar.color = new Color(0.6f, 0.9f, 1f, 0f);
        _scanBar.raycastTarget = false;
        var brt = _scanBar.rectTransform;
        brt.anchorMin = new Vector2(0f, 0.5f); brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(0f, 16f); // full width, 16px tall

        _gearInGame = BuildInGameGear();
        _startOverlay = BuildStart();
        _celebOverlay = BuildCeleb();
        _hint = BuildHint();
        _dailyResultPanel = BuildDailyResult();
        _settingsPanel = BuildSettings();   // built late so it draws above the menu

        _newBest = Display(Text_("NewBest", _canvas.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 470), new Vector2(980, 90), 56, TextAnchor.MiddleCenter, Spaced("NEW BEST!")));
        _newBest.color = new Color(1f, 0.85f, 0.3f, 0f);
        Neon(_newBest, Gold, 0.8f);

        _rewindText = Display(Text_("Rewind", _canvas.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(1000, 120), 74, TextAnchor.MiddleCenter, "REWIND  -" + Mathf.RoundToInt(GameConfig.RewindSeconds) + "s"));
        _rewindText.color = new Color(0.5f, 0.85f, 1f, 0f);
        Neon(_rewindText, Accent, 0.7f);

        // Lower-centre: clear of the top HUD, and clear of the celebration panel's title/stars/score
        // (which occupy roughly +260 down to -120).
        _bannerText = Display(Text_("Banner", _canvas.transform as RectTransform, new Vector2(0.5f, 0.5f),
                            new Vector2(0, -270), new Vector2(1040, 190), 58, TextAnchor.MiddleCenter, ""));
        _bannerText.color = new Color(1f, 1f, 1f, 0f);
        Neon(_bannerText, Accent, 0.7f);

        // Top-most: opaque cover for masking the between-level swap (created last = drawn last).
        _cover = FullScreen("Cover", new Color(0, 0, 0, 0));

        SetScoreImmediate(0);
    }

    // ---------- public API ----------
    public void SetLevel(int level) => _levelText.text = Spaced("LEVEL " + level);

    /// <summary>Small sector caption under the level number, e.g. "THE DEEP  ·  3/5".</summary>
    public void SetSector(string sectorName, int levelInSector, int levelsPerSector, Color color)
    {
        if (_sectorText == null) return;
        _sectorText.text = sectorName + "   ·   " + levelInSector + "/" + levelsPerSector;
        _sectorText.color = Color.Lerp(color, Color.white, 0.35f);
        NeonColor(_sectorText, color, 0.6f);   // caption glows in its sector's colour
    }

    public void SetScoreImmediate(int score)
    {
        _scoreTarget = score; _scoreDisplay = score; _scoreShown = score;
        _scoreText.text = score.ToString();
    }

    public void RollScoreTo(int target) => _scoreTarget = target;

    /// <summary>Reset the reveal counter for a new level (kept the old name so callers don't change).</summary>
    public void BuildPingDots(int total)
    {
        _pingFlashT = -1f;
        _pingIcon.rectTransform.localScale = Vector3.one;
        SetPingsRemaining(total);
    }

    public void SetPingsRemaining(int remaining)
    {
        _pingCountText.text = remaining.ToString();
        Color col = remaining > 0 ? DotFull : new Color(1f, 0.45f, 0.45f, 1f);
        _pingCountText.color = col;
        _pingIcon.color = new Color(col.r, col.g, col.b, remaining > 0 ? 1f : 0.45f);
    }

    /// <summary>Lose a reveal: update the count and flash the counter red.</summary>
    public void LosePing(int remaining)
    {
        SetPingsRemaining(remaining);
        _pingFlashT = 0f;
        _pingCountText.color = PingLostCol;
        _pingIcon.color = PingLostCol;
        _pingIcon.rectTransform.localScale = Vector3.one * 1.5f;
    }

    public void SetTimer(int seconds)
    {
        if (seconds == _lastTimer) return;   // only touch the string when it changes
        _lastTimer = seconds;
        if (seconds < 0)
        {
            _timerText.text = "";
            if (_timerCaption != null) _timerCaption.text = "";
            if (_timerRing != null) _timerRing.color = new Color(Accent.r, Accent.g, Accent.b, 0f);
            _timerUrgent = false;
            return;
        }
        _timerText.text = seconds.ToString();
        if (_timerCaption != null && _timerCaption.text.Length == 0) _timerCaption.text = Spaced("SECONDS");

        // The clock escalates in three stages so the pressure is legible at a glance: calm cyan,
        // amber warning, then hostile red with a pulsing bloom behind it.
        _timerUrgent = seconds <= GameConfig.TimerTickFrom;
        bool warn = seconds <= GameConfig.TimerWarnAt;

        Color c = _timerUrgent ? Danger : (warn ? Gold : Accent);
        _timerText.color = Color.Lerp(c, Color.white, 0.35f);   // saturated, not washed out
        NeonColor(_timerText, c, _timerUrgent ? 0.95f : (warn ? 0.75f : 0.5f));
        if (_timerCaption != null)
            _timerCaption.color = new Color(c.r, c.g, c.b, _timerUrgent ? 0.85f : 0.5f);
        if (_timerRing != null)
            _timerRing.color = new Color(c.r, c.g, c.b, _timerUrgent ? 0.20f : (warn ? 0.11f : 0.05f));
    }

    public void SetStreak(int streakCount, float multiplier)
    {
        _streakCount = streakCount; _streakMul = multiplier;
        _streakOn = streakCount > 0;
        _streakText.text = _streakOn ? "x" + multiplier.ToString("0.0") : "";
    }

    public void ShowStart(int bestScore, int bestStreak, int dayStreak, bool dailyDone)
    {
        _startSub.text = bestScore > 0
            ? "Best  " + bestScore + "        Streak  " + bestStreak
            : "Drag to move   •   Tap to ping";

        // Daily button reflects today's state at a glance, and locks once it's been played —
        // one attempt per day is what makes the daily meaningful.
        if (dailyDone)
        {
            _dailyLabel.text = "DAILY DONE";
            _dailyLabel.color = new Color(0.55f, 0.85f, 0.65f, 0.8f);
            _dailyBtn.interactable = false;

            // Shift the text left by half the tick's footprint and hang the tick off its right
            // edge, so text + tick read as one centred group.
            const float icon = 34f, gap = 16f;
            _dailyLabel.ForceMeshUpdate();
            float w = _dailyLabel.preferredWidth;
            _dailyLabel.rectTransform.anchoredPosition = new Vector2(-(icon + gap) * 0.5f, 0f);
            _dailyCheck.rectTransform.anchoredPosition = new Vector2(w * 0.5f + gap * 0.5f, 0f);
            _dailyCheck.gameObject.SetActive(true);
        }
        else
        {
            _dailyLabel.text = dayStreak > 1 ? "DAILY MAZE   " + dayStreak + "×" : "DAILY MAZE";
            _dailyLabel.color = new Color(1f, 0.85f, 0.4f, 1f);
            _dailyLabel.rectTransform.anchoredPosition = Vector2.zero;
            _dailyCheck.gameObject.SetActive(false);
            _dailyBtn.interactable = true;
        }
        _dailyStreakGlow = dayStreak > 1 && !dailyDone ? Mathf.Clamp01(0.25f + dayStreak * 0.08f) : 0f;

        _startOverlay.SetActive(true);
        _celebOverlay.SetActive(false);
        ShowInGameGear(false);
        ShowHud(false);          // no gameplay readouts behind the menu
    }
    public void HideStart() { _startOverlay.SetActive(false); ShowInGameGear(true); ShowHud(true); }

    /// <summary>Toggle the entire gameplay HUD (readouts + in-game gear).</summary>
    public void ShowHud(bool show)
    {
        if (_hudGroup == null) return;
        _hudGroup.alpha = show ? 1f : 0f;
        _hudGroup.blocksRaycasts = show;
        _hudGroup.interactable = show;
    }

    public void ShowHint() => _hint.SetActive(true);
    public void HideHint() => _hint.SetActive(false);

    /// <summary>Retitle the celebration panel (e.g. "SECTOR CLEAR" on a finale).</summary>
    public void SetCelebrationTitle(string title, Color color)
    {
        _celebTitle.text = Spaced(title);
        _celebTitle.color = Color.Lerp(color, Color.white, 0.45f);
        NeonColor(_celebTitle, color, 0.9f);
        if (_celebGlow != null) _celebGlow.color = new Color(color.r, color.g, color.b, 0.18f);
    }

    public void ShowCelebration(int level)
    {
        var win = new Color(0.4f, 1f, 0.75f, 1f);
        _celebTitle.text = Spaced("LEVEL " + level + " CLEAR");
        _celebTitle.color = new Color(0.85f, 1f, 0.93f, 1f);
        NeonColor(_celebTitle, win, 0.85f);
        if (_celebGlow != null) _celebGlow.color = new Color(win.r, win.g, win.b, 0.16f);
        _celebScore.text = "";
        for (int i = 0; i < 3; i++) { _starShown[i] = false; _starT[i] = 0f; _stars[i].transform.localScale = Vector3.zero; }
        _celebOverlay.SetActive(true);
    }
    public void SetCelebrationScoreLine(string line) => _celebScore.text = line;
    public void HideCelebration() => _celebOverlay.SetActive(false);
    public void PopStar(int index) { if (index >= 0 && index < 3) { _starShown[index] = true; _starT[index] = 0f; } }

    /// <summary>
    /// Big centered callout used for sector intros, CLUTCH clears and the near-miss line on a fail.
    /// Pops in, holds, then fades — all on unscaled time so it survives hitstop.
    /// </summary>
    public void ShowBanner(string message, Color color, float holdSeconds = 1.1f)
    {
        _bannerText.text = message;
        _bannerColor = Color.Lerp(color, Color.white, 0.3f);
        NeonColor(_bannerText, color, 0.8f);
        _bannerHold = holdSeconds;
        _bannerT = 0f;
    }

    public void ShowNewBest() => _newBestT = 0f;
    public void HideNewBest() { _newBestT = -1f; _newBest.color = new Color(1f, 0.85f, 0.3f, 0f); }
    public void ShowRewind() => _rewindT = 0f;
    public void PlayRewindEffect() => _rewindFxT = 0f;
    public void Flash(float strength) { _flashColor = Color.white; _flashA = Mathf.Max(_flashA, strength); }
    public void FlashColor(Color c, float strength) { _flashColor = c; _flashA = Mathf.Max(_flashA, strength); }
    public void DarkFlash() => _darkA = Mathf.Max(_darkA, GameConfig.PingDarkenAmount);

    /// <summary>0 = clear, 1 = full black. Used to mask the between-level swap.</summary>
    public void SetCover(float target) => _coverTarget = target;

    // ---------- animation ----------
    private void Update()
    {
        float dt = Time.unscaledDeltaTime; // keep the UI alive during hitstop (timeScale=0)

        // Rolling score.
        if (_scoreShown != _scoreTarget)
        {
            _scoreDisplay = Mathf.Lerp(_scoreDisplay, _scoreTarget, 1f - Mathf.Exp(-10f * dt));
            if (Mathf.Abs(_scoreDisplay - _scoreTarget) < 0.6f) _scoreDisplay = _scoreTarget;
            int r = Mathf.RoundToInt(_scoreDisplay);
            if (r != _scoreShown) { _scoreShown = r; _scoreText.text = r.ToString(); }
        }

        // Flash / darken decay.
        if (_flashA > 0f) { _flashA = Mathf.Max(0f, _flashA - dt * 2.2f); _flash.color = new Color(_flashColor.r, _flashColor.g, _flashColor.b, _flashA); }
        if (_darkA > 0f)  { _darkA  = Mathf.Max(0f, _darkA  - dt / GameConfig.PingDarkenTime); _dark.color = new Color(0, 0, 0, _darkA); }

        // Level-transition cover.
        if (_coverA != _coverTarget)
        {
            _coverA = Mathf.MoveTowards(_coverA, _coverTarget, dt / 0.2f);
            _cover.color = new Color(0, 0, 0, _coverA);
        }

        // Stars pop in with overshoot.
        for (int i = 0; i < 3; i++)
        {
            if (!_starShown[i]) continue;
            if (_starT[i] < 1f)
            {
                _starT[i] = Mathf.Min(1f, _starT[i] + dt / 0.3f);
                float s = Easing.OutBack(_starT[i]);
                _stars[i].transform.localScale = Vector3.one * s;
                _stars[i].color = StarLit;
            }
        }

        // Streak flame: brighten + pulse with the multiplier.
        float targetGlow = _streakOn ? Mathf.Clamp01(0.2f + _streakCount * 0.12f) : 0f;
        float pulse = _streakOn ? (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
        var gc = _streakGlow.color;
        float ga = Mathf.Lerp(gc.a, targetGlow * pulse, dt * 8f);
        _streakGlow.color = new Color(GameConfig.StreakColor.r, GameConfig.StreakColor.g, GameConfig.StreakColor.b, ga);
        float scale = _streakOn ? Mathf.Min(2.2f, 1f + _streakCount * 0.12f) : 1f;
        _streakGlow.rectTransform.localScale = Vector3.one * scale;
        var tc = _streakText.color;
        float sa = Mathf.Lerp(tc.a, _streakOn ? 1f : 0f, dt * 8f);
        _streakText.color = new Color(1f, 0.86f, 0.5f, sa);
        NeonColor(_streakText, GameConfig.StreakColor, sa * 0.85f);   // flame edge fades with it

        // NEW BEST pop then fade.
        if (_newBestT >= 0f)
        {
            _newBestT += dt;
            float t = _newBestT;
            float pop = t < 0.3f ? Easing.OutBack(t / 0.3f) : 1f;
            float alpha = t < 1.2f ? 1f : Mathf.Clamp01(1f - (t - 1.2f) / 0.6f);
            _newBest.transform.localScale = Vector3.one * pop;
            _newBest.color = new Color(1f, 0.85f, 0.3f, alpha);
            if (t > 1.8f) _newBestT = -1f;
        }

        // Lost-reveal counter flash: red pop settling back to normal.
        if (_pingFlashT >= 0f && _pingFlashT < 1f)
        {
            _pingFlashT = Mathf.Min(1f, _pingFlashT + dt / 0.45f);
            float e = Easing.OutCubic(_pingFlashT);
            _pingIcon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, e);
            Color col = Color.Lerp(PingLostCol, DotFull, e);
            _pingIcon.color = col; _pingCountText.color = col;
            if (_pingFlashT >= 1f) { _pingIcon.rectTransform.localScale = Vector3.one; _pingFlashT = -1f; }
        }

        // Final seconds: the clock throbs. Motion reads faster than colour in peripheral vision.
        if (_timerUrgent && _timerText != null)
        {
            float beat = 1f + 0.10f * Mathf.Sin(Time.unscaledTime * 9f);
            _timerText.rectTransform.localScale = new Vector3(beat, beat, 1f);
            if (_timerRing != null)
                _timerRing.rectTransform.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 9f));
        }
        else if (_timerText != null && _timerText.rectTransform.localScale.x != 1f)
        {
            _timerText.rectTransform.localScale = Vector3.one;
            if (_timerRing != null) _timerRing.rectTransform.localScale = Vector3.one;
        }

        // Main-menu attract animation: the PLAY button breathes so it's unmistakably the thing to
        // press, and the daily flame flickers when a streak is running.
        if (_startOverlay != null && _startOverlay.activeSelf)
        {
            float beat = Mathf.Sin(Time.unscaledTime * 3.4f);
            float s = 1f + 0.045f * beat;
            if (_playBtnRT != null) _playBtnRT.localScale = new Vector3(s, s, 1f);
            if (_playLabel != null)
                _playLabel.color = new Color(1f, 1f, 1f, 0.85f + 0.15f * (0.5f + 0.5f * beat));

            if (_dailyFlame != null)
            {
                float flicker = _dailyStreakGlow * (0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 7f));
                _dailyFlame.color = new Color(GameConfig.StreakColor.r, GameConfig.StreakColor.g,
                                              GameConfig.StreakColor.b, flicker);
            }
        }

        // Banner callout (sector intro / orb / near-miss): pop in, hold, fade.
        if (_bannerT >= 0f)
        {
            _bannerT += dt;
            float pop = _bannerT < 0.28f ? Easing.OutBack(_bannerT / 0.28f) : 1f;
            float fadeStart = 0.28f + _bannerHold;
            float alpha = _bannerT < fadeStart ? 1f : Mathf.Clamp01(1f - (_bannerT - fadeStart) / 0.5f);
            _bannerText.transform.localScale = Vector3.one * pop;
            _bannerText.color = new Color(_bannerColor.r, _bannerColor.g, _bannerColor.b, alpha);
            if (_bannerT > fadeStart + 0.5f) _bannerT = -1f;
        }

        // Rewind callout: pop in, hold, fade (unscaled so it shows during the time-freeze).
        if (_rewindT >= 0f)
        {
            _rewindT += dt;
            float t = _rewindT;
            float pop = t < 0.3f ? Easing.OutBack(t / 0.3f) : 1f;
            float alpha = t < 0.9f ? 1f : Mathf.Clamp01(1f - (t - 0.9f) / 0.5f);
            _rewindText.transform.localScale = Vector3.one * pop;
            _rewindText.color = new Color(0.5f, 0.85f, 1f, alpha);
            if (t > 1.4f) _rewindT = -1f;
        }

        // Rewind screen effect: gentle cyan tint + a scan bar sweeping down a few times.
        if (_rewindFxT >= 0f)
        {
            _rewindFxT += dt;
            float dur = GameConfig.RewindDuration;
            float t = Mathf.Clamp01(_rewindFxT / dur);
            float fade = 1f - t; // ease the whole effect out toward the end

            float tint = (0.08f + 0.03f * Mathf.Sin(Time.unscaledTime * 12f)) * fade; // soft, no strobe
            _rewindOverlay.color = new Color(0.3f, 0.7f, 1f, tint);

            float frac = Mathf.Repeat(t * 3f, 1f);                       // 3 downward sweeps
            _scanBar.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(960f, -960f, frac));
            _scanBar.color = new Color(0.6f, 0.9f, 1f, 0.45f * fade);

            if (_rewindFxT >= dur)
            {
                _rewindFxT = -1f;
                _rewindOverlay.color = new Color(0.3f, 0.7f, 1f, 0f);
                _scanBar.color = new Color(0.6f, 0.9f, 1f, 0f);
            }
        }

        // Tutorial hint pulse so it's actually noticed.
        if (_hint != null && _hint.activeSelf)
        {
            float a = 0.62f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
            _hintText.color = new Color(0.75f, 0.9f, 1f, a);
            float sc = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 3f);
            _hintText.rectTransform.localScale = new Vector3(sc, sc, 1f);
        }
    }

    // ---------- builders ----------
    /// <summary>
    /// Build a TextMeshPro label. TMP renders from a signed-distance field, so glyphs stay crisp
    /// at any size or DPI — unlike legacy uGUI Text, whose "glow" had to be faked with an Outline
    /// component that literally draws the text four extra times and smears it.
    /// </summary>
    private TMP_Text Text_(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, string init)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = _tmpFont;
        t.fontSize = fontSize;
        t.alignment = MapAlign(align);
        t.color = TextCol;
        t.text = init;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return t;
    }

    private static TextAlignmentOptions MapAlign(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
            case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
            default:                      return TextAlignmentOptions.Center;
        }
    }

    private Image FullScreen(string name, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_canvas.transform, false);
        var img = go.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    // ---------- theme ----------
    // Sonar-HUD look: near-black translucent fills with a bright glowing stroke, so panels read
    // like readouts on the same display the maze is drawn on, not like stock mobile-app buttons.
    private static readonly Color Accent   = new Color(0.45f, 0.85f, 1.00f, 1f); // cyan
    private static readonly Color Gold     = new Color(1.00f, 0.82f, 0.35f, 1f);
    private static readonly Color Danger   = new Color(1.00f, 0.42f, 0.42f, 1f);
    private static readonly Color PanelFill= new Color(0.03f, 0.07f, 0.12f, 0.55f);
    private static readonly Color StarLit  = new Color(0.70f, 0.95f, 1.00f, 1f); // sonar-marker rating

    /// <summary>
    /// Real shader-based glow on a TMP label: the SDF material renders the halo, so the glyph
    /// edge itself stays perfectly sharp. Safe to call repeatedly — fontMaterial is a per-label
    /// instance, so re-tinting just updates it.
    /// </summary>
    private static void Neon(TMP_Text t, Color glow, float strength = 0.55f)
    {
        if (t == null) return;
        var mat = t.fontMaterial;
        mat.EnableKeyword(ShaderUtilities.Keyword_Glow);
        mat.SetColor(ShaderUtilities.ID_GlowColor, new Color(glow.r, glow.g, glow.b, strength));
        mat.SetFloat(ShaderUtilities.ID_GlowPower, 0.4f);
        mat.SetFloat(ShaderUtilities.ID_GlowOuter, 0.3f);
        mat.SetFloat(ShaderUtilities.ID_GlowInner, 0.05f);
    }

    private static void NeonColor(TMP_Text t, Color glow, float strength = 0.55f)
    {
        Neon(t, glow, strength);
    }

    /// <summary>Letter-spaced caps for titles — reads as instrument labelling rather than body copy.</summary>
    private static string Spaced(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length * 2);
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i < s.Length - 1) sb.Append(' ');
        }
        return sb.ToString();
    }

    // ---------- button helper ----------

    /// <summary>
    /// The game's button style: a flat rectangular panel inside a floating corner-bracket frame.
    /// The fill keeps square corners so it matches the hard angles of the bracket arms.
    /// <paramref name="primary"/> makes it the loud call-to-action (brighter frame + tinted fill).
    /// </summary>
    private Button Button_(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                           string label, int fontSize, Color accent, bool primary, out TMP_Text labelText)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        // Fill — a plain rectangle with hard corners (no sprite means no rounding). Also the
        // raycast and tint target.
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(rt, false);
        var fill = fillGO.AddComponent<Image>();
        fill.raycastTarget = true;
        fill.color = primary ? new Color(accent.r * 0.22f, accent.g * 0.26f, accent.b * 0.38f, 0.5f)
                             : new Color(0.04f, 0.07f, 0.11f, 0.5f);
        var frt2 = fill.rectTransform;
        frt2.anchorMin = Vector2.zero; frt2.anchorMax = Vector2.one;
        frt2.offsetMin = Vector2.zero; frt2.offsetMax = Vector2.zero;

        // The reticle corners, sitting 8 units OUTSIDE the fill so the frame floats clear of the
        // panel rather than hugging it.
        var lineGO = new GameObject("Brackets");
        lineGO.transform.SetParent(rt, false);
        var line = lineGO.AddComponent<Image>();
        line.sprite = _brackets; line.type = Image.Type.Sliced;
        line.raycastTarget = false;
        line.color = new Color(accent.r, accent.g, accent.b, primary ? 1f : 0.75f);
        var lrt2 = line.rectTransform;
        lrt2.anchorMin = Vector2.zero; lrt2.anchorMax = Vector2.one;
        lrt2.offsetMin = new Vector2(-8, -8); lrt2.offsetMax = new Vector2(8, 8);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        // Every button stamps the time it was pressed. Gameplay polls raw pointer state and fires
        // its ping on pointer-UP — the same instant onClick runs — so this timestamp lets the
        // player reliably discard that press. It doesn't depend on UI raycast timing, which makes
        // it a dependable backstop to the IsOverUI() check.
        btn.onClick.AddListener(() => LastUiPressTime = Time.unscaledTime);
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(1.7f, 1.7f, 1.7f, 1f);
        colors.fadeDuration = 0.07f;
        btn.colors = colors;

        labelText = Text_(name + "Label", rt, new Vector2(0.5f, 0.5f), Vector2.zero,
                          new Vector2(size.x, size.y), fontSize, TextAnchor.MiddleCenter, label);
        // Label sits well above its own glow colour, otherwise same-hue text on a same-hue stroke
        // (red on red especially) turns to mush.
        labelText.color = primary ? new Color(0.95f, 0.99f, 1f, 1f) : Color.Lerp(accent, Color.white, 0.45f);
        Neon(labelText, accent, primary ? 0.7f : 0.45f);
        labelText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        return btn;
    }

    private GameObject BuildStart()
    {
        var go = new GameObject("StartOverlay");
        go.transform.SetParent(_canvas.transform, false);
        var panel = go.AddComponent<Image>();
        // Nearly opaque: lets the starfield hint through without the maze's exit/player glows
        // reading as stray UI blobs behind the buttons.
        panel.color = new Color(0.01f, 0.015f, 0.03f, 0.975f);
        panel.raycastTarget = true;   // blocks taps leaking into gameplay behind the menu
        var prt = panel.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        var root = go.transform as RectTransform;

        // Title: a soft bloom behind letter-spaced caps, echoing the sonar reveal itself.
        var titleGlowGO = new GameObject("TitleGlow");
        titleGlowGO.transform.SetParent(root, false);
        var titleGlow = titleGlowGO.AddComponent<Image>();
        titleGlow.sprite = VisualUtils.RadialGlow();
        titleGlow.raycastTarget = false;
        titleGlow.color = new Color(Accent.r, Accent.g, Accent.b, 0.16f);
        var tgrt = titleGlow.rectTransform;
        tgrt.anchorMin = tgrt.anchorMax = new Vector2(0.5f, 0.5f); tgrt.pivot = new Vector2(0.5f, 0.5f);
        tgrt.anchoredPosition = new Vector2(0, 430); tgrt.sizeDelta = new Vector2(1000, 420);

        var title = Display(Text_("Title", root, new Vector2(0.5f, 0.5f), new Vector2(0, 430), new Vector2(1020, 160), 84, TextAnchor.MiddleCenter, Spaced("ECHO MAZE")));
        title.color = new Color(0.85f, 0.97f, 1f, 1f);
        Neon(title, Accent, 0.85f);

        _startSub = Text_("Sub", root, new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1000, 260), 30, TextAnchor.MiddleCenter, "");
        _startSub.color = new Color(0.62f, 0.78f, 0.92f, 0.85f);

        // Primary action.
        var playBtn = Button_("PlayBtn", root, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(560, 150),
                              Spaced("PLAY"), 58, Accent, true, out _playLabel);
        _playBtnRT = playBtn.GetComponent<RectTransform>();
        playBtn.onClick.AddListener(() => { if (OnPlay != null) OnPlay(); });

        // Secondary: the daily ritual, with its own streak flame.
        _dailyBtn = Button_("DailyBtn", root, new Vector2(0.5f, 0.5f), new Vector2(0, -175), new Vector2(500, 116),
                            "DAILY MAZE", 38, Gold, false, out _dailyLabel);
        _dailyBtn.onClick.AddListener(() => { if (OnDaily != null) OnDaily(); });

        var flameGO = new GameObject("DailyFlame");
        flameGO.transform.SetParent(_dailyBtn.transform, false);
        _dailyFlame = flameGO.AddComponent<Image>();
        _dailyFlame.sprite = VisualUtils.RadialGlow();
        _dailyFlame.raycastTarget = false;
        _dailyFlame.color = new Color(GameConfig.StreakColor.r, GameConfig.StreakColor.g, GameConfig.StreakColor.b, 0f);
        var frt = _dailyFlame.rectTransform;
        frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f); frt.pivot = new Vector2(0.5f, 0.5f);
        frt.anchoredPosition = new Vector2(58, 0); frt.sizeDelta = new Vector2(120, 120);
        _dailyFlame.transform.SetAsFirstSibling();

        // Tick shown once today's daily is played. Positioned in ShowStart, where the label width
        // is known, so the "DAILY DONE ✓" group stays optically centred.
        var checkGO = new GameObject("DailyCheck");
        checkGO.transform.SetParent(_dailyBtn.transform, false);
        _dailyCheck = checkGO.AddComponent<Image>();
        _dailyCheck.sprite = VisualUtils.Check();
        _dailyCheck.raycastTarget = false;
        _dailyCheck.color = new Color(0.55f, 0.85f, 0.65f, 0.85f);
        var crt2 = _dailyCheck.rectTransform;
        crt2.anchorMin = crt2.anchorMax = new Vector2(0.5f, 0.5f); crt2.pivot = new Vector2(0.5f, 0.5f);
        crt2.sizeDelta = new Vector2(34, 34);
        checkGO.SetActive(false);

        // Settings gear.
        TMP_Text gearLabel;
        var gearBtn = Button_("GearBtn", root, new Vector2(0.5f, 0.5f), new Vector2(0, -350), new Vector2(110, 110),
                              "", 1, Accent, false, out gearLabel);
        gearBtn.onClick.AddListener(OpenSettings);
        var gearIcon = new GameObject("GearIcon");
        gearIcon.transform.SetParent(gearBtn.transform, false);
        var gi = gearIcon.AddComponent<Image>();
        gi.sprite = VisualUtils.Gear(); gi.raycastTarget = false;
        gi.color = new Color(0.8f, 0.9f, 1f, 0.9f);
        var girt = gi.rectTransform;
        girt.anchorMin = girt.anchorMax = new Vector2(0.5f, 0.5f); girt.pivot = new Vector2(0.5f, 0.5f);
        girt.anchoredPosition = Vector2.zero; girt.sizeDelta = new Vector2(66, 66);

        return go;
    }

    private GameObject BuildCeleb()
    {
        var go = new GameObject("CelebOverlay");
        go.transform.SetParent(_canvas.transform, false);
        var panel = go.AddComponent<Image>();
        // Dark enough that maze glows behind can't sit on top of the headline text, but still
        // translucent so the burst/particles read through.
        panel.color = new Color(0.01f, 0.02f, 0.04f, 0.78f); panel.raycastTarget = false;
        var prt = panel.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;

        // Bloom behind the headline so a clear feels like a burst of light, not a text change.
        var cGlowGO = new GameObject("CTitleGlow");
        cGlowGO.transform.SetParent(go.transform, false);
        _celebGlow = cGlowGO.AddComponent<Image>();
        _celebGlow.sprite = VisualUtils.RadialGlow();
        _celebGlow.raycastTarget = false;
        _celebGlow.color = new Color(0.5f, 1f, 0.8f, 0.16f);
        var cgrt = _celebGlow.rectTransform;
        cgrt.anchorMin = cgrt.anchorMax = new Vector2(0.5f, 0.5f); cgrt.pivot = new Vector2(0.5f, 0.5f);
        cgrt.anchoredPosition = new Vector2(0, 230); cgrt.sizeDelta = new Vector2(1100, 520);

        _celebTitle = Display(Text_("CTitle", go.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 260), new Vector2(1040, 160), 68, TextAnchor.MiddleCenter, Spaced("LEVEL CLEAR")));
        _celebTitle.color = new Color(0.85f, 1f, 0.93f, 1f);
        Neon(_celebTitle, new Color(0.4f, 1f, 0.75f, 1f), 0.85f);

        // Warm bloom under the star row.
        var starGlowGO = new GameObject("StarGlow");
        starGlowGO.transform.SetParent(go.transform, false);
        var starGlow = starGlowGO.AddComponent<Image>();
        starGlow.sprite = VisualUtils.RadialGlow();
        starGlow.raycastTarget = false;
        starGlow.color = new Color(Accent.r, Accent.g, Accent.b, 0.16f);
        var sgrt = starGlow.rectTransform;
        sgrt.anchorMin = sgrt.anchorMax = new Vector2(0.5f, 0.5f); sgrt.pivot = new Vector2(0.5f, 0.5f);
        sgrt.anchoredPosition = new Vector2(0, 90); sgrt.sizeDelta = new Vector2(720, 380);

        // Star row — sonar ping-markers, tinted cyan to match the HUD rather than gold.
        var starSprite = VisualUtils.PingStar();
        for (int i = 0; i < 3; i++)
        {
            var s = new GameObject("Star" + i);
            s.transform.SetParent(go.transform, false);
            var img = s.AddComponent<Image>();
            img.sprite = starSprite; img.raycastTarget = false; img.color = StarLit;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((i - 1) * 150f, 90f); rt.sizeDelta = new Vector2(120, 120);
            rt.localScale = Vector3.zero;
            _stars[i] = img;
        }

        _celebScore = Text_("CScore", go.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(1000, 140), 42, TextAnchor.MiddleCenter, "");
        _celebScore.color = new Color(0.9f, 0.97f, 1f, 0.95f);
        Neon(_celebScore, Accent, 0.5f);
        go.SetActive(false);
        return go;
    }

    // ---------- settings ----------

    public void OpenSettings()
    {
        RefreshSettingLabels();
        _confirmRow.SetActive(false);
        _settingsPanel.SetActive(true);
    }

    public void CloseSettings() => _settingsPanel.SetActive(false);
    public bool SettingsOpen => _settingsPanel != null && _settingsPanel.activeSelf;

    private void RefreshSettingLabels()
    {
        _soundLabel.text = "SOUND        " + (SaveData.SoundOn ? "ON" : "OFF");
        _soundLabel.color = SaveData.SoundOn ? new Color(0.7f, 1f, 0.8f, 1f) : new Color(1f, 0.6f, 0.6f, 1f);
        _hapticsLabel.text = "VIBRATION   " + (SaveData.HapticsOn ? "ON" : "OFF");
        _hapticsLabel.color = SaveData.HapticsOn ? new Color(0.7f, 1f, 0.8f, 1f) : new Color(1f, 0.6f, 0.6f, 1f);
    }

    private GameObject BuildSettings()
    {
        var go = new GameObject("SettingsPanel");
        go.transform.SetParent(_canvas.transform, false);

        // Scrim: dims whatever is behind (menu or gameplay) enough that it stops competing for
        // attention, without hiding it — the settings read as a card floating above the app.
        var dim = go.AddComponent<Image>();
        dim.color = new Color(0.01f, 0.015f, 0.03f, 0.82f);
        dim.raycastTarget = true;   // also swallows taps that miss the card
        var drt = dim.rectTransform; drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;

        // The card itself.
        var cardGO = new GameObject("Card");
        cardGO.transform.SetParent(go.transform, false);
        var card = cardGO.AddComponent<Image>();
        card.sprite = _roundRect;
        card.type = Image.Type.Sliced;
        card.color = new Color(0.07f, 0.10f, 0.16f, 1f);
        card.raycastTarget = true;
        var root = card.rectTransform;
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(880, 1180);

        var title = Display(Text_("SetTitle", root, new Vector2(0.5f, 0.5f), new Vector2(0, 460), new Vector2(820, 110), 68, TextAnchor.MiddleCenter, "SETTINGS"));
        title.color = new Color(0.6f, 0.9f, 1f, 1f);

        // Rows sit 155 apart, not 140: the bracket halo bleeds 6 units past each button, and at the
        // tighter spacing the two toggles' glows visibly touched.
        var soundBtn = Button_("SoundBtn", root, new Vector2(0.5f, 0.5f), new Vector2(0, 292), new Vector2(700, 120),
                               "", 38, Accent, false, out _soundLabel);
        soundBtn.onClick.AddListener(() =>
        {
            SaveData.SoundOn = !SaveData.SoundOn;
            SaveData.ApplySettings();
            RefreshSettingLabels();
        });

        var hapticBtn = Button_("HapticBtn", root, new Vector2(0.5f, 0.5f), new Vector2(0, 137), new Vector2(700, 120),
                                "", 38, Accent, false, out _hapticsLabel);
        hapticBtn.onClick.AddListener(() =>
        {
            SaveData.HapticsOn = !SaveData.HapticsOn;
            SaveData.ApplySettings();
            RefreshSettingLabels();
            if (SaveData.HapticsOn) Haptics.Medium();   // immediate confirmation you can feel
        });

        // Destructive: hidden behind a confirm step so it can't be hit by accident.
        TMP_Text resetLabel;
        var resetBtn = Button_("ResetBtn", root, new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(700, 110),
                               "RESET PROGRESS", 34, Danger, false, out resetLabel);
        resetBtn.onClick.AddListener(() => _confirmRow.SetActive(true));

        var resetNote = Text_("ResetNote", root, new Vector2(0.5f, 0.5f), new Vector2(0, -95), new Vector2(760, 60), 24, TextAnchor.MiddleCenter,
                              "clears stars and day streak · best score is kept");
        resetNote.color = new Color(0.75f, 0.8f, 0.9f, 0.6f);

        // Confirm row (hidden until Reset is pressed).
        _confirmRow = new GameObject("ConfirmRow");
        _confirmRow.transform.SetParent(root, false);
        var crt = _confirmRow.AddComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = new Vector2(0, -240); crt.sizeDelta = new Vector2(800, 210);

        var warn = Text_("Warn", crt, new Vector2(0.5f, 1f), new Vector2(0, -4), new Vector2(800, 60), 30, TextAnchor.UpperCenter,
                         "Clear stars and day streak?");
        warn.color = new Color(1f, 0.8f, 0.8f, 0.95f);

        TMP_Text yesLabel, noLabel;
        var yesBtn = Button_("ResetYes", crt, new Vector2(0.5f, 0.5f), new Vector2(-175, -35), new Vector2(320, 100),
                             "CLEAR", 34, Danger, true, out yesLabel);
        yesBtn.onClick.AddListener(() =>
        {
            SaveData.ResetProgress();
            _confirmRow.SetActive(false);
            CloseSettings();
            // Wiping progress must also abandon the run in flight — otherwise the player keeps
            // playing level 14 with a freshly-zeroed save, which is incoherent.
            if (OnProgressReset != null) OnProgressReset();
            ShowBanner("PROGRESS RESET", new Color(1f, 0.7f, 0.7f, 1f), 0.9f);
        });
        var noBtn = Button_("ResetNo", crt, new Vector2(0.5f, 0.5f), new Vector2(175, -35), new Vector2(320, 100),
                            "CANCEL", 34, Accent, false, out noLabel);
        noBtn.onClick.AddListener(() => _confirmRow.SetActive(false));
        _confirmRow.SetActive(false);

        TMP_Text closeLabel;
        var closeBtn = Button_("CloseBtn", root, new Vector2(0.5f, 0.5f), new Vector2(0, -460), new Vector2(460, 120),
                               Spaced("CLOSE"), 40, Accent, true, out closeLabel);
        closeBtn.onClick.AddListener(CloseSettings);

        go.SetActive(false);
        return go;
    }

    // ---------- in-game gear ----------

    private GameObject BuildInGameGear()
    {
        // Bottom-left of the safe area: far from the thumb's play zone and the top HUD.
        TMP_Text lbl;
        var btn = Button_("GearInGame", _safe, new Vector2(0f, 0f), new Vector2(80, 80), new Vector2(96, 96),
                          "", 1, Accent, false, out lbl);
        btn.onClick.AddListener(OpenSettings);

        var icon = new GameObject("Icon");
        icon.transform.SetParent(btn.transform, false);
        var img = icon.AddComponent<Image>();
        img.sprite = VisualUtils.Gear(); img.raycastTarget = false;
        img.color = new Color(0.8f, 0.9f, 1f, 0.7f);
        var irt2 = img.rectTransform;
        irt2.anchorMin = irt2.anchorMax = new Vector2(0.5f, 0.5f); irt2.pivot = new Vector2(0.5f, 0.5f);
        irt2.anchoredPosition = Vector2.zero; irt2.sizeDelta = new Vector2(58, 58);
        return btn.gameObject;
    }

    public void ShowInGameGear(bool show) { if (_gearInGame != null) _gearInGame.SetActive(show); }

    // ---------- daily result ----------

    private GameObject BuildDailyResult()
    {
        var go = new GameObject("DailyResult");
        go.transform.SetParent(_canvas.transform, false);
        var dim = go.AddComponent<Image>();
        // Fully opaque: the menu/maze behind must not read through and compete for attention.
        dim.color = new Color(0.02f, 0.03f, 0.05f, 1f);
        dim.raycastTarget = true;
        var drt = dim.rectTransform; drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
        var root = go.transform as RectTransform;

        var title = Display(Text_("DTitle", root, new Vector2(0.5f, 0.5f), new Vector2(0, 320), new Vector2(1000, 130), 76, TextAnchor.MiddleCenter, "DAILY COMPLETE"));
        title.color = new Color(1f, 0.85f, 0.4f, 1f);

        _dailyResultText = Text_("DBody", root, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1000, 420), 44, TextAnchor.MiddleCenter, "");
        _dailyResultText.color = new Color(0.9f, 0.95f, 1f, 0.95f);

        TMP_Text lbl;
        var okBtn = Button_("DailyOk", root, new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(460, 130),
                            Spaced("CONTINUE"), 42, Accent, true, out lbl);
        okBtn.onClick.AddListener(() =>
        {
            go.SetActive(false);
            if (OnDailyResultClosed != null) OnDailyResultClosed();
        });

        go.SetActive(false);
        return go;
    }

    public void ShowDailyResult(bool cleared, int score, int dayStreak, bool newBest)
    {
        _dailyResultText.text =
            (cleared ? "You solved today's maze!\n\n" : "Out of time on today's maze.\n\n") +
            "Score   " + score + "\n" +
            "Day streak   " + dayStreak + "\n" +
            (newBest ? "\nNEW DAILY BEST!" : "\nDaily best   " + SaveData.DailyBest) +
            "\n\nA new maze unlocks tomorrow.";
        _dailyResultPanel.SetActive(true);
    }

    public void SetHintText(string s) { if (_hintText != null) _hintText.text = s; }

    private GameObject BuildHint()
    {
        _hintText = Text_("Hint", _safe, new Vector2(0.5f, 0.5f), new Vector2(0, -300), new Vector2(1000, 120), 48, TextAnchor.MiddleCenter, "DRAG to move   •   TAP to ping");
        _hintText.color = new Color(0.75f, 0.9f, 1f, 0.9f);
        _hintText.gameObject.SetActive(false);
        return _hintText.gameObject;
    }
}
