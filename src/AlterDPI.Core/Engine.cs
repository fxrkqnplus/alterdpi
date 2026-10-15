using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlterDPI.Core;

public interface INetworkEngine : IDisposable { string Identity { get; } bool Running { get; } Task Apply(Profile profile, CancellationToken token); void Stop(); }
public sealed class WindowsEngine : INetworkEngine
{
    private Process? process;
    private IntPtr job;
    private string? current;
    private static readonly Dictionary<string, string> Expected = new() { ["network-engine.exe"] = "8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD", ["WinDivert.dll"] = "6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E", ["WinDivert64.sys"] = "E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50" };
    public string Identity => Expected["network-engine.exe"] + "/v1";
    public bool Running => process is { HasExited: false };
    public async Task Apply(Profile profile, CancellationToken token)
    {
        if (profile.Id == "direct") { Stop(); return; }
        if (current == profile.Id && Running) return;
        foreach (var file in Expected) { var p = Path.Combine(AppContext.BaseDirectory, "engine", file.Key); if (!File.Exists(p) || Trust.HashFile(p) != file.Value) throw new InvalidDataException("INTEGRITY"); }
        foreach (var other in Process.GetProcessesByName("network-engine").Concat(Process.GetProcessesByName("goodbyedpi"))) using (other) { if (process == null || other.Id != process.Id) throw new InvalidOperationException("ENGINE_BUSY"); }
        Stop(); var path = Path.Combine(AppContext.BaseDirectory, "engine", "network-engine.exe"); var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(path)! }; foreach (var a in profile.Args) start.ArgumentList.Add(a);
        process = Process.Start(start) ?? throw new InvalidOperationException("ENGINE_START"); job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) { Stop(); throw new InvalidOperationException("ENGINE_START"); }
        var info = new JobInfo { Basic = new BasicLimits { Flags = 0x2000 } }; var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<JobInfo>());
        try { Marshal.StructureToPtr(info, pointer, false); if (!SetInformationJobObject(job, 9, pointer, (uint)Marshal.SizeOf<JobInfo>()) || !AssignProcessToJobObject(job, process.Handle)) { Stop(); throw new InvalidOperationException("ENGINE_START"); } } finally { Marshal.FreeHGlobal(pointer); }
        _ = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync(); await Task.Delay(350, token); if (process.HasExited) { Stop(); throw new InvalidOperationException("ENGINE_START"); }
        current = profile.Id;
    }
    public void Stop() { if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; } if (process != null) { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } process.Dispose(); process = null; } current = null; }
    public void Dispose() => Stop();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long Time1, Time2; public uint Flags; public UIntPtr Min, Max; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong R1, R2, R3, R4, R5, R6; }
    [StructLayout(LayoutKind.Sequential)] private struct JobInfo { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int info, IntPtr value, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
