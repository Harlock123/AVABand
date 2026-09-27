namespace Angband.Audio;

/// <summary>
/// The "Audio buffer" setting: how many sample frames OpenAL mixes at a time. Small is OpenAL's
/// own default (lowest latency); bigger periods add a few tens of milliseconds but keep a jittery
/// output — a virtual machine's emulated sound card above all — from breaking up.
/// </summary>
public static class AudioBuffer
{
    public const string Automatic = "auto", Small = "small", Medium = "medium", Large = "large";

    public static readonly IReadOnlyList<(string Id, string Label)> Choices =
    [
        (Automatic, "Automatic (large in a virtual machine)"),
        (Small, "Small — lowest latency"),
        (Medium, "Medium (1024 frames, about 21 ms)"),
        (Large, "Large (2048 frames, about 43 ms) — smooth in virtual machines"),
    ];

    /// <summary>The period for a choice (0 = OpenAL's default).</summary>
    public static int PeriodFrames(string? choice, bool inVirtualMachine) => choice switch
    {
        Small => 0,
        Medium => 1024,
        Large => 2048,
        _ => inVirtualMachine ? 2048 : 0,
    };
}

/// <summary>
/// Whether we are running in a virtual machine (as systemd-detect-virt would tell): the firmware's
/// vendor names on PCs, QEMU's machine and firmware devices on ARM, a hypervisor node or CPU flag.
/// </summary>
public static class VirtualMachine
{
    private static readonly string[] VendorMarks =
        ["QEMU", "KVM", "VMware", "VirtualBox", "innotek", "Parallels", "Xen", "Bochs", "Apple Virtualization", "Virtual Machine", "BHYVE", "UTM"];

    public static bool Detect()
    {
        try
        {
            if (!OperatingSystem.IsLinux()) return false;
            foreach (var f in new[] { "/sys/class/dmi/id/sys_vendor", "/sys/class/dmi/id/product_name", "/sys/class/dmi/id/board_vendor" })
                if (Read(f) is { } text && VendorMarks.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))) return true;
            if (Read("/proc/device-tree/compatible") is { } machine && machine.Contains("dummy-virt", StringComparison.Ordinal)) return true;
            if (Directory.Exists("/proc/device-tree/hypervisor") || File.Exists("/sys/hypervisor/type")) return true;
            if (Directory.Exists("/sys/firmware/qemu_fw_cfg")
                || Directory.Exists("/sys/bus/platform/devices") && Directory.EnumerateFileSystemEntries("/sys/bus/platform/devices").Any(d => d.Contains("fw-cfg", StringComparison.Ordinal) || d.Contains("fw_cfg", StringComparison.Ordinal)))
                return true;
            return Read("/proc/cpuinfo") is { } cpu && cpu.Contains(" hypervisor", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
