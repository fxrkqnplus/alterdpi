# AlterDPI

<img src="assets/alterdpi.png" alt="AlterDPI" width="120">

**0.5.0fx · Furkan Okan · Windows 10/11 x64 pilot sürümü**

> Pilot doğrulaması sürüyor. Son kurulum paketinde henüz Windows yayıncı imzası yok; genel dağıtım için hazır değildir. [Doğrulama durumu](docs/DOGRULAMA.md) · [Kod imzalama adımları](docs/KOD-IMZASI.md)

AlterDPI, internet bağlantınıza uygun erişim yöntemini seçmeye yardımcı olur. Roblox, Discord ve Wattpad gibi hizmetlerde web ve uygulama erişimini kolaylaştırmayı hedefler. Son başarılı yöntemi hatırlar, ağ değişince bağlantıyı yeniden değerlendirir.

İsteğe bağlı web koruması bilinen zararlı alan adlarını engellemeye yardımcı olur. Küçük arayüz ve saat yanındaki simge üzerinden yönetilir.

## Kullanım

1. Sürümler bölümünden **AlterDPI-Kurulum.exe** dosyasını indirin ve açın.
2. Windows yönetici iznini onaylayıp kurulumu tamamlayın.
3. AlterDPI'ı açın ve **Erişimi etkinleştir** düğmesine tıklayın.

İlk kurulumda bütün özellikler kapalıdır. Web korumasını ayrıca açabilirsiniz. Özel uyarı ekranı için [tarayıcı kurulumunu](browser/KURULUM.html) tamamlayın. Pilot eklenti kurulumu tek seferlik manuel adım gerektirir.

Pencerenin X düğmesi uygulamayı saat yanına küçültür. Sağ tık → **Çıkış** kendi ağ değişikliklerini geri alır. Windows başlangıcını ayarlardan açıp kapatabilirsiniz. Kaldırmak için Windows uygulama listesinden AlterDPI'ı seçin; kayıtları silmek isteğe bağlıdır.

## Bilmeniz gerekenler

- VPN, trafik proxy'si veya farklı bir IP adresi sağlamaz.
- IP, MTU, IPv6 ve mevcut hop limit ayarını değiştirmez.
- Her sağlayıcıda, her hizmette veya bütün gelecek engellerde erişim garantisi yoktur.
- Web koruması bilinmeyen bütün tehditleri önleyemez. Kendi şifreli DNS'ini kullanan uygulamalar ve kurum politikaları kapsamı sınırlayabilir.
- Firefox pilot eklentisi geçicidir; tarayıcı yeniden açıldığında yeniden yüklemek gerekir.
- Geçici iznin bitmesi yeni DNS sorgularını ve tarayıcı geçişlerini yeniden sınırlar; önceden açık bağlantıları veya uygulamaların kendi önbelleklerini zorla kapatmaz.
- Kişisel ağ dökümü veya gezinti geçmişi geliştiriciye gönderilmez. DNS, bağlantı kontrolü ve güncelleme için gerekli ağ istekleri yapılır.

## Kullanım sorumluluğu

Yazılım olduğu gibi sunulur. Kullanım kararları ve yürürlükteki kurallara uygunluk kullanıcının sorumluluğundadır. Geliştirici, uygulanabilir hukuk ve lisans koşullarının izin verdiği ölçüde, kullanımdan doğan sonuçlar için sorumluluk üstlenmez. Bu metin yasal hakları veya zorunlu sorumlulukları ortadan kaldırmaz.

Gerekli üçüncü taraf bildirimleri [lisans belgelerinde](third_party/NOTICES.md) bulunur.
