using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Angband.Audio;

/// <summary>
/// Sets OpenAL Soft's mixing period. Its context attributes don't reach the PipeWire backend, but
/// its configuration does: OpenAL Soft reads the file named by <c>ALSOFT_CONF</c> after the
/// system's and the user's own <c>alsoft.conf</c>, so a file holding just the period overrides
/// that and nothing else. A player's own <c>ALSOFT_CONF</c> is left alone.
/// </summary>
public static class OpenAlConfig
{
    public const string Variable = "ALSOFT_CONF";

    public static void UsePeriod(int periodFrames)
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
            var path = Path.Combine(Path.GetTempPath(), $"avaband-openal-{periodFrames}.conf");
            File.WriteAllText(path, $"# Written by AVABand: OpenAL Soft's mixing period.\n[general]\nperiod_size = {periodFrames}\nperiods = 3\n");
            SetNativeEnvironment(Variable, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.WriteLine($"Could not set the audio period: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets a variable native code can see: .NET keeps its own copy of the environment on Unix,
    /// which OpenAL Soft's getenv never reads, so this goes through libc's setenv there.
    /// </summary>
    private static unsafe void SetNativeEnvironment(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (OperatingSystem.IsWindows()) return;
        string[] libraries = OperatingSystem.IsMacOS() ? ["/usr/lib/libSystem.B.dylib"] : ["libc.so.6", "libc"];
        foreach (var library in libraries)
        {
            if (!NativeLibrary.TryLoad(library, out var handle)) continue;
            if (!NativeLibrary.TryGetExport(handle, "setenv", out var setenv)) continue;
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');
            var valueBytes = System.Text.Encoding.UTF8.GetBytes(value + '\0');
            fixed (byte* n = nameBytes, v = valueBytes)
                ((delegate* unmanaged<byte*, byte*, int, int>)setenv)(n, v, 1);
            return;
        }
    }
}
