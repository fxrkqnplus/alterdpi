using System.Collections.Concurrent;
using System.Globalization;

namespace AlterDPI.Core;

public sealed class Threats(TimeProvider? timeProvider = null)
{
    private DateTimeOffset Now => (timeProvider ?? TimeProvider.System).GetUtcNow();
    private HashSet<string> domains = new(StringComparer.Ordinal);
    private string[] ordered = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> exceptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Host, int Count, DateTimeOffset Expires)> challenges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> providerBlocks = new(StringComparer.Ordinal);
    public long Version { get; private set; }
    public DateTimeOffset Updated { get; private set; }
    public DateTimeOffset Expires { get; private set; }
    public int Count => Volatile.Read(ref domains).Count;
    public static string Normalize(string host)
    {
        host = host.TrimEnd('.'); if (host.Length is < 1 or > 253 || host.Any(c => char.IsControl(c) || c is '/' or ':' or '\\' or '*' or '@')) throw new ArgumentException("HOST");
        var ascii = new IdnMapping().GetAscii(host).ToLowerInvariant(); if (ascii.Split('.').Any(x => x.Length is < 1 or > 63 || x[0] == '-' || x[^1] == '-' || x.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))) throw new ArgumentException("HOST"); return ascii;
    }
    public void Load(string root)
    {
        var m = Trust.Verify(File.ReadAllBytes(Path.Combine(root, "data.json")), File.ReadAllBytes(Path.Combine(root, "data.sig")), "data", Version);
        if (m.Files.Length != 1 || m.Files[0].Path != "domains.txt") throw new InvalidDataException("INTEGRITY"); Trust.VerifyFiles(root, m);
        var next = new HashSet<string>(StringComparer.Ordinal); foreach (var line in File.ReadLines(Path.Combine(root, "domains.txt"))) { if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue; next.Add(Normalize(line)); if (next.Count > 1_000_000) throw new InvalidDataException("INTEGRITY"); }
        if (next.Count < 100) throw new InvalidDataException("INTEGRITY"); Volatile.Write(ref ordered, next.Order(StringComparer.Ordinal).ToArray()); Volatile.Write(ref domains, next); Version = m.Build; Updated = m.Created; Expires = m.Expires;
    }
    public bool IsExcepted(string host) => exceptions.TryGetValue(host, out var expiry) && expiry > Now;
    public bool IsBlocked(string host)
    {
        return IsBlockedDns(Normalize(host));
    }
    public bool IsBlockedDns(string host)
    {
        host = host.TrimEnd('.').ToLowerInvariant(); if (IsExcepted(host)) return false; var set = Volatile.Read(ref domains);
        for (var h = host; ;) { if (set.Contains(h)) return true; var dot = h.IndexOf('.'); if (dot < 0) return false; h = h[(dot + 1)..]; }
    }
    public void RecordProviderBlock(string host) { if (providerBlocks.Count > 512) providerBlocks.Clear(); providerBlocks[Normalize(host)] = Now.AddMinutes(1); }
    public bool WasBlocked(string host) { host = Normalize(host); return !IsExcepted(host) && (IsBlocked(host) || (providerBlocks.TryGetValue(host, out var t) && t > Now)); }
    public int RemainingSeconds(string host) => exceptions.TryGetValue(host, out var expiry) ? Math.Clamp((int)(expiry - Now).TotalSeconds, 0, 600) : 0;
    public string[] RevokeExpired()
    {
        var removed = new List<string>(); foreach (var pair in exceptions.Where(x => x.Value <= Now)) if (((ICollection<KeyValuePair<string, DateTimeOffset>>)exceptions).Remove(pair)) removed.Add(pair.Key); return removed.ToArray();
    }
    public string[] Page(int offset, int take = 400) => Volatile.Read(ref ordered).Skip(Math.Clamp(offset, 0, 1_000_000)).Take(Math.Clamp(take, 1, 400)).ToArray();
    public HostException[] ActiveExceptions() => exceptions.Where(x => x.Value > Now).Select(x => new HostException(x.Key, x.Value)).ToArray();
    public string Begin(string host)
    {
        foreach (var pair in challenges.Where(p => p.Value.Expires < Now)) challenges.TryRemove(pair.Key, out _);
        host = Normalize(host); if (!WasBlocked(host) || challenges.Count >= 16) throw new InvalidOperationException("HOST"); var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)); challenges[token] = (host, 0, Now.AddMinutes(1)); return token;
    }
    public bool Confirm(string token)
    {
        if (!challenges.TryGetValue(token, out var c) || c.Expires < Now) { challenges.TryRemove(token, out _); throw new InvalidOperationException("TOKEN"); }
        if (c.Count == 0) { challenges[token] = (c.Host, 1, c.Expires); return false; }
        if (!challenges.TryRemove(token, out _)) throw new InvalidOperationException("TOKEN"); exceptions[c.Host] = Now.AddMinutes(10); return true;
    }
    public void Cancel(string token) => challenges.TryRemove(token, out _);
    public string[] ClearExceptions() { var hosts = exceptions.Keys.ToArray(); exceptions.Clear(); challenges.Clear(); return hosts; }
}
