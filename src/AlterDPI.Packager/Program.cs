using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlterDPI.Core;

if (args.Length < 4) { Console.Error.WriteLine("seed <source> <destination> <protected-key> | sign <root> <kind> <protected-key>"); return 2; }
var mode = args[0]; string root, kind, key;
if (mode == "seed")
{
    root = Path.GetFullPath(args[2]); kind = "data"; key = args[3]; Directory.CreateDirectory(root);
    var domains = File.ReadLines(args[1]).Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#')).Select(Threats.Normalize).Where(x => x.Contains('.') && !IPAddress.TryParse(x, out _)).Distinct().Order(StringComparer.Ordinal).ToArray();
    if (domains.Length < 100) throw new InvalidDataException("INTEGRITY"); File.WriteAllLines(Path.Combine(root, "domains.txt"), domains, new UTF8Encoding(false));
}
else { root = Path.GetFullPath(args[1]); kind = args[2]; key = args[3]; }
if (kind is not ("application" or "data")) throw new ArgumentException("KIND");
var name = kind == "data" ? "data" : "application";
var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(x => Path.GetFileName(x) != name + ".json" && Path.GetFileName(x) != name + ".sig").Select(x => new ManifestFile(Path.GetRelativePath(root, x).Replace('\\', '/'), Trust.HashFile(x), new FileInfo(x).Length)).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
if (mode == "update") { name = "update"; files = files.Where(x => x.Path == "AlterDPI-Kurulum.exe").ToArray(); }
var now = DateTimeOffset.UtcNow; var buildText = Environment.GetEnvironmentVariable("ALTERDPI_BUILD"); var build = string.IsNullOrEmpty(buildText) ? now.ToUnixTimeSeconds() : long.Parse(buildText, System.Globalization.CultureInfo.InvariantCulture); var manifest = new SignedManifest(kind, Product.DisplayVersion, build, now, now.AddDays(kind == "data" ? 3 : 365), files);
var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Product.Json);
using var rsa = RSA.Create(); if (File.Exists(key)) { var raw = ProtectedData.Unprotect(File.ReadAllBytes(key), null, DataProtectionScope.CurrentUser); try { rsa.ImportPkcs8PrivateKey(raw, out _); } finally { CryptographicOperations.ZeroMemory(raw); } }
else { var pem = Environment.GetEnvironmentVariable(kind == "data" ? "ALTERDPI_DATA_SIGNING_KEY" : "ALTERDPI_APPLICATION_SIGNING_KEY") ?? throw new InvalidOperationException("SIGNING_KEY"); rsa.ImportFromPem(pem); }
var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); Trust.Verify(bytes, signature, kind);
File.WriteAllBytes(Path.Combine(root, name + ".json"), bytes); File.WriteAllBytes(Path.Combine(root, name + ".sig"), signature);
Console.WriteLine(JsonSerializer.Serialize(new { kind, files = files.Length, build = manifest.Build }, Product.Json)); return 0;
