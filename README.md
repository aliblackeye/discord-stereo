# Discord Stereo

Discord'a **gerçek stereo** ses ve **yüksek bitrate** (Opus tavanı 510 kbps) göndermenizi sağlar — düşük gecikmeyle. Beraber müzik dinlerken, enstrüman çalarken veya stereo bir kaynağı paylaşırken sesin karşı tarafa **mono değil, sol/sağ ayrı** gitmesini ister misiniz? Bu araç onu yapar; **Go Live / ekran paylaşımı gerektirmez**, doğrudan mikrofon kanalından gider.

Discord, mikrofon yolundaki sesi kalite/bant genişliği için tek kanala (mono) indirir. Bu araç, Discord'un ses modülüne (`discord_voice`) küçük, **imza tabanlı** yamalar uygulayarak bu indirmeyi durdurur ve kodlayıcıyı 2 kanala + yüksek bitrate'e alır. İmza tabanlı olduğu için **Discord sürümünden bağımsızdır**.

> **English:** Sends **true stereo** audio to Discord at a **high bitrate** (up to Opus max 510 kbps) with low latency, straight through the microphone channel — no Go Live / screen share needed. It applies small, **signature-based** patches to Discord's `discord_voice` module, so it is **version-independent**. See usage below.

---

## Kurulum ve kullanım

1. `DiscordStereo.exe`'yi indir (veya `build.cmd` ile derle — aşağıya bak).
2. Çift tıkla. Basit bir menü açılır:
   - **1) Kur ve Discord'u başlat** — yamaları uygular ve Discord'u açar.
   - **2) Kaldır** — her şeyi orijinaline döndürür.
   - **3) Otomatik başlatma** — her açılışta otomatik uygulanmasını açar/kapatır.
3. Discord açılınca ses kanalına gir ve stereo sesi çal. Karşı taraf **kulaklıkla** dinlerse sol/sağ ayrımını duyar.

**Not:** Discord **yönetici olarak** çalışıyorsa araç onu kapatamaz. O durumda önce sistem tepsisinden sağ tık → *Quit Discord* yapıp tekrar dene.

### Komut satırı (isteğe bağlı)

```
DiscordStereo.exe --apply        # yamala ve başlat
DiscordStereo.exe --restore      # orijinale döndür
DiscordStereo.exe --status       # durumu göster
DiscordStereo.exe --install      # her açılışta otomatik çalıştır
DiscordStereo.exe --uninstall    # otomatik çalıştırmayı kaldır
DiscordStereo.exe --bitrate 384000   # bitrate'i ayarla (8000–510000, varsayılan 510000)
```

## En iyi ses için Discord ayarları

Yamalar işlemeyi zaten kapatır, ama garanti olsun diye Discord → **Ses ve Görüntü**:

- **Gürültü Bastırma (Krisp): Kapalı**
- **Yankı Giderme: Kapalı**
- **Otomatik Kazanç Kontrolü: Kapalı**
- **Gelişmiş Ses Etkinliği / Sinyal İşleme: Kapalı**

Kulaklık kullan (hoparlörde yankı olabilir, çünkü yankı gideren kapalı).

## Nasıl çalışır

İki katman birlikte çalışır:

1. **JS kancası** (`index.js`): ses bağlantısının kodlayıcısını 2 kanala, bitrate'i seçilen değere alır ve mono'ya iten işlemeyi (gürültü/yankı/AGC) kapatır. Böylece mikrofon 2 kanal **yakalanır**.
2. **Native yamalar** (`discord_voice.node`, imza tabanlı, 3 nokta):
   - *Stereo*: gönderim akışında kanal sayısı 1 → 2.
   - *Downmix bypass*: yakalanan 2 kanalı mono'ya indiren dalı atlar (asıl düzeltme).
   - *High-pass off*: müzikte alçak frekansları kesen filtreyi kapatır.

Tüm değişiklikler **yedeklenir** (`%LOCALAPPDATA%\DiscordStereo\backup\`), dosya adında orijinal SHA-256 ile; *Kaldır* birebir geri yükler. İmzalar dosyada tam bir kez eşleşmezse hiçbir şey yazılmaz (yanlış sürüme dokunmaz).

## Derleme

.NET Framework 4.x ile gelen C# derleyicisini kullanır, harici bağımlılık yoktur:

```
build.cmd
```

`DiscordStereo.exe` üretilir. (Alternatif: `csc.exe ... src\Program.cs`.)

## Kalıcılık ve güncellemeler

- Discord kendini güncellediğinde modül dosyaları yenilenir ve yamalar gider. **Otomatik başlatma** açıksa, bir sonraki açılışta araç güncel sürümü tespit edip yeniden uygular.
- Discord'u tamamen kapatıp normal kısayoldan açarsan JS kancası bazen Discord tarafından geri alınabilir; en garantisi Discord'u **bu araçla** (menü → Kur ve başlat, ya da otomatik başlatma) açmandır.

## Uyarı / sorumluluk reddi

Bu araç **yalnızca kendi bilgisayarındaki kendi Discord istemcini** değiştirir. İstemci değişiklikleri Discord'un Hizmet Şartlarına aykırı olabilir; kullanım **tamamen kendi sorumluluğundadır**. Yazar hiçbir sorumluluk kabul etmez. Yamalar yereldir; hiçbir veri hiçbir yere gönderilmez.

## Teşekkür

Topluluğun stereo çalışmalarından esinlenilmiştir (edoStereo; DiscordVoicePatcher / Vencord voicePatcher). Bu proje bağımsız, tek dosyalık, sürümden bağımsız bir uygulamadır.

## Lisans

MIT — bkz. [LICENSE](LICENSE).
