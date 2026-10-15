# Pilot doğrulaması

15 Ekim 2026 · AlterDPI 0.5.0fx · Furkan Okan

- Derleme: sıfır hata ve uyarı.
- Temel kontroller: **81 başarılı**, sıfır başarısız.
- Eklenti kontrolleri: **21 başarılı**, sıfır başarısız. Eşitleme başarısızken iznin kaldırılması ve zaman aşımı dahildir.
- Eski DNS yedeği: **12 başarılı**; gerçek Windows PowerShell sürümüyle metin, IPv4 nesne yedeği, IPv6 metni ve geçersiz adresler sınandı.
- Arayüz yerleşimi: **8 başarılı**; normal pencere çerçevesi/görev çubuğu düğmesi yokluğu, kaydırmasız ana/ayar ekranları ve küçük alana sığdırma sınandı. Gerçek tepsi tıklaması yerine yerleşim testi yapılmıştır.
- Dört şifreli DNS sağlayıcısı zararsız bir sorguya yanıt verdi; Quad9 HTTP/2 yolu çalıştı. Bunlar tek ortam ölçümleridir.
- Hop limit ölçümleri önce/sonra **65**. IP, MTU veya IPv6 değiştiren komut eklenmedi.
- İlk pilot grafik kurulumunda hizmet LocalSystem olarak çalıştı; dosya ve durum klasörleri Administrators sahipliğinde korundu. İlk özellik tercihleri kapalıydı.
- Canlı kontrolde Windows'un SYSTEM işlem yolunu normal kullanıcıya göstermediği görüldü. İletişimde hizmetin korumalı yapılandırması ve çekirdeğin bildirdiği işlem kimliği SCM üzerinden doğrulanacak şekilde düzeltildi. İstemci görüntüsü ve kullanıcı kimliği hizmet tarafında denetlenir.
- Son paketin çalıştırılması Windows Uygulama Denetimi tarafından **VerifiedAndReputableDesktop / 3077 / 0xc0e90002** ile engellendi. Bu nedenle son sürümün canlı hizmet–arayüz iletişimi, etkinleştirme, uygulama/oyun/ses, yeniden başlatma ve gerçek ağ geçişi kabul testleri tamamlanmış sayılmaz.

Kaynak güvenlik incelemesi dört sınırlı bulgu üretti. Özel DNS etiketleri, izin süresi/önbelleği, tarayıcıda izin sonlandırma ve ilk durum klasörünün oluşturulması düzeltildi; ilgili çevrimdışı testler geçti. İnceleme raporu düzeltmelerden önceki kaynak kopyasını kaydeder. Üçüncü taraf motor/sürücü ve tüm bağımlılık iç kodları eksiksiz denetlenmiş sayılmaz.

Kurulum RSA-PSS ile doğrulanan paket metadatası içerir; bu **Windows kod imzası değildir**. Bu bilgisayardaki Smart App Control tek uygulamaya özel izin sunmaz. Canlı pilotu sürdürmeden önce güvenilir yayıncı imzası ve bileşenlerin uyumu tamamlanmalıdır. Windows ayarının sonradan değiştirilmesi bir kabul testi sayılmaz. Güvenlik politikasını aşan bir yol eklenmedi. [İmzalama adımları](KOD-IMZASI.md).

Önceki deneme kurulumu kaldırıldı: AlterDPI hizmeti, program klasörü ve uygulamaya ait DNS kuralı kalmadığı doğrulandı. Önceden çalışan paket korunuyor. Son kurulum paketinin kaldırma ekranı düzeltmesi derlendi; son paket canlı çalıştırılmadığı için bu ekranın uçtan uca doğrulaması bekliyor.

Genel uygulama yayını, imzalama sırları ve üretim veri akışı henüz etkinleştirilmedi. Kaynak yayınında CI kontrolleri çalışır; veri işi ayrıca açılana kadar kapalıdır. Windows'un başlangıç seçeneği arayüz/özelliklerin oturumda açılışını yönetir; ağ özellikleri kapalıyken arka plan hizmeti boşta kalabilir. Tarayıcı pilotu elle kurulup tarayıcıdan kaldırılır.

Hizmet/oyun açıkken önce–sonra gecikme ve kaynak tüketimi karşılaştırması, son paket çalıştırılamadığı için tamamlanmadı; performans kazancı iddia edilmez.

Kullanıcının pilot denemesinden sonra bağlantı seçimi, eski DNS yedeği ve kapanış/kaldırma hataları düzeltildi. Eski pilot bu bilgisayardan kaldırıldı; hizmet, program klasörü ve uygulamaya ait DNS kuralı kalmadığı ölçüldü. [Düzeltmeler](DUZELTMELER.md). Yeni kodun canlı kabul testleri hâlâ bekliyor.
