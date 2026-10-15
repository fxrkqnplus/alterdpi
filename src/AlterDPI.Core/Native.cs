using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AlterDPI.Core;

public sealed class NativeCommandException(string command, int exitCode, string code) : InvalidOperationException(code)
{
    public string Command { get; } = Path.GetFileName(command);
    public int ExitCode { get; } = exitCode;
}

public static class Native
{
    public static int HopLimit => IPGlobalProperties.GetIPGlobalProperties().GetIPv4GlobalStatistics().DefaultTtl;
    public static async Task<string> Run(string file, IEnumerable<string> arguments, CancellationToken token, int timeout = 15000)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 }; foreach (var a in arguments) start.ArgumentList.Add(a);
        start.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules");
        foreach (var key in start.Environment.Keys.Where(x => x.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || x.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || x.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PROCESS"); using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(timeout);
        try { var output = process.StandardOutput.ReadToEndAsync(limit.Token); var error = process.StandardError.ReadToEndAsync(limit.Token); await process.WaitForExitAsync(limit.Token); var result = await output; var errorText = await error; if (result.Length > 1024 * 1024 || process.ExitCode != 0) throw new NativeCommandException(file, process.ExitCode, process.ExitCode switch { 10 => "NO_NETWORK", 12 => "POLICY_CONFLICT", _ => errorText.Contains("LEGACY_DNS", StringComparison.Ordinal) ? "LEGACY_DNS" : "NATIVE" }); return result.Trim(); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
    }
    public static Task<string> Policy(string action, CancellationToken token, string? data = null)
    {
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var args = new List<string> { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", Path.Combine(AppContext.BaseDirectory, "network.ps1"), "-Action", action };
        if (data != null) { if (data.Length > 256 * 1024) throw new InvalidDataException("POLICY"); args.Add("-Data"); args.Add(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data))); }
        return Run(shell, args, token, 20000);
    }
    public static async Task<NetworkInfo> Network(Store store, CancellationToken token)
    {
        var json = await Policy("Read", token); var n = JsonSerializer.Deserialize<NetworkInfo>(json, Product.Json) ?? throw new InvalidOperationException("NO_NETWORK");
        if (n.Adapter.Length == 0 || n.Dns.Length == 0) throw new InvalidOperationException("NO_NETWORK"); return n with { Key = store.Fingerprint(new[] { n.Key, n.HopLimit.ToString(), n.Mtu.ToString(), n.Family.ToString(), n.HasIpv6.ToString(), n.Tunnel.ToString(), n.Managed.ToString(), n.Proxy.ToString(), string.Join(',', n.Dns.Order(StringComparer.Ordinal)), string.Join(',', n.Suffixes.Order(StringComparer.Ordinal)) }) };
    }
    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)] public static extern bool DnsFlushResolverCacheEntry_W(string entry);
}
public sealed class DnsPolicy(Store store)
{
    private string[] owned = [];
    public async Task Recover(CancellationToken token)
    {
        await Native.Policy("Remove", token); store.Write("dns-owned.json", Array.Empty<string>()); owned = [];
    }
    public async Task Apply(CancellationToken token)
    {
        if (owned.Length > 0) return;
        store.Write("dns-owned.json", new[] { "pending" });
        try { var json = await Native.Policy("Apply", token); owned = JsonSerializer.Deserialize<string[]>(json, Product.Json) ?? throw new InvalidDataException("POLICY"); store.Write("dns-owned.json", owned); } catch { await Recover(CancellationToken.None); throw; }
    }
    public async Task Remove(CancellationToken token) { await Recover(token); }
}
