using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using ARSoft.Tools.Net.Dns;

namespace AlterDPI.Core;

public sealed record Resolver(string Id, string Url, string[] V4, string[] V6, bool Filtered = false, bool Http2Only = false);
public sealed class DohTransport : IClientTransport
{
    private readonly HttpClient client;
    private readonly Uri uri;
    private readonly bool strict;
    public DohTransport(Resolver resolver, int family)
    {
        uri = new(resolver.Url); strict = resolver.Http2Only;
        var ips = (family == 6 ? resolver.V6 : resolver.V4).Select(IPAddress.Parse).ToArray();
        var handler = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2), PooledConnectionLifetime = TimeSpan.FromMinutes(5), MaxConnectionsPerServer = 8, AllowAutoRedirect = false };
        handler.ConnectCallback = async (_, token) =>
        {
            foreach (var ip in ips) { var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true }; try { await socket.ConnectAsync(new IPEndPoint(ip, 443), token); return new NetworkStream(socket, true); } catch { socket.Dispose(); token.ThrowIfCancellationRequested(); } }
            throw new HttpRequestException("DNS_UNAVAILABLE");
        };
        client = new(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }
    public ushort MaximumAllowedQuerySize => 4096;
    public bool SupportsReliableTransfer => true;
    public bool SupportsMulticastTransfer => false;
    public bool SupportsPooledConnections => false;
    public Task<IClientConnection?> ConnectAsync(DnsClientEndpointInfo endpointInfo, int queryTimeout, CancellationToken token = default) => Task.FromResult<IClientConnection?>(new Connection(this, client, uri, strict));
    public Task<IClientConnection?> GetPooledConnectionAsync(DnsClientEndpointInfo endpointInfo, CancellationToken token = default) => Task.FromResult<IClientConnection?>(null);
    public void Dispose() => client.Dispose();
    private sealed class Connection(IClientTransport transport, HttpClient client, Uri uri, bool strict) : IClientConnection
    {
        private DnsReceivedRawPackage? reply;
        public IClientTransport Transport => transport;
        public bool IsFaulty { get; private set; }
        public async Task<bool> SendAsync(DnsRawPackage package, CancellationToken token = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(3)); token = deadline.Token;
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Version = HttpVersion.Version20, VersionPolicy = strict ? HttpVersionPolicy.RequestVersionExact : HttpVersionPolicy.RequestVersionOrLower, Content = new ByteArrayContent(package.ToArraySegment(false).ToArray()) };
            request.Content.Headers.ContentType = new("application/dns-message"); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/dns-message" || response.Content.Headers.ContentLength > 65535) return false;
            using var stream = await response.Content.ReadAsStreamAsync(token); using var data = new MemoryStream(); var buffer = new byte[4096]; int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0) { if (data.Length + count > 65535) return false; data.Write(buffer, 0, count); }
            if (data.Length < 12) return false; var body = data.ToArray(); var framed = new byte[body.Length + 2]; framed[0] = (byte)(body.Length >> 8); framed[1] = (byte)body.Length; body.CopyTo(framed, 2);
            reply = new(framed, new IPEndPoint(IPAddress.None, 443), new IPEndPoint(IPAddress.Loopback, 0)); return true;
        }
        public Task<DnsReceivedRawPackage?> ReceiveAsync(DnsMessageIdentification identification, CancellationToken token = default) { var value = reply; reply = null; return Task.FromResult(value); }
        public void RestartIdleTimeout(TimeSpan? timeout) { }
        public void MarkFaulty() => IsFaulty = true;
        public void Dispose() { }
    }
}
