using UnityEngine;

/// <summary>
/// Cross-platform haptics with graceful fallback.
/// - Android: real amplitude control via the system Vibrator (API 26+ VibrationEffect),
///   so light / medium / heavy actually feel different. Guarded behind reflection-free
///   AndroidJavaObject calls compiled only for Android.
/// - iOS / others: falls back to Handheld.Vibrate for the strong cues only (a full buzz
///   on every ping would be obnoxious), and stays silent for the light ones.
/// - Editor: no-op.
///
/// Throttling is the caller's job (e.g. SonarManager throttles reveal haptics).
/// </summary>
public static class Haptics
{
    public static bool Enabled = true;

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject _vibrator;
    private static bool _api26;
    private static bool _init;

    private static void EnsureInit()
    {
        if (_init) return;
        _init = true;
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                _api26 = version.GetStatic<int>("SDK_INT") >= 26;
            }
        }
        catch { _vibrator = null; }
    }

    private static void OneShot(long ms, int amplitude)
    {
        EnsureInit();
        if (_vibrator == null) return;
        try
        {
            if (_api26)
            {
                using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude))
                {
                    _vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                _vibrator.Call("vibrate", ms);
            }
        }
        catch { }
    }

    private static void Pattern(long[] timings, int[] amplitudes)
    {
        EnsureInit();
        if (_vibrator == null) return;
        try
        {
            if (_api26)
            {
                using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var effect = effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1))
                {
                    _vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                _vibrator.Call("vibrate", timings, -1);
            }
        }
        catch { }
    }
#endif

    public static void Light()
    {
        if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        OneShot(12, 45);
#endif
        // iOS/others: intentionally silent — too frequent to buzz the whole device.
    }

    public static void Medium()
    {
        if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        OneShot(22, 120);
#endif
    }

    public static void Heavy()
    {
        if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        OneShot(38, 255);
#elif (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        Handheld.Vibrate();
#endif
    }

    /// <summary>Celebratory rat-a-tat for a level clear.</summary>
    public static void Success()
    {
        if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        Pattern(new long[] { 0, 30, 40, 30, 40, 70 }, new int[] { 0, 130, 0, 190, 0, 255 });
#elif UNITY_IOS && !UNITY_EDITOR
        Handheld.Vibrate();
#endif
    }

    /// <summary>Short "nope" for touching a decoy.</summary>
    public static void Wrong()
    {
        if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        OneShot(60, 90);
#endif
    }
}
