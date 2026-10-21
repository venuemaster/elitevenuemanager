using System;
using System.IO;
using System.Runtime.InteropServices;

namespace EVM;

/// <summary>Plays a "ding-dong" doorbell through Windows (only you hear it). The chime is generated
/// in code and saved as a .wav in the plugin's config folder, so nothing extra has to be downloaded.</summary>
internal static class Doorbell
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndFilename = 0x00020000;

    private static string? cachedPath;
    private static int cachedVolume = -1;
    private static int cachedStyle = -1;

    /// <summary>style 0 = classic ding-dong, 1 = soft chime, 2 = three-note bell.</summary>
    public static void Ring(int volumePercent, int style, string customPath)
    {
        try
        {
            string path;
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath.Trim().Trim('"')))
                path = customPath.Trim().Trim('"');
            else
                path = BuiltIn(Math.Clamp(volumePercent, 1, 100), Math.Clamp(style, 0, 2));
            PlaySound(path, IntPtr.Zero, SndAsync | SndNoDefault | SndFilename);
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Doorbell could not play"); }
    }

    private static string BuiltIn(int volume, int style)
    {
        if (cachedPath != null && cachedVolume == volume && cachedStyle == style && File.Exists(cachedPath)) return cachedPath;
        var dir = Plugin.PluginInterface.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"doorbell_{style}_{volume}.wav");
        if (!File.Exists(path)) File.WriteAllBytes(path, Make(volume / 100.0, style));
        cachedPath = path; cachedVolume = volume; cachedStyle = style;
        return path;
    }

    private static byte[] Make(double volume, int style)
    {
        const int rate = 22050;
        // (frequency, start seconds)
        (double f, double t)[] notes = style switch
        {
            1 => new[] { (783.99, 0.0), (1046.5, 0.18) },                 // soft chime G5 -> C6
            2 => new[] { (659.25, 0.0), (523.25, 0.28), (392.0, 0.56) },  // E5 C5 G4
            _ => new[] { (659.25, 0.0), (523.25, 0.45) },                 // classic ding-dong E5 -> C5
        };
        var decay = style == 1 ? 3.5 : 2.2;
        var length = notes[^1].t + 1.6;
        var n = (int)(rate * length);
        var samples = new double[n];
        foreach (var (f, t0) in notes)
        {
            var start = (int)(t0 * rate);
            for (var i = start; i < n; i++)
            {
                var t = (i - start) / (double)rate;
                var env = Math.Exp(-decay * t) * Math.Min(1.0, t / 0.005);   // quick attack, bell decay
                var s = Math.Sin(2 * Math.PI * f * t)
                        + 0.35 * Math.Sin(2 * Math.PI * f * 2.0 * t) * Math.Exp(-2 * t)
                        + 0.15 * Math.Sin(2 * Math.PI * f * 3.01 * t) * Math.Exp(-4 * t);
                samples[i] += s * env;
            }
        }
        var peak = 0.0;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        var gain = peak > 0 ? 0.9 * volume / peak : 0;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(n * 2);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * gain * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
