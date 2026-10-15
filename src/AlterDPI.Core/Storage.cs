using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlterDPI.Core;

public sealed class Store(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    private readonly object gate = new();
    public T? Read<T>(string name)
    {
        try { lock (gate) { var f = Path.Combine(Root, name); if (!File.Exists(f) || new FileInfo(f).Length > 8 * 1024 * 1024) return default; return JsonSerializer.Deserialize<T>(File.ReadAllText(f), Product.Json); } }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }
    public void Write<T>(string name, T value)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Root); var target = Path.Combine(Root, name); var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Product.Json), new UTF8Encoding(false)); File.Move(temp, target, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public string Fingerprint(IEnumerable<string> values)
    {
        lock (gate)
        {
            var salt = Read<string>("network-salt.json"); Span<byte> buffer = stackalloc byte[32]; if (salt == null || !Convert.TryFromBase64String(salt, buffer, out var length) || length != 32) { salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); Write("network-salt.json", salt); }
            return Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(salt), Encoding.UTF8.GetBytes(string.Join('\n', values))));
        }
    }
    public GoodProfile? Cached(NetworkInfo n, string engineId) => Read<GoodProfile[]>("profiles.json")?.Take(20).FirstOrDefault(p => p.Network == n.Key && p.EngineId == engineId && Profiles.All.Any(x => x.Id == p.Profile) && (p.Resolver == "system" || DnsManager.Resolvers.Any(r => r.Id == p.Resolver)) && double.IsFinite(p.LatencyMs) && p.LatencyMs >= 0);
    public void SaveProfile(GoodProfile profile) => Write("profiles.json", new[] { profile }.Concat((Read<GoodProfile[]>("profiles.json") ?? []).Where(x => x.Network != profile.Network)).Take(20).ToArray());
}
public sealed class Journal(Store store)
{
    public string Log(Problem p, string phase = "")
    {
        var dir = Path.Combine(store.Root, "kayitlar"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"hata-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { time = DateTimeOffset.Now, phase, problem = p }, Product.Json), new UTF8Encoding(false));
        foreach (var old in new DirectoryInfo(dir).GetFiles("hata-*.json").OrderByDescending(x => x.CreationTimeUtc).Skip(30)) old.Delete();
        return file;
    }
}
public static class Problems
{
    public static Problem From(Exception e) => e switch
    {
        System.ComponentModel.Win32Exception { NativeErrorCode: 1223 } => new("CANCELLED", "Windows izin penceresi iptal edildi.", "Kurulum veya güncelleme uygulanmadı.", "Devam etmek isterseniz kurulum dosyasını tekrar açın.", "warning"),
        System.ComponentModel.Win32Exception { NativeErrorCode: 5 } => new("PERMISSION", "Windows işlemi için gerekli izin alınamadı.", "İşlem uygulanmadı.", "Kurulumdaki Windows izin penceresini onaylayın."),
        System.ComponentModel.Win32Exception { NativeErrorCode: 32 } => new("FILE_IN_USE", "Dosya başka bir işlem tarafından kullanılıyor.", "Dosyalar değiştirilemedi.", "AlterDPI'ı tepsi menüsünden kapatıp yeniden deneyin.", "warning"),
        OperationCanceledException => new("CANCELLED", "İşlem iptal edildi veya ağ değişti.", "Eski seçim durduruldu.", "Güncel bağlantı ile yeniden deneyin.", "warning"),
        UnauthorizedAccessException => new("PERMISSION", "Gerekli erişim izni yok.", "İşlem uygulanamadı.", "Kurulumu yönetici izniyle tamamlayın."),
        _ => e.Message switch
        {
            "NO_NETWORK" => new("NO_NETWORK", "Kullanılabilir internet rotası yok.", "Bağlantı yöntemi seçilemiyor.", "Kablo, Wi-Fi ve ağın giriş sayfasını kontrol edin.", "warning"),
            "POLICY_CONFLICT" => new("POLICY_CONFLICT", "Mevcut kurum veya özel DNS politikası ile çakışma var.", "Bu politika değiştirilmedi; koruma kapsamı sınırlı olabilir.", "Ağ yöneticisinin izin verdiği bağlantıda deneyin.", "warning"),
            "DNS_PORT" => new("DNS_PORT", "Yerel DNS portu başka bir program tarafından kullanılıyor.", "Ortak DNS yöneticisi başlatılamadı.", "Diğer yerel DNS uygulamasının ayarlarını kontrol edin."),
            "INTEGRITY" => new("INTEGRITY", "Dosya imzası veya özeti doğrulanamadı.", "Dosya kullanılmadı.", "Resmî depodan doğru paketi yeniden indirin."),
            "NO_PROFILE" => new("NO_PROFILE", "Denenen bağlantı yolları doğrulanamadı.", "Erişim etkinleştirilemedi; önceki durum korundu.", "Normal interneti, sağlayıcı kısıtını ve kayıt dosyasını kontrol edin."),
            "TRANSIENT" => new("REMOTE_LIMIT", "Uzak servis geçici olarak isteği reddetti.", "Ağ profili için kesin sonuç alınamadı.", "Bir süre sonra tekrar deneyin.", "warning"),
            "DNS_UNAVAILABLE" => new("DNS_UNAVAILABLE", "DNS yollarından yanıt alınamadı.", "Yeni alan adları çözülemeyebilir; yerel engel listesi korunuyor.", "İnternet bağlantısını ve DNS erişimini kontrol edin.", "warning"),
            "ENGINE_BUSY" => new("ENGINE_BUSY", "Başka bir paket işleme uygulaması çalışıyor.", "Çakışma nedeniyle ikinci motor başlatılmadı.", "Diğer kopyayı kapatıp yeniden deneyin."),
            "ENGINE_START" => new("ENGINE_START", "Ağ motoru çalışır durumda kalmadı.", "Bağlantı uyarlaması uygulanamadı.", "Windows sürücü, imza ve güvenlik yazılımı olaylarını inceleyin."),
            "PIPE_IDENTITY" => new("PIPE_IDENTITY", "Arka plan hizmetinin kimliği doğrulanamadı.", "Güvenilmeyen bağlantı reddedildi.", "Resmî kurulum paketini yeniden çalıştırın."),
            "INSTALL_ROOT" or "SERVICE_OWNER" => new("INSTALL_OWNER", "Kurulumun dosya veya hizmet sahipliği doğrulanamadı.", "Dosyalar güvenlik için değiştirilmedi.", "Mevcut kurulum klasörünü ve kayıtları inceleyin."),
            "APP_RUNNING" => new("APP_RUNNING", "AlterDPI hâlâ açık.", "Güncelleme dosyaları değiştirilmedi.", "Tepsi menüsünden Çıkış seçip kurulumu yeniden açın."),
            "UPDATE" => new("UPDATE", "Güncelleme kaynağından doğrulanmış paket alınamadı.", "Mevcut sürüm korundu.", "İnternet erişimini ve resmî sürüm sayfasını kontrol edin.", "warning"),
            _ => new("INTERNAL", "İşlem beklenmedik bir hata nedeniyle tamamlanamadı.", "Değişiklikler geri alınmaya çalışıldı.", "Tarih damgalı kaydı kontrol edip tekrar deneyin.")
        }
    };
}
