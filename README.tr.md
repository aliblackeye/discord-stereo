# Discord Stereo

[🇬🇧 English](README.md) · **🇹🇷 Türkçe**

Discord'a **gerçek stereo** ses ve **yüksek bitrate** (Discord'un ses codec'i olan Opus'un desteklediği en yüksek değer, 510 kbps'e kadar) göndermenizi sağlar — düşük gecikmeyle, doğrudan **mikrofon kanalından**. **Go Live / ekran paylaşımı gerektirmez.** Beraber müzik dinlerken, enstrüman çalarken veya stereo bir kaynağı paylaşırken sesin karşı tarafa **mono değil, sol/sağ ayrı** gitmesini sağlar.

Discord, mikrofon yolundaki sesi bant genişliği için tek kanala (mono) indirir. Bu araç, Discord'un ses modülüne (`discord_voice`) küçük, **imza tabanlı** yamalar uygulayarak bu indirmeyi durdurur ve kodlayıcıyı 2 kanala + yüksek bitrate'e alır. İmza tabanlı olduğu için **Discord sürümünden bağımsızdır**.

> ⚠️ Bu araç **kendi bilgisayarındaki kendi Discord istemcini** değiştirir. İstemci değişiklikleri Discord'un Hizmet Şartlarına aykırı olabilir; kullanım tamamen **kendi sorumluluğundadır**. Her şey yerelde çalışır — hiçbir veri hiçbir yere gönderilmez.

## Kurulum ve kullanım

1. [Son sürümden](../../releases/latest) **DiscordStereo.exe**'yi indir.
2. Çift tıkla. Basit bir menü açılır:
   - **1) Kur ve Discord'u başlat** — yamayı uygular, Discord'u açar.
   - **2) Kaldır** — her şeyi orijinaline döndürür.
   - **3) Otomatik başlatma** — her Windows açılışında otomatik uygular.
   - **4) Dil** — English / Türkçe.
3. Discord açılınca ses kanalına gir ve stereo kaynağını çal. Karşı taraf **kulaklıkla** dinlerse sol/sağ ayrımını duyar.

> Windows SmartScreen uyarı verirse (imzasız uygulama): **Ayrıntılar → Yine de çalıştır**. Dosya bütünlüğünü doğrulamak için [indirileni doğrulama](#i̇ndirileni-doğrulama).
>
> Discord **yönetici olarak** çalışıyorsa araç onu kapatamaz — sistem tepsisinden sağ tık → *Quit Discord* yapıp tekrar dene.

### Komut satırı (isteğe bağlı)

```
DiscordStereo.exe --apply         # yamala ve başlat
DiscordStereo.exe --restore       # orijinale döndür
DiscordStereo.exe --status        # durumu göster
DiscordStereo.exe --install       # her açılışta otomatik çalıştır
DiscordStereo.exe --uninstall     # otomatik çalıştırmayı kaldır
DiscordStereo.exe --bitrate 384000    # bitrate (8000–510000, varsayılan 510000)
DiscordStereo.exe --lang en|tr        # dili zorla
```

Dil ve bitrate tercihin `%LOCALAPPDATA%\DiscordStereo\settings.txt` içinde hatırlanır.

## En iyi ses için Discord ayarları

Yamalar işlemeyi zaten kapatır, ama garanti olsun diye Discord → **Ses ve Görüntü**:

- **Gürültü Bastırma (Krisp): Kapalı**
- **Yankı Giderme: Kapalı**
- **Otomatik Kazanç Kontrolü: Kapalı**
- **Gelişmiş Ses Etkinliği / Sinyal İşleme: Kapalı**

Kulaklık kullan (yankı gideren kapalıyken hoparlörde yankı olabilir).

## Nasıl çalışır

İki katman birlikte çalışır:

1. **JS kancası** (`index.js`): ses kodlayıcısını 2 kanala, bitrate'i seçilen değere alır ve mono'ya iten işlemeyi (gürültü/yankı/AGC) kapatır. Böylece mikrofon 2 kanal **yakalanır**.
2. **Native yamalar** (`discord_voice.node`, imza tabanlı, 3 nokta):
   - *Stereo*: gönderim akışında kanal sayısı 1 → 2.
   - *Downmix bypass*: yakalanan 2 kanalı mono'ya indiren dalı atlar (asıl düzeltme).
   - *High-pass off*: müzikte alçak frekansları kesen filtreyi kapatır.

Tüm değişiklikler `%LOCALAPPDATA%\DiscordStereo\backup\` altında orijinal SHA-256 ile **yedeklenir**; *Kaldır* birebir geri yükler. İmza dosyada tam bir kez eşleşmezse hiçbir şey yazılmaz (yanlış sürüme dokunmaz).

## Sorun giderme / SSS

**Karşı taraf hâlâ mono duyuyor.**
- **Kulaklıkla** dinlediğinden emin ol — hoparlör sol/sağı bulanıklaştırır.
- Kaynağın gerçekten stereo ve sol/sağı ayrı olmalı (belirgin pan'lı bir parçayla dene).
- `--status`'ün **STEREO AKTIF** dediğini doğrula. Discord güncellendiyse aracı tekrar çalıştır.

**Çalıştı ama Discord'u yeniden başlatınca durdu.**
- Discord'u **bu araçla** aç (menü → *Kur ve başlat*) ya da **otomatik başlatma**yı aç. Discord bazı manuel açılışlarda diskteki JS kancasını geri alabilir; araç yeniden uygular.

**Bitrate sınırlı görünüyor / kalite 510 kbps'ten düşük.**
- Discord, ses kanalı başına **sunucu tarafında bir bitrate üst sınırı** uygular. Boost'suz sunucular daha düşük kısar (çoğu zaman 64–96 kbps); boost'lu sunucular daha fazlasına izin verir. Araç istemci tarafında yüksek bitrate ister ama sunucu yine de kısabilir. Bu bir Discord sınırı, hata değil.

**"Discord kapatılamadı."**
- Discord yönetici olarak çalışıyor. Tepsiden *Quit Discord* yapıp tekrar dene ya da bu aracı da yönetici olarak çalıştır.

**Antivirüs / SmartScreen exe'yi işaretliyor.**
- Binary imzasız ve başka bir uygulamanın dosyalarını yamalıyor; bu sezgisel taramaları tetikler. Kaynaktan kendin derle (aşağıda) ya da yayınlanan hash'i doğrula.

**PTB / Canary'de çalışır mı?**
- Araç Stable, PTB, Canary ve Development'ı en yeniden başlayarak tespit eder. Öncelikli olarak Stable'da test edilmiştir.

## İndirileni doğrulama

Her sürüm `DiscordStereo.exe`'nin SHA-256'sını listeler (release'deki `SHA256SUMS.txt`). Kontrol:

```powershell
Get-FileHash .\DiscordStereo.exe -Algorithm SHA256
```

Değeri release sayfasındakiyle karşılaştır.

## Kaynaktan derleme

Windows'un .NET Framework 4.x ile gelen C# derleyicisini kullanır, harici bağımlılık yoktur:

```
build.cmd
```

Veya .NET SDK ile:

```
dotnet build -c Release
```

`DiscordStereo.exe` üretilir.

## Kalıcılık ve güncellemeler

- Discord kendini güncellediğinde modül dosyaları yenilenir ve yama gider. **Otomatik başlatma** açıksa, araç yeni sürümü tespit edip bir sonraki açılışta yeniden uygular.
- En garantisi Discord'u normal kısayoldan değil, **bu araçla** (ya da otomatik başlatma ile) açmandır.

## Teşekkür

Topluluğun stereo çalışmalarından esinlenilmiştir (edoStereo; DiscordVoicePatcher / Vencord voicePatcher). Bu proje bağımsız, tek dosyalık, sürümden bağımsız bir uygulamadır.

## Lisans

MIT — bkz. [LICENSE](LICENSE).
