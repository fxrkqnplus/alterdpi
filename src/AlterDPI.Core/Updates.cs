using System.Net;
using System.Text.Json;

namespace AlterDPI.Core;

public sealed record UpdateInfo(string Status, string? Version = null, string? Installer = null);
public sealed class Updates(Store store, Threats threats) : IDisposable
{
    private readonly HttpClient client = new(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = TimeSpan.FromSeconds(12) };
    private (SignedManifest Manifest, string Url)? pending;
    public async Task<UpdateInfo> Check(CancellationToken token)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AlterDPI/0.5.0");
        var bytes = await Get($"https://api.github.com/repos/{Product.Repository}/releases?per_page=20", 1024 * 1024, token);
        using var releases = JsonDocument.Parse(bytes);
        foreach (var release in releases.RootElement.EnumerateArray().Where(x => !x.GetProperty("draft").GetBoolean()))
        {
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            string? Url(string name) => assets.FirstOrDefault(x => x.GetProperty("name").GetString() == name) is var a && a.ValueKind == JsonValueKind.Object ? a.GetProperty("browser_download_url").GetString() : null;
            var manifestUrl = Url("update.json"); var sigUrl = Url("update.sig"); var installer = Url("AlterDPI-Kurulum.exe"); if (manifestUrl == null || sigUrl == null || installer == null) continue;
            ValidateReleaseUrl(manifestUrl); ValidateReleaseUrl(sigUrl); ValidateReleaseUrl(installer);
            var manifest = Trust.Verify(await Get(manifestUrl, 256 * 1024, token), await Get(sigUrl, 1024, token), "application", store.Read<long>("app-build.json"));
            if (manifest.Files.Length != 1 || manifest.Files[0].Path != "AlterDPI-Kurulum.exe") throw new InvalidDataException("INTEGRITY");
            if (manifest.Expires < DateTimeOffset.UtcNow) throw new InvalidDataException("INTEGRITY");
            var v = Version.Parse(manifest.Version.Replace("fx", "", StringComparison.Ordinal)); if (v < Product.Version || v == Product.Version && manifest.Build <= store.Read<long>("app-build.json")) continue;
            pending = (manifest, installer); return new("available", manifest.Version);
        }
        return new("current");
    }
    public async Task<UpdateInfo> Download(CancellationToken token)
    {
        var p = pending ?? throw new InvalidOperationException("UPDATE"); var directory = Path.Combine(Product.InstallRoot, "updates", p.Manifest.Build.ToString()); Directory.CreateDirectory(directory);
        var path = Trust.ConfinedPath(directory, "AlterDPI-Kurulum.exe"); var file = p.Manifest.Files[0]; var data = await Get(p.Url, (int)file.Bytes, token); if (data.LongLength != file.Bytes) throw new InvalidDataException("INTEGRITY");
        var temp = path + ".partial"; await File.WriteAllBytesAsync(temp, data, token); if (Trust.HashFile(temp) != file.Sha256.ToUpperInvariant()) { File.Delete(temp); throw new InvalidDataException("INTEGRITY"); }
        File.Move(temp, path, true);
        return new("ready", p.Manifest.Version, path);
    }
    public async Task<bool> Data(CancellationToken token)
    {
        var url = $"https://github.com/{Product.Repository}/releases/download/security-data/";
        var metadata = await Get(url + "data.json", 256 * 1024, token); var signature = await Get(url + "data.sig", 1024, token); var m = Trust.Verify(metadata, signature, "data", threats.Version);
        if (m.Build <= threats.Version) return false; if (m.Files.Length != 1 || m.Files[0].Path != "domains.txt") throw new InvalidDataException("INTEGRITY");
        if (m.Expires < DateTimeOffset.UtcNow) throw new InvalidDataException("INTEGRITY");
        var root = Path.Combine(store.Root, "security-" + m.Build); var stage = root + "-" + Guid.NewGuid().ToString("N") + ".partial"; Directory.CreateDirectory(stage); var bytes = await Get(url + "domains.txt", 16 * 1024 * 1024, token);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(stage, "domains.txt"), bytes, token);
            await File.WriteAllBytesAsync(Path.Combine(stage, "data.json"), metadata, token); await File.WriteAllBytesAsync(Path.Combine(stage, "data.sig"), signature, token);
            new Threats().Load(stage); if (Directory.Exists(root)) { Trust.PlainTree(root); Directory.Delete(root, true); }
            Directory.Move(stage, root);
            threats.Load(root); store.Write("security-active.json", m.Build);
            foreach (var old in Directory.GetDirectories(store.Root, "security-*").Where(d => Path.GetFileName(d) != Path.GetFileName(root)).OrderByDescending(d => d).Skip(1)) { Trust.PlainTree(old); Directory.Delete(old, true); }
            return true;
        }
        finally { if (Directory.Exists(stage)) { Trust.PlainTree(stage); Directory.Delete(stage, true); } }
    }
    public async Task<byte[]> Get(string url, int maximum, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(maximum > 16 * 1024 * 1024 ? 75 : 12)); token = deadline.Token;
        if (maximum < 1 || maximum > 256 * 1024 * 1024) throw new InvalidDataException("INTEGRITY"); using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("INTEGRITY"); using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[16384]; int n;
        while ((n = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + n > maximum) throw new InvalidDataException("INTEGRITY"); output.Write(buffer, 0, n); }
        return output.ToArray();
    }
    private static void ValidateReleaseUrl(string url) { if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || u.Host != "github.com" || u.UserInfo.Length != 0 || !u.AbsolutePath.StartsWith('/' + Product.Repository + "/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("INTEGRITY"); }
    public void Dispose() => client.Dispose();
}
