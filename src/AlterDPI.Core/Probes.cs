using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace AlterDPI.Core;

public static class Probes
{
    // Neutral identifiers are used in UI and logs; provider names are documentation examples only.
    private static readonly string[] Targets = ["https://www.google.com/generate_204", "https://discord.com/api/v10/gateway", "https://users.roblox.com/v1/users/1", "https://www.wattpad.com/"];
    public static async Task<Verification> Check(bool full, CancellationToken token)
    {
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = true, MaxAutomaticRedirections = 3, ConnectTimeout = TimeSpan.FromSeconds(2) }; using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var results = await Task.WhenAll(Targets.Take(full ? 4 : 3).Select(async (url, i) =>
        {
            var clock = Stopwatch.StartNew(); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5)); var requestToken = deadline.Token; try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestToken); var status = (int)response.StatusCode;
                if (status is 403 or 429 or >= 500) return new Verification(false, clock.Elapsed.TotalMilliseconds, "C0" + i, true);
                if (status is < 200 or >= 400) return new Verification(false, clock.Elapsed.TotalMilliseconds, "C0" + i);
                if (i is 1 or 2) { using var stream = await response.Content.ReadAsStreamAsync(requestToken); using var data = new MemoryStream(); var bytes = new byte[4096]; int read; while ((read = await stream.ReadAsync(bytes, requestToken)) > 0) { if (data.Length + read > 65536) return new(false, clock.Elapsed.TotalMilliseconds, "C0" + i); data.Write(bytes, 0, read); } using var json = JsonDocument.Parse(data.ToArray()); if (i == 1 && (!json.RootElement.TryGetProperty("url", out var gateway) || !Uri.TryCreate(gateway.GetString(), UriKind.Absolute, out var u) || u.Scheme != "wss" || u.Host != "gateway.discord.gg")) return new(false, clock.Elapsed.TotalMilliseconds, "C01"); if (i == 2 && (!json.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt32(out var number) || number != 1)) return new(false, clock.Elapsed.TotalMilliseconds, "C02"); }
                return new Verification(true, clock.Elapsed.TotalMilliseconds);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or JsonException or TaskCanceledException) { token.ThrowIfCancellationRequested(); return new Verification(false, clock.Elapsed.TotalMilliseconds, "C0" + i); }
        }));
        return Evaluate(results);
    }
    public static Verification Evaluate(IReadOnlyList<Verification> results)
    {
        if (results.Count < 2) return new(false, 0, "CHECKS");
        // Require a confirmed protected endpoint and the neutral baseline; a WAF refusal is inconclusive.
        var ok = results[0].Ok && results.Skip(1).Any(x => x.Ok);
        return new(ok, results.Select(x => x.LatencyMs).Order().ElementAt(results.Count / 2), string.Join(",", results.Where(x => !x.Ok).Select(x => x.Failure)), results.Any(x => x.Transient));
    }
}
