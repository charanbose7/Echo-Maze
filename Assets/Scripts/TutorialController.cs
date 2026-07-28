using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// First-run tutorial, deliberately BLOCKING: playtesters skipped text-only hints entirely and
/// started with no idea what the controls were, so this refuses to advance until the player has
/// actually performed each action.
///
///   Step 1 — an animated finger mimes a drag; the player must really drag the dot.
///   Step 2 — the finger mimes a tap; the player must really fire a ping.
///
/// Only ever runs on the very first play (SaveData.HintSeen), and only in the endless run.
/// </summary>
public class TutorialController : MonoBehaviour
{
    private enum Step { Drag, Ping, Done }

    private GameManager _gm;
    private UIManager _ui;
    private PlayerController _player;

    private Canvas _canvas;
    private GameObject _root;
    private Image _finger;
    private Image _fingerRipple;
    private Text _title, _sub;

    private Step _step = Step.Drag;
    private float _t;
    private float _dragAccum;
    private Vector2 _lastPlayerPos;
    private bool _running;

    /// <summary>True when this player has never completed the tutorial.</summary>
    public bool ShouldRun => !SaveData.HintSeen;

    public void Begin(GameManager gm, UIManager ui, PlayerController player)
    {
        _gm = gm; _ui = ui; _player = player;
        _step = Step.Drag; _t = 0f; _dragAccum = 0f;
        _lastPlayerPos = player.transform.position;
        BuildUI();
        _running = true;
        SetStepText();
    }

    private void BuildUI()
    {
        if (_root != null) { _root.SetActive(true); return; }

        _canvas = GetComponentInChildren<Canvas>();
        if (_canvas == null) return;

        _root = new GameObject("TutorialOverlay");
        _root.transform.SetParent(_canvas.transform, false);
        var rt = _root.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Dim the maze slightly so the instruction reads, without hiding the game.
        var dim = new GameObject("Dim");
        dim.transform.SetParent(_root.transform, false);
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0.01f, 0.015f, 0.03f, 0.55f);
        dimImg.raycastTarget = false;
        var drt = dimImg.rectTransform;
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;

        _title = MakeText(font, "TutTitle", new Vector2(0, 300), 62, "DRAG TO MOVE");
        _title.color = new Color(0.75f, 0.92f, 1f, 1f);
        _sub = MakeText(font, "TutSub", new Vector2(0, 210), 34, "slide your finger anywhere");
        _sub.color = new Color(0.8f, 0.88f, 1f, 0.8f);

        // The mimed finger: a soft dot plus an expanding ripple for taps.
        var ripple = new GameObject("Ripple");
        ripple.transform.SetParent(_root.transform, false);
        _fingerRipple = ripple.AddComponent<Image>();
        _fingerRipple.sprite = VisualUtils.HollowRing();
        _fingerRipple.raycastTarget = false;
        _fingerRipple.color = new Color(1f, 1f, 1f, 0f);
        var rrt = _fingerRipple.rectTransform;
        rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f); rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.sizeDelta = new Vector2(190, 190);

        var fingerGO = new GameObject("Finger");
        fingerGO.transform.SetParent(_root.transform, false);
        _finger = fingerGO.AddComponent<Image>();
        _finger.sprite = VisualUtils.Disc();
        _finger.raycastTarget = false;
        _finger.color = new Color(1f, 1f, 1f, 0.85f);
        var frt = _finger.rectTransform;
        frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f); frt.pivot = new Vector2(0.5f, 0.5f);
        frt.sizeDelta = new Vector2(86, 86);
    }

    private Text MakeText(Font font, string name, Vector2 pos, int size, string content)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter;
        t.text = content; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(1000, 120);
        return t;
    }

    private void SetStepText()
    {
        if (_title == null) return;
        if (_step == Step.Drag) { _title.text = "DRAG TO MOVE";  _sub.text = "slide your finger anywhere"; }
        else                    { _title.text = "TAP TO PING";   _sub.text = "reveals the walls around you"; }
    }

    private void Update()
    {
        if (!_running) return;
        _t += Time.unscaledDeltaTime;

        if (_step == Step.Drag) AnimateDrag();
        else if (_step == Step.Ping) AnimateTap();

        CheckProgress();
    }

    /// <summary>Finger slides back and forth to mime a drag.</summary>
    private void AnimateDrag()
    {
        float loop = Mathf.Repeat(_t, 2.2f) / 2.2f;
        float eased = Easing.InOutSine(Mathf.PingPong(loop * 2f, 1f));
        float x = Mathf.Lerp(-190f, 190f, eased);
        _finger.rectTransform.anchoredPosition = new Vector2(x, -140f);
        _finger.color = new Color(1f, 1f, 1f, 0.9f);
        _fingerRipple.color = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>Finger presses in place with an expanding ripple to mime a tap.</summary>
    private void AnimateTap()
    {
        float loop = Mathf.Repeat(_t, 1.5f) / 1.5f;
        _finger.rectTransform.anchoredPosition = new Vector2(0f, -140f);
        float press = loop < 0.2f ? 1f - loop / 0.2f * 0.25f : 0.75f + Mathf.Min(1f, (loop - 0.2f) / 0.3f) * 0.25f;
        _finger.rectTransform.localScale = Vector3.one * press;

        float rip = Mathf.Clamp01((loop - 0.15f) / 0.6f);
        _fingerRipple.rectTransform.anchoredPosition = new Vector2(0f, -140f);
        _fingerRipple.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.8f, rip);
        _fingerRipple.color = new Color(1f, 1f, 1f, (1f - rip) * 0.7f * (loop > 0.15f ? 1f : 0f));
    }

    /// <summary>Gate each step on the player genuinely performing the action.</summary>
    private void CheckProgress()
    {
        if (_step == Step.Drag)
        {
            Vector2 now = _player.transform.position;
            _dragAccum += Vector2.Distance(now, _lastPlayerPos);
            _lastPlayerPos = now;

            if (_dragAccum > GameConfig.TutorialDragDistance)
            {
                _step = Step.Ping;
                _t = 0f;
                _finger.rectTransform.localScale = Vector3.one;
                SetStepText();
                Haptics.Light();
            }
        }
        else if (_step == Step.Ping)
        {
            // GameManager tells us when a ping actually fired.
            if (_pinged)
            {
                _step = Step.Done;
                Finish();
            }
        }
    }

    private bool _pinged;
    /// <summary>Called by GameManager when the player fires a ping.</summary>
    public void NotifyPinged() => _pinged = true;

    /// <summary>Is the tutorial currently blocking the ping action? (Drag step must come first.)</summary>
    public bool PingAllowed => _step != Step.Drag;

    /// <summary>Force the overlay away (e.g. when bailing back to the main menu).</summary>
    public void Hide()
    {
        _running = false;
        if (_root != null) _root.SetActive(false);
    }

    private void Finish()
    {
        _running = false;
        SaveData.MarkHintSeen();
        if (_root != null) _root.SetActive(false);
        if (_ui != null) _ui.ShowBanner("GO!", new Color(0.7f, 1f, 0.85f, 1f), 0.6f);
        if (_gm != null) _gm.OnTutorialComplete();
    }
}
