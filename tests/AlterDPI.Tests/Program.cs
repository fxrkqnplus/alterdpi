using System.Net;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using AlterDPI.Core;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "../../../../..")); var scratch = Path.Combine(Path.GetTempPath(), "AlterDPI-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
if(args.Length>2&&args[1]=="--service-readonly")
{
    var pid=uint.Parse(args[2]);Console.WriteLine(JsonSerializer.Serialize(new{identity=ProcessIdentity.IsSystemService(pid),image=ProcessIdentity.Image(pid)}));
    try{var reply=await ControlClient.Send(new("state"));Console.WriteLine("Unexpected external client acceptance: "+reply.Ok);}catch(Exception e){Console.WriteLine(e.GetType().Name+": "+e.Message);}Directory.Delete(scratch);return 0;
}
int passed = 0, failed = 0; var hop = Native.HopLimit;
void Check(string name, bool ok) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL " + name); } }
void Reject(string name, Action action) { try { action(); Check(name, false); } catch { Check(name, true); } }
try
{
    Check("version", Product.DisplayVersion == "0.5.0fx");
    Check("features-default-off", new Preferences() is { Consent: false, Access: false, Protection: false });
    Check("host-case", Threats.Normalize("EXAMPLE.COM.") == "example.com");
    Check("host-idna", Threats.Normalize("örnek.com").StartsWith("xn--", StringComparison.Ordinal));
    foreach (var h in new[] { "evil.com/path", "*.evil.com", "user@evil.com", "evil.com:443", "x..com", "-x.com", "x\n.com" }) Reject("host-invalid", () => Threats.Normalize(h));
    foreach (var p in new[] { "../bad.exe", "/bad.exe", "C:/bad.exe", "a\\bad.exe", "file.exe:ads", "a/../../bad", "CON.exe", "a/aux", "a/b." }) Check("path-invalid", !Trust.SafeRelative(p));
    Check("path-good", Trust.SafeRelative("engine/WinDivert64.sys"));
    Reject("path-confine", () => Trust.ConfinedPath(scratch, "../outside"));
    var now = DateTimeOffset.UtcNow;
    var valid = new SignedManifest("data", "0.5.0fx", 100, now, now.AddDays(1), [new("domains.txt", new string('A', 64), 1)]);
    Check("manifest-valid", Trust.Validate(valid, "data", 1) == valid);
    Reject("manifest-replay", () => Trust.Validate(valid, "data", 101));
    Reject("manifest-kind", () => Trust.Validate(valid, "application", 0));
    Reject("manifest-duplicates", () => Trust.Validate(valid with { Files = [valid.Files[0], valid.Files[0]] }, "data", 0));
    Reject("manifest-hash", () => Trust.Validate(valid with { Files = [valid.Files[0] with { Sha256 = "z" }] }, "data", 0));
    var seed = Path.Combine(root, "assets", "security"); var meta = File.ReadAllBytes(Path.Combine(seed, "data.json")); var sig = File.ReadAllBytes(Path.Combine(seed, "data.sig"));
    Check("signature-valid", Trust.Verify(meta, sig, "data").Files.Length == 1);
    var corrupted = meta.ToArray(); corrupted[corrupted.Length / 2] ^= 1; Reject("signature-tamper", () => Trust.Verify(corrupted, sig, "data"));
    using (var other = RSA.Create(2048)) Reject("signature-wrong-key", () => Trust.Verify(meta, other.SignData(meta, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), "data"));
    var clock = new TestClock(now); var threats = new Threats(clock); threats.Load(seed); var host = threats.Page(0, 1)[0];
    Check("list-loaded", threats.Count >= 100); Check("block-exact", threats.IsBlocked(host)); Check("block-subdomain", threats.IsBlocked("sub." + host));
    var challenge = threats.Begin(host); Check("first-confirm-no-bypass", !threats.Confirm(challenge) && threats.IsBlocked(host)); Check("second-confirm-grants", threats.Confirm(challenge) && !threats.IsBlocked(host));
    Check("exception-exact-only", threats.IsBlocked("sub." + host)); Reject("challenge-reuse", () => threats.Confirm(challenge)); clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1))); Check("exception-expired", threats.IsBlocked(host));
    var canceled = threats.Begin(host); threats.Cancel(canceled); Reject("challenge-canceled", () => threats.Confirm(canceled));
    var expired = threats.Begin(host); clock.Advance(TimeSpan.FromSeconds(61)); Reject("challenge-expired", () => threats.Confirm(expired));
    Check("block-dns-service-label", threats.IsBlockedDns("_service._tcp." + host)); Check("unlisted-service-label", !threats.IsBlockedDns("_service._tcp.safe.invalid"));
    var acl = new DirectorySecurity(); var sid = WindowsIdentity.GetCurrent().User!; acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false); acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
    var atomic = Path.Combine(scratch, "atomic"); SafeDirectory.CreateNew(atomic, acl); Check("atomic-descriptor", new DirectoryInfo(atomic).GetAccessControl().AreAccessRulesProtected); File.WriteAllText(Path.Combine(atomic, "sentinel"), "unchanged"); Reject("atomic-no-adoption", () => SafeDirectory.CreateNew(atomic, acl)); Check("atomic-preserve-existing", File.ReadAllText(Path.Combine(atomic, "sentinel")) == "unchanged");
    var store = new Store(Path.Combine(scratch, "state")); var fingerprint = store.Fingerprint(["A", "B"]); Check("fingerprint-stable", fingerprint == store.Fingerprint(["A", "B"])); Check("fingerprint-changed", fingerprint != store.Fingerprint(["A", "C"]));
    var n = new NetworkInfo(fingerprint, "Test", 4, 1500, 65, false, false, false, ["8.8.8.8"], [], false, false);
    store.SaveProfile(new(n.Key, "engine", "sequence", "google", 12, now)); Check("cache-reuse", store.Cached(n, "engine")?.Profile == "sequence"); Check("cache-new-engine", store.Cached(n, "changed") == null); Check("cache-new-network", store.Cached(n with { Key = "new" }, "engine") == null);
    store.SaveProfile(new(n.Key, "engine", "sequence", "bad-resolver", 12, now)); Check("cache-invalid-resolver", store.Cached(n, "engine") == null);
    for (var i = 0; i < 25; i++) store.SaveProfile(new("N" + i, "engine", "direct", "system", 1, now)); Check("cache-bounded", store.Read<GoodProfile[]>("profiles.json")?.Length == 20);
    File.WriteAllText(Path.Combine(store.Root, "profiles.json"), "{broken"); Check("cache-corrupt-safe", store.Cached(n, "engine") == null);
    File.WriteAllText(Path.Combine(store.Root, "network-salt.json"), "\"bad-base64\""); Check("salt-corrupt-recovers", store.Fingerprint(["A"]).Length == 64);
    Check("reject-current-process-as-service", !ProcessIdentity.IsSystemService((uint)Environment.ProcessId));
    Check("previous-first", Profiles.Ordered(n, "sequence").First().Id == "sequence"); Check("no-duplicate-profiles", Profiles.Ordered(n, "sequence").Select(p => p.Id).Distinct().Count() == Profiles.All.Length);
    if (args.Contains("--network"))
    {
        foreach (var resolver in DnsManager.Resolvers)
        {
            using var transport = new DohTransport(resolver, 4); using var client = new DnsClient(new[] { IPAddress.Loopback }, new IClientTransport[] { transport }, false, 4000);
            var query = new DnsMessage { IsRecursionDesired = true }; query.Questions.Add(new DnsQuestion(DomainName.Parse("www.microsoft.com"), RecordType.A, RecordClass.INet));
            var clockNet = System.Diagnostics.Stopwatch.StartNew(); try { var response = await client.SendMessageAsync(query); var ok = response?.ReturnCode == ReturnCode.NoError && response.AnswerRecords.OfType<ARecord>().Any(); Check("doh-" + resolver.Id, ok); Console.WriteLine(JsonSerializer.Serialize(new { resolver = resolver.Id, ok, elapsedMs = Math.Round(clockNet.Elapsed.TotalMilliseconds) })); } catch (Exception e) { Check("doh-" + resolver.Id, false); Console.WriteLine(resolver.Id + ": " + e.GetType().Name); }
        }
    }
    var tunnel = Profiles.Ordered(n with { Tunnel = true }).Select(p => p.Id).ToArray(); Check("tunnel-order", Array.IndexOf(tunnel, "sni") < Array.IndexOf(tunnel, "checksum"));
    Check("no-udp-blocking", Profiles.All.All(p => !p.Args.Contains("-q") && !p.Args.Contains("-9")));
    using (var stream = new MemoryStream()) { await Frames.Write(stream, new ControlRequest("state"), CancellationToken.None); stream.Position = 0; Check("frame-roundtrip", (await Frames.Read<ControlRequest>(stream, CancellationToken.None))?.Operation == "state"); }
    foreach (var size in new[] { 0, -1, Frames.Maximum + 1 }) { using var s = new MemoryStream(BitConverter.GetBytes(size)); try { await Frames.Read<ControlRequest>(s, CancellationToken.None); Check("frame-bounds", false); } catch (InvalidDataException) { Check("frame-bounds", true); } }
    using (var dns = new DnsManager(threats)) { dns.Configure(n, "google", true); var q = new DnsMessage { IsRecursionDesired = true }; q.Questions.Add(new(DomainName.Parse(host), RecordType.A, RecordClass.INet)); var answer = await dns.Resolve(q, CancellationToken.None); Check("local-block-no-network", answer.ReturnCode == ReturnCode.NxDomain); }
    using (var dns = new DnsManager(threats))
    {
        dns.Configure(n with { Family = 6, Dns = ["127.0.0.1"] }, "system", true); var q = new DnsMessage { IsRecursionDesired = true }; q.Questions.Add(new(DomainName.Parse("_service._tcp." + host), RecordType.Srv, RecordClass.INet)); Check("underscore-ipv6-block-before-upstream", (await dns.Resolve(q, CancellationToken.None)).ReturnCode == ReturnCode.NxDomain);
        var grant = threats.Begin(host); threats.Confirm(grant); threats.Confirm(grant); var original = q.CreateResponseInstance(); original.AnswerRecords.Add(new ARecord(DomainName.Parse(host), 86400, IPAddress.Parse("192.0.2.1")));
        var limited = dns.FinalizeAnswer(original, q, host, true); Check("exception-ttl-bound", limited.AnswerRecords[0].TimeToLive is > 0 and <= 600);
        clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1))); Check("inflight-expiry-blocks", dns.FinalizeAnswer(original, q, host, true).ReturnCode == ReturnCode.NxDomain); Check("expiry-revocation-host", threats.RevokeExpired().Contains(host));
        grant = threats.Begin(host); threats.Confirm(grant); threats.Confirm(grant); Check("clear-revocation-host", threats.ClearExceptions().Contains(host)); Check("clear-reblocks", threats.IsBlockedDns(host));
    }
    var ps = File.ReadAllText(Path.Combine(root, "packaging", "network.ps1")); Check("no-hop-writes", !ps.Contains("defaultcurhoplimit", StringComparison.OrdinalIgnoreCase) && !ps.Contains("Set-NetIPv4Protocol", StringComparison.OrdinalIgnoreCase)); Check("no-adapter-writes", !ps.Contains("Set-NetIPInterface", StringComparison.OrdinalIgnoreCase) && !ps.Contains("Set-DnsClientServerAddress", StringComparison.OrdinalIgnoreCase));
    Check("http2-required", DnsManager.Resolvers.Single(x => x.Id == "quad9").Http2Only);
    Check("hop-preserved", Native.HopLimit == hop);
}
finally
{
    if (Path.GetFullPath(scratch).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "AlterDPI-tests-", StringComparison.OrdinalIgnoreCase)) Directory.Delete(scratch, true);
}
Console.WriteLine(JsonSerializer.Serialize(new { passed, failed, hopBefore = hop, hopAfter = Native.HopLimit }, Product.Json)); return failed == 0 ? 0 : 1;
sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
