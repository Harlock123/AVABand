using System.Text;
using Angband.Audio;

namespace Angband.Tests;

/// <summary>
/// Sound that can't start says why, and the Windows OpenAL we ship needs no Visual C++ runtime.
/// </summary>
public class AudioFailureTests
{
    [Fact]
    public void AMissingVcRuntime_IsNamed_WithTheRedistributable()
    {
        var text = AudioFailure.Describe(new DllNotFoundException("Unable to load DLL 'soft_oal.dll'"), ["VCRUNTIME140_1.dll"]);
        Assert.Contains("VCRUNTIME140_1.dll is missing", text);
        Assert.Contains("Visual C++ Redistributable", text);

        var two = AudioFailure.Describe(new DllNotFoundException("x"), ["VCRUNTIME140.dll", "MSVCP140.dll"]);
        Assert.Contains("VCRUNTIME140.dll, MSVCP140.dll are missing", two);
    }

    [Fact]
    public void ALibraryThatWontLoad_SaysSo_WithTheSystemsReason()
    {
        var ex = new TypeInitializationException("Silk", new DllNotFoundException("Unable to load DLL 'soft_oal.dll': The specified module could not be found."));
        var text = AudioFailure.Describe(ex, []);
        Assert.Contains("OpenAL library", text);
        Assert.Contains("The specified module could not be found.", text);
        Assert.DoesNotContain("Visual C++", text);

        Assert.Equal("Sound couldn't start: boom", AudioFailure.Describe(new InvalidOperationException("boom"), []));
    }

    [Fact]
    public void TheSilentEngine_CarriesItsReason()
    {
        Assert.Null(new NullAudioEngine().Reason);
        Assert.Equal(AudioFailure.NoDevice, new NullAudioEngine(AudioFailure.NoDevice).Reason);
        if (!OperatingSystem.IsWindows()) Assert.Empty(AudioFailure.MissingVcRuntimeHere());
    }

    [Fact]
    public void OpeningOrFallingBack_AlwaysExplainsSilence()
    {
        using var engine = OpenAlAudioEngine.CreateOrSilent();
        if (!engine.IsAvailable) Assert.StartsWith("Sound couldn't start", Assert.IsType<NullAudioEngine>(engine).Reason);
    }

    /// <summary>
    /// The bundled Windows OpenAL Soft (x64, x86) must import nothing from the Visual C++ runtime —
    /// that is the whole point of shipping it in place of Silk.NET's.
    /// </summary>
    [Theory]
    [InlineData("win-x64", 0x8664)]
    [InlineData("win-x86", 0x14c)]
    public void TheBundledWindowsOpenAl_NeedsNoVcRuntime(string rid, int machine)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "AVABand.sln"))) dir = Path.GetDirectoryName(dir)!;
        var (arch, imports) = PeImports(File.ReadAllBytes(Path.Combine(dir, "src", "Angband.Audio", "native", rid, "soft_oal.dll")));
        Assert.Equal(machine, arch);
        Assert.Contains("KERNEL32.dll", imports, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(imports, i => i.StartsWith("VCRUNTIME", StringComparison.OrdinalIgnoreCase)
                                            || i.StartsWith("MSVCP", StringComparison.OrdinalIgnoreCase)
                                            || i.StartsWith("api-ms-win-crt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A PE file's machine type and the DLLs its import table names.</summary>
    private static (int Machine, List<string> Imports) PeImports(byte[] d)
    {
        int U16(int o) => BitConverter.ToUInt16(d, o);
        int I32(int o) => BitConverter.ToInt32(d, o);
        var pe = I32(0x3c);
        int machine = U16(pe + 4), sections = U16(pe + 6), optSize = U16(pe + 20), opt = pe + 24;
        var dataDirs = opt + (U16(opt) == 0x20b ? 112 : 96);
        var importRva = I32(dataDirs + 8);
        int Offset(int rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = opt + optSize + i * 40;
                int va = I32(s + 12), size = Math.Max(I32(s + 8), 1), raw = I32(s + 20);
                if (rva >= va && rva < va + size) return rva - va + raw;
            }
            throw new InvalidDataException($"RVA {rva:x} is in no section");
        }
        var names = new List<string>();
        for (var e = Offset(importRva); I32(e + 12) != 0; e += 20)
        {
            var n = Offset(I32(e + 12));
            names.Add(Encoding.ASCII.GetString(d, n, Array.IndexOf(d, (byte)0, n) - n));
        }
        return (machine, names);
    }
}
