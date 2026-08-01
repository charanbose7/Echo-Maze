using UnityEngine;
using System.Runtime.InteropServices;

/// <summary>
/// Cross-platform haptics for Android and iOS.
///
/// Six intents, mapped to whatever each platform considers idiomatic:
///
///   Selection  UI tick — buttons, toggles
///   Light      ping fired, wall brushed, star popped, clock ticking
///   Medium     something happened that matters — orb, echo return
///   Heavy      failure, rewind
///   Success    level cleared
///   Wrong      hit a decoy
///
/// ANDROID uses the system Vibrator with VibrationEffect (API 26+). Since Android exposes raw
/// duration and amplitude rather than named effects, the six intents become tuned pulses.
///
/// iOS uses UIFeedbackGenerator through a small native bridge (Assets/Plugins/iOS). These are the
/// same generators the OS uses for its own UI, so the feel is native and Apple's own guidance is
/// simply to call them — they no-op on hardware without a Taptic Engine instead of failing.
///
/// Vibration is a motor, not a speaker: neither platform routes this through the media volume, so
/// it works with the phone muted. What legitimately silences it is the user's own OS setting
/// (iOS "System Haptics", Android's vibration-intensity sliders), which an app cannot and should
/// not override.
///
/// Four traps this implementation exists to avoid, all of them learned the hard way:
///
///  1. MISSING PERMISSION (Android). Reaching the vibrator through AndroidJavaObject is invisible
///     to Unity's manifest scanner, so android.permission.VIBRATE never lands in the APK and every
///     call is dropped silently — no exception, nothing in logcat. AndroidManifestPostProcessor
///     injects it, and Handheld.Vibrate() is referenced below because that IS a pattern the
///     scanner recognises.
///
///  2. PER-CALL JNI ALLOCATION (Android). Building a new AndroidJavaClass + VibrationEffect on
///     every buzz burns JNI local references, which are capped at 512 — the classic "works for a
///     while, then stops". Everything here is built once and replayed; VibrationEffect is
///     immutable, so that is safe.
///
///  3. PULSES TOO SHORT TO FEEL (Android). A 12ms pulse does nothing on a rotary (ERM) motor — the
///     mass barely starts turning before it ends. Devices without amplitude control get a
///     longer, wider-spaced ladder, since duration is then the only thing distinguishing tiers.
///
///  4. FLOODING THE MOTOR (Android). Vibrator.vibrate() does not queue — it CANCELS whatever is
///     playing. Firing faster than a pulse takes to play leaves the motor stuttering on/off,
///     producing nothing perceptible while every log line reports success. Fire() below refuses to
///     interrupt a playing effect unless the new one outranks it.
///
/// Editor and other platforms: no-op.
/// </summary>
public static class Haptics
{
    /// <summary>The six haptic intents, ordered light to heavy.</summary>
    public enum Kind { Selection, Light, Medium, Heavy, Success, Wrong }

    public static bool Enabled = true;

    /// <summary>What the platform layer negotiated at startup. Logged at boot; useful over adb/Xcode.</summary>
    public static string Status { get; private set; } = "editor / unsupported platform";

    // Louder events may cut off quieter ones, never the reverse.
    private const int PrioAmbient = 1;   // UI tick, ping, wall brush
    private const int PrioEvent   = 2;   // orb, echo return, countdown
    private const int PrioMajor   = 3;   // clear, fail, rewind, decoy

    private static float _busyUntil = -10f;
    private static int _busyPriority;

    // ---- public API -------------------------------------------------------------------

    /// <summary>Faint tick for UI selection — buttons, toggles.</summary>
    public static void Selection() { Fire(Kind.Selection, PrioAmbient); }

    /// <summary>Light tap: ping fired, wall brushed, star popped, clock ticking down.</summary>
    public static void Light() { Fire(Kind.Light, PrioAmbient); }

    /// <summary>Mid tap: something happened that matters — orb, echo return.</summary>
    public static void Medium() { Fire(Kind.Medium, PrioEvent); }

    /// <summary>The heaviest single thump: failure, rewind.</summary>
    public static void Heavy() { Fire(Kind.Heavy, PrioMajor); }

    /// <summary>Celebratory pattern for a level clear.</summary>
    public static void Success() { Fire(Kind.Success, PrioMajor); }

    /// <summary>Blunt "that was bad" for touching a decoy.</summary>
    public static void Wrong() { Fire(Kind.Wrong, PrioMajor); }

    /// <summary>
    /// Gate every haptic on the motor being free.
    ///
    /// On Android this is essential — a new vibrate() cancels the one still playing, so an
    /// unguarded stream of taps cancels itself into silence. On iOS the system schedules its own
    /// events, but the same spacing keeps rapid-fire feedback from turning into mush.
    /// </summary>
    private static void Fire(Kind kind, int priority)
    {
        if (!Enabled || !PlatformReady) return;

        // Unscaled: the rewind penalty sets timeScale to 0 and still wants feedback.
        float now = Time.unscaledTime;
        if (now < _busyUntil && priority <= _busyPriority) return;

        _busyUntil = now + (DurationMs(kind) + RestMs) * 0.001f;
        _busyPriority = priority;

        PlatformPlay(kind);
    }

    private static void Report()
    {
        Debug.Log("[EchoMaze] Haptics: " + Status);
    }

    /// <summary>
    /// Fire every effect in sequence so a device that feels dead can be told apart from one whose
    /// OS is muting us. Not wired to any button — call from a debug hook when needed.
    /// </summary>
    public static void SelfTest(MonoBehaviour host)
    {
        if (host != null) host.StartCoroutine(SelfTestRoutine());
    }

    private static System.Collections.IEnumerator SelfTestRoutine()
    {
        Selection(); yield return new WaitForSecondsRealtime(0.4f);
        Light();     yield return new WaitForSecondsRealtime(0.4f);
        Medium();    yield return new WaitForSecondsRealtime(0.4f);
        Heavy();     yield return new WaitForSecondsRealtime(0.6f);
        Wrong();     yield return new WaitForSecondsRealtime(0.7f);
        Success();
    }

    // ======================================================================================
    //  ANDROID
    // ======================================================================================
#if UNITY_ANDROID && !UNITY_EDITOR

    // Two ladders, because a large share of Android devices cannot vary vibration strength.
    //
    // WITH amplitude control: short pulses, strength carries the difference.
    private const long MsSelect = 14, MsLight = 20, MsMedium = 32, MsHeavy = 55;
    private const int AmpSelect = 70, AmpLight = 110, AmpMedium = 190, AmpHeavy = 255;
    //
    // WITHOUT it: every pulse plays at full power and the amplitude argument is discarded, so
    // duration is the ONLY thing separating the tiers — spread much wider, and long enough that a
    // rotary motor has time to spin up at all.
    private const long MsSelectFlat = 25, MsLightFlat = 40, MsMediumFlat = 65, MsHeavyFlat = 110;

    private const int DefaultAmplitude = -1;   // VibrationEffect.DEFAULT_AMPLITUDE

    // Total wall-clock length of each waveform, so the guard knows how long to hold off.
    private const long SuccessMs = 305;   // 0+35+60+50+60+100
    private const long WrongMs   = 185;   // 0+55+70+60

    /// <summary>Silence after each effect so consecutive taps read as separate events.</summary>
    private const float RestMs = 45f;

    private static AndroidJavaObject _vibrator;     // held for the app's lifetime, never disposed
    private static AndroidJavaObject _attributes;   // classifies these as touch feedback
    private static AndroidJavaObject _select, _light, _medium, _heavy, _success, _wrong;

    private static bool _ready;
    private static bool _amplitudeControl;
    private static bool _handheldFallback;   // Vibrator unreachable; use Handheld.Vibrate for big beats

    private static bool PlatformReady { get { return _ready || _handheldFallback; } }

    // How the vibration is classified, best first.
    //
    // TOUCH, not MEDIA. The per-class intensity is what the OS multiplies our amplitude by, and it
    // differs per device — `adb shell dumpsys vibrator_manager` prints the table. On the hardware
    // this was developed against, TOUCH scaled 1.4x while MEDIA scaled 1.0x, so filing a game's
    // buzzes as media quietly cost strength. Check that dump before changing this.
    private const int TierVibrationAttributes = 2;   // android.os.VibrationAttributes, USAGE_TOUCH
    private const int TierAudioAttributes     = 1;   // android.media.AudioAttributes, SONIFICATION
    private const int TierNone                = 0;   // bare vibrate(effect)

    private static int _attrTier = TierNone;

    private static long DurationMs(Kind kind)
    {
        switch (kind)
        {
            case Kind.Selection: return _amplitudeControl ? MsSelect : MsSelectFlat;
            case Kind.Light:     return _amplitudeControl ? MsLight  : MsLightFlat;
            case Kind.Medium:    return _amplitudeControl ? MsMedium : MsMediumFlat;
            case Kind.Heavy:     return _amplitudeControl ? MsHeavy  : MsHeavyFlat;
            case Kind.Success:   return SuccessMs;
            default:             return WrongMs;
        }
    }

    private static AndroidJavaObject EffectFor(Kind kind)
    {
        switch (kind)
        {
            case Kind.Selection: return _select;
            case Kind.Light:     return _light;
            case Kind.Medium:    return _medium;
            case Kind.Heavy:     return _heavy;
            case Kind.Success:   return _success;
            default:             return _wrong;
        }
    }

    /// <summary>Runs once, before the first scene, on Unity's main thread (the JNI-attached one).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        try
        {
            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdk = version.GetStatic<int>("SDK_INT");

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                // Android 12 deprecated getSystemService("vibrator") in favour of the manager. The
                // old call still works on most builds, but a few hand back a vibrator that ignores
                // amplitude, so prefer the supported route where it exists.
                if (sdk >= 31)
                {
                    using (var mgr = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                        if (mgr != null) _vibrator = mgr.Call<AndroidJavaObject>("getDefaultVibrator");
                }
                if (_vibrator == null)
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            if (_vibrator == null) { Status = "no vibrator service"; Report(); return; }
            if (!_vibrator.Call<bool>("hasVibrator")) { Status = "device reports no vibrator"; Report(); return; }

            _amplitudeControl = _vibrator.Call<bool>("hasAmplitudeControl");

            BuildAttributes(TierVibrationAttributes);

            using (var fx = new AndroidJavaClass("android.os.VibrationEffect"))
            {
                if (_amplitudeControl)
                {
                    _select  = fx.CallStatic<AndroidJavaObject>("createOneShot", MsSelect, AmpSelect);
                    _light   = fx.CallStatic<AndroidJavaObject>("createOneShot", MsLight,  AmpLight);
                    _medium  = fx.CallStatic<AndroidJavaObject>("createOneShot", MsMedium, AmpMedium);
                    _heavy   = fx.CallStatic<AndroidJavaObject>("createOneShot", MsHeavy,  AmpHeavy);
                    // Rising three-tap for a clear, blunt double-thud for a mistake.
                    _success = fx.CallStatic<AndroidJavaObject>("createWaveform",
                                   new long[] { 0, 30, 55, 40, 55, 90 }, new int[] { 0, 150, 0, 205, 0, 255 }, -1);
                    _wrong   = fx.CallStatic<AndroidJavaObject>("createWaveform",
                                   new long[] { 0, 45, 60, 50 }, new int[] { 0, 210, 0, 230 }, -1);
                }
                else
                {
                    // DEFAULT_AMPLITUDE is the documented "device decides" value; a number here
                    // would simply be discarded.
                    _select  = fx.CallStatic<AndroidJavaObject>("createOneShot", MsSelectFlat, DefaultAmplitude);
                    _light   = fx.CallStatic<AndroidJavaObject>("createOneShot", MsLightFlat,  DefaultAmplitude);
                    _medium  = fx.CallStatic<AndroidJavaObject>("createOneShot", MsMediumFlat, DefaultAmplitude);
                    _heavy   = fx.CallStatic<AndroidJavaObject>("createOneShot", MsHeavyFlat,  DefaultAmplitude);
                    // On/off overload — rhythm and pulse length stand in for rising amplitude.
                    _success = fx.CallStatic<AndroidJavaObject>("createWaveform",
                                   new long[] { 0, 35, 60, 50, 60, 100 }, -1);
                    _wrong   = fx.CallStatic<AndroidJavaObject>("createWaveform",
                                   new long[] { 0, 55, 70, 60 }, -1);
                }
            }

            _ready = true;
            Status = "android ok (sdk " + sdk + ", amplitude control "
                   + (_amplitudeControl ? "yes" : "no") + ", " + TierName(_attrTier) + ")";
        }
        catch (System.Exception e)
        {
            // Couldn't reach the Vibrator at all (unexpected OEM shape, blocked reflection...).
            // Fall back rather than going silent.
            _ready = false;
            _handheldFallback = true;
            Status = "android init failed (" + e.Message + ") — Handheld.Vibrate fallback";
        }
        Report();
    }

    /// <summary>
    /// Build the attributes object for a tier, returning false if that tier isn't available.
    /// Constants are read off the Java class rather than hardcoded, so a wrong guess about an
    /// integer value can't silently mis-classify every buzz.
    /// </summary>
    private static bool BuildAttributes(int tier)
    {
        _attributes = null;
        _attrTier = TierNone;

        if (tier >= TierVibrationAttributes)
        {
            try
            {
                using (var vaClass = new AndroidJavaClass("android.os.VibrationAttributes"))
                {
                    int usageTouch = vaClass.GetStatic<int>("USAGE_TOUCH");
                    using (var b = new AndroidJavaObject("android.os.VibrationAttributes$Builder"))
                    using (var b1 = b.Call<AndroidJavaObject>("setUsage", usageTouch))
                        _attributes = b1.Call<AndroidJavaObject>("build");
                }
                _attrTier = TierVibrationAttributes;
                return true;
            }
            catch { _attributes = null; }
        }

        if (tier >= TierAudioAttributes)
        {
            try
            {
                // USAGE_ASSISTANCE_SONIFICATION = 13, CONTENT_TYPE_SONIFICATION = 4. AOSP maps
                // SONIFICATION to VibrationAttributes.USAGE_TOUCH, matching the tier above.
                using (var builder = new AndroidJavaObject("android.media.AudioAttributes$Builder"))
                using (var b1 = builder.Call<AndroidJavaObject>("setUsage", 13))
                using (var b2 = b1.Call<AndroidJavaObject>("setContentType", 4))
                    _attributes = b2.Call<AndroidJavaObject>("build");
                _attrTier = TierAudioAttributes;
                return true;
            }
            catch { _attributes = null; }
        }

        return false;
    }

    private static string TierName(int tier)
    {
        if (tier == TierVibrationAttributes) return "VibrationAttributes(USAGE_TOUCH)";
        if (tier == TierAudioAttributes) return "AudioAttributes(SONIFICATION)";
        return "no attributes";
    }

    private static void PlatformPlay(Kind kind)
    {
        if (!_ready) { FallbackVibrate(kind); return; }

        var effect = EffectFor(kind);
        if (effect == null) return;

        try
        {
            if (_attrTier != TierNone) _vibrator.Call("vibrate", effect, _attributes);
            else                       _vibrator.Call("vibrate", effect);
        }
        catch (System.Exception e)
        {
            // Building an attributes object proves the CLASS exists, not that this Vibrator exposes
            // a matching vibrate() overload. Step down a tier and retry rather than abandoning
            // classification, which would hand us back to whatever default scaling the OEM applies.
            if (_attrTier != TierNone && BuildAttributes(_attrTier - 1))
            {
                Status = "fell back to " + TierName(_attrTier);
                Report();
                try { _vibrator.Call("vibrate", effect, _attributes); }
                catch { BuildAttributes(TierNone); try { _vibrator.Call("vibrate", effect); } catch { _ready = false; } }
            }
            else if (_attrTier != TierNone)
            {
                BuildAttributes(TierNone);
                Status = "no attributes supported, using plain vibrate";
                Report();
                try { _vibrator.Call("vibrate", effect); } catch { _ready = false; }
            }
            else
            {
                _ready = false;
                Status = "vibrate failed: " + e.Message;
                Report();
            }
        }

    }

    /// <summary>
    /// Crude whole-device buzz, used only when the Vibrator service could not be obtained at all.
    /// Doubles as the reason Unity's manifest scanner adds VIBRATE — it recognises this API, but
    /// cannot see our JNI calls. AndroidManifestPostProcessor is the actual guarantee; this is the
    /// belt to its braces.
    /// </summary>
    private static void FallbackVibrate(Kind kind)
    {
        // Handheld.Vibrate is a fixed ~500ms buzz, far too blunt for taps — only the big beats.
        if (kind == Kind.Heavy || kind == Kind.Success || kind == Kind.Wrong) Handheld.Vibrate();
    }

    // ======================================================================================
    //  iOS
    // ======================================================================================
#elif UNITY_IOS && !UNITY_EDITOR

    // MarshalAs(I1) is required, not decorative: C#'s default marshalling for bool is the 4-byte
    // Win32 BOOL, while the C++ side returns a 1-byte bool. Without this the runtime reads three
    // bytes of whatever happened to be adjacent, so _ready would be decided by stack garbage.
    [DllImport("__Internal")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool _EchoHapticsInit();
    [DllImport("__Internal")] private static extern void _EchoHapticsImpact(int style);
    [DllImport("__Internal")] private static extern void _EchoHapticsNotification(int type);
    [DllImport("__Internal")] private static extern void _EchoHapticsSelection();

    private static bool _ready;
    private static bool PlatformReady { get { return _ready; } }

    // iOS schedules its own haptic events and UIFeedbackGenerator is built to be called repeatedly
    // (a picker wheel ticks on every notch), so this spacing exists only to stop a sonar sweep from
    // smearing into one continuous rattle — not to protect the motor as on Android.
    private const float RestMs = 20f;

    private static long DurationMs(Kind kind)
    {
        switch (kind)
        {
            case Kind.Selection: return 30;
            case Kind.Light:     return 40;
            case Kind.Medium:    return 55;
            case Kind.Heavy:     return 75;
            case Kind.Success:   return 350;   // the system success pattern is a multi-tap
            default:             return 300;   // ...as is the error pattern
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        try
        {
            _ready = _EchoHapticsInit();
            Status = _ready
                ? "ios ok (UIFeedbackGenerator)"
                : "ios unavailable (requires iOS 10+)";
        }
        catch (System.Exception e)
        {
            _ready = false;
            Status = "ios init failed: " + e.Message;
        }
        Report();
    }

    private static void PlatformPlay(Kind kind)
    {
        // Map each intent onto the generator Apple intends for it, so the game feels like the rest
        // of the OS rather than inventing its own vocabulary.
        switch (kind)
        {
            case Kind.Selection: _EchoHapticsSelection();      break;  // selectionChanged
            case Kind.Light:     _EchoHapticsImpact(0);        break;  // impact .light
            case Kind.Medium:    _EchoHapticsImpact(1);        break;  // impact .medium
            case Kind.Heavy:     _EchoHapticsImpact(2);        break;  // impact .heavy
            case Kind.Success:   _EchoHapticsNotification(0);  break;  // notification .success
            default:             _EchoHapticsNotification(2);  break;  // notification .error
        }
    }

    // ======================================================================================
    //  EDITOR / EVERYTHING ELSE
    // ======================================================================================
#else

    private const float RestMs = 0f;
    private static bool PlatformReady { get { return false; } }
    private static long DurationMs(Kind kind) { return 0; }
    private static void PlatformPlay(Kind kind) { }

#endif
}
