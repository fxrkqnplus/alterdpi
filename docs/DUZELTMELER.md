# Pilot düzeltmeleri

15 Ekim 2026 · 0.5.0fx · Furkan Okan

- **Kurulum dili:** NSIS girişi açıkça UTF-8 okunur. Satır sonları NSIS biçimine çevrildi; kurulum/kaldırma metinleri ve marka görseli düzenlendi.
- **Tepsi paneli:** başlık çubuğu, görev çubuğu düğmesi ve kaydırma çubuğu kaldırıldı. Saat yanındaki simge paneli açıp gizler; başka yere tıklamak veya Escape gizler. Kısa geçiş animasyonu, yuvarlak düğmeler ve küçük ekranlara sığdırma eklendi. Simgenin taşma alanında veya doğrudan saat yanında görünmesini Windows'un kullanıcı ayarı belirler.
- **Bağlantı seçimi:** tek bir hedefin geçici reddi profil aramasını durdurmaz. Genel bağlantı ve en az bir korunan hedef doğrulanmadan başarı verilmez. Kontrollerin bir kısmı sonuçsuzsa sarı durum gösterilir; oyun veya ses başarısı iddia edilmez.
- **DNS geri yükleme:** eski kurallardaki DNS sunucularının nesne olarak yedeklenmesi giderildi. Mevcut IPv4 nesne yedekleri de okunur; belirsiz veya geçersiz adres tahmin edilmez. Bu hata hizmetin durmasını, Çıkış'ı ve kaldırmayı bozuyordu.
- **Kapanış:** geri alma hatası pencereyi açık tutmaz. Hizmet kapanış hataları yakalanır; kapatma isteği yeniden etkinleştirme işlemine dönüşmez. Tamamlanmamış geri alma hizmet çalışırken tekrar denenir.
- **Kaldırma/onarım:** durma aşamasındaki hizmet beklenir; aynı oturumdaki yalnız uygulamaya ait arayüz kapatılır. Mevcut uygulama hizmeti yeniden oluşturulmaya çalışılmadan onarılır.
- **Kayıtlar:** ana hata kodunun yanında istisna türü, Windows hata değeri, alt işlem çıkış kodu ve yöntem adları saklanır. Gezinti adresleri, ham komut hataları ve kişisel dosya yolları kayda eklenmez. Arayüzde kısa özet ve ayrıntı bağlantısı gösterilir.

Bu bilgisayardaki eski pilotun bozuk DNS yedeği yedeklenip onarıldı; eski hizmet/arayüz ve kurulum kaldırıldı. Uygulamaya ait DNS kuralı kalmadığı ve hop limitin **65** olduğu ölçüldü. Yeni kaynak için çevrimdışı testler ve kurulum derlemesi yapılmıştır. Yeni paketin gerçek kurulum/kaldırma, tepsi etkileşimi, uygulama/oyun/ses ve ağ geçişi kabul testleri tamamlanmış sayılmaz; Windows yayıncı imzası bekleniyor. Önceki güvenlik raporu bu değişikliklerin yeniden taraması değildir.
