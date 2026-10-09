using System.IO;
using System.Runtime.InteropServices;

namespace Compositor.Document;

/// The size and memory ceilings a document is held to, in one place (upstream
/// DocumentLimits.swift). The pixel budget scales with the machine's physical memory,
/// clamped between one surface and 800 MP.
public static class DocumentLimits
{
    public const int MaxSide = 30_000;
    public const double MaxSideExtent = MaxSide;
    public const long MaxSurfacePixels = 200_000_000;
    public const double MaxSurfaceExtent = MaxSurfacePixels;

    public static long DocumentPixelBudget { get; } =
        Math.Min(800_000_000L, Math.Max(MaxSurfacePixels, PhysicalMemoryBytes() / 16));

    public static int MaxSurfaceMegapixels => (int)(MaxSurfacePixels / 1_000_000);
    public static int DocumentBudgetMegapixels => (int)(DocumentPixelBudget / 1_000_000);

    private static long PhysicalMemoryBytes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : MaxSurfacePixels * 4;
        }
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var name = System.Text.Encoding.ASCII.GetBytes("hw.memsize\0");
                var len = (nuint)sizeof(long);
                unsafe
                {
                    if (sysctlbyname(name, out var value, ref len, null, nuint.Zero) == 0)
                        return value;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var text = File.ReadAllText("/proc/meminfo");
                var match = System.Text.RegularExpressions.Regex.Match(text, @"MemTotal:\s+(\d+) kB");
                if (match.Success) return long.Parse(match.Groups[1].Value) * 1024;
            }
        }
        catch { /* fall through to the floor */ }
        return MaxSurfacePixels * 4; // unseen machine: 800 MP, the cap.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("libc")]
    private static unsafe extern int sysctlbyname(byte[] name, out long value, ref nuint oldLen, void* _, nuint zero);
}
