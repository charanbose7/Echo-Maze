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

    private AudioClip _ping, _tickClip, _win, _streak, _star, _wrong;

    public void Init()
    {
        _main = gameObject.AddComponent<AudioSource>();
        _main.playOnAwake = false; _main.spatialBlend = 0f;

        _tick = gameObject.AddComponent<AudioSource>();
        _tick.playOnAwake = false; _tick.spatialBlend = 0f; _tick.volume = 0.28f;

        _ping     = BuildSweep(880f, 220f, 0.35f, 0.5f);
        _tickClip = BuildSweep(1400f, 1100f, 0.035f, 0.6f);
        _win      = BuildArpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 0.5f);
        _streak   = BuildArpeggio(new[] { 659.25f, 987.77f }, 0.09f, 0.45f);
        _star     = BuildSweep(1200f, 1600f, 0.09f, 0.45f);
        _wrong    = BuildSweep(220f, 150f, 0.18f, 0.5f);
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
}
