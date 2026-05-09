using System.Media;

namespace GroqVoice;

/// <summary>
/// Two short percussive click sounds, synthesized once at module load:
/// a high "ping" for record-start and a lower "pong" for record-stop.
/// Designed to be unobtrusive but distinctly different — like a ping-pong ball.
/// </summary>
public static class Click
{
    private static readonly SoundPlayer _high = Build(freq: 1480, decayMs: 55, gain: 0.22f);
    private static readonly SoundPlayer _low  = Build(freq:  580, decayMs: 75, gain: 0.22f);

    public static void High() { try { _high.Play(); } catch { } }
    public static void Low()  { try { _low.Play();  } catch { } }

    private static SoundPlayer Build(int freq, int decayMs, float gain)
    {
        const int sr = 22050;
        int n = sr * decayMs / 1000;
        // exponential decay rate so the envelope is ~5% at decayMs
        float rate = 3000f / decayMs;
        // 2 ms cosine attack to avoid an initial click/pop
        int attackN = sr * 2 / 1000;

        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float env = MathF.Exp(-t * rate);
            float attack = i < attackN ? 0.5f * (1f - MathF.Cos(MathF.PI * i / attackN)) : 1f;
            // sine + faint third-harmonic for a slightly woody "tok" character
            float wave = MathF.Sin(2 * MathF.PI * freq * t)
                       + 0.20f * MathF.Sin(2 * MathF.PI * freq * 3 * t);
            float s = wave * env * attack * gain;
            if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
            pcm[i] = (short)(s * 32767);
        }

        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + n * 2);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);     // PCM
            bw.Write((short)1);     // mono
            bw.Write(sr);
            bw.Write(sr * 2);       // bytes/sec
            bw.Write((short)2);     // block align
            bw.Write((short)16);    // bits/sample
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(n * 2);
            for (int i = 0; i < n; i++) bw.Write(pcm[i]);
        }
        ms.Position = 0;
        var p = new SoundPlayer(ms);
        p.Load();
        return p;
    }
}
