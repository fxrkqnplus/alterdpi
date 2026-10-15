using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AlterDPI.Core;

public static class Trust
{
    private sealed record Keys(string Application, string Data);
    private static Keys Keyring { get; }
    static Trust() { using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AlterDPI.trust.json")!; Keyring = JsonSerializer.Deserialize<Keys>(s, Product.Json)!; }
    public static SignedManifest Verify(byte[] bytes, byte[] signature, string kind, long minimum = 0)
    {
        if (bytes.Length > 256 * 1024 || signature.Length > 1024 || kind is not ("application" or "data")) throw new InvalidDataException("INTEGRITY");
        using var rsa = RSA.Create(); rsa.ImportFromPem(kind == "application" ? Keyring.Application : Keyring.Data);
        if (!rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new InvalidDataException("INTEGRITY");
        return Validate(JsonSerializer.Deserialize<SignedManifest>(bytes, Product.Json) ?? throw new InvalidDataException("INTEGRITY"), kind, minimum);
    }
    public static SignedManifest Validate(SignedManifest m, string kind, long minimum)
    {
        if (m.Kind != kind || m.Build < minimum || m.Build <= 0 || m.Files == null || m.Files.Length is < 1 or > 4096 || m.Created > DateTimeOffset.UtcNow.AddHours(24) || m.Expires <= m.Created || m.Version == null || !Version.TryParse(m.Version.Replace("fx", "", StringComparison.Ordinal), out _)) throw new InvalidDataException("INTEGRITY");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Files) { if (!SafeRelative(f.Path) || !paths.Add(f.Path) || f.Bytes is < 0 or > 256 * 1024 * 1024 || f.Sha256.Length != 64 || !f.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("INTEGRITY"); }
        if (m.Files.Sum(x => x.Bytes) > 512L * 1024 * 1024) throw new InvalidDataException("INTEGRITY"); return m;
    }
    public static bool SafeRelative(string p) => p.Length is > 0 and < 240 && !Path.IsPathRooted(p) && !p.Contains(':') && !p.Contains('\\') && p.Split('/').All(x => x.Length > 0 && x is not ("." or "..") && !x.EndsWith('.') && !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(x.Split('.')[0], StringComparer.OrdinalIgnoreCase) && x.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'));
    public static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static string ConfinedPath(string root, string relative)
    {
        if (!SafeRelative(relative)) throw new InvalidDataException("INTEGRITY");
        var normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative)); if (!target.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("INTEGRITY");
        for (var p = Path.GetDirectoryName(target); p != null && p.Length >= normalized.Length - 1; p = Path.GetDirectoryName(p)) if (Directory.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("INTEGRITY");
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("INTEGRITY"); return target;
    }
    public static void VerifyFiles(string root, SignedManifest m)
    {
        foreach (var f in m.Files) { var p = ConfinedPath(root, f.Path); if (!File.Exists(p) || new FileInfo(p).Length != f.Bytes || !HashFile(p).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("INTEGRITY"); }
    }
    public static SignedManifest VerifyInstalled(string root)
    {
        var m = Verify(File.ReadAllBytes(Path.Combine(root, "application.json")), File.ReadAllBytes(Path.Combine(root, "application.sig")), "application"); VerifyFiles(root, m); return m;
    }
    public static void PlainTree(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("INSTALL_ROOT");
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(entry); if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("INSTALL_ROOT");
            if ((attributes & FileAttributes.Directory) != 0) PlainTree(entry);
        }
    }
}
