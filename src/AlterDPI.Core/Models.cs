using System.Text.Json;

namespace AlterDPI.Core;

public static class Product
{
    public const string Name = "AlterDPI", DisplayVersion = "0.5.0fx", Service = "AlterDPI", Pipe = "AlterDPI.Control.v1";
    public const string Repository = "fxrkqnplus/alterdpi";
    public static readonly Version Version = new(0, 5, 0);
    public static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name);
    public static string StateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
}
public sealed record Problem(string Code, string Cause, string Effect, string Solution, string Level = "error");
public sealed record AppState
{
    public string Version { get; init; } = Product.DisplayVersion;
    public bool Access { get; init; }
    public bool Protection { get; init; }
    public string Stage { get; init; } = "Hazır";
    public string Level { get; init; } = "idle";
    public string ProtectionMode { get; init; } = "Kapalı";
    public string Profile { get; init; } = "";
    public string Network { get; init; } = "";
    public long Revision { get; init; }
    public long ListVersion { get; init; }
    public int DomainCount { get; init; }
    public bool ListStale { get; init; }
    public int HopLimit { get; init; }
    public double? LatencyMs { get; init; }
    public Problem? Problem { get; init; }
    public string? LogFile { get; init; }
    public HostException[] Exceptions { get; init; } = [];
}
public sealed record Preferences(bool Consent = false, bool Access = false, bool Protection = false);
public sealed record NetworkInfo(string Key, string Adapter, int Family, int Mtu, int HopLimit, bool Tunnel, bool Mobile, bool Managed, string[] Dns, string[] Suffixes, bool HasIpv6, bool Proxy, int InterfaceIndex = 0);
public sealed record Profile(string Id, string Label, string[] Args);
public sealed record GoodProfile(string Network, string EngineId, string Profile, string Resolver, double LatencyMs, DateTimeOffset Saved, bool Partial = false);
public sealed record HostException(string Host, DateTimeOffset Expires);
public sealed record ControlRequest(string Operation, bool? Enabled = null, string? Host = null, string? Token = null, int Offset = 0, string? Id = null);
public sealed record ControlReply(bool Ok, AppState? State = null, object? Data = null, Problem? Problem = null, string? Id = null);
public sealed record SignedManifest(string Kind, string Version, long Build, DateTimeOffset Created, DateTimeOffset Expires, ManifestFile[] Files);
public sealed record ManifestFile(string Path, string Sha256, long Bytes);
public sealed record Verification(bool Ok, double LatencyMs, string Failure = "", bool Transient = false)
{
    public bool Partial => Ok && Failure.Length > 0;
}

public static class Profiles
{
    public static readonly Profile[] All = [
        new("direct","Doğrudan erişim",[]),new("sequence","Uyumlu bağlantı",["-6","-p"]),
        new("sni","Akış uyarlaması",["-p","-r","-s","-f","2","-k","2","-n","-e","2","--native-frag","--reverse-frag","--frag-by-sni","--max-payload","1200"]),
        new("combined","Birleşik bağlantı",["-8","-p"]),new("checksum","Alternatif bağlantı",["-7","-p"]),
        new("fragment","Standart uyarlama",["-p","-r","-s","-f","2","-k","2","-n","-e","2","--native-frag","--reverse-frag","--max-payload","1200"])];
    public static IEnumerable<Profile> Ordered(NetworkInfo n, string? previous = null)
    {
        var ids = n.Tunnel || n.Mtu is > 0 and < 1400 ? new[] { "direct", "sequence", "sni", "fragment", "combined", "checksum" } : new[] { "direct", "sequence", "combined", "sni", "checksum", "fragment" };
        return new[] { previous }.Concat(ids).Where(x => x != null).Distinct().Select(x => All.Single(p => p.Id == x));
    }
}
