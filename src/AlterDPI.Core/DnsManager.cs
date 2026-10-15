using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;

namespace AlterDPI.Core;

public sealed class DnsManager(Threats threats) : IDisposable
{
    public static readonly Resolver[] Resolvers = [
        new("google","https://dns.google/dns-query",["8.8.8.8","8.8.4.4"],["2001:4860:4860::8888","2001:4860:4860::8844"]),
        new("cloudflare","https://cloudflare-dns.com/dns-query",["1.1.1.1","1.0.0.1"],["2606:4700:4700::1111","2606:4700:4700::1001"]),
        new("quad9","https://dns.quad9.net/dns-query",["9.9.9.9","149.112.112.112"],["2620:fe::fe","2620:fe::9"],true,true),
        new("security","https://security.cloudflare-dns.com/dns-query",["1.1.1.2","1.0.0.2"],["2606:4700:4700::1112","2606:4700:4700::1002"],true)];
    private DnsServer? server;
    private NetworkInfo? network;
    private readonly ConcurrentDictionary<string, (DnsClient Client, DohTransport Transport)> clients = new();
    private readonly SemaphoreSlim capacity = new(32, 32);
    private readonly CancellationTokenSource lifetime = new();
    private volatile bool protection;
    private string selected = "system";
    // ARSoft 3.6.1 exposes TTL mutation internally; the pinned method is bound once and regression-tested.
    private static readonly Action<DnsRecordBase, int> SetTtl = typeof(DnsRecordBase).GetProperty(nameof(DnsRecordBase.TimeToLive))!.GetSetMethod(true)!.CreateDelegate<Action<DnsRecordBase, int>>();
    public event Action<string>? ModeChanged;
    public string Selected => selected;
    public bool Running => server != null;
    public void Configure(NetworkInfo n, string resolver, bool protect) { network = n; selected = resolver; protection = protect; }
    public void SetProtection(bool enabled) => protection = enabled;
    public void Select(string resolver) { if (resolver != "system" && !Resolvers.Any(x => x.Id == resolver)) throw new ArgumentException("RESOLVER"); selected = resolver; }
    public void Start()
    {
        if (server != null) return;
        try
        {
            server = new DnsServer(new IServerTransport[] { new UdpServerTransport(IPAddress.Loopback, 53), new TcpServerTransport(IPAddress.Loopback, 53, 3000) });
            server.QueryReceived += Handle; server.ExceptionThrown += (_, _) => Task.CompletedTask; server.Start();
        }
        catch { server?.Stop(); server = null; throw new InvalidOperationException("DNS_PORT"); }
    }
    private async Task Handle(object sender, QueryReceivedEventArgs args)
    {
        if (args.Query is not DnsMessage query || query.Questions.Count != 1 || !IPAddress.IsLoopback(args.RemoteEndpoint.Address)) { return; }
        var response = query.CreateResponseInstance(); response.ReturnCode = ReturnCode.ServerFailure; args.Response = response;
        if (!capacity.Wait(0)) return;
        try { args.Response = await Resolve(query, lifetime.Token); } catch (Exception e) when (e is not OutOfMemoryException) { ModeChanged?.Invoke("unavailable"); } finally { capacity.Release(); }
    }
    private bool IsPrivate(string host)
    {
        var n = network; return !host.Contains('.') || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal) || host.EndsWith(".home.arpa", StringComparison.Ordinal) || host.EndsWith(".in-addr.arpa", StringComparison.Ordinal) || host.EndsWith(".ip6.arpa", StringComparison.Ordinal) || (n?.Suffixes.Any(s => host == s || host.EndsWith('.' + s, StringComparison.Ordinal)) ?? false);
    }
    public async Task<DnsMessage> Resolve(DnsMessage query, CancellationToken token)
    {
        if (query.Questions.Count != 1) throw new InvalidDataException("DNS"); var n = network ?? throw new InvalidOperationException("NO_NETWORK"); var host = query.Questions[0].Name.ToString().TrimEnd('.').ToLowerInvariant(); var regularHost = !host.Contains('_') && host.Length > 0;
        if (protection && threats.IsBlockedDns(host)) return Block(query);
        var excepted = threats.IsExcepted(host);
        // DNS64 and local names must continue using the network's original resolver.
        if (IsPrivate(host) || n.Family == 6) return FinalizeAnswer(await SystemQuery(query, n, token), query, host, excepted);
        var primary = Resolvers.FirstOrDefault(r => r.Id == selected)?.Filtered == true ? selected : "quad9";
        var unfiltered = Resolvers.Any(r => r.Id == selected && r.Filtered) ? "google" : selected;
        var choices = protection && !excepted ? new[] { primary, "quad9", "security", selected, "google", "cloudflare" } : new[] { unfiltered, "google", "cloudflare" };
        foreach (var id in choices.Distinct())
        {
            token.ThrowIfCancellationRequested(); try
            {
                var result = id == "system" ? await SystemQuery(query, n, token) : await DohQuery(query, id, n.Family, token);
                if (protection && regularHost && !threats.IsExcepted(host) && (id is "quad9" or "security") && IsProviderBlock(result, id)) { threats.RecordProviderBlock(host); var blocked = query.CreateResponseInstance(); blocked.ReturnCode = ReturnCode.NxDomain; return blocked; }
                if (result.ReturnCode == ReturnCode.ServerFailure) continue; ModeChanged?.Invoke(protection ? (id is "quad9" or "security" ? "filtered" : "local") : "normal"); return FinalizeAnswer(result, query, host, excepted);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or SocketException or OperationCanceledException) { token.ThrowIfCancellationRequested(); }
        }
        throw new InvalidOperationException("DNS_UNAVAILABLE");
    }
    private static DnsMessage Block(DnsMessage query) { var blocked = query.CreateResponseInstance(); blocked.ReturnCode = ReturnCode.NxDomain; blocked.IsRecursionAllowed = true; return blocked; }
    public DnsMessage FinalizeAnswer(DnsMessage answer, DnsMessage query, string host, bool excepted)
    {
        if (protection && (threats.IsBlockedDns(host) || excepted && !threats.IsExcepted(host))) return Block(query);
        if (excepted) foreach (var record in answer.AnswerRecords.Concat(answer.AuthorityRecords).Concat(answer.AdditionalRecords).Where(r => (ushort)r.RecordType != 41)) SetTtl(record, Math.Clamp(record.TimeToLive,0,threats.RemainingSeconds(host))); return answer;
    }
    private static bool IsProviderBlock(DnsMessage result, string id) => id == "quad9" && result.ReturnCode == ReturnCode.NxDomain && result.AuthorityRecords.Count == 0 || id == "security" && result.AnswerRecords.OfType<ARecord>().Any(x => x.Address.Equals(IPAddress.Any));
    private static async Task<DnsMessage> SystemQuery(DnsMessage query, NetworkInfo n, CancellationToken token)
    {
        var addresses = n.Dns.Select(IPAddress.Parse).Where(x => !IPAddress.IsLoopback(x)).Select(x => { if (x.IsIPv6LinkLocal && x.ScopeId == 0 && n.InterfaceIndex > 0) x.ScopeId = n.InterfaceIndex; return x; }).Distinct().ToArray(); if (addresses.Length == 0) throw new IOException("DNS_UNAVAILABLE"); using var client = new DnsClient(addresses, 2000);
        return await client.SendMessageAsync(query, token) ?? throw new IOException("DNS_UNAVAILABLE");
    }
    private async Task<DnsMessage> DohQuery(DnsMessage query, string id, int family, CancellationToken token)
    {
        var pair = clients.GetOrAdd(id + family, _ => { var provider = Resolvers.Single(x => x.Id == id); var transport = new DohTransport(provider, family); return (new DnsClient(new[] { IPAddress.Loopback }, new IClientTransport[] { transport }, false, 3000), transport); });
        return await pair.Client.SendMessageAsync(query, token) ?? throw new IOException("DNS_UNAVAILABLE");
    }
    public async Task<string[]> Rank(NetworkInfo n, bool protect, CancellationToken token)
    {
        if (n.Family == 6) return ["system"];
        var candidates = Resolvers.Where(x => !protect || x.Filtered).ToArray(); var measurements = await Task.WhenAll(candidates.Select(async r =>
        {
            var samples = new List<double>(); for (int i = 0; i < 3; i++) { var q = new DnsMessage { IsRecursionDesired = true }; q.Questions.Add(new DnsQuestion(DomainName.Parse("www.microsoft.com"), RecordType.A, RecordClass.INet)); var clock = Stopwatch.StartNew(); try { var a = await DohQuery(q, r.Id, n.Family, token); if (a.ReturnCode == ReturnCode.NoError) samples.Add(clock.Elapsed.TotalMilliseconds); } catch (Exception e) when (e is not OperationCanceledException) { } token.ThrowIfCancellationRequested(); }
            return (Id: r.Id, Time: samples.Count > 0 ? samples.Order().ElementAt(samples.Count / 2) : double.PositiveInfinity);
        }));
        return measurements.OrderBy(x => x.Time).Select(x => x.Id).ToArray();
    }
    public void Stop() { server?.Stop(); server = null; }
    public void Dispose() { lifetime.Cancel(); Stop(); foreach (var c in clients.Values) { ((IDisposable)c.Client).Dispose(); c.Transport.Dispose(); } lifetime.Dispose(); capacity.Dispose(); }
}
