# Windows kod imzası

15 Ekim 2026 · AlterDPI 0.5.0fx · Furkan Okan

Mevcut paket içindeki bütünlük imzası, Windows'un güvendiği yayıncı imzasının yerine geçmez. Kendimizin oluşturacağı sertifika da genel dağıtım için yeterli değildir. [Microsoft açıklaması](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control).

## Ücretsiz seçenek: SignPath Foundation

1. Kaynakları ve gerekli üçüncü taraf lisanslarını herkese açık `fxrkqnplus/alterdpi` deposunda tut. Depoya özel anahtar, kişisel günlük veya ağ yedeği ekleme. Yayın yalnız pilot durumunu açıkça belirten kaynakları kapsar; Windows imzasız kurulumun genel dağıtımı henüz hazır değildir.
2. GitHub hesabında iki aşamalı doğrulamayı etkinleştir. Projenin amacı, destek sınırları ve pilot durumu README'de açık kalmalı.
3. [SignPath Foundation](https://signpath.org/) üzerindeki **Apply** yolunu kullan. Bu bir masaüstü programı değildir. Başvuruda projenin ağ trafiğine müdahalesini ve kullandığı üçüncü taraf motor/sürücüyü açıkla. Yalnız kendi yazılımımızın imzalanması istenir; üçüncü taraflar bize aitmiş gibi imzalanmaz.
4. Kabul edilirsek hizmetin verdiği proje/politika bilgileriyle GitHub derlemesi bağlanır ve imzalama politikası yayımlanır. Her yayın imzalama onayı ister. Hesap parolası veya özel anahtar sohbet üzerinden paylaşılmaz.

Ücretsiz hizmetin kabulü garanti değildir. Yayımlanmış ve bakımı süren proje, doğrulanabilir kaynak–derleme ilişkisi, güvenlik ve bilinirlik koşulları vardır; yeni bir proje için hemen sonuçlanmayabilir. Ağ aracımızın uygunluğunu sağlayıcının değerlendirmesi gerekir. Henüz SignPath başvurusu veya bağlantısı yapılmadı. [Koşullar](https://signpath.org/terms).

## Alternatif: güvenilir sağlayıcıdan sertifika

Türkiye'de gerçek kişi/kurum başvurusu kabul eden bir kod imzalama sağlayıcısının uygunluğunu ve toplam ücretini satın almadan önce doğrula. Yayıncı kimliği kontrol edilir; özel anahtar sağlayıcının desteklediği donanım veya bulut hizmetinde korunur. Microsoft'un güncel belgesinde Azure Artifact Signing bireysel başvuruları ABD/Kanada ile sınırlıdır; Türkiye'deki bireysel başvuru için uygun olduğu varsayılmamalıdır. [Microsoft seçenekleri](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).

## İmzalama sırasında yapılacaklar

Sağlayıcı seçilince derleme otomasyonuna Windows Authenticode adımı eklenecek; mevcut `build.ps1` bunu henüz yapmaz. Doğru sıra:

1. AlterDPI'ın kendi derlenmiş çalıştırılabilir dosyaları ve kitaplıkları SHA-256 ve zaman damgasıyla imzalanıp doğrulanır. Üçüncü tarafların mevcut imzaları ve dosyaları korunur; uyumlulukları ayrıca test edilir.
2. Son dosyalardan `application.json` / `application.sig` üretilir. Kurulumdaki kaldırıcı da derleme sırasında imzalanır; kurulum dosyası oluşturulur ve imzalanır.
3. İmzalanmış son kurulum dosyasından `update.json` / `update.sig` yeniden üretilir. Sonradan dosya değiştirilmez.
4. Windows imzası doğrulanır; güvenlik korumaları açıkken temiz kurulum/kaldırma, hizmet iletişimi, gerçek uygulama/oyun/ses ve ağ geçişi testleri tamamlanır.

Yerel sertifika yolunda Windows SDK içindeki **SignTool** kullanılabilir. Bir imza bütün programın güvenliğini veya bütün Windows politikalarında çalışacağını tek başına garanti etmez. [SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool).
