using UnityEngine;

/// <summary>
/// All sound is synthesized at runtime with AudioClip.Create — no audio files.
/// Ping = soft sine sweep down (with random pitch), wall tick = tiny blip (pitch rises
/// as the ring sweeps), win = rising arpeggio, plus streak-up / star / wrong cues.
/// </summary>
public class ProceduralAudio : MonoBehaviour
{
    private const int SampleRate = 44100;

    private AudioSource _main;   // ping / win / streak / star / wrong
    private AudioSource _tick;   // ticks (own source so they overlap the ping)
    private AudioSource _move;   // looping movement whoosh (volume rides speed)

    private AudioClip _ping, _tickClip, _win, _streak, _star, _wrong, _moveClip, _rewind, _lose, _timeWarn, _countTick;
    private float _moveTargetVol, _moveLevel;

    public void Init()
    {
        _main = gameObject.AddComponent<AudioSource>();
        _main.playOnAwake = false; _main.spatialBlend = 0f;

        _tick = gameObject.AddComponent<AudioSource>();
        _tick.playOnAwake = false; _tick.spatialBlend = 0f; _tick.volume = 0.28f;

        _move = gameObject.AddComponent<AudioSource>();
        _move.playOnAwake = false; _move.spatialBlend = 0f; _move.loop = true; _move.volume = 0f;

        _ping     = BuildSweep(880f, 220f, 0.35f, 0.5f);
        _tickClip = BuildSweep(1400f, 1100f, 0.035f, 0.6f);
        _win      = BuildArpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 0.5f);
        _streak   = BuildArpeggio(new[] { 659.25f, 987.77f }, 0.09f, 0.45f);
        _star     = BuildSweep(1200f, 1600f, 0.09f, 0.45f);
        _wrong    = BuildSweep(220f, 150f, 0.18f, 0.5f);
        _rewind   = BuildRewind();
        _lose     = BuildArpeggio(new[] { 392f, 311f, 233f }, 0.18f, 0.5f); // descending = "aww, failed"
        _timeWarn = BuildSweep(620f, 620f, 0.18f, 0.45f);                   // steady heads-up beep
        _countTick= BuildSweep(1000f, 1000f, 0.05f, 0.5f);                  // short countdown tick
        _moveClip = BuildMoveLoop();

        _move.clip = _moveClip;
        _move.Play(); // runs continuously at volume 0; SetMoveLevel opens it up
    }

    /// <summary>Time-rewind shimmer for the decoy penalty (kept gentle).</summary>
    public void PlayRewind() { if (_main) { _main.pitch = 1f; _main.PlayOneShot(_rewind, 0.6f); } }

    public void PlayLose() { if (_main) { _main.pitch = 1f; _main.PlayOneShot(_lose, 0.8f); } }
    public void PlayTimeWarning() { if (_main) { _main.pitch = 1f; _main.PlayOneShot(_timeWarn, 0.5f); } }
    /// <summary>Countdown tick; pitch rises as the last seconds run out for urgency.</summary>
    public void PlayCountdownTick(int secsLeft)
    {
        if (_main == null) return;
        _main.pitch = 1f + (5 - Mathf.Clamp(secsLeft, 1, 5)) * 0.12f;
        _main.PlayOneShot(_countTick, 0.55f);
    }

    /// <summary>0 = still (silent), 1 = full speed. Called by the player each frame.</summary>
    public void SetMoveLevel(float level)
    {
        _moveLevel = Mathf.Clamp01(level);
        _moveTargetVol = _moveLevel * GameConfig.MoveAudioMaxVol;
    }

    private void Update()
    {
        if (_move == null) return;
        // Smoothly open/close the whoosh and bend its pitch up with speed.
        _move.volume = Mathf.MoveTowards(_move.volume, _moveTargetVol, Time.unscaledDeltaTime * 0.8f);
        float targetPitch = 0.8f + 0.5f * _moveLevel;
        _move.pitch = Mathf.Lerp(_move.pitch, targetPitch, Time.unscaledDeltaTime * 6f);
    }

    public void PlayPing(float pitch = 1f) { if (_main) { _main.pitch = pitch; _main.PlayOneShot(_ping); } }
    public void PlayTick(float pitch = 1f) { if (_tick) { _tick.pitch = Mathf.Clamp(pitch, 0.6f, 2.5f); _tick.PlayOneShot(_tickClip); } }
    public void PlayWin()    { if (_main) { _main.pitch = 1f; _main.PlayOneShot(_win); } }
    public void PlayStreak(int streakLevel) { if (_main) { _main.pitch = Mathf.Clamp(1f + streakLevel * 0.08f, 1f, 2f); _main.PlayOneShot(_streak); } }
    public void PlayStar(int index) { if (_main) { _main.pitch = 1f + index * 0.18f; _main.PlayOneShot(_star); } }
    public void PlayWrong()  { if (_main) { _main.pitch = 1f; _main.PlayOneShot(_wrong); } }

    private AudioClip BuildSweep(float startHz, float endHz, float duration, float volume)
    {
        int samples = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
        var data = new float[samples];
        double phase = 0.0;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float freq = Mathf.Lerp(startHz, endHz, t);
            phase += 2.0 * Mathf.PI * freq / SampleRate;
            float attack = Mathf.Clamp01(t / 0.05f);
            float decay = Mathf.Exp(-3f * t);
            data[i] = Mathf.Sin((float)phase) * attack * decay * volume;
        }
        var clip = AudioClip.Create("sweep", samples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip BuildArpeggio(float[] notesHz, float noteDuration, float volume)
    {
        int noteSamples = Mathf.RoundToInt(SampleRate * noteDuration);
        int total = noteSamples * notesHz.Length;
        var data = new float[total];
        for (int n = 0; n < notesHz.Length; n++)
        {
            double phase = 0.0;
            float freq = notesHz[n];
            for (int i = 0; i < noteSamples; i++)
            {
                float t = (float)i / noteSamples;
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float attack = Mathf.Clamp01(t / 0.04f);
                float decay = Mathf.Exp(-2.5f * t);
                data[n * noteSamples + i] = Mathf.Sin((float)phase) * attack * decay * volume;
            }
        }
        var clip = AudioClip.Create("arp", total, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>
    /// Seamless 1-second SPACE-THEME movement drone: a warm synth pad from detuned sine
    /// oscillators + gentle harmonics, with a slow tremolo. All frequencies are whole Hz so
    /// each completes an exact number of cycles in 1 second -> the loop is perfectly seamless
    /// (no noise, no cloth-rustle, no click). Pitch bends up with speed at playback time.
    /// </summary>
    private AudioClip BuildMoveLoop()
    {
        int n = SampleRate;
        var data = new float[n];
        const float TAU = 2f * Mathf.PI;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            // Two slightly detuned low sines beat together for a living "engine" body,
            // plus a soft octave and a fifth for a sci-fi shimmer.
            float body =
                  Mathf.Sin(TAU * 100f * t) * 0.5f
                + Mathf.Sin(TAU * 101f * t) * 0.5f   // 1 Hz beat
                + Mathf.Sin(TAU * 200f * t) * 0.14f  // octave
                + Mathf.Sin(TAU * 150f * t) * 0.10f; // fifth
            float tremolo = 0.82f + 0.18f * Mathf.Sin(TAU * 3f * t); // slow amplitude LFO
            data[i] = body * tremolo * 0.5f;
        }

        var clip = AudioClip.Create("move", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>
    /// Soft, dreamy time-rewind shimmer: two pure sines gliding downward together with a
    /// gentle sine tremolo (no square-wave stutter) and a smooth bell envelope. Quiet and
    /// non-harsh, so it reads as "rewinding" without being abrasive.
    /// </summary>
    private AudioClip BuildRewind()
    {
        float duration = 0.9f;
        int samples = Mathf.RoundToInt(SampleRate * duration);
        var data = new float[samples];
        double p1 = 0.0, p2 = 0.0;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float freq = Mathf.Lerp(760f, 260f, Easing.OutQuad(t));      // smooth glide down
            p1 += 2.0 * Mathf.PI * freq / SampleRate;
            p2 += 2.0 * Mathf.PI * (freq * 1.5f) / SampleRate;           // a soft fifth on top
            float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 9f * t); // gentle wobble
            float env = Mathf.Sin(Mathf.PI * t);                         // smooth fade in AND out (bell)
            float s = Mathf.Sin((float)p1) + 0.35f * Mathf.Sin((float)p2);
            data[i] = s * tremolo * env * 0.16f;                         // low amplitude
        }
        var clip = AudioClip.Create("rewind", samples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
