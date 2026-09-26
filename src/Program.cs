// Discord Stereo — mikrofon/uygulama sesini Discord'a gerçek stereo ve yüksek bitrate ile,
// düşük gecikmeyle gönderir. Discord'un ses modülüne (discord_voice) imza tabanlı yamalar uygular;
// bu yüzden sürümden bağımsızdır. Tüm değişiklikler yedeklenir ve geri alınabilir.
//
// Bu araç YALNIZCA kendi bilgisayarındaki kendi Discord istemcini değiştirir. Kullanım riski sana aittir;
// istemci değişiklikleri Discord'un Hizmet Şartlarına aykırı olabilir.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DiscordStereo
{
    internal static class Program
    {
        const int PatchVersion = 1;
        const int DefaultBitrate = 510000; // Opus ses codec'inin desteklediği en yüksek bitrate (en yüksek kalite)
        const string Marker = "DISCORD-STEREO-PATCH";

        // -------- Native (discord_voice.node) imza tabanlı yamalar --------
        sealed class NativePatch
        {
            public string Name;
            public int[] Sig;      // -1 = joker bayt
            public int SigOffset;  // eşleşme başından yama noktasına uzaklık
            public byte[] Verify;  // yazmadan önce beklenen baytlar
            public byte[] Write;   // yazılacak baytlar
        }

        static readonly NativePatch[] NativePatches = new[]
        {
            new NativePatch {
                Name = "Stereo (kanal 1->2)",
                Sig = new[]{0xE8,-1,-1,-1,-1,0xBD,-1,0x00,0x00,0x00,0x80,0xBC,0x24,0x80,0x01,0x00,0x00,0x01},
                SigOffset = 6, Verify = new byte[]{0x01}, Write = new byte[]{0x02},
            },
            new NativePatch {
                Name = "Downmix bypass (mono indirmeyi atla)",
                Sig = new[]{0x48,0x89,0xF9,0xE8,-1,-1,-1,-1,0x84,0xC0,0x74,0x0D,0x83,0xBE,0x78,0x02,0x00,0x00,0x09,0x0F,0x8F,-1,-1,-1,-1,0x4C,0x89,0x6C,0x24,0x40,0x44,0x0F,0xB6,0xAC,0x24,-1,-1,0x00,0x00},
                SigOffset = 8,
                Verify = new byte[]{0x84,0xC0,0x74,0x0D,0x83,0xBE,0x78,0x02,0x00,0x00,0x09,0x0F,0x8F},
                Write  = new byte[]{0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0xE9},
            },
            new NativePatch {
                Name = "High-pass filtre off",
                Sig = new[]{0x48,0x8B,0x42,-1,0x45,0x84,0xC0,0x74,-1,0x48,0x85,0xC0,0x0F,0x84,-1,-1,-1,-1,0x45,0x31,0xF6,0x48,0x8D,0x5C,0x24,-1},
                SigOffset = -30, Verify = new byte[]{0x41}, Write = new byte[]{0xC3},
            },
        };

        // -------- JS kancası (index.js) --------
        const string AnchorBind = "function bindConnectionInstance(instance) {";
        const string AnchorConnOptions = "        setTransportOptions: (options) => instance.setTransportOptions(options),";
        const string PatchedConnOptions = "        setTransportOptions: (options) => instance.setTransportOptions(discordStereo.connectionOptions(instance, options)), /* DISCORD-STEREO-PATCH */";
        const string AnchorExports = "module.exports = VoiceEngine;";
        const string PatchedExports = "discordStereo.installEngineHooks(VoiceEngine); /* DISCORD-STEREO-PATCH */";

        const string JsTemplate = @"/* DISCORD-STEREO-PATCH BEGIN v__VERSION__ orig-sha256=__ORIG_SHA256__ */
// Mikrofonu/sesi stereo + yüksek bitrate ile gönderen kanca (DiscordStereo ekledi).
const discordStereo = (() => {
    const CHANNELS = 2;
    const BITRATE = __BITRATE__;
    const PROCESSING_OFF = [
        'echoCancellation', 'echoCancellationPreEcho', 'builtInEchoCancellation',
        'noiseSuppression', 'noiseCancellation', 'noiseCancellationDuringProcessing', 'automaticGainControl',
    ];
    const connections = new WeakMap();
    let lastMessage = null;
    function say(message) {
        if (message === lastMessage) return;
        lastMessage = message;
        try { log('info', `[DiscordStereo] ${message}`); } catch (e) {}
    }
    function forceProcessingOff(value, path, changed, depth) {
        if (value == null || typeof value !== 'object' || depth > 4) return;
        for (const key of Object.keys(value)) {
            const current = value[key];
            if (PROCESSING_OFF.includes(key) && (typeof current === 'boolean' || typeof current === 'number')) {
                if (current) { value[key] = typeof current === 'boolean' ? false : 0; changed.push(path + key); }
            } else if (current != null && typeof current === 'object') {
                forceProcessingOff(current, `${path}${key}.`, changed, depth + 1);
            }
        }
    }
    function connectionOptions(instance, options) {
        try {
            if (options == null || typeof options !== 'object') return options;
            const encoder = options.audioEncoder;
            if (encoder != null && typeof encoder === 'object') {
                const wasMono = !(encoder.channels >= CHANNELS);
                connections.set(instance, wasMono);
                if (wasMono) {
                    encoder.channels = CHANNELS;
                    if (encoder.params != null && typeof encoder.params === 'object' && 'stereo' in encoder.params) encoder.params.stereo = '1';
                    if (encoder.fec === true) encoder.fec = false;
                    options.encodingVoiceBitRate = BITRATE;
                    if (options.fec === true) options.fec = false;
                    say(`stereo on: channels=${CHANNELS} bitrate=${BITRATE}`);
                }
            } else if (connections.get(instance) === true && typeof options.encodingVoiceBitRate === 'number' && options.encodingVoiceBitRate !== BITRATE) {
                options.encodingVoiceBitRate = BITRATE;
            }
        } catch (e) { say(`connectionOptions error: ${e && e.message}`); }
        return options;
    }
    function wrapEngine(engine, name, transform) {
        const original = engine[name];
        if (typeof original !== 'function') return;
        engine[name] = function (...args) {
            try { transform(args); } catch (e) { say(`${name} error: ${e && e.message}`); }
            return original.apply(engine, args);
        };
    }
    function installEngineHooks(engine) {
        for (const name of ['setTransportOptions', 'setEmitVADLevel', 'startLocalAudioRecording', 'setLoopback']) {
            wrapEngine(engine, name, (args) => { const c = []; args.forEach((a, i) => forceProcessingOff(a, `arg${i}.`, c, 0)); if (c.length) say(`${name}: processing off`); });
        }
        wrapEngine(engine, 'setVoiceChannelCountCap', (args) => { if (typeof args[0] === 'number' && args[0] > 0 && args[0] < CHANNELS) args[0] = 0; });
        say(`loaded: channels=${CHANNELS} bitrate=${BITRATE}`);
    }
    return { connectionOptions, installEngineHooks };
})();
/* DISCORD-STEREO-PATCH END */
";

        // ================= Install tespiti =================
        sealed class Install
        {
            public string Channel;   // Discord / DiscordPTB / DiscordCanary
            public string Version;
            public string NodePath;
            public string IndexPath;
            public string UpdateExe;
            public string ExeName;    // Discord.exe
            public string ProcName;   // Discord
        }

        static List<Install> FindInstalls()
        {
            var result = new List<Install>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var channels = new[]
            {
                new { Dir = "Discord",          Exe = "Discord.exe",          Proc = "Discord" },
                new { Dir = "DiscordPTB",       Exe = "DiscordPTB.exe",       Proc = "DiscordPTB" },
                new { Dir = "DiscordCanary",    Exe = "DiscordCanary.exe",    Proc = "DiscordCanary" },
                new { Dir = "DiscordDevelopment",Exe= "DiscordDevelopment.exe",Proc = "DiscordDevelopment" },
            };
            foreach (var ch in channels)
            {
                string root = Path.Combine(localAppData, ch.Dir);
                if (!Directory.Exists(root)) continue;
                var apps = Directory.GetDirectories(root, "app-*")
                    .Select(d => new { Path = d, Ver = ParseVersion(Path.GetFileName(d).Substring(4)) })
                    .Where(x => x.Ver != null).OrderByDescending(x => x.Ver).ToList();
                foreach (var app in apps)
                {
                    string modules = Path.Combine(app.Path, "modules");
                    if (!Directory.Exists(modules)) continue;
                    var voiceDirs = Directory.GetDirectories(modules, "discord_voice-*")
                        .OrderByDescending(d => { int n; return int.TryParse(Path.GetFileName(d).Substring("discord_voice-".Length), out n) ? n : 0; });
                    foreach (var vd in voiceDirs)
                    {
                        string node = Path.Combine(vd, "discord_voice", "discord_voice.node");
                        string index = Path.Combine(vd, "discord_voice", "index.js");
                        if (File.Exists(node) && File.Exists(index))
                        {
                            result.Add(new Install {
                                Channel = ch.Dir, Version = Path.GetFileName(app.Path).Substring(4),
                                NodePath = node, IndexPath = index,
                                UpdateExe = Path.Combine(root, "Update.exe"), ExeName = ch.Exe, ProcName = ch.Proc,
                            });
                            break; // bu app için en yeni voice modülü yeterli
                        }
                    }
                    break; // en yeni app yeterli
                }
            }
            return result;
        }

        static Version ParseVersion(string s) { Version v; return Version.TryParse(s, out v) ? v : null; }

        // ================= Yardımcılar =================
        static string Sha256(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", ""); }
        static string Sha256File(string p) { return Sha256(File.ReadAllBytes(p)); }

        static string BackupDir(Install i)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordStereo", "backup", i.Channel + "-" + i.Version);
            Directory.CreateDirectory(dir);
            return dir;
        }

        // sig içinde -1 joker. Dönüş: -1 yok, -2 birden fazla, >=0 tek eşleşme indeksi.
        static int FindSig(byte[] data, int[] sig)
        {
            int found = -1, n = sig.Length, limit = data.Length - n, first = sig[0];
            for (int i = 0; i <= limit; i++)
            {
                if (data[i] != first) continue;
                bool ok = true;
                for (int j = 1; j < n; j++) { int s = sig[j]; if (s >= 0 && data[i + j] != s) { ok = false; break; } }
                if (ok) { if (found >= 0) return -2; found = i; }
            }
            return found;
        }

        static bool SeqEqual(byte[] data, int at, byte[] expect)
        {
            if (at < 0 || at + expect.Length > data.Length) return false;
            for (int k = 0; k < expect.Length; k++) if (data[at + k] != expect[k]) return false;
            return true;
        }

        enum SiteState { Original, Patched, NoSig, Ambiguous, Unexpected }

        static SiteState ResolveNative(byte[] data, NativePatch p, out int at)
        {
            at = -1;
            int m = FindSig(data, p.Sig);
            if (m == -2) return SiteState.Ambiguous;
            if (m < 0)
            {
                // Yama imzanın içindeyse orijinal imza bulunmaz; yamalı imzayı dene.
                if (p.SigOffset >= 0 && p.SigOffset + p.Write.Length <= p.Sig.Length)
                {
                    var ps = (int[])p.Sig.Clone();
                    for (int k = 0; k < p.Write.Length; k++) ps[p.SigOffset + k] = p.Write[k];
                    int pm = FindSig(data, ps);
                    if (pm >= 0) { at = pm + p.SigOffset; return SiteState.Patched; }
                }
                return SiteState.NoSig;
            }
            at = m + p.SigOffset;
            if (SeqEqual(data, at, p.Write)) return SiteState.Patched;
            if (SeqEqual(data, at, p.Verify)) return SiteState.Original;
            return SiteState.Unexpected;
        }

        // ================= Native apply/restore =================
        static void ApplyNative(Install i, bool quiet)
        {
            byte[] data = File.ReadAllBytes(i.NodePath);
            string origHash = Sha256(data);
            var plan = new List<KeyValuePair<NativePatch, int>>();
            foreach (var p in NativePatches)
            {
                int at; var st = ResolveNative(data, p, out at);
                switch (st)
                {
                    case SiteState.Patched: if (!quiet) Console.WriteLine("  [" + p.Name + "] zaten uygulanmış."); break;
                    case SiteState.Original: plan.Add(new KeyValuePair<NativePatch, int>(p, at)); break;
                    case SiteState.Ambiguous: throw new Exception("[" + p.Name + "] imzası birden fazla yerde; iptal.");
                    case SiteState.NoSig: throw new Exception("[" + p.Name + "] imzası bulunamadı. Discord sürümü desteklenmiyor olabilir; hiçbir şey yazılmadı.");
                    case SiteState.Unexpected: throw new Exception("[" + p.Name + "] noktasında beklenmeyen baytlar; iptal.");
                }
            }
            if (plan.Count == 0) { if (!quiet) Console.WriteLine("  Native: zaten tam."); return; }

            string backup = Path.Combine(BackupDir(i), "discord_voice.node." + origHash + ".orig");
            if (!File.Exists(backup)) File.WriteAllBytes(backup, data);
            if (Sha256File(backup) != origHash) throw new Exception("Yedek doğrulanamadı.");

            foreach (var kv in plan)
                for (int k = 0; k < kv.Key.Write.Length; k++) data[kv.Value + k] = kv.Key.Write[k];
            File.WriteAllBytes(i.NodePath, data);

            byte[] after = File.ReadAllBytes(i.NodePath);
            foreach (var kv in plan)
                if (!SeqEqual(after, kv.Value, kv.Key.Write)) throw new Exception("Doğrulama başarısız: " + kv.Key.Name);
            if (!quiet) Console.WriteLine("  Native: " + plan.Count + " nokta yamalandı.");
        }

        static void RestoreNative(Install i)
        {
            var backups = Directory.Exists(BackupDir(i))
                ? Directory.GetFiles(BackupDir(i), "discord_voice.node.*.orig") : new string[0];
            string good = backups.FirstOrDefault(b =>
            {
                string h = Sha256File(b);
                return Path.GetFileName(b) == "discord_voice.node." + h + ".orig";
            });
            if (good == null) { Console.WriteLine("  Native: sağlam yedek yok, atlandı."); return; }
            if (Sha256File(i.NodePath) == Sha256File(good)) { Console.WriteLine("  Native: zaten orijinal."); return; }
            File.Copy(good, i.NodePath, true);
            Console.WriteLine("  Native: orijinale döndürüldü.");
        }

        // ================= JS apply/restore =================
        static int CountOccurrences(string s, string sub)
        {
            int c = 0, idx = 0;
            while ((idx = s.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0) { c++; idx += sub.Length; }
            return c;
        }

        static bool IsJsPatched(Install i) { return File.ReadAllText(i.IndexPath).Contains(Marker); }

        static void ApplyJs(Install i, int bitrate, bool quiet)
        {
            string text = File.ReadAllText(i.IndexPath);
            if (text.Contains(Marker)) { if (!quiet) Console.WriteLine("  JS: zaten uygulanmış."); return; }
            foreach (var a in new[] { AnchorBind, AnchorConnOptions, AnchorExports })
                if (CountOccurrences(text, a) != 1)
                    throw new Exception("JS: beklenen satır bulunamadı (Discord sürümü farklı olabilir); hiçbir şey yazılmadı.");

            string origHash = Sha256File(i.IndexPath);
            string backup = Path.Combine(BackupDir(i), "index.js." + origHash + ".orig");
            if (!File.Exists(backup)) File.Copy(i.IndexPath, backup, false);

            string block = JsTemplate
                .Replace("__VERSION__", PatchVersion.ToString())
                .Replace("__ORIG_SHA256__", origHash)
                .Replace("__BITRATE__", bitrate.ToString())
                .Replace("\r\n", "\n");
            string patched = text
                .Replace(AnchorBind, block + "\n" + AnchorBind)
                .Replace(AnchorConnOptions, PatchedConnOptions)
                .Replace(AnchorExports, PatchedExports + "\n" + AnchorExports);
            File.WriteAllText(i.IndexPath, patched, new UTF8Encoding(false));
            if (!quiet) Console.WriteLine("  JS: kanca eklendi (bitrate " + (bitrate / 1000) + " kbps).");
        }

        static void RestoreJs(Install i)
        {
            if (!IsJsPatched(i)) { Console.WriteLine("  JS: zaten orijinal."); return; }
            var backups = Directory.Exists(BackupDir(i))
                ? Directory.GetFiles(BackupDir(i), "index.js.*.orig") : new string[0];
            string good = backups.FirstOrDefault(b =>
            {
                string h = Sha256File(b);
                return Path.GetFileName(b) == "index.js." + h + ".orig";
            });
            if (good == null) { Console.WriteLine("  JS: sağlam yedek yok, atlandı."); return; }
            File.Copy(good, i.IndexPath, true);
            Console.WriteLine("  JS: orijinale döndürüldü.");
        }

        // ================= Discord süreç yönetimi =================
        static bool CloseDiscord(Install i, bool quiet)
        {
            var procs = Process.GetProcessesByName(i.ProcName);
            if (procs.Length == 0) return true;
            if (!quiet) Console.WriteLine("Discord kapatılıyor...");
            var psi = new ProcessStartInfo("taskkill", "/F /IM " + i.ExeName + " /T")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            try { using (var p = Process.Start(psi)) { p.WaitForExit(8000); } } catch { }
            for (int t = 0; t < 30 && Process.GetProcessesByName(i.ProcName).Length > 0; t++) System.Threading.Thread.Sleep(300);
            return Process.GetProcessesByName(i.ProcName).Length == 0;
        }

        static void LaunchDiscord(Install i)
        {
            try
            {
                if (File.Exists(i.UpdateExe))
                    Process.Start(new ProcessStartInfo(i.UpdateExe, "--processStart " + i.ExeName) { UseShellExecute = false });
                else
                {
                    string exe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(i.NodePath)))), i.ExeName);
                    if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                }
            }
            catch (Exception e) { Console.WriteLine("Discord başlatılamadı: " + e.Message); }
        }

        // ================= Otomatik başlatma (HKCU Run) =================
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "DiscordStereo";

        static void InstallStartup()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                k.SetValue(RunValue, "\"" + exe + "\" --startup");
            Console.WriteLine("Otomatik başlatma AÇILDI. Her açılışta yama uygulanıp Discord başlatılacak.");
        }

        static void UninstallStartup()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue, false);
            Console.WriteLine("Otomatik başlatma KAPANDI.");
        }

        static bool StartupEnabled()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(RunValue) != null;
        }

        // ================= Yüksek seviye akışlar =================
        static Install Pick(List<Install> installs)
        {
            if (installs.Count == 0) { Console.WriteLine("Discord kurulumu bulunamadı."); return null; }
            return installs[0]; // en yeni Stable öncelik sırasına göre
        }

        static bool DoApply(Install i, int bitrate, bool launch, bool quiet)
        {
            if (!CloseDiscord(i, quiet))
            {
                Console.WriteLine();
                Console.WriteLine("Discord kapatılamadı (muhtemelen yönetici olarak çalışıyor).");
                Console.WriteLine("Lütfen sistem tepsisinden sağ tık > \"Quit Discord\" ile kapat, sonra tekrar dene.");
                return false;
            }
            try
            {
                if (!quiet) Console.WriteLine("Yama uygulanıyor (" + i.Channel + " " + i.Version + ")...");
                ApplyNative(i, quiet);
                ApplyJs(i, bitrate, quiet);
            }
            catch (Exception e) { Console.WriteLine("HATA: " + e.Message); return false; }
            if (launch) { LaunchDiscord(i); if (!quiet) Console.WriteLine("Discord başlatıldı. Ses kanalına gir ve stereo çal."); }
            return true;
        }

        static void DoRestore(Install i, bool launch)
        {
            if (!CloseDiscord(i, false))
            {
                Console.WriteLine("Discord kapatılamadı; tepsiden \"Quit Discord\" yapıp tekrar dene.");
                return;
            }
            RestoreNative(i);
            RestoreJs(i);
            if (launch) LaunchDiscord(i);
            Console.WriteLine("Geri alma tamam.");
        }

        static void ShowStatus(Install i)
        {
            Console.WriteLine("Discord   : " + i.Channel + " " + i.Version);
            byte[] node = File.ReadAllBytes(i.NodePath);
            int applied = 0;
            foreach (var p in NativePatches)
            {
                int at; var st = ResolveNative(node, p, out at);
                if (st == SiteState.Patched) applied++;
                Console.WriteLine("  [native] " + p.Name + " : " + (st == SiteState.Patched ? "UYGULANMIS" : st == SiteState.Original ? "orijinal" : st.ToString()));
            }
            Console.WriteLine("  [js] kanca : " + (IsJsPatched(i) ? "UYGULANMIS" : "yok"));
            bool full = applied == NativePatches.Length && IsJsPatched(i);
            Console.WriteLine("  Durum      : " + (full ? "STEREO AKTIF" : applied + "/" + NativePatches.Length + " native, js " + (IsJsPatched(i) ? "var" : "yok")));
            Console.WriteLine("  Otomatik   : " + (StartupEnabled() ? "acik" : "kapali"));
        }

        // ================= Menü / Main =================
        static void Banner()
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("  Discord Stereo  -  gercek stereo + yuksek kalite");
            Console.WriteLine("==============================================");
        }

        static void Menu(Install i, int bitrate)
        {
            while (true)
            {
                Console.WriteLine();
                ShowStatus(i);
                Console.WriteLine();
                Console.WriteLine("  1) Kur ve Discord'u baslat");
                Console.WriteLine("  2) Kaldir (orijinale dondur)");
                Console.WriteLine("  3) Otomatik baslatmayi " + (StartupEnabled() ? "KAPAT" : "AC"));
                Console.WriteLine("  4) Cikis");
                Console.Write("Secim: ");
                string c = Console.ReadLine();
                Console.WriteLine();
                if (c == "1") DoApply(i, bitrate, true, false);
                else if (c == "2") DoRestore(i, true);
                else if (c == "3") { if (StartupEnabled()) UninstallStartup(); else InstallStartup(); }
                else if (c == "4") return;
            }
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var set = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
            int bitrate = DefaultBitrate;
            for (int k = 0; k < args.Length - 1; k++)
                if (args[k].ToLowerInvariant() == "--bitrate") { int b; if (int.TryParse(args[k + 1], out b)) bitrate = Math.Max(8000, Math.Min(510000, b)); }
            bool launch = !set.Contains("--no-launch");

            var installs = FindInstalls();
            var i = Pick(installs);

            if (set.Contains("--dump-js"))
            {
                Console.Out.Write(JsTemplate.Replace("__VERSION__", PatchVersion.ToString())
                    .Replace("__ORIG_SHA256__", new string('0', 64)).Replace("__BITRATE__", bitrate.ToString()));
                return 0;
            }
            if (set.Contains("--help") || set.Contains("-h"))
            {
                Banner();
                Console.WriteLine("Kullanim: DiscordStereo.exe [--apply|--restore|--status|--install|--uninstall|--startup] [--bitrate N] [--no-launch]");
                Console.WriteLine("Argumansiz calistirinca menu acilir.");
                return 0;
            }
            if (i == null && !set.Contains("--uninstall")) { if (!set.Contains("--startup")) { Console.WriteLine("Discord bulunamadi."); Pause(set); } return 1; }

            if (set.Contains("--startup")) { if (i != null) DoApply(i, bitrate, true, true); return 0; }
            if (set.Contains("--status")) { Banner(); ShowStatus(i); Pause(set); return 0; }
            if (set.Contains("--restore")) { Banner(); DoRestore(i, launch); Pause(set); return 0; }
            if (set.Contains("--install")) { InstallStartup(); return 0; }
            if (set.Contains("--uninstall")) { UninstallStartup(); return 0; }
            if (set.Contains("--apply")) { Banner(); bool ok = DoApply(i, bitrate, launch, false); Pause(set); return ok ? 0 : 1; }

            // argumansiz: menu
            Banner();
            Menu(i, bitrate);
            return 0;
        }

        static void Pause(HashSet<string> set)
        {
            if (set.Contains("--quiet") || set.Contains("--startup")) return;
            Console.WriteLine();
            Console.Write("Kapatmak icin Enter...");
            try { Console.ReadLine(); } catch { }
        }
    }
}
