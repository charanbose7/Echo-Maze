using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds and animates the whole HUD in code (legacy uGUI Text so nothing needs
/// importing). Safe-area aware. Owns all the "readout" juice: rolling score, streak
/// flame, star pops, NEW BEST callout, ping flash/darken. GameManager tells it WHAT
/// happened; UIManager decides how it looks.
/// </summary>
public class UIManager : MonoBehaviour
{
    private Font _font;
    private Canvas _canvas;
    private RectTransform _safe;

    private Text _levelText, _scoreText, _timerText, _streakText;
    private RectTransform _pingRow;
    private Sprite _dotSprite;
    private readonly List<Image> _dots = new List<Image>();

    private Image _streakGlow;
    private GameObject _startOverlay, _celebOverlay, _hint;
    private Text _hintText;
    private Text _startSub, _celebTitle, _celebScore;
    private Image[] _stars = new Image[3];
    private Text _newBest;
    private Text _rewindText;
    private float _rewindT = -1f;

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
    private int _lostDot = -1; private float _lostDotT;

    private static readonly Color DotLost = new Color(1f, 0.3f, 0.2f, 1f);

    private static readonly Color DotFull = new Color(0.6f, 0.9f, 1f, 1f);
    private static readonly Color DotUsed = new Color(0.6f, 0.9f, 1f, 0.16f);
    private static readonly Color TextCol = new Color(0.85f, 0.92f, 1f, 0.9f);

    public void BuildUI()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _dotSprite = VisualUtils.Disc();

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

        _levelText = Text_("Level", _safe, new Vector2(0, 1), new Vector2(30, -24), new Vector2(500, 60), 42, TextAnchor.UpperLeft, "LEVEL 1");
        _timerText = Text_("Timer", _safe, new Vector2(0, 1), new Vector2(30, -84), new Vector2(500, 50), 32, TextAnchor.UpperLeft, "");
        _scoreText = Text_("Score", _safe, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(700, 70), 52, TextAnchor.UpperCenter, "0");

        // Streak flame + multiplier under the score.
        var glowGO = new GameObject("StreakGlow");
        glowGO.transform.SetParent(_safe, false);
        _streakGlow = glowGO.AddComponent<Image>();
        _streakGlow.sprite = VisualUtils.RadialGlow();
        _streakGlow.raycastTarget = false;
        _streakGlow.color = new Color(GameConfig.StreakColor.r, GameConfig.StreakColor.g, GameConfig.StreakColor.b, 0f);
        var grt = _streakGlow.rectTransform;
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 1f); grt.pivot = new Vector2(0.5f, 1f);
        grt.anchoredPosition = new Vector2(0, -96); grt.sizeDelta = new Vector2(220, 220);
        _streakText = Text_("Streak", _safe, new Vector2(0.5f, 1), new Vector2(0, -104), new Vector2(400, 50), 34, TextAnchor.UpperCenter, "");
        _streakText.color = new Color(1f, 0.8f, 0.4f, 0f);

        // Ping dots (top-right).
        var rowGO = new GameObject("PingRow");
        rowGO.transform.SetParent(_safe, false);
        _pingRow = rowGO.AddComponent<RectTransform>();
        _pingRow.anchorMin = _pingRow.anchorMax = new Vector2(1, 1); _pingRow.pivot = new Vector2(1, 1);
        // Its own line BELOW the score so a long dot row never overlaps the score number.
        _pingRow.anchoredPosition = new Vector2(-30, -100); _pingRow.sizeDelta = new Vector2(800, 36);

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

        _startOverlay = BuildStart();
        _celebOverlay = BuildCeleb();
        _hint = BuildHint();

        _newBest = Text_("NewBest", _canvas.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 470), new Vector2(900, 90), 64, TextAnchor.MiddleCenter, "NEW BEST!");
        _newBest.color = new Color(1f, 0.85f, 0.3f, 0f);

        _rewindText = Text_("Rewind", _canvas.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(1000, 120), 74, TextAnchor.MiddleCenter, "REWIND  -" + Mathf.RoundToInt(GameConfig.RewindSeconds) + "s");
        _rewindText.color = new Color(0.5f, 0.85f, 1f, 0f);

        // Top-most: opaque cover for masking the between-level swap (created last = drawn last).
        _cover = FullScreen("Cover", new Color(0, 0, 0, 0));

        SetScoreImmediate(0);
    }

    // ---------- public API ----------
    public void SetLevel(int level) => _levelText.text = "LEVEL " + level;

    public void SetScoreImmediate(int score)
    {
        _scoreTarget = score; _scoreDisplay = score; _scoreShown = score;
        _scoreText.text = score.ToString();
    }

    public void RollScoreTo(int target) => _scoreTarget = target;

    public void BuildPingDots(int total)
    {
        foreach (var d in _dots) if (d) Destroy(d.gameObject);
        _dots.Clear();
        _lostDot = -1;
        const float sz = 20f, gap = 6f;
        for (int i = 0; i < total; i++)
        {
            var go = new GameObject("Dot" + i);
            go.transform.SetParent(_pingRow, false);
            var img = go.AddComponent<Image>();
            img.sprite = _dotSprite; img.color = DotFull; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(sz, sz);
            rt.anchoredPosition = new Vector2(-i * (sz + gap), 0);
            _dots.Add(img);
        }
    }

    public void SetPingsRemaining(int remaining)
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            int fromLeft = _dots.Count - 1 - i;
            _dots[i].color = fromLeft < remaining ? DotFull : DotUsed;
        }
    }

    /// <summary>Lose a ping to a decoy: recolor the bar and play a red shrink pop on the lost dot.</summary>
    public void LosePing(int remaining)
    {
        SetPingsRemaining(remaining);
        int i = _dots.Count - 1 - remaining; // the dot that just went dark
        if (i >= 0 && i < _dots.Count)
        {
            _lostDot = i;
            _lostDotT = 0f;
            _dots[i].color = DotLost;
            _dots[i].rectTransform.localScale = Vector3.one * 1.7f;
        }
    }

    public void SetTimer(int seconds)
    {
        if (seconds == _lastTimer) return;   // only touch the string when it changes
        _lastTimer = seconds;
        if (seconds < 0) { _timerText.text = ""; return; }
        _timerText.text = seconds + "s";
        _timerText.color = seconds < 10 ? new Color(1f, 0.5f, 0.5f, 1f) : new Color(1f, 1f, 1f, 0.55f);
    }

    public void SetStreak(int streakCount, float multiplier)
    {
        _streakCount = streakCount; _streakMul = multiplier;
        _streakOn = streakCount > 0;
        _streakText.text = _streakOn ? "x" + multiplier.ToString("0.0") : "";
    }

    public void ShowStart(int bestScore, int bestStreak)
    {
        _startSub.text = "Drag to move  •  Tap to ping\nReach the exit before pings run out\n\n" +
                         (bestScore > 0 ? "Best  " + bestScore + "     Streak  " + bestStreak + "\n\n" : "\n") +
                         "Tap to play";
        _startOverlay.SetActive(true);
        _celebOverlay.SetActive(false);
    }
    public void HideStart() => _startOverlay.SetActive(false);

    public void ShowHint() => _hint.SetActive(true);
    public void HideHint() => _hint.SetActive(false);

    public void ShowCelebration(int level)
    {
        _celebTitle.text = "LEVEL " + level + " CLEAR";
        _celebScore.text = "";
        for (int i = 0; i < 3; i++) { _starShown[i] = false; _starT[i] = 0f; _stars[i].transform.localScale = Vector3.zero; }
        _celebOverlay.SetActive(true);
    }
    public void SetCelebrationScoreLine(string line) => _celebScore.text = line;
    public void HideCelebration() => _celebOverlay.SetActive(false);
    public void PopStar(int index) { if (index >= 0 && index < 3) { _starShown[index] = true; _starT[index] = 0f; } }

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
                _stars[i].color = new Color(1f, 0.85f, 0.3f, 1f);
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
        _streakText.color = new Color(1f, 0.8f, 0.4f, Mathf.Lerp(tc.a, _streakOn ? 1f : 0f, dt * 8f));

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

        // Lost-ping dot: pops big + red, then shrinks and settles to the "used" look.
        if (_lostDot >= 0 && _lostDot < _dots.Count && _lostDotT < 1f)
        {
            _lostDotT = Mathf.Min(1f, _lostDotT + dt / 0.45f);
            float e = Easing.OutCubic(_lostDotT);
            var img = _dots[_lostDot];
            img.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, e);
            img.color = Color.Lerp(DotLost, DotUsed, e);
            if (_lostDotT >= 1f) { img.rectTransform.localScale = Vector3.one; img.color = DotUsed; _lostDot = -1; }
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
    private Text Text_(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, string init)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = _font; t.fontSize = fontSize; t.alignment = align; t.color = TextCol; t.text = init;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return t;
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

    private GameObject BuildStart()
    {
        var go = new GameObject("StartOverlay");
        go.transform.SetParent(_canvas.transform, false);
        var panel = go.AddComponent<Image>();
        panel.color = new Color(0.01f, 0.015f, 0.03f, 0.85f); panel.raycastTarget = false;
        var prt = panel.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        var title = Text_("Title", go.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(1000, 160), 96, TextAnchor.MiddleCenter, "ECHO MAZE");
        title.color = new Color(0.6f, 0.9f, 1f, 1f);
        _startSub = Text_("Sub", go.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(1000, 500), 40, TextAnchor.MiddleCenter, "");
        _startSub.color = new Color(0.8f, 0.88f, 1f, 0.85f);
        return go;
    }

    private GameObject BuildCeleb()
    {
        var go = new GameObject("CelebOverlay");
        go.transform.SetParent(_canvas.transform, false);
        var panel = go.AddComponent<Image>();
        panel.color = new Color(0.01f, 0.02f, 0.04f, 0.55f); panel.raycastTarget = false;
        var prt = panel.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;

        _celebTitle = Text_("CTitle", go.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 260), new Vector2(1000, 140), 80, TextAnchor.MiddleCenter, "LEVEL CLEAR");
        _celebTitle.color = new Color(0.7f, 1f, 0.85f, 1f);

        // Star row.
        var starSprite = VisualUtils.Star();
        for (int i = 0; i < 3; i++)
        {
            var s = new GameObject("Star" + i);
            s.transform.SetParent(go.transform, false);
            var img = s.AddComponent<Image>();
            img.sprite = starSprite; img.raycastTarget = false; img.color = new Color(1f, 0.85f, 0.3f, 1f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((i - 1) * 150f, 90f); rt.sizeDelta = new Vector2(120, 120);
            rt.localScale = Vector3.zero;
            _stars[i] = img;
        }

        _celebScore = Text_("CScore", go.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(1000, 120), 46, TextAnchor.MiddleCenter, "");
        _celebScore.color = new Color(0.9f, 0.95f, 1f, 0.9f);
        go.SetActive(false);
        return go;
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
