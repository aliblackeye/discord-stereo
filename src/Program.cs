// Discord Stereo — sends true stereo + high-bitrate audio to Discord through the mic channel,
// low latency. Applies signature-based patches to Discord's discord_voice module, so it is
// version-independent. All changes are backed up and reversible.
//
// This tool only modifies YOUR OWN Discord client on YOUR OWN computer. Use at your own risk;
// client modifications may violate Discord's Terms of Service.
//
// Türkçe: Discord'a mikrofon kanalından gerçek stereo + yüksek bitrate ses gönderir (düşük gecikme).
// Discord'un ses modülüne (discord_voice) imza tabanlı yamalar uygular; sürümden bağımsızdır.
// Tüm değişiklikler yedeklenir ve geri alınabilir. Yalnızca kendi bilgisayarındaki kendi Discord'unu
// değiştirir; kullanım riski sana aittir (istemci değişikliği Discord ToS'una aykırı olabilir).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscordStereo
{
    internal static class Program
    {
        const int PatchVersion = 2;
        const int DefaultBitrate = 510000; // highest bitrate the Opus audio codec supports (best quality)
        const string Marker = "DISCORD-STEREO-PATCH";

        enum Lang { En, Tr }
        static Lang lang = Lang.En;
        static string T(string en, string tr) { return lang == Lang.Tr ? tr : en; }

        // -------- Native (discord_voice.node) signature-based patches --------
        sealed class NativePatch
        {
            public string Name;
            public int[] Sig;      // -1 = wildcard byte
            public int SigOffset;  // distance from match start to patch point
            public byte[] Verify;  // expected bytes before writing
            public byte[] Write;   // bytes to write
        }

        static readonly NativePatch[] NativePatches = new[]
        {
            new NativePatch {
                Name = "Stereo (channels 1->2)",
                Sig = new[]{0xE8,-1,-1,-1,-1,0xBD,-1,0x00,0x00,0x00,0x80,0xBC,0x24,0x80,0x01,0x00,0x00,0x01},
                SigOffset = 6, Verify = new byte[]{0x01}, Write = new byte[]{0x02},
            },
            new NativePatch {
                Name = "Downmix bypass (skip mono downmix)",
                Sig = new[]{0x48,0x89,0xF9,0xE8,-1,-1,-1,-1,0x84,0xC0,0x74,0x0D,0x83,0xBE,0x78,0x02,0x00,0x00,0x09,0x0F,0x8F,-1,-1,-1,-1,0x4C,0x89,0x6C,0x24,0x40,0x44,0x0F,0xB6,0xAC,0x24,-1,-1,0x00,0x00},
                SigOffset = 8,
                Verify = new byte[]{0x84,0xC0,0x74,0x0D,0x83,0xBE,0x78,0x02,0x00,0x00,0x09,0x0F,0x8F},
                Write  = new byte[]{0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90,0xE9},
            },
            new NativePatch {
                Name = "High-pass filter off",
                Sig = new[]{0x48,0x8B,0x42,-1,0x45,0x84,0xC0,0x74,-1,0x48,0x85,0xC0,0x0F,0x84,-1,-1,-1,-1,0x45,0x31,0xF6,0x48,0x8D,0x5C,0x24,-1},
                SigOffset = -30, Verify = new byte[]{0x41}, Write = new byte[]{0xC3},
            },
        };

        // -------- JS hook (index.js) --------
        const string AnchorBind = "function bindConnectionInstance(instance) {";
        const string AnchorConnOptions = "        setTransportOptions: (options) => instance.setTransportOptions(options),";
        const string PatchedConnOptions = "        setTransportOptions: (options) => instance.setTransportOptions(discordStereo.connectionOptions(instance, options)), /* DISCORD-STEREO-PATCH */";
        const string AnchorExports = "module.exports = VoiceEngine;";
        const string PatchedExports = "discordStereo.installEngineHooks(VoiceEngine); /* DISCORD-STEREO-PATCH */";

        const string JsTemplate = @"/* DISCORD-STEREO-PATCH BEGIN v__VERSION__ orig-sha256=__ORIG_SHA256__ */
// Hook that makes the voice connection send stereo at a high bitrate (added by DiscordStereo).
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
                connections.set(instance, true);
                // Enforce stereo signalling + bitrate on EVERY call, even when the encoder already
                // reports 2 channels (the native patch sets that). Otherwise Discord keeps the Opus
                // stereo flag at 0 and its default ~64 kbps, so the far end gets narrow, low-bitrate
                // audio even though 2 channels are captured.
                encoder.channels = CHANNELS;
                if (encoder.params == null || typeof encoder.params !== 'object') encoder.params = {};
                encoder.params.stereo = '1';
                if (typeof encoder.rate === 'number') encoder.rate = BITRATE;
                if (encoder.fec === true) encoder.fec = false;
                options.encodingVoiceBitRate = BITRATE;
                if (options.fec === true) options.fec = false;
                say(`stereo enforced: channels=${CHANNELS} stereo=1 bitrate=${BITRATE}${wasMono ? ' (was mono)' : ''}`);
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

        // ================= Install detection =================
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

        static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        static List<Install> FindInstalls()
        {
            var result = new List<Install>();
            var channels = new[]
            {
                new { Dir = "Discord",           Exe = "Discord.exe",            Proc = "Discord" },
                new { Dir = "DiscordPTB",        Exe = "DiscordPTB.exe",         Proc = "DiscordPTB" },
                new { Dir = "DiscordCanary",     Exe = "DiscordCanary.exe",      Proc = "DiscordCanary" },
                new { Dir = "DiscordDevelopment",Exe = "DiscordDevelopment.exe", Proc = "DiscordDevelopment" },
            };
            foreach (var ch in channels)
            {
                string root = Path.Combine(LocalAppData, ch.Dir);
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
                            break;
                        }
                    }
                    break;
                }
            }
            return result;
        }

        static Version ParseVersion(string s) { Version v; return Version.TryParse(s, out v) ? v : null; }

        // ================= Helpers =================
        static string Sha256(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", ""); }
        static string Sha256File(string p) { return Sha256(File.ReadAllBytes(p)); }

        static string BackupDir(Install i)
        {
            string dir = Path.Combine(LocalAppData, "DiscordStereo", "backup", i.Channel + "-" + i.Version);
            Directory.CreateDirectory(dir);
            return dir;
        }

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
            var missing = new List<string>();
            foreach (var p in NativePatches)
            {
                int at; var st = ResolveNative(data, p, out at);
                switch (st)
                {
                    case SiteState.Patched: if (!quiet) Console.WriteLine("  [" + p.Name + "] " + T("already applied.", "zaten uygulanmış.")); break;
                    case SiteState.Original: plan.Add(new KeyValuePair<NativePatch, int>(p, at)); break;
                    case SiteState.Ambiguous: throw new Exception("[" + p.Name + "] " + T("signature matched in multiple places; aborting.", "imzası birden fazla yerde; iptal."));
                    case SiteState.NoSig: missing.Add(p.Name); break;
                    case SiteState.Unexpected: throw new Exception("[" + p.Name + "] " + T("unexpected bytes at patch site; aborting (nothing written).", "noktasında beklenmeyen baytlar; iptal (hiçbir şey yazılmadı)."));
                }
            }
            if (missing.Count == NativePatches.Length)
                throw new Exception(T("No signatures matched. This Discord version may be unsupported; nothing written.",
                                      "Hiçbir imza eşleşmedi. Bu Discord sürümü desteklenmiyor olabilir; hiçbir şey yazılmadı."));
            if (missing.Count > 0 && !quiet)
                Console.WriteLine("  " + T("Warning: signature(s) not found: ", "Uyarı: bulunamayan imza(lar): ") + string.Join(", ", missing));
            if (plan.Count == 0) { if (!quiet) Console.WriteLine("  " + T("Native: already applied.", "Native: zaten tam.")); return; }

            string backup = Path.Combine(BackupDir(i), "discord_voice.node." + origHash + ".orig");
            if (!File.Exists(backup)) File.WriteAllBytes(backup, data);
            if (Sha256File(backup) != origHash) throw new Exception(T("Backup verification failed.", "Yedek doğrulanamadı."));

            foreach (var kv in plan)
                for (int k = 0; k < kv.Key.Write.Length; k++) data[kv.Value + k] = kv.Key.Write[k];
            File.WriteAllBytes(i.NodePath, data);

            byte[] after = File.ReadAllBytes(i.NodePath);
            foreach (var kv in plan)
                if (!SeqEqual(after, kv.Value, kv.Key.Write)) throw new Exception(T("Verification failed: ", "Doğrulama başarısız: ") + kv.Key.Name);
            if (!quiet) Console.WriteLine("  " + string.Format(T("Native: {0} site(s) patched.", "Native: {0} nokta yamalandı."), plan.Count));
        }

        static void RestoreNative(Install i)
        {
            var backups = Directory.Exists(BackupDir(i)) ? Directory.GetFiles(BackupDir(i), "discord_voice.node.*.orig") : new string[0];
            string good = backups.FirstOrDefault(b => Path.GetFileName(b) == "discord_voice.node." + Sha256File(b) + ".orig");
            if (good == null) { Console.WriteLine("  " + T("Native: no valid backup, skipped.", "Native: sağlam yedek yok, atlandı.")); return; }
            if (Sha256File(i.NodePath) == Sha256File(good)) { Console.WriteLine("  " + T("Native: already original.", "Native: zaten orijinal.")); return; }
            File.Copy(good, i.NodePath, true);
            Console.WriteLine("  " + T("Native: restored to original.", "Native: orijinale döndürüldü."));
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
            if (text.Contains(Marker))
            {
                var vm = Regex.Match(text, @"DISCORD-STEREO-PATCH BEGIN v(\d+)");
                if (vm.Success && vm.Groups[1].Value == PatchVersion.ToString())
                { if (!quiet) Console.WriteLine("  " + T("JS: already applied (current version).", "JS: zaten uygulanmış (güncel sürüm).")); return; }
                // Older hook version on disk: restore the original, then re-apply the new hook.
                if (!quiet) Console.WriteLine("  " + T("JS: updating hook to the current version.", "JS: kanca güncel sürüme yükseltiliyor."));
                RestoreJs(i);
                text = File.ReadAllText(i.IndexPath);
                if (text.Contains(Marker))
                { if (!quiet) Console.WriteLine("  " + T("JS: could not restore before update; left as is.", "JS: güncelleme öncesi geri alınamadı; olduğu gibi bırakıldı.")); return; }
            }
            foreach (var a in new[] { AnchorBind, AnchorConnOptions, AnchorExports })
                if (CountOccurrences(text, a) != 1)
                    throw new Exception(T("JS: expected line not found (Discord version may differ); nothing written.",
                                          "JS: beklenen satır bulunamadı (Discord sürümü farklı olabilir); hiçbir şey yazılmadı."));

            string origHash = Sha256File(i.IndexPath);
            string backup = Path.Combine(BackupDir(i), "index.js." + origHash + ".orig");
            if (!File.Exists(backup)) File.Copy(i.IndexPath, backup, false);

            string block = JsTemplate.Replace("__VERSION__", PatchVersion.ToString())
                .Replace("__ORIG_SHA256__", origHash).Replace("__BITRATE__", bitrate.ToString()).Replace("\r\n", "\n");
            string patched = text.Replace(AnchorBind, block + "\n" + AnchorBind)
                .Replace(AnchorConnOptions, PatchedConnOptions)
                .Replace(AnchorExports, PatchedExports + "\n" + AnchorExports);
            File.WriteAllText(i.IndexPath, patched, new UTF8Encoding(false));
            if (!quiet) Console.WriteLine("  " + string.Format(T("JS: hook added (bitrate {0} kbps).", "JS: kanca eklendi (bitrate {0} kbps)."), bitrate / 1000));
        }

        static void RestoreJs(Install i)
        {
            if (!IsJsPatched(i)) { Console.WriteLine("  " + T("JS: already original.", "JS: zaten orijinal.")); return; }
            var backups = Directory.Exists(BackupDir(i)) ? Directory.GetFiles(BackupDir(i), "index.js.*.orig") : new string[0];
            string good = backups.FirstOrDefault(b => Path.GetFileName(b) == "index.js." + Sha256File(b) + ".orig");
            if (good == null) { Console.WriteLine("  " + T("JS: no valid backup, skipped.", "JS: sağlam yedek yok, atlandı.")); return; }
            File.Copy(good, i.IndexPath, true);
            Console.WriteLine("  " + T("JS: restored to original.", "JS: orijinale döndürüldü."));
        }

        // ================= Discord process management =================
        static bool CloseDiscord(Install i, bool quiet)
        {
            if (Process.GetProcessesByName(i.ProcName).Length == 0) return true;
            if (!quiet) Console.WriteLine(T("Closing Discord...", "Discord kapatılıyor..."));
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
            catch (Exception e) { Console.WriteLine(T("Could not start Discord: ", "Discord başlatılamadı: ") + e.Message); }
        }

        // ================= Auto-start (HKCU Run) =================
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "DiscordStereo";

        static void InstallStartup()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                k.SetValue(RunValue, "\"" + exe + "\" --startup");
            Console.WriteLine(T("Auto-start ENABLED. Patches will be applied and Discord launched at each login.",
                                "Otomatik başlatma AÇILDI. Her açılışta yama uygulanıp Discord başlatılacak."));
        }

        static void UninstallStartup()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue, false);
            Console.WriteLine(T("Auto-start DISABLED.", "Otomatik başlatma KAPANDI."));
        }

        static bool StartupEnabled()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(RunValue) != null;
        }

        // ================= Settings (remember language/bitrate) =================
        static string SettingsPath { get { return Path.Combine(LocalAppData, "DiscordStereo", "settings.txt"); } }

        static Dictionary<string, string> LoadSettings()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (var line in File.ReadAllLines(SettingsPath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        static void SaveSetting(string key, string value)
        {
            try
            {
                var d = LoadSettings();
                d[key] = value;
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, d.Select(kv => kv.Key + "=" + kv.Value));
            }
            catch { }
        }

        // ================= High-level flows =================
        static Install Pick(List<Install> installs) { return installs.Count == 0 ? null : installs[0]; }

        static bool DoApply(Install i, int bitrate, bool launch, bool quiet)
        {
            if (!CloseDiscord(i, quiet))
            {
                Console.WriteLine();
                Console.WriteLine(T("Could not close Discord (it is probably running as administrator).",
                                    "Discord kapatılamadı (muhtemelen yönetici olarak çalışıyor)."));
                Console.WriteLine(T("Right-click the tray icon > \"Quit Discord\", then try again.",
                                    "Sistem tepsisinden sağ tık > \"Quit Discord\" ile kapat, sonra tekrar dene."));
                return false;
            }
            try
            {
                if (!quiet) Console.WriteLine(string.Format(T("Patching ({0} {1})...", "Yama uygulanıyor ({0} {1})..."), i.Channel, i.Version));
                ApplyNative(i, quiet);
                ApplyJs(i, bitrate, quiet);
            }
            catch (Exception e) { Console.WriteLine(T("ERROR: ", "HATA: ") + e.Message); return false; }
            if (launch) { LaunchDiscord(i); if (!quiet) Console.WriteLine(T("Discord launched. Join a voice channel and play stereo.", "Discord başlatıldı. Ses kanalına gir ve stereo çal.")); }
            return true;
        }

        static void DoRestore(Install i, bool launch)
        {
            if (!CloseDiscord(i, false))
            {
                Console.WriteLine(T("Could not close Discord; quit it from the tray and try again.",
                                    "Discord kapatılamadı; tepsiden \"Quit Discord\" yapıp tekrar dene."));
                return;
            }
            RestoreNative(i);
            RestoreJs(i);
            if (launch) LaunchDiscord(i);
            Console.WriteLine(T("Restore done.", "Geri alma tamam."));
        }

        static void ShowStatus(Install i)
        {
            Console.WriteLine(T("Discord   : ", "Discord   : ") + i.Channel + " " + i.Version);
            byte[] node = File.ReadAllBytes(i.NodePath);
            int applied = 0;
            foreach (var p in NativePatches)
            {
                int at; var st = ResolveNative(node, p, out at);
                if (st == SiteState.Patched) applied++;
                string s = st == SiteState.Patched ? T("APPLIED", "UYGULANMIS") : st == SiteState.Original ? T("original", "orijinal") : st.ToString();
                Console.WriteLine("  [native] " + p.Name + " : " + s);
            }
            Console.WriteLine("  [js] hook  : " + (IsJsPatched(i) ? T("APPLIED", "UYGULANMIS") : T("none", "yok")));
            bool full = applied == NativePatches.Length && IsJsPatched(i);
            Console.WriteLine("  " + T("Status     : ", "Durum      : ") + (full ? T("STEREO ACTIVE", "STEREO AKTIF")
                : applied + "/" + NativePatches.Length + T(" native, js ", " native, js ") + (IsJsPatched(i) ? T("yes", "var") : T("no", "yok"))));
            Console.WriteLine("  " + T("Auto-start : ", "Otomatik   : ") + (StartupEnabled() ? T("on", "acik") : T("off", "kapali")));
        }

        // ================= Verify from Discord's own logs =================
        static string ReadShared(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        static void VerifyFromLogs(Install i)
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), i.ProcName.ToLowerInvariant(), "logs");
            if (!Directory.Exists(logDir)) { Console.WriteLine(T("No voice logs yet. Join a voice channel and play audio, then run verify again.", "Henüz ses logu yok. Bir ses kanalına girip ses çal, sonra tekrar doğrula.")); return; }
            // The webrtc logs persist across sessions and Discord keeps two of them
            // (discord-webrtc_0 / _1). Concatenating both and taking the last match can pick a
            // value from the OLD file. Order the files so the freshest content comes last, and
            // gate on the periodic capture line so stale sessions are not read as current.
            Func<string, DateTime> newestTs = s =>
            {
                DateTime best = DateTime.MinValue;
                foreach (Match tm in Regex.Matches(s, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})"))
                {
                    DateTime ts;
                    if (DateTime.TryParseExact(tm.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts) && ts > best)
                        best = ts;
                }
                return best;
            };
            // discord-webrtc_* holds the live capture stats; discord-last-webrtc_* holds the
            // stream setup (ConfigureStream / ApplyConfig). Read all four, freshest last.
            var parts = new[] { "discord-last-webrtc_0", "discord-last-webrtc_1", "discord-webrtc_0", "discord-webrtc_1" }
                .Select(n => Path.Combine(logDir, n)).Where(File.Exists)
                .Select(p => ReadShared(p)).Where(t => t.Length > 0)
                .OrderBy(newestTs)               // freshest file last => LastOrDefault picks current data
                .ToList();
            if (parts.Count == 0) { Console.WriteLine(T("No voice session found. Join a voice channel and play audio first.", "Ses oturumu bulunamadı. Önce bir ses kanalına girip ses çal.")); return; }
            string text = string.Concat(parts);

            // Freshness proxy: captured_audio_processor logs every ~10 s while the mic is live.
            DateTime voiceTs = DateTime.MinValue;
            foreach (Match m in Regex.Matches(text, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})[^\n]*?captured_audio_processor"))
            {
                DateTime ts;
                if (DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts) && ts > voiceTs)
                    voiceTs = ts;
            }
            if (voiceTs == DateTime.MinValue || (DateTime.Now - voiceTs) > TimeSpan.FromMinutes(5))
            {
                string when = voiceTs == DateTime.MinValue ? T("none found", "bulunamadı") : voiceTs.ToString("yyyy-MM-dd HH:mm");
                Console.WriteLine("  " + T("No live voice capture in the logs (last: ", "Loglarda canlı ses yakalama yok (son: ") + when + ").");
                Console.WriteLine("  " + T("Join a voice channel, play ~10 s of audio, then run verify again.",
                                            "Bir ses kanalına gir, ~10 sn ses çal, sonra doğrulamayı tekrar çalıştır."));
                return;
            }

            var cfg = Regex.Matches(text, @"ConfigureStream.*?format:\s*\{name:\s*opus[^}]*num_channels:\s*(\d)[^}]*stereo:\s*(\d)").Cast<Match>().LastOrDefault();
            var cap = Regex.Matches(text, @"captured_audio_processor\.cpp:\d+\).*?channels:\s*(\d)").Cast<Match>().LastOrDefault();
            var apmF = Regex.Matches(text, @"APM frames processed:\s*(\d+)").Cast<Match>().LastOrDefault();
            string nc = cfg != null ? cfg.Groups[1].Value : "?";
            string st = cfg != null ? cfg.Groups[2].Value : "?";
            string ch = cap != null ? cap.Groups[1].Value : "?";
            bool alive = Process.GetProcessesByName(i.ProcName).Length > 0;
            string noLog = T("(not in current logs)", "(guncel logda yok)");

            Console.WriteLine("  " + T("Opus channels    : ", "Opus kanal       : ") + (nc == "?" ? noLog : nc + (nc == "2" ? "  OK" : "")));
            Console.WriteLine("  " + T("Opus stereo flag : ", "Opus stereo      : ") + (st == "?" ? noLog : st + (st == "1" ? "  OK" : "")));
            Console.WriteLine("  " + T("Captured channels: ", "Yakalanan kanal  : ") + ch + (ch == "2" ? "  OK" : ""));

            // Ses isleme durumlari (APM ApplyConfig): muzik icin hepsi kapali (0) olmali.
            var apm = Regex.Matches(text, @"AudioProcessing::ApplyConfig:.*").Cast<Match>().LastOrDefault();
            if (apm != null)
            {
                string a = apm.Value;
                Func<string, string> g = pat => { var m = Regex.Match(a, pat); return m.Success ? m.Groups[1].Value : "?"; };
                // high-pass config bayragi Discord tarafindan hep 1 birakilir; asil is native yama fonksiyonu etkisizlestirir.
                bool hpPatched = false;
                try
                {
                    byte[] nd = File.ReadAllBytes(i.NodePath);
                    var hpp = NativePatches.FirstOrDefault(p => p.Name.StartsWith("High-pass"));
                    if (hpp != null) { int at; hpPatched = ResolveNative(nd, hpp, out at) == SiteState.Patched; }
                }
                catch { }
                string ec = g(@"echo_canceller:\s*\{\s*enabled:\s*(\d)");
                string ns = g(@"noise_suppression:\s*\{\s*enabled:\s*(\d)");
                string g1 = g(@"gain_controller1:\s*\{\s*enabled:\s*(\d)");
                string g2 = g(@"gain_controller2:\s*\{\s*enabled:\s*(\d)");
                bool apmRunning = apmF != null && apmF.Groups[1].Value != "0";
                Action<string, string> row = (label, v) => Console.WriteLine("  " + label + v + (v == "0" ? "  OK" : v == "?" ? "" : (apmRunning ? T("  <- ON (colors music)", "  <- ACIK (muzigi renklendirir)") : T("  <- configured, but APM idle", "  <- yapilandirilmis ama APM bosta"))));
                Console.WriteLine();
                Console.WriteLine("  " + T("--- Capture processing ---", "--- Yakalama islemesi ---"));
                Console.WriteLine("  " + T("APM running          : ", "APM calisiyor        : ") + (apmF == null ? "?" : apmRunning
                        ? apmF.Groups[1].Value + T(" frames  <- processing IS active", " kare  <- isleme AKTIF")
                        : T("no (0 frames) — capture passes through untouched  OK", "hayir (0 kare) — yakalama dokunulmadan geciyor  OK")));
                Console.WriteLine("  " + T("High-pass (bass cut) : ", "High-pass (bass)     : ") + (hpPatched ? T("off (patched)  OK", "kapali (yamali)  OK") : T("ON  <- re-apply patch", "ACIK  <- yamayi tekrar uygula")));
                row(T("Echo cancel          : ", "Echo giderme         : "), ec);
                row(T("Noise suppression    : ", "Gurultu bastirma     : "), ns);
                row(T("Auto gain (AGC1)     : ", "Oto kazanc (AGC1)    : "), g1);
                row(T("Adaptive gain (AGC2) : ", "Adaptif kazanc(AGC2) : "), g2);
            }
            else
                Console.WriteLine("  " + T("(Processing states: rejoin voice for a fresh reading)", "(Isleme durumlari: taze okuma icin ses kanalina tekrar gir)"));

            Console.WriteLine("  " + T("Discord running  : ", "Discord ayakta   : ") + (alive ? T("yes", "evet") : T("no", "hayır")));
            Console.WriteLine();
            if (ch == "2")
                Console.WriteLine(T("=> Logs confirm STEREO capture (2 channels).", "=> Loglar STEREO yakalamayı (2 kanal) doğruluyor.")
                                  + (st == "1" ? T("  Opus stereo flag = 1.", "  Opus stereo = 1.") : ""));
            else if (ch == "?")
                Console.WriteLine(T("=> Not enough capture data. Join voice, play ~10 s, verify again.", "=> Yeterli yakalama verisi yok. Ses kanalına gir, ~10 sn çal, tekrar doğrula."));
            else
                Console.WriteLine(T("=> Capture is not stereo (channels=" + ch + "). If Discord updated, re-apply the patch.", "=> Yakalama stereo değil (kanal=" + ch + "). Discord güncellendiyse yamayı tekrar uygula."));
        }

        // ================= Self-test =================
        static int SelfTest()
        {
            int fail = 0;
            Action<bool, string> check = (ok, msg) => { Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + msg); if (!ok) fail++; };

            // 1) Native patch definitions are internally consistent.
            foreach (var p in NativePatches)
            {
                check(p.Sig.Length > 0 && p.Sig[0] >= 0, p.Name + ": signature starts with a concrete byte");
                check(p.Verify.Length > 0 && p.Write.Length > 0, p.Name + ": verify/write non-empty");
                if (p.SigOffset >= 0)
                {
                    bool consistent = true;
                    for (int k = 0; k < p.Verify.Length; k++)
                    {
                        int idx = p.SigOffset + k;
                        int sb = idx < p.Sig.Length ? p.Sig[idx] : -1;
                        if (sb >= 0 && sb != p.Verify[k]) consistent = false;
                    }
                    check(consistent, p.Name + ": verify bytes match the signature");
                }
            }

            // 2) JS template has the required placeholders and markers.
            foreach (var tok in new[] { "__BITRATE__", "__VERSION__", "__ORIG_SHA256__", "BEGIN", "END", "installEngineHooks", "connectionOptions" })
                check(JsTemplate.Contains(tok), "JS template contains " + tok);

            // 3) In-memory apply against an original backup (if available): all sites patch + idempotent.
            var i = Pick(FindInstalls());
            string orig = null;
            if (i != null && Directory.Exists(BackupDir(i)))
                orig = Directory.GetFiles(BackupDir(i), "discord_voice.node.*.orig")
                    .FirstOrDefault(b => Path.GetFileName(b) == "discord_voice.node." + Sha256File(b) + ".orig"
                                         && NativePatches.All(p => { int at; return ResolveNative(File.ReadAllBytes(b), p, out at) == SiteState.Original; }));
            if (orig == null) { Console.WriteLine("  SKIP  in-memory apply test (no original backup found)"); }
            else
            {
                byte[] data = File.ReadAllBytes(orig);
                foreach (var p in NativePatches)
                {
                    int at; var st = ResolveNative(data, p, out at);
                    if (st == SiteState.Original) for (int k = 0; k < p.Write.Length; k++) data[at + k] = p.Write[k];
                }
                bool allPatched = NativePatches.All(p => { int at; return ResolveNative(data, p, out at) == SiteState.Patched; });
                check(allPatched, "all native sites report Patched after in-memory apply");
                // idempotent: re-resolving/re-applying changes nothing
                byte[] again = (byte[])data.Clone();
                foreach (var p in NativePatches)
                { int at; if (ResolveNative(again, p, out at) == SiteState.Original) for (int k = 0; k < p.Write.Length; k++) again[at + k] = p.Write[k]; }
                check(again.SequenceEqual(data), "second apply is a no-op (idempotent)");
            }

            Console.WriteLine(fail == 0 ? "SELF-TEST: PASS" : "SELF-TEST: FAIL (" + fail + ")");
            return fail == 0 ? 0 : 1;
        }

        // ================= Menu / Main =================
        static void Banner()
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("  Discord Stereo  -  " + T("true stereo + high quality", "gercek stereo + yuksek kalite"));
            Console.WriteLine("==============================================");
        }

        static void Menu(Install i, int bitrate)
        {
            while (true)
            {
                Console.WriteLine();
                ShowStatus(i);
                Console.WriteLine();
                Console.WriteLine("  1) " + T("Install and start Discord", "Kur ve Discord'u baslat"));
                Console.WriteLine("  2) " + T("Uninstall (restore original)", "Kaldir (orijinale dondur)"));
                Console.WriteLine("  3) " + (StartupEnabled() ? T("Turn auto-start OFF", "Otomatik baslatmayi KAPAT") : T("Turn auto-start ON", "Otomatik baslatmayi AC")));
                Console.WriteLine("  4) " + T("Verify stereo (from Discord logs)", "Stereo dogrula (Discord loglarindan)"));
                Console.WriteLine("  5) " + string.Format(T("Bitrate: {0} kbps", "Bitrate: {0} kbps"), bitrate / 1000));
                Console.WriteLine("  6) " + T("Language: English / Türkçe", "Dil: English / Türkçe"));
                Console.WriteLine("  7) " + T("Exit", "Cikis"));
                Console.Write(T("Choice: ", "Secim: "));
                string c = Console.ReadLine();
                if (c == null) return; // stdin kapali / etkilesimsiz: sonsuz donguye girme
                Console.WriteLine();
                if (c == "1") DoApply(i, bitrate, true, false);
                else if (c == "2") DoRestore(i, true);
                else if (c == "3") { if (StartupEnabled()) UninstallStartup(); else InstallStartup(); }
                else if (c == "4") VerifyFromLogs(i);
                else if (c == "5")
                {
                    Console.Write(T("New bitrate in kbps (48-510, e.g. 384 or 510): ", "Yeni bitrate kbps (48-510, ör. 384 veya 510): "));
                    string v = Console.ReadLine(); int kb;
                    if (int.TryParse((v ?? "").Trim(), out kb))
                    {
                        bitrate = Math.Max(8000, Math.Min(510000, kb * 1000));
                        SaveSetting("bitrate", bitrate.ToString());
                        Console.WriteLine(string.Format(T("Bitrate set to {0} kbps. Re-run 'Install' to apply.", "Bitrate {0} kbps yapildi. Uygulamak icin 'Kur'u tekrar calistir."), bitrate / 1000));
                    }
                }
                else if (c == "6") { lang = lang == Lang.Tr ? Lang.En : Lang.Tr; SaveSetting("lang", lang == Lang.Tr ? "tr" : "en"); }
                else if (c == "7") return;
            }
        }

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            var set = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));

            // Language: OS default -> saved setting -> --lang flag
            lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? Lang.Tr : Lang.En;
            var settings = LoadSettings();
            string savedLang; if (settings.TryGetValue("lang", out savedLang)) lang = savedLang == "tr" ? Lang.Tr : Lang.En;
            for (int k = 0; k < args.Length - 1; k++)
                if (args[k].ToLowerInvariant() == "--lang") { string v = args[k + 1].ToLowerInvariant(); lang = v.StartsWith("tr") ? Lang.Tr : Lang.En; }

            int bitrate = DefaultBitrate;
            string savedBr; if (settings.TryGetValue("bitrate", out savedBr)) { int b; if (int.TryParse(savedBr, out b)) bitrate = b; }
            for (int k = 0; k < args.Length - 1; k++)
                if (args[k].ToLowerInvariant() == "--bitrate") { int b; if (int.TryParse(args[k + 1], out b)) { bitrate = Math.Max(8000, Math.Min(510000, b)); SaveSetting("bitrate", bitrate.ToString()); } }

            bool launch = !set.Contains("--no-launch");

            if (set.Contains("--dump-js"))
            {
                Console.Out.Write(JsTemplate.Replace("__VERSION__", PatchVersion.ToString())
                    .Replace("__ORIG_SHA256__", new string('0', 64)).Replace("__BITRATE__", bitrate.ToString()));
                return 0;
            }
            if (set.Contains("--selftest")) { Banner(); return SelfTest(); }
            if (set.Contains("--help") || set.Contains("-h"))
            {
                Banner();
                Console.WriteLine(T("Usage: DiscordStereo.exe [--apply|--restore|--status|--verify|--install|--uninstall|--startup|--selftest] [--bitrate N] [--lang en|tr] [--no-launch]",
                                    "Kullanim: DiscordStereo.exe [--apply|--restore|--status|--verify|--install|--uninstall|--startup|--selftest] [--bitrate N] [--lang en|tr] [--no-launch]"));
                Console.WriteLine(T("Run with no arguments for the menu.", "Argumansiz calistirinca menu acilir."));
                return 0;
            }

            var installs = FindInstalls();
            var i = Pick(installs);
            if (i == null && !set.Contains("--uninstall") && !set.Contains("--install"))
            {
                if (!set.Contains("--startup")) { Console.WriteLine(T("Discord installation not found.", "Discord kurulumu bulunamadi.")); Pause(set); }
                return 1;
            }

            if (set.Contains("--startup")) { if (i != null) DoApply(i, bitrate, true, true); return 0; }
            if (set.Contains("--status")) { Banner(); ShowStatus(i); Pause(set); return 0; }
            if (set.Contains("--verify")) { Banner(); VerifyFromLogs(i); Pause(set); return 0; }
            if (set.Contains("--restore")) { Banner(); DoRestore(i, launch); Pause(set); return 0; }
            if (set.Contains("--install")) { InstallStartup(); return 0; }
            if (set.Contains("--uninstall")) { UninstallStartup(); return 0; }
            if (set.Contains("--apply")) { Banner(); bool ok = DoApply(i, bitrate, launch, false); Pause(set); return ok ? 0 : 1; }

            Banner();
            Menu(i, bitrate);
            return 0;
        }

        static void Pause(HashSet<string> set)
        {
            if (set.Contains("--quiet") || set.Contains("--startup")) return;
            Console.WriteLine();
            Console.Write(T("Press Enter to close...", "Kapatmak icin Enter..."));
            try { Console.ReadLine(); } catch { }
        }
    }
}
