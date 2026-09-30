using System.Runtime.InteropServices;

namespace Angband.Audio;

/// <summary>
/// Why sound could not start, in words for the Sound tab. The usual cause on Windows is a missing
/// Microsoft Visual C++ runtime: the x64 and x86 builds ship an OpenAL Soft that does not need it
/// (src/Angband.Audio/native), but ARM64 still uses Silk.NET's, which does.
/// </summary>
public static class AudioFailure
{
    /// <summary>The Visual C++ runtime DLLs Silk.NET's OpenAL Soft for Windows imports.</summary>
    public static readonly IReadOnlyList<string> VcRuntimeDlls = ["VCRUNTIME140.dll", "VCRUNTIME140_1.dll", "MSVCP140.dll"];

    public const string NoDevice =
        "Sound couldn't start: OpenAL found no audio output device. Check that a speaker or headphones " +
        "are connected and enabled in the system's sound settings, then restart the game.";

    public const string NoContext =
        "Sound couldn't start: the audio device opened, but OpenAL couldn't start playing on it. " +
        "Another program may be holding it exclusively; close it and restart the game.";

    /// <summary>
    /// The message for a failure to start OpenAL. <paramref name="missingVcRuntime"/> lists the
    /// Visual C++ runtime DLLs that could not be loaded (empty when none are missing, or when the
    /// OpenAL library in use doesn't need them).
    /// </summary>
    public static string Describe(Exception ex, IReadOnlyList<string> missingVcRuntime)
    {
        if (missingVcRuntime.Count > 0)
            return $"Sound couldn't start: OpenAL needs the Microsoft Visual C++ runtime ({string.Join(", ", missingVcRuntime)} " +
                   $"{(missingVcRuntime.Count == 1 ? "is" : "are")} missing). Install the Visual C++ Redistributable " +
                   "(https://aka.ms/vs/17/release/vc_redist.arm64.exe) and restart the game.";
        var inner = ex;
        while (inner is TypeInitializationException { InnerException: { } next }) inner = next;
        return inner is DllNotFoundException or FileNotFoundException or BadImageFormatException
            ? $"Sound couldn't start: the OpenAL library (soft_oal.dll / libopenal) couldn't be loaded. {inner.Message}"
            : $"Sound couldn't start: {inner.Message}";
    }

    /// <summary>
    /// Which Visual C++ runtime DLLs this machine lacks — asked only on Windows ARM64, the one
    /// Windows build whose OpenAL needs them.
    /// </summary>
    public static IReadOnlyList<string> MissingVcRuntimeHere()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return [];
        return VcRuntimeDlls.Where(dll =>
        {
            if (!NativeLibrary.TryLoad(dll, out var handle)) return true;
            NativeLibrary.Free(handle);
            return false;
        }).ToList();
    }
}
