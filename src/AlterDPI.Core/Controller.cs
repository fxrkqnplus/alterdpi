using System.Net.NetworkInformation;

namespace AlterDPI.Core;

public sealed class Controller : IDisposable
{
    public Store Store { get; }
    public Threats Threats { get; } = new();
    private readonly Journal journal;
    private readonly INetworkEngine engine;
    private readonly DnsManager dns;
    private readonly DnsPolicy policy;
    private readonly Updates updates;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? selection, debounce;
    private readonly object selectionLock = new(), stateLock = new();
    private readonly Timer timer;
    private AppState state = new();
    private Preferences preferences;
    private NetworkInfo? network;
    private long generation;
    private DateTimeOffset nextData = DateTimeOffset.MinValue;
    private DateTimeOffset nextHealth = DateTimeOffset.MinValue, nextRetry = DateTimeOffset.MinValue;
    private bool timerBusy;
    private bool cleanupPending;
    public AppState State => state with { Exceptions = Threats.ActiveExceptions(), ListVersion = Threats.Version, DomainCount = Threats.Count, HopLimit = Native.HopLimit, ListStale = Threats.Count > 0 && DateTimeOffset.UtcNow > Threats.Expires };
    public event Action<AppState>? Changed;
    public Controller(Store? store = null, INetworkEngine? engine = null)
    {
        Store = store ?? new(Product.StateRoot); journal = new(Store); this.engine = engine ?? new WindowsEngine(); policy = new(Store); dns = new(Threats); updates = new(Store, Threats);
        preferences = Store.Read<Preferences>("preferences.json") ?? new(); timer = new(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        dns.ModeChanged += mode => { var s = state; if (!s.Protection) return; var text = mode switch { "filtered" => "Etkin", "local" => "Yerel liste etkin", "unavailable" => "DNS erişimi sınırlı", _ => s.ProtectionMode }; if (s.ProtectionMode != text) Publish(s with { ProtectionMode = text, Level = mode == "unavailable" ? "warning" : s.Level }); };
    }
    public async Task Initialize(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
        Trust.VerifyInstalled(AppContext.BaseDirectory); await policy.Recover(token); await Native.Policy("LegacyResume", token);
        var seed = Path.Combine(AppContext.BaseDirectory, "security"); Threats.Load(seed); var last = Store.Read<long>("security-active.json"); if (last > Threats.Version) { try { Threats.Load(Path.Combine(Store.Root, "security-" + last)); } catch { Report(new InvalidDataException("INTEGRITY")); } }
        NetworkChange.NetworkAddressChanged += OnNetwork; NetworkChange.NetworkAvailabilityChanged += OnAvailability; timer.Change(3000, 3000); Publish(new() { Stage = "Hazır" });
        }
        finally { gate.Release(); }
    }
    private void Publish(AppState value) { lock (stateLock) state = value with { Revision = Interlocked.Increment(ref generation) }; Changed?.Invoke(State); }
    private void CancelSelection() { lock (selectionLock) selection?.Cancel(); }
    private void Report(Exception error) { var p = Problems.From(error); var file = journal.Log(p, state.Stage, error); Publish(state with { Level = p.Level, Problem = p, LogFile = file, Stage = "Kontrol gerekiyor" }); }
    public async Task<ControlReply> Execute(ControlRequest request, CancellationToken token)
    {
        try
        {
            switch (request.Operation)
            {
                case "state": return new(true, State, preferences);
                case "consent": preferences = preferences with { Consent = request.Enabled == true }; Store.Write("preferences.json", preferences); return new(true, State);
                case "resume": if (preferences.Consent && (preferences.Access != state.Access || preferences.Protection != state.Protection)) await Configure(preferences.Access, preferences.Protection, token); return new(true, State);
                case "access": if (!preferences.Consent) throw new UnauthorizedAccessException(); await Configure(request.Enabled == true, state.Protection, token); return new(true, State);
                case "protection": if (!preferences.Consent) throw new UnauthorizedAccessException(); await Configure(state.Access, request.Enabled == true, token); return new(true, State);
                case "stop": await Configure(false, false, token); return new(true, State);
                case "check-update": return new(true, State, await updates.Check(token));
                case "download-update": return new(true, State, await updates.Download(token));
                case "snapshot": return new(true, State, new { domains = state.Protection ? Threats.Page(request.Offset) : Array.Empty<string>(), total = state.Protection ? Threats.Count : 0, version = Threats.Version });
                case "classify": return new(true, State, new { blocked = state.Protection && Threats.WasBlocked(request.Host ?? ""), reason = "Bilinen zararlı veya güvenilmez alan adı" });
                case "begin-exception": if (!state.Protection) throw new InvalidOperationException("HOST"); return new(true, State, new { token = Threats.Begin(request.Host ?? "") });
                case "confirm-exception": if (!state.Protection) throw new InvalidOperationException("HOST"); var granted = Threats.Confirm(request.Token ?? ""); if (granted) { foreach (var e in Threats.ActiveExceptions()) Native.DnsFlushResolverCacheEntry_W(e.Host); Publish(state); } return new(true, State, new { granted });
                case "cancel-exception": Threats.Cancel(request.Token ?? ""); return new(true, State);
                case "clear-exceptions": Revoke(Threats.ClearExceptions()); Publish(state); return new(true, State);
                default: return new(false, State, Problem: new("REQUEST", "Geçersiz istek.", "İşlem yapılmadı.", "Uygulamayı yeniden açın."));
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (request.Operation is "check-update" or "download-update") { var p = Problems.From(e); journal.Log(p, "Güncelleme", e); return new(false, State, Problem: p); }
            Report(e); return new(false, State, Problem: Problems.From(e));
        }
    }
    private async Task Configure(bool access, bool protection, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token); cts.CancelAfter(TimeSpan.FromSeconds(90));
        lock (selectionLock) { selection?.Cancel(); selection = cts; }
        var entered = false; var before = state; var oldNetwork = network; var oldResolver = dns.Selected;
        try
        {
            await gate.WaitAsync(cts.Token); entered = true; before = state; oldNetwork = network; oldResolver = dns.Selected; cts.Token.ThrowIfCancellationRequested();
            Publish(state with { Stage = "Ağ kontrol ediliyor", Level = "warning", Problem = null });
            if (!access && !protection)
            {
                preferences = preferences with { Access = false, Protection = false }; Store.Write("preferences.json", preferences);
                cleanupPending = true; engine.Stop(); dns.SetProtection(false); await policy.Remove(CancellationToken.None); dns.Stop(); Revoke(Threats.ClearExceptions()); await Native.Policy("LegacyResume", CancellationToken.None);
                cleanupPending = false; Publish(new() { Stage = "Kapalı" }); return;
            }
            var n = await Native.Network(Store, cts.Token); network = n; var effective = n with { Key = Store.Fingerprint(new[] { n.Key, engine.Identity, protection.ToString() }) };
            await Native.Policy("LegacyPause", cts.Token);
            var cached = Store.Cached(effective, engine.Identity); dns.Configure(n, cached?.Resolver ?? "system", protection); dns.Start(); await policy.Apply(cts.Token);
            if (!access) { var ranked = await dns.Rank(n, true, cts.Token); dns.Select(ranked[0]); engine.Stop(); preferences = preferences with { Access = false, Protection = true }; Store.Write("preferences.json", preferences); Publish(new() { Protection = true, ProtectionMode = n.Family == 6 ? "Yerel liste etkin" : "Etkin", Stage = "Koruma etkin", Level = "success", Network = n.Adapter }); return; }
            Publish(state with { Stage = cached != null ? "Kayıtlı yöntem uygulanıyor" : "Uygun bağlantı hazırlanıyor" });
            GoodProfile? good = null;
            if (cached != null) good = await TryProfiles(effective, [cached.Resolver], cached.Profile, cts.Token, [Profiles.All.Single(p => p.Id == cached.Profile)]);
            good ??= await TryProfiles(effective, ["system"], null, cts.Token, [Profiles.All[0]]);
            if (good == null) { var ranked = await dns.Rank(n, protection, cts.Token); good = await TryProfiles(effective, ranked, null, cts.Token, [Profiles.All[0]]); good ??= await TryProfiles(effective, ranked.Take(2), null, cts.Token, Profiles.Ordered(n).Where(p => p.Id != "direct").ToArray()); }
            if (good == null) throw new InvalidOperationException("NO_PROFILE");
            cts.Token.ThrowIfCancellationRequested(); cleanupPending = false; Store.SaveProfile(good); preferences = preferences with { Access = true, Protection = protection }; Store.Write("preferences.json", preferences);
            Publish(new() { Access = true, Protection = protection, ProtectionMode = protection ? (n.Family == 6 ? "Yerel liste etkin" : "Etkin") : "Kapalı", Stage = good.Partial ? "Bağlantı hazır · kontrol sınırlı" : "Bağlantı hazır", Level = good.Partial ? "warning" : "success", Profile = Profiles.All.Single(p => p.Id == good.Profile).Label, Network = n.Adapter, LatencyMs = good.LatencyMs });
        }
        catch
        {
            if (entered && !access && !protection) { Publish(new() { Stage = "Ağ ayarları geri yükleniyor", Level = "warning" }); throw; }
            if (entered) try { network = oldNetwork; if (before.Access || before.Protection) { await Native.Policy("LegacyPause", CancellationToken.None); if (oldNetwork != null) dns.Configure(oldNetwork, oldResolver, before.Protection); dns.Start(); await policy.Apply(CancellationToken.None); var previous = Profiles.All.FirstOrDefault(p => p.Label == before.Profile) ?? Profiles.All[0]; await engine.Apply(previous, CancellationToken.None); Publish(before); } else { engine.Stop(); await policy.Remove(CancellationToken.None); dns.Stop(); await Native.Policy("LegacyResume", CancellationToken.None); Publish(before); } } catch (Exception rollback) { Report(rollback); }
            throw;
        }
        finally { lock (selectionLock) { if (ReferenceEquals(selection, cts)) selection = null; } if (entered) gate.Release(); }
    }
    private async Task<GoodProfile?> TryProfiles(NetworkInfo n, IEnumerable<string> resolvers, string? previous, CancellationToken token, Profile[] candidates)
    {
        GoodProfile? best = null; var compared = 0;
        foreach (var profile in candidates)
        {
            foreach (var resolver in resolvers.Distinct())
            {
                dns.Select(resolver);
                token.ThrowIfCancellationRequested(); Publish(state with { Stage = "Bağlantı yöntemi kontrol ediliyor" }); await engine.Apply(profile, token); var result = await Probes.Check(false, token);
                if (result.Ok) { var candidate = new GoodProfile(n.Key, engine.Identity, profile.Id, resolver, result.LatencyMs, DateTimeOffset.UtcNow, result.Partial); if (previous != null || profile.Id == "direct") return candidate; if (best == null || candidate.LatencyMs < best.LatencyMs) best = candidate; }
                if (best != null && ++compared >= 2) { dns.Select(best.Resolver); await engine.Apply(Profiles.All.Single(p => p.Id == best.Profile), token); return best; }
                // A temporary endpoint refusal must not skip the remaining network profiles.
            }
        }
        if (best != null) { dns.Select(best.Resolver); await engine.Apply(Profiles.All.Single(p => p.Id == best.Profile), token); }
        return best;
    }
    private void OnNetwork(object? sender, EventArgs args) => NetworkChanged();
    private void OnAvailability(object? sender, NetworkAvailabilityEventArgs args) => NetworkChanged();
    private void NetworkChanged(bool force = false)
    {
        if (!preferences.Access && !preferences.Protection) return; CancellationTokenSource current;
        lock (selectionLock) { selection?.Cancel(); debounce?.Cancel(); current = debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); }
        _ = Task.Run(async () => { try { await Task.Delay(1500, current.Token); var n = await Native.Network(Store, current.Token); if (!force && n.Key == network?.Key) return; await Configure(preferences.Access, preferences.Protection, current.Token); } catch (OperationCanceledException) { } catch (Exception e) { Report(e); } finally { lock (selectionLock) { if (ReferenceEquals(debounce, current)) debounce = null; } current.Dispose(); } });
    }
    private async void Tick()
    {
        if (timerBusy || lifetime.IsCancellationRequested) return; timerBusy = true;
        try
        {
            if (cleanupPending && selection == null && DateTimeOffset.UtcNow >= nextRetry) { nextRetry = DateTimeOffset.UtcNow.AddSeconds(30); await Configure(false, false, lifetime.Token); return; }
            var revoked = Threats.RevokeExpired(); if (revoked.Length > 0) { Revoke(revoked); Publish(state); }
            if (state.Access && state.Profile != Profiles.All[0].Label && !engine.Running && selection == null && DateTimeOffset.UtcNow >= nextRetry) { nextRetry = DateTimeOffset.UtcNow.AddMinutes(2); NetworkChanged(true); }
            if (state.Access && selection == null && DateTimeOffset.UtcNow >= nextHealth) { nextHealth = DateTimeOffset.UtcNow.AddMinutes(5); var health = await Probes.Check(false, lifetime.Token); if (!health.Ok && !health.Transient && selection == null) NetworkChanged(true); }
            if ((state.Access || state.Protection) && DateTimeOffset.UtcNow >= nextData && selection == null) { nextData = DateTimeOffset.UtcNow.AddHours(6); try { if (await updates.Data(lifetime.Token)) Publish(state); } catch (HttpRequestException) {/* No unsigned fallback: retain the last verified list. */} catch (Exception e) when (e is not OperationCanceledException) { Report(e); } }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Report(e); }
        finally { timerBusy = false; }
    }
    private static void Revoke(string[] hosts) { foreach (var host in hosts) Native.DnsFlushResolverCacheEntry_W(host); }
    public async Task Shutdown() { lifetime.Cancel(); CancelSelection(); timer.Change(Timeout.Infinite, Timeout.Infinite); await gate.WaitAsync(); try { engine.Stop(); await policy.Remove(CancellationToken.None); dns.Stop(); Revoke(Threats.ClearExceptions()); await Native.Policy("LegacyResume", CancellationToken.None); } finally { gate.Release(); } }
    public void Dispose() { NetworkChange.NetworkAddressChanged -= OnNetwork; NetworkChange.NetworkAvailabilityChanged -= OnAvailability; timer.Dispose(); lock (selectionLock) debounce?.Cancel(); lifetime.Cancel(); dns.Dispose(); engine.Dispose(); updates.Dispose(); }
}
