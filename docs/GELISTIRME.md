# Geliştirme ve yayın

Windows x64, .NET SDK 10.0.402, Node.js 24 ve NSIS 3.13 gerekir. Son kullanıcının .NET kurmasına gerek yoktur.

```powershell
dotnet build src/AlterDPI.App -c Release
dotnet run --project tests/AlterDPI.Tests -- "$pwd"
node tests/browser.test.cjs
powershell -NoProfile -ExecutionPolicy RemoteSigned -File tests/network.test.ps1
dotnet run --project tests/AlterDPI.UiTests
./tools/build.ps1 -UnsignedCi
```

Gerçek DNS yollarının zararsız sorgu testi isteğe bağlıdır: test komutunun sonuna `--network` eklenir. Bu test ağ ayarı değiştirmez. Birim testleri oyun, ses veya yeniden başlatma doğrulaması değildir.

İmzalı yerel kurulum için `build.ps1 -Keys <depo-dışındaki-anahtar-klasörü>` kullanılır. Çıktı `artifacts/release` klasöründedir. Özel anahtarlar DPAPI ile kullanıcıya bağlı korunur; depoya veya paylaşım paketine eklenmez.

GitHub'da korunan **release** environment'ına `ALTERDPI_APPLICATION_SIGNING_KEY` ve `ALTERDPI_DATA_SIGNING_KEY` PEM sırları tanımlanmadan yayın yapılmamalıdır. Anahtarlar gömülü `assets/trust.json` açık anahtarlarıyla eşleşmelidir; paketleyici eşleşmeyen imzayı reddeder. Anahtar aktarımı güvenli ortamda yapılır; konsola yazdırılmaz. Anahtar değişimi yeni güven kökleri içeren, eski anahtarla doğrulanabilen bir uygulama sürümü gerektirir.

**Pilot paket** iş akışı yalnız indirmeye hazır çıktı üretir. İncelemeden sonra `AlterDPI-Kurulum.exe`, `update.json` ve `update.sig` aynı GitHub sürümüne eklenir. **İmzalı koruma verisi** iş akışı, yapılandırılan imzalama sırrıyla günlük veri sürümünü yeniler. Anahtar ve korunan yayın ortamı hazır olduktan sonra depo değişkeni `ALTERDPI_DATA_UPDATES_ENABLED=true` yapılır; öncesinde zamanlanmış veri işi çalışmaz. Kaynak yayını CI kontrollerini başlatır; uygulama/veri yayını ayrıca etkinleştirilir.

RSA-PSS/SHA-256 paket imzası Windows Authenticode sertifikası değildir. Windows pilotu engelleyebilir. Genel dağıtımda ayrıca [kod imzalama](KOD-IMZASI.md) ve tarayıcı mağaza incelemesi gerekir; mevcut yayın iş akışı Authenticode imzası üretmez.

Şifreli DNS sağlayıcılarının kısa ölçümü karşılaştırılır. Yeni ağda uygun TCP adaylarının başarısı ve gecikmesi değerlendirilir; aynı ağda kayıtlı yol doğrudan uygulanır. Sağlık kontrolü beş dakikada bir, veri kontrolü altı saatte bir yapılır. Veri yaşı arayüzde belirtilir. Başarısız adaylar her saniye yeniden denenmez.

Kaynaklar: [Microsoft dağıtım davranışı](https://learn.microsoft.com/en-us/dotnet/core/deploying/runtime-patch-selection), [Chrome kuralları](https://developer.chrome.com/docs/extensions/reference/api/declarativeNetRequest), [Quad9 HTTP/2](https://docs.quad9.net/FAQs/#dns-over-https-no-http11-support), [alan listesi](https://github.com/hagezi/dns-blocklists), [motor lisansı](https://www.apache.org/licenses/LICENSE-2.0).
