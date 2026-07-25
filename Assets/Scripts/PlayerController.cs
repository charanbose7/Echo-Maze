using UnityEngine;

/// <summary>
/// The glowing dot. Dynamic Rigidbody2D (walls physically block it) driven by velocity.
/// Touch feels like a FLOATING JOYSTICK: press anywhere, and the drag vector from that
/// point steers the dot (magnitude scales speed). A quick tap that never leaves the
/// deadzone fires a ping instead. WASD/arrows also work.
///
/// Juice: the dot squashes toward its motion, breathes when idle, drags a trail, and
/// kicks up particles + a light haptic when it scrapes a wall.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    private Rigidbody2D _rb;
    private Camera _cam;
    private GameManager _gm;
    private SonarManager _sonar;
    private FxManager _fx;
    private Transform _glow;
    private TrailRenderer _trail;
    private Vector3 _glowBase;
    private Vector3 _glowScale;
    private float _spawnT = 1f; // 0..1 pop-in progress (1 = done)

    // Gesture state.
    private bool _pointerActive;
    private bool _dragging;
    private Vector2 _downScreen;
    private float _downTime;
    private Vector2 _moveDir;   // normalized steer direction
    private float _moveMag;     // 0..1 speed scale

    private float _lastSlideTime;
    private float _lastSlideHaptic;

    public void Init(GameManager gm, SonarManager sonar, Camera cam, Transform glow, FxManager fx, TrailRenderer trail)
    {
        _gm = gm; _sonar = sonar; _cam = cam; _fx = fx; _glow = glow; _trail = trail;
        _rb = GetComponent<Rigidbody2D>();
        _glowBase = Vector3.one * GameConfig.PlayerGlowScale;
        _glowScale = _glowBase;
        if (_glow != null) _glow.localScale = _glowBase;
    }

    public void PlaceAt(Vector2 worldPos)
    {
        _rb.position = worldPos;
        transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
        _rb.linearVelocity = Vector2.zero;
        _pointerActive = _dragging = false;
        _moveMag = 0f;

        // No streak from the previous level's position, and pop in instead of snapping.
        if (_trail != null) _trail.Clear();
        _spawnT = 0f;
        _glowScale = Vector3.zero;
        if (_glow != null) _glow.localScale = Vector3.zero;
    }

    private void Update()
    {
        if (_gm.State != GameState.Playing)
        {
            _pointerActive = _dragging = false;
            _moveMag = 0f;
            AnimateGlow();
            return;
        }

        if (EchoInput.PingKeyDown) _gm.RequestPing();

        // ---- Floating joystick ----
        if (EchoInput.PointerDown)
        {
            _pointerActive = true;
            _dragging = false;
            _downScreen = EchoInput.PointerScreen;
            _downTime = Time.time;
        }

        if (_pointerActive && EchoInput.PointerHeld)
        {
            Vector2 delta = EchoInput.PointerScreen - _downScreen;
            float dist = delta.magnitude;
            if (!_dragging && (dist > GameConfig.JoystickDeadzonePx ||
                               Time.time - _downTime > GameConfig.TapMaxDuration))
                _dragging = true;

            if (_dragging && dist > 0.0001f)
            {
                _moveDir = delta / dist;
                _moveMag = Mathf.Clamp01(dist / GameConfig.JoystickFullThrowPx);
            }
            else _moveMag = 0f;
        }

        if (_pointerActive && EchoInput.PointerUp)
        {
            if (!_dragging) _gm.RequestPing(); // tap without drag = ping
            _pointerActive = _dragging = false;
            _moveMag = 0f;
        }

        AnimateGlow();
    }

    private void FixedUpdate()
    {
        if (_gm.State != GameState.Playing) { _rb.linearVelocity = Vector2.zero; return; }

        Vector2 wasd = EchoInput.MoveAxis;
        if (wasd.sqrMagnitude > 0.01f)
            _rb.linearVelocity = wasd * GameConfig.PlayerMoveSpeed;
        else if (_dragging && _moveMag > 0f)
            _rb.linearVelocity = _moveDir * (GameConfig.PlayerMoveSpeed * _moveMag);
        else
            _rb.linearVelocity = Vector2.zero;
    }

    /// <summary>Squash toward motion, or breathe when idle. Smoothed, no linear pops.</summary>
    private void AnimateGlow()
    {
        if (_glow == null) return;

        // Spawn pop-in takes priority — dot grows from nothing with an overshoot.
        if (_spawnT < 1f)
        {
            _spawnT = Mathf.Min(1f, _spawnT + Time.deltaTime / GameConfig.SpawnPopTime);
            float s = Easing.OutBack(_spawnT);
            _glowScale = _glowBase * s;
            _glow.localScale = _glowScale;
            _glow.localRotation = Quaternion.identity;
            return;
        }

        Vector2 v = _rb.linearVelocity;
        float speedFrac = Mathf.Clamp01(v.magnitude / GameConfig.PlayerMoveSpeed);

        Vector3 target;
        if (speedFrac > 0.06f)
        {
            float stretch = 1f + GameConfig.SquashAmount * speedFrac;
            float squash = 1f - GameConfig.SquashAmount * 0.5f * speedFrac;
            target = new Vector3(_glowBase.x * stretch, _glowBase.y * squash, _glowBase.z);
            float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            _glow.localRotation = Quaternion.Euler(0f, 0f, ang);
        }
        else
        {
            float breath = 1f + GameConfig.BreathAmplitude * Mathf.Sin(Time.time * GameConfig.BreathSpeed);
            target = _glowBase * breath;
            _glow.localRotation = Quaternion.identity;
        }

        _glowScale = Vector3.Lerp(_glowScale, target, Time.deltaTime * GameConfig.SquashLerp);
        _glow.localScale = _glowScale;
    }

    private void OnCollisionStay2D(Collision2D col)
    {
        if (_gm.State != GameState.Playing) return;
        if (_rb.linearVelocity.sqrMagnitude < 1f) return;         // only when actually sliding
        if (Time.time - _lastSlideTime < 0.05f) return;
        if (col.contactCount == 0) return;

        _lastSlideTime = Time.time;
        if (_fx != null) _fx.PlaySlide(col.GetContact(0).point);

        if (Time.time - _lastSlideHaptic > 0.12f)
        {
            Haptics.Light();
            _lastSlideHaptic = Time.time;
        }
    }
}
