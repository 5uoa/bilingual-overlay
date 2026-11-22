using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace BilingualOverlay
{
    internal static class Program
    {
        public const string AppVersion = "1.1.0";
        private static Mutex singleInstanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (HasArgument(args, "--self-test"))
            {
                string reportPath = GetArgumentValue(args, "--report");
                Environment.ExitCode = SelfTest.Run(reportPath);
                return;
            }

            string executableName = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
            if (HasArgument(args, "--install") ||
                string.Equals(executableName, "BilingualOverlaySetup", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = InstallationManager.RunSetup();
                return;
            }
            if (HasArgument(args, "--uninstall"))
            {
                Environment.ExitCode = InstallationManager.RunUninstall();
                return;
            }

            bool createdNew;
            singleInstanceMutex = new Mutex(true, "BilingualOverlay.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "中英对照悬浮窗已经在运行。请查看屏幕右侧或系统托盘。",
                    "中英对照悬浮窗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ConfigureDpiAwareness();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                OverlayForm overlay = new OverlayForm(HasArgument(args, "--demo"));
                Application.Run(overlay);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "程序启动失败：\r\n" + ex.Message,
                    "中英对照悬浮窗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                singleInstanceMutex.ReleaseMutex();
                singleInstanceMutex.Dispose();
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetArgumentValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static void ConfigureDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    return;
                }
            }
            catch
            {
            }
            try
            {
                if (SetProcessDpiAwareness(2) == 0)
                {
                    return;
                }
            }
            catch
            {
            }
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr awareness);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();
    }

    internal enum TranslationDirection
    {
        Auto = 0,
        ChineseToEnglish = 1,
        EnglishToChinese = 2
    }

    internal sealed class OverlaySettings
    {
        public const string DefaultSettingsHotkey = "Ctrl+Alt+F10";
        public const string DefaultTranslateHotkey = "Ctrl+Alt+T";
        public const string DefaultHistoryHotkey = "Ctrl+Alt+H";

        public bool LocalOnly { get; set; }
        public int TimeoutSeconds { get; set; }
        public List<string> ExcludedProcesses { get; set; }
        public double FontScale { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public double Opacity { get; set; }
        public bool ClickThrough { get; set; }
        public bool LockPosition { get; set; }
        public bool AutoHideFullScreen { get; set; }
        public int LanguageDirection { get; set; }
        public bool? HistoryEnabled { get; set; }
        public bool ClearHistoryOnStartup { get; set; }
        public string SettingsHotkey { get; set; }
        public string TranslateHotkey { get; set; }
        public string HistoryHotkey { get; set; }
        public bool StartWithWindows { get; set; }
        public string UpdateManifestUrl { get; set; }

        public static OverlaySettings CreateDefault()
        {
            return new OverlaySettings
            {
                LocalOnly = false,
                TimeoutSeconds = 9,
                ExcludedProcesses = new List<string>
                {
                    "keepass.exe",
                    "keepassxc.exe",
                    "1password.exe",
                    "bitwarden.exe",
                    "lastpass.exe"
                },
                FontScale = 1.0,
                WindowWidth = 590,
                WindowHeight = 300,
                Opacity = 0.97,
                ClickThrough = false,
                LockPosition = false,
                AutoHideFullScreen = true,
                LanguageDirection = (int)TranslationDirection.Auto,
                HistoryEnabled = true,
                ClearHistoryOnStartup = false,
                SettingsHotkey = DefaultSettingsHotkey,
                TranslateHotkey = DefaultTranslateHotkey,
                HistoryHotkey = DefaultHistoryHotkey,
                StartWithWindows = false,
                UpdateManifestUrl = ""
            };
        }

        public OverlaySettings Clone()
        {
            OverlaySettings copy = (OverlaySettings)MemberwiseClone();
            copy.ExcludedProcesses = ExcludedProcesses == null
                ? new List<string>()
                : new List<string>(ExcludedProcesses);
            return copy;
        }

        public void Normalize()
        {
            TimeoutSeconds = Clamp(TimeoutSeconds, 3, 30);
            FontScale = Clamp(FontScale, 0.75, 1.8);
            WindowWidth = Clamp(WindowWidth, 500, 1000);
            WindowHeight = Clamp(WindowHeight, 240, 700);
            Opacity = Clamp(Opacity, 0.35, 1.0);
            LanguageDirection = Clamp(LanguageDirection, 0, 2);
            if (!HistoryEnabled.HasValue)
            {
                HistoryEnabled = true;
            }
            SettingsHotkey = ShortcutGesture.NormalizeOrDefault(
                SettingsHotkey, DefaultSettingsHotkey, allowFunctionKeys: true);
            TranslateHotkey = ShortcutGesture.NormalizeOrDefault(
                TranslateHotkey, DefaultTranslateHotkey, allowFunctionKeys: false);
            HistoryHotkey = ShortcutGesture.NormalizeOrDefault(
                HistoryHotkey, DefaultHistoryHotkey, allowFunctionKeys: false);
            UpdateManifestUrl = (UpdateManifestUrl ?? string.Empty).Trim();

            List<string> normalized = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ExcludedProcesses != null)
            {
                foreach (string process in ExcludedProcesses)
                {
                    string value = ProcessIdentity.Normalize(process);
                    if (value.Length > 0 && seen.Add(value))
                    {
                        normalized.Add(value);
                    }
                }
            }
            ExcludedProcesses = normalized;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return minimum;
            }
            return Math.Min(Math.Max(value, minimum), maximum);
        }
    }

    internal static class SettingsStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static string lastSaveSummary = "";

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BilingualOverlay",
                    "settings.json");
            }
        }

        public static string FallbackPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            }
        }

        public static string LastSaveSummary
        {
            get
            {
                return lastSaveSummary ?? "";
            }
        }

        public static OverlaySettings Load()
        {
            return LoadFromCandidates(GetCandidatePaths());
        }

        public static OverlaySettings Load(string path)
        {
            OverlaySettings settings = TryLoad(path);
            return settings ?? OverlaySettings.CreateDefault();
        }

        internal static OverlaySettings LoadFromCandidates(IEnumerable<string> paths)
        {
            OverlaySettings newest = null;
            DateTime newestWriteTime = DateTime.MinValue;
            if (paths != null)
            {
                foreach (string path in paths)
                {
                    OverlaySettings settings = TryLoad(path);
                    if (settings != null)
                    {
                        DateTime writeTime = DateTime.MinValue;
                        try
                        {
                            writeTime = File.GetLastWriteTimeUtc(path);
                        }
                        catch
                        {
                        }
                        if (newest == null || writeTime > newestWriteTime)
                        {
                            newest = settings;
                            newestWriteTime = writeTime;
                        }
                    }
                }
            }

            return newest ?? OverlaySettings.CreateDefault();
        }

        public static bool Save(OverlaySettings settings)
        {
            return SaveToCandidates(settings, GetCandidatePaths());
        }

        public static bool Save(OverlaySettings settings, string path)
        {
            return SaveToPath(settings, path);
        }

        internal static bool SaveToCandidates(
            OverlaySettings settings,
            IEnumerable<string> paths)
        {
            List<string> savedPaths = new List<string>();
            if (paths != null)
            {
                foreach (string path in paths)
                {
                    if (SaveToPath(settings, path))
                    {
                        savedPaths.Add(path);
                    }
                }
            }
            lastSaveSummary = string.Join("；", savedPaths.ToArray());
            return savedPaths.Count > 0;
        }

        private static IEnumerable<string> GetCandidatePaths()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] candidates = { DefaultPath, FallbackPath };
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static OverlaySettings TryLoad(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    OverlaySettings settings =
                        Serializer.Deserialize<OverlaySettings>(File.ReadAllText(path, Encoding.UTF8));
                    if (settings != null)
                    {
                        settings.Normalize();
                        return settings;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool SaveToPath(OverlaySettings settings, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }
                OverlaySettings snapshot = settings == null
                    ? OverlaySettings.CreateDefault()
                    : settings.Clone();
                snapshot.Normalize();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(path, Serializer.Serialize(snapshot), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class ShortcutGesture
    {
        public bool Control { get; private set; }
        public bool Alt { get; private set; }
        public bool Shift { get; private set; }
        public int VirtualKey { get; private set; }
        public string DisplayText { get; private set; }

        public bool Matches(bool control, bool alt, bool shift, int virtualKey)
        {
            return Control == control &&
                Alt == alt &&
                Shift == shift &&
                VirtualKey == virtualKey;
        }

        public static ShortcutGesture Parse(string value, bool allowFunctionKeys)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return null;
            }

            string[] parts = text.Split('+');
            bool control = false;
            bool alt = false;
            bool shift = false;
            string keyName = null;
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                {
                    return null;
                }
                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    if (control)
                    {
                        return null;
                    }
                    control = true;
                }
                else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    if (alt)
                    {
                        return null;
                    }
                    alt = true;
                }
                else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    if (shift)
                    {
                        return null;
                    }
                    shift = true;
                }
                else
                {
                    if (keyName != null)
                    {
                        return null;
                    }
                    keyName = part;
                }
            }

            if (keyName == null || (!control && !alt && !shift))
            {
                return null;
            }

            int virtualKey;
            string normalizedKey;
            if (!TryParseKey(keyName, allowFunctionKeys, out virtualKey, out normalizedKey))
            {
                return null;
            }

            List<string> normalizedParts = new List<string>();
            if (control)
            {
                normalizedParts.Add("Ctrl");
            }
            if (alt)
            {
                normalizedParts.Add("Alt");
            }
            if (shift)
            {
                normalizedParts.Add("Shift");
            }
            normalizedParts.Add(normalizedKey);
            return new ShortcutGesture
            {
                Control = control,
                Alt = alt,
                Shift = shift,
                VirtualKey = virtualKey,
                DisplayText = string.Join("+", normalizedParts.ToArray())
            };
        }

        public static string NormalizeOrDefault(
            string value,
            string fallback,
            bool allowFunctionKeys)
        {
            ShortcutGesture parsed = Parse(value, allowFunctionKeys);
            if (parsed != null)
            {
                return parsed.DisplayText;
            }
            ShortcutGesture fallbackGesture = Parse(fallback, allowFunctionKeys);
            return fallbackGesture == null ? fallback : fallbackGesture.DisplayText;
        }

        public static bool IsValid(string value, bool allowFunctionKeys)
        {
            return Parse(value, allowFunctionKeys) != null;
        }

        public static bool HasDuplicates(
            string first,
            string second,
            string third,
            bool firstAllowsFunctionKeys,
            bool otherAllowsFunctionKeys)
        {
            ShortcutGesture firstGesture = Parse(first, firstAllowsFunctionKeys);
            ShortcutGesture secondGesture = Parse(second, otherAllowsFunctionKeys);
            ShortcutGesture thirdGesture = Parse(third, otherAllowsFunctionKeys);
            if (firstGesture == null || secondGesture == null || thirdGesture == null)
            {
                return false;
            }
            return string.Equals(
                    firstGesture.DisplayText,
                    secondGesture.DisplayText,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    firstGesture.DisplayText,
                    thirdGesture.DisplayText,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    secondGesture.DisplayText,
                    thirdGesture.DisplayText,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseKey(
            string keyName,
            bool allowFunctionKeys,
            out int virtualKey,
            out string normalized)
        {
            virtualKey = 0;
            normalized = null;
            if (keyName.Length == 1)
            {
                char c = char.ToUpperInvariant(keyName[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    virtualKey = c;
                    normalized = c.ToString();
                    return true;
                }
                return false;
            }

            if (allowFunctionKeys &&
                keyName[0] == 'F' &&
                keyName.Length <= 3)
            {
                int number;
                if (int.TryParse(keyName.Substring(1), out number) && number >= 1 && number <= 24)
                {
                    virtualKey = 0x70 + number - 1;
                    normalized = "F" + number;
                    return true;
                }
            }

            if (allowFunctionKeys &&
                string.Equals(keyName, "PrintScreen", StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = 0x2C;
                normalized = "PrintScreen";
                return true;
            }
            return false;
        }
    }

    internal sealed class TranslationRecord
    {
        public DateTime CreatedAt { get; set; }
        public string Original { get; set; }
        public string Translation { get; set; }
        public string Provider { get; set; }
        public bool IsFavorite { get; set; }

        public TranslationRecord Clone()
        {
            return (TranslationRecord)MemberwiseClone();
        }
    }

    internal static class TranslationHistoryStore
    {
        public const int MaximumRecords = 500;
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BilingualOverlay",
                    "history.json");
            }
        }

        public static string FallbackPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
            }
        }

        public static List<TranslationRecord> Load()
        {
            return LoadFromCandidates(new[] { DefaultPath, FallbackPath });
        }

        internal static List<TranslationRecord> LoadFromCandidates(IEnumerable<string> paths)
        {
            List<TranslationRecord> newest = null;
            DateTime newestWriteTime = DateTime.MinValue;
            if (paths != null)
            {
                foreach (string path in paths)
                {
                    List<TranslationRecord> loaded = TryLoad(path);
                    if (loaded == null)
                    {
                        continue;
                    }
                    DateTime writeTime = DateTime.MinValue;
                    try
                    {
                        writeTime = File.GetLastWriteTimeUtc(path);
                    }
                    catch
                    {
                    }
                    if (newest == null || writeTime > newestWriteTime)
                    {
                        newest = loaded;
                        newestWriteTime = writeTime;
                    }
                }
            }
            return newest ?? new List<TranslationRecord>();
        }

        public static bool Save(List<TranslationRecord> records)
        {
            List<TranslationRecord> snapshot = Normalize(records);
            string payload = Serializer.Serialize(snapshot);
            bool saved = false;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in new[] { DefaultPath, FallbackPath })
            {
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                {
                    continue;
                }
                try
                {
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(path, payload, Encoding.UTF8);
                    saved = true;
                }
                catch
                {
                }
            }
            return saved;
        }

        public static TranslationRecord Add(
            List<TranslationRecord> records,
            string original,
            string translated,
            string provider,
            DateTime createdAt)
        {
            string source = CapturedText.Normalize(original);
            string target = CapturedText.Normalize(translated);
            if (source.Length == 0 || target.Length == 0)
            {
                return null;
            }

            bool wasFavorite = false;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                TranslationRecord existing = records[i];
                if (string.Equals(existing.Original, source, StringComparison.Ordinal) &&
                    string.Equals(existing.Translation, target, StringComparison.Ordinal))
                {
                    wasFavorite = existing.IsFavorite;
                    records.RemoveAt(i);
                }
            }
            TranslationRecord record = new TranslationRecord
            {
                CreatedAt = createdAt,
                Original = source,
                Translation = target,
                Provider = provider ?? string.Empty,
                IsFavorite = wasFavorite
            };
            records.Insert(0, record);
            if (records.Count > MaximumRecords)
            {
                records.RemoveRange(MaximumRecords, records.Count - MaximumRecords);
            }
            return record;
        }

        public static int ClearHistoryKeepFavorites(List<TranslationRecord> records)
        {
            int before = records.Count;
            records.RemoveAll(delegate(TranslationRecord record)
            {
                return record != null && !record.IsFavorite;
            });
            return before - records.Count;
        }

        public static string BuildCsv(IEnumerable<TranslationRecord> records)
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("时间,原文,译文,来源,收藏");
            foreach (TranslationRecord record in records)
            {
                if (record == null)
                {
                    continue;
                }
                csv.Append(CsvCell(record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));
                csv.Append(',');
                csv.Append(CsvCell(record.Original));
                csv.Append(',');
                csv.Append(CsvCell(record.Translation));
                csv.Append(',');
                csv.Append(CsvCell(record.Provider));
                csv.Append(',');
                csv.AppendLine(record.IsFavorite ? "是" : "否");
            }
            return csv.ToString();
        }

        private static string CsvCell(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static List<TranslationRecord> TryLoad(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }
                List<TranslationRecord> records =
                    Serializer.Deserialize<List<TranslationRecord>>(
                        File.ReadAllText(path, Encoding.UTF8));
                return Normalize(records);
            }
            catch
            {
                return null;
            }
        }

        private static List<TranslationRecord> Normalize(IEnumerable<TranslationRecord> records)
        {
            List<TranslationRecord> normalized = new List<TranslationRecord>();
            if (records == null)
            {
                return normalized;
            }
            foreach (TranslationRecord raw in records)
            {
                if (raw == null)
                {
                    continue;
                }
                TranslationRecord record = raw.Clone();
                record.Original = CapturedText.Normalize(record.Original);
                record.Translation = CapturedText.Normalize(record.Translation);
                record.Provider = (record.Provider ?? string.Empty).Trim();
                if (record.Original.Length == 0 || record.Translation.Length == 0)
                {
                    continue;
                }
                normalized.Add(record);
                if (normalized.Count >= MaximumRecords)
                {
                    break;
                }
            }
            return normalized;
        }
    }

    internal static class StartupManager
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "BilingualOverlay";

        public static string BuildRunCommand(string executablePath)
        {
            string path = Path.GetFullPath(executablePath ?? Application.ExecutablePath);
            return "\"" + path + "\"";
        }

        public static bool IsEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled, string executablePath, out string error)
        {
            error = "";
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        error = "无法打开当前用户启动项。";
                        return false;
                    }
                    if (enabled)
                    {
                        key.SetValue(ValueName, BuildRunCommand(executablePath));
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    internal static class VersionTools
    {
        public static bool IsNewer(string candidate, string current)
        {
            int[] candidateParts = ParseParts(candidate);
            int[] currentParts = ParseParts(current);
            int count = Math.Max(candidateParts.Length, currentParts.Length);
            for (int i = 0; i < count; i++)
            {
                int left = i < candidateParts.Length ? candidateParts[i] : 0;
                int right = i < currentParts.Length ? currentParts[i] : 0;
                if (left != right)
                {
                    return left > right;
                }
            }
            return false;
        }

        private static int[] ParseParts(string version)
        {
            string text = (version ?? string.Empty).Trim().TrimStart('v', 'V');
            string[] parts = text.Split('.');
            List<int> numbers = new List<int>();
            foreach (string part in parts)
            {
                StringBuilder digits = new StringBuilder();
                foreach (char c in part)
                {
                    if (c < '0' || c > '9')
                    {
                        break;
                    }
                    digits.Append(c);
                }
                int number;
                if (!int.TryParse(digits.ToString(), out number))
                {
                    number = 0;
                }
                numbers.Add(number);
            }
            return numbers.ToArray();
        }
    }

    internal sealed class UpdateManifest
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class UpdateCheckResult
    {
        public bool Success;
        public bool HasUpdate;
        public string Message;
        public UpdateManifest Manifest;
    }

    internal sealed class UpdateDownloadResult
    {
        public bool Success;
        public string FilePath;
        public string Message;
    }

    internal static class UpdateService
    {
        public static async Task<UpdateCheckResult> CheckAsync(string manifestUrl)
        {
            string url = (manifestUrl ?? string.Empty).Trim();
            if (url.Length == 0)
            {
                return new UpdateCheckResult
                {
                    Message = "尚未填写更新清单地址。"
                };
            }

            Uri manifestUri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out manifestUri) ||
                !string.Equals(manifestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult
                {
                    Message = "更新清单必须使用 HTTPS 地址。"
                };
            }

            try
            {
                using (HttpClient client = new HttpClient())
                using (CancellationTokenSource timeout =
                    new CancellationTokenSource(TimeSpan.FromSeconds(12)))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "BilingualOverlay/" + Program.AppVersion);
                    HttpResponseMessage response = await client.GetAsync(
                        manifestUri,
                        timeout.Token).ConfigureAwait(false);
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    UpdateManifest manifest = serializer.Deserialize<UpdateManifest>(json);
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                    {
                        return new UpdateCheckResult { Message = "更新清单格式无效。" };
                    }

                    bool hasUpdate = VersionTools.IsNewer(manifest.Version, Program.AppVersion);
                    return new UpdateCheckResult
                    {
                        Success = true,
                        HasUpdate = hasUpdate,
                        Manifest = manifest,
                        Message = hasUpdate
                            ? "发现新版本 " + manifest.Version + "。"
                            : "当前已是最新版本 " + Program.AppVersion + "。"
                    };
                }
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Message = "检查更新失败：" + ex.Message
                };
            }
        }

        public static async Task<UpdateDownloadResult> DownloadAsync(UpdateManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                return new UpdateDownloadResult { Message = "更新清单缺少下载地址。" };
            }
            string expectedHash = (manifest.Sha256 ?? string.Empty).Trim();
            if (!IsSha256(expectedHash))
            {
                return new UpdateDownloadResult
                {
                    Message = "更新清单缺少有效的 SHA-256，已拒绝下载。"
                };
            }

            Uri downloadUri;
            if (!Uri.TryCreate(manifest.DownloadUrl.Trim(), UriKind.Absolute, out downloadUri) ||
                !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateDownloadResult
                {
                    Message = "更新包必须使用 HTTPS 地址。"
                };
            }

            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BilingualOverlay",
                "updates");
            string destination = Path.Combine(
                directory,
                "BilingualOverlaySetup-" + SafeFileName(manifest.Version) + ".exe");
            string temporary = destination + ".download";
            try
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                using (HttpClient client = new HttpClient())
                using (CancellationTokenSource timeout =
                    new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "BilingualOverlay/" + Program.AppVersion);
                    using (HttpResponseMessage response = await client.GetAsync(
                        downloadUri,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength.HasValue &&
                            response.Content.Headers.ContentLength.Value > 200L * 1024L * 1024L)
                        {
                            return new UpdateDownloadResult
                            {
                                Message = "更新包超过 200 MB 安全限制。"
                            };
                        }

                        using (Stream input = await response.Content
                            .ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream output = new FileStream(
                            temporary,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None))
                        {
                            byte[] buffer = new byte[81920];
                            long total = 0;
                            while (true)
                            {
                                int read = await input.ReadAsync(
                                    buffer,
                                    0,
                                    buffer.Length,
                                    timeout.Token).ConfigureAwait(false);
                                if (read <= 0)
                                {
                                    break;
                                }
                                total += read;
                                if (total > 200L * 1024L * 1024L)
                                {
                                    throw new InvalidOperationException(
                                        "更新包超过 200 MB 安全限制。");
                                }
                                await output.WriteAsync(
                                    buffer,
                                    0,
                                    read,
                                    timeout.Token).ConfigureAwait(false);
                            }
                        }
                    }
                }

                string actualHash = ComputeSha256(temporary);
                if (!string.Equals(
                    actualHash,
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temporary);
                    return new UpdateDownloadResult
                    {
                        Message = "SHA-256 校验失败；更新包可能损坏或被篡改。"
                    };
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Move(temporary, destination);
                return new UpdateDownloadResult
                {
                    Success = true,
                    FilePath = destination,
                    Message = "更新包已通过 SHA-256 校验。"
                };
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                }
                return new UpdateDownloadResult
                {
                    Message = ex.Message
                };
            }
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    text.Append(value.ToString("X2"));
                }
                return text.ToString();
            }
        }

        private static string SafeFileName(string version)
        {
            string value = (version ?? "update").Trim();
            StringBuilder safe = new StringBuilder();
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                {
                    safe.Append(c);
                }
            }
            return safe.Length == 0 ? "update" : safe.ToString();
        }
    }

    internal static class InstallationManager
    {
        private const string UninstallKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BilingualOverlay";
        private static readonly string[] ProgramFileNames =
        {
            "BilingualOverlay.exe",
            "dictionary.tsv",
            "glossary.tsv",
            "README-使用说明.txt"
        };

        public static string InstallDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "BilingualOverlay");
            }
        }

        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BilingualOverlay");
            }
        }

        public static int RunSetup()
        {
            DialogResult answer = MessageBox.Show(
                "将把中英对照悬浮窗安装到当前用户目录：\r\n" +
                InstallDirectory +
                "\r\n\r\n不会删除已有设置、历史或收藏。是否继续？",
                "安装中英对照悬浮窗 " + Program.AppVersion,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return 2;
            }

            try
            {
                StopRunningCopies();
                Install();
                DialogResult launch = MessageBox.Show(
                    "安装完成。\r\n\r\n已创建开始菜单和桌面快捷方式，并注册卸载入口。是否立即启动？",
                    "安装完成",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (launch == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(InstallDirectory, "BilingualOverlay.exe"),
                        UseShellExecute = true
                    });
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "安装失败：\r\n" + ex.Message,
                    "安装失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        public static int RunUninstall()
        {
            string current = Path.GetFullPath(Application.ExecutablePath);
            string installed = Path.Combine(InstallDirectory, "BilingualOverlay.exe");
            if (!string.Equals(current, Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "请从开始菜单运行“卸载中英对照悬浮窗”，或运行安装目录中的程序。",
                    "无法卸载",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 2;
            }

            DialogResult answer = MessageBox.Show(
                "确定卸载中英对照悬浮窗吗？\r\n\r\n选择“是”同时删除设置、历史与收藏；选择“否”保留个人数据。",
                "卸载中英对照悬浮窗",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel)
            {
                return 2;
            }

            bool removeData = answer == DialogResult.Yes;
            string error;
            StartupManager.SetEnabled(false, current, out error);
            RemoveShortcuts();
            RemoveUninstallRegistration();
            if (removeData)
            {
                SafeDeleteDirectory(DataDirectory);
                SafeDeleteFile(Path.Combine(InstallDirectory, "settings.json"));
                SafeDeleteFile(Path.Combine(InstallDirectory, "history.json"));
                SafeDeleteFile(Path.Combine(InstallDirectory, "window-position.txt"));
            }

            foreach (string name in new[]
            {
                "dictionary.tsv", "glossary.tsv", "README-使用说明.txt"
            })
            {
                SafeDeleteFile(Path.Combine(InstallDirectory, name));
            }
            ScheduleSelfDelete(current, InstallDirectory);
            MessageBox.Show(
                "卸载完成。程序文件将在进程退出后删除。",
                "卸载完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        internal static void Install()
        {
            string sourceDirectory = AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(InstallDirectory);
            CopyProgramFiles(sourceDirectory, InstallDirectory);

            string executable = Path.Combine(InstallDirectory, "BilingualOverlay.exe");
            CreateShortcut(
                Path.Combine(GetStartMenuDirectory(), "中英对照悬浮窗.lnk"),
                executable,
                "",
                "实时中英对照悬浮窗");
            CreateShortcut(
                Path.Combine(GetStartMenuDirectory(), "卸载中英对照悬浮窗.lnk"),
                executable,
                "--uninstall",
                "卸载中英对照悬浮窗");
            CreateShortcut(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "中英对照悬浮窗.lnk"),
                executable,
                "",
                "实时中英对照悬浮窗");
            RegisterUninstall(executable);
        }

        internal static int CopyProgramFiles(
            string sourceDirectory,
            string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            int copied = 0;
            foreach (string name in ProgramFileNames)
            {
                string source = Path.Combine(sourceDirectory, name);
                string destination = Path.Combine(destinationDirectory, name);
                if (File.Exists(source) &&
                    !string.Equals(
                        Path.GetFullPath(source),
                        Path.GetFullPath(destination),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, destination, true);
                    copied++;
                }
            }
            if (!File.Exists(Path.Combine(destinationDirectory, "BilingualOverlay.exe")))
            {
                throw new FileNotFoundException(
                    "安装包缺少 BilingualOverlay.exe。",
                    Path.Combine(sourceDirectory, "BilingualOverlay.exe"));
            }
            return copied;
        }

        private static void RegisterUninstall(string executable)
        {
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法创建卸载注册信息。");
                }
                key.SetValue("DisplayName", "中英对照悬浮窗");
                key.SetValue("DisplayVersion", Program.AppVersion);
                key.SetValue("Publisher", "BilingualOverlay");
                key.SetValue("InstallLocation", InstallDirectory);
                key.SetValue("DisplayIcon", executable);
                key.SetValue("UninstallString", "\"" + executable + "\" --uninstall");
                key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }

        private static void RemoveUninstallRegistration()
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    UninstallKeyPath, false);
            }
            catch
            {
            }
        }

        private static void RemoveShortcuts()
        {
            SafeDeleteFile(Path.Combine(GetStartMenuDirectory(), "中英对照悬浮窗.lnk"));
            SafeDeleteFile(Path.Combine(GetStartMenuDirectory(), "卸载中英对照悬浮窗.lnk"));
            SafeDeleteFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "中英对照悬浮窗.lnk"));
        }

        private static string GetStartMenuDirectory()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void CreateShortcut(
            string shortcutPath,
            string executable,
            string arguments,
            string description)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember(
                    "TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { executable });
                shortcutType.InvokeMember(
                    "Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                shortcutType.InvokeMember(
                    "WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    new object[] { InstallDirectory });
                shortcutType.InvokeMember(
                    "Description", BindingFlags.SetProperty, null, shortcut,
                    new object[] { description });
                shortcutType.InvokeMember(
                    "Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        private static void StopRunningCopies()
        {
            foreach (Process process in Process.GetProcessesByName("BilingualOverlay"))
            {
                try
                {
                    if (process.Id == Process.GetCurrentProcess().Id)
                    {
                        continue;
                    }
                    process.CloseMainWindow();
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill();
                        process.WaitForExit(1500);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static void ScheduleSelfDelete(string executable, string directory)
        {
            try
            {
                string command =
                    "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" +
                    executable.Replace("\"", "\"\"") + "\" & rmdir \"" +
                    directory.Replace("\"", "\"\"") + "\"";
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = command,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch
            {
            }
        }
    }

    internal static class ProcessIdentity
    {
        public static string Normalize(string processName)
        {
            string value = (processName ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                value = Path.GetFileName(value);
            }
            catch
            {
            }

            if (value.Length > 0 && value.IndexOf('.') < 0)
            {
                value += ".exe";
            }
            return value.ToLowerInvariant();
        }
    }

    internal static class PrivacyGuard
    {
        private static readonly string[] SensitiveTitleTerms =
        {
            "密码", "password", "private browsing", "incognito", "inprivate", "无痕"
        };

        public static string GetBlockReason(
            string processName,
            string windowTitle,
            IEnumerable<string> excludedProcesses)
        {
            if (IsExcluded(processName, excludedProcesses))
            {
                return "程序排除";
            }

            string title = (windowTitle ?? string.Empty).ToLowerInvariant();
            foreach (string term in SensitiveTitleTerms)
            {
                if (title.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "敏感窗口";
                }
            }
            return null;
        }

        public static bool IsExcluded(string processName, IEnumerable<string> excludedProcesses)
        {
            string current = ProcessIdentity.Normalize(processName);
            if (current.Length == 0 || excludedProcesses == null)
            {
                return false;
            }

            foreach (string excluded in excludedProcesses)
            {
                if (string.Equals(current, ProcessIdentity.Normalize(excluded), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal sealed class CaptureCandidate
    {
        public string Text;
        public string Source;
    }

    internal static class InputFallback
    {
        public static CaptureCandidate Choose(string selectedText, string clipboardText, int maxCharacters)
        {
            string selected = CapturedText.Normalize(selectedText);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                return new CaptureCandidate
                {
                    Text = CapturedText.Tail(selected, maxCharacters),
                    Source = "选中文字"
                };
            }

            string clipboard = CapturedText.Normalize(clipboardText);
            if (!string.IsNullOrWhiteSpace(clipboard))
            {
                return new CaptureCandidate
                {
                    Text = CapturedText.Tail(clipboard, maxCharacters),
                    Source = "剪贴板"
                };
            }
            return null;
        }
    }

    internal static class ClickThroughWindowStyle
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public static int WithClickThrough(int exStyle, bool enabled)
        {
            if (enabled)
            {
                return exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED;
            }

            return exStyle & ~WS_EX_TRANSPARENT;
        }

        public static bool Apply(IntPtr handle, bool enabled)
        {
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            int current = GetExtendedStyle(handle);
            int next = WithClickThrough(current, enabled);
            if (next != current && !SetExtendedStyle(handle, next))
            {
                return false;
            }

            SetWindowPos(
                handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            return true;
        }

        public static bool IsEnabled(IntPtr handle)
        {
            return handle != IntPtr.Zero &&
                (GetExtendedStyle(handle) & WS_EX_TRANSPARENT) != 0;
        }

        private static int GetExtendedStyle(IntPtr handle)
        {
            if (IntPtr.Size == 8)
            {
                long value = GetWindowLongPtr64(handle, GWL_EXSTYLE).ToInt64();
                return unchecked((int)value);
            }

            return GetWindowLong32(handle, GWL_EXSTYLE);
        }

        private static bool SetExtendedStyle(IntPtr handle, int style)
        {
            if (IntPtr.Size == 8)
            {
                SetLastError(0);
                IntPtr previous = SetWindowLongPtr64(handle, GWL_EXSTYLE, new IntPtr(style));
                return previous != IntPtr.Zero || Marshal.GetLastWin32Error() == 0;
            }

            SetLastError(0);
            int previous32 = SetWindowLong32(handle, GWL_EXSTYLE, style);
            return previous32 != 0 || Marshal.GetLastWin32Error() == 0;
        }

        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint errorCode);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr handle, int index, int newStyle);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr handle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }

    internal sealed class OverlayForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_DPICHANGED = 0x02E0;
        private const int HTCLIENT = 1;
        private const int HTTRANSPARENT = -1;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private readonly Color background = Color.FromArgb(17, 24, 39);
        private readonly Color panelColor = Color.FromArgb(30, 41, 59);
        private readonly Color accent = Color.FromArgb(52, 211, 153);
        private readonly Color muted = Color.FromArgb(148, 163, 184);

        private readonly Label statusLabel;
        private readonly Label shortcutLabel;
        private readonly RichTextBox originalText;
        private readonly RichTextBox translationText;
        private readonly WinFormsTimer debounceTimer;
        private readonly WinFormsTimer fullscreenTimer;
        private readonly GlobalKeyboardHook keyboardHook;
        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenu trayMenu;
        private readonly MenuItem pauseMenuItem;
        private readonly MenuItem clickThroughMenuItem;
        private readonly MenuItem lockPositionMenuItem;
        private readonly MenuItem localOnlyMenuItem;
        private readonly Button favoriteButton;
        private readonly AutomationFocusChangedEventHandler focusChangedHandler;
        private readonly Font titleFont;
        private readonly Font captionFont;
        private Font contentFont;
        private Font translationFont;
        private List<TranslationRecord> history;

        private OverlaySettings settings;
        private AutomationSnapshot lastSnapshot;
        private CancellationTokenSource translationCancellation;
        private readonly bool demoMode;
        private bool monitoring = true;
        private bool readingFocusedText;
        private bool readingPending;
        private bool formVisible = true;
        private bool hiddenForFullscreen;
        private bool dragging;
        private Point dragOffset;
        private int translationSequence;
        private bool translationRunning;
        private string completedTranslationSource;
        private string currentOriginal;
        private string currentTranslation;
        private string currentProvider;
        private bool currentTranslationIsError;
        private bool displayRecoveryPending;

        public OverlayForm()
            : this(false)
        {
        }

        public OverlayForm(bool showDemo)
        {
            settings = SettingsStore.Load();
            settings.Normalize();
            history = TranslationHistoryStore.Load();
            if (settings.ClearHistoryOnStartup)
            {
                if (TranslationHistoryStore.ClearHistoryKeepFavorites(history) > 0)
                {
                    TranslationHistoryStore.Save(history);
                }
            }
            demoMode = showDemo ||
                string.Equals(
                    Environment.GetEnvironmentVariable("BILINGUAL_OVERLAY_DEMO"),
                    "1",
                    StringComparison.Ordinal);
            Text = "中英对照悬浮窗";
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(settings.WindowWidth, settings.WindowHeight);
            MinimumSize = new Size(500, 240);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = background;
            Opacity = settings.Opacity;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(1);
            AllowDrop = false;

            titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            captionFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
            contentFont = CreateContentFont(settings.FontScale);
            translationFont = CreateTranslationFont(settings.FontScale);

            TableLayoutPanel rootLayout = new TableLayoutPanel();
            rootLayout.Dock = DockStyle.Fill;
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 3;
            rootLayout.BackColor = background;
            rootLayout.Padding = new Padding(1);
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            Controls.Add(rootLayout);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Padding = new Padding(18, 11, 48, 5);
            header.BackColor = background;
            header.MouseDown += DragMouseDown;
            header.MouseMove += DragMouseMove;
            header.MouseUp += DragMouseUp;
            rootLayout.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "实时中英对照";
            title.Font = titleFont;
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(18, 12);
            title.MouseDown += DragMouseDown;
            title.MouseMove += DragMouseMove;
            title.MouseUp += DragMouseUp;
            header.Controls.Add(title);

            statusLabel = new Label();
            statusLabel.Text = "●  监听中";
            statusLabel.Font = captionFont;
            statusLabel.ForeColor = accent;
            statusLabel.AutoSize = false;
            statusLabel.AutoEllipsis = true;
            statusLabel.Size = new Size(260, 24);
            statusLabel.Location = new Point(126, 14);
            statusLabel.MouseDown += DragMouseDown;
            statusLabel.MouseMove += DragMouseMove;
            statusLabel.MouseUp += DragMouseUp;
            header.Controls.Add(statusLabel);

            Label settingsLabel = new Label();
            settingsLabel.Text = "⚙";
            settingsLabel.Font = new Font("Segoe UI Symbol", 14f, FontStyle.Regular, GraphicsUnit.Point);
            settingsLabel.ForeColor = muted;
            settingsLabel.TextAlign = ContentAlignment.MiddleCenter;
            settingsLabel.Size = new Size(36, 32);
            settingsLabel.Location = new Point(ClientSize.Width - 85, 7);
            settingsLabel.Cursor = Cursors.Hand;
            settingsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsLabel.Click += delegate { OpenSettings(); };
            settingsLabel.MouseEnter += delegate { settingsLabel.ForeColor = Color.White; };
            settingsLabel.MouseLeave += delegate { settingsLabel.ForeColor = muted; };
            header.Controls.Add(settingsLabel);

            Label closeLabel = new Label();
            closeLabel.Text = "×";
            closeLabel.Font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Point);
            closeLabel.ForeColor = muted;
            closeLabel.TextAlign = ContentAlignment.MiddleCenter;
            closeLabel.Size = new Size(36, 32);
            closeLabel.Location = new Point(ClientSize.Width - 45, 7);
            closeLabel.Cursor = Cursors.Hand;
            closeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeLabel.Click += delegate { HideOverlay(); };
            closeLabel.MouseEnter += delegate { closeLabel.ForeColor = Color.White; };
            closeLabel.MouseLeave += delegate { closeLabel.ForeColor = muted; };
            header.Controls.Add(closeLabel);

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(18, 8, 18, 6);
            content.BackColor = background;
            rootLayout.Controls.Add(content, 0, 1);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.BackColor = background;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
            content.Controls.Add(layout);

            originalText = CreateContentBox(
                "在任意输入框里开始打字…",
                contentFont,
                Color.FromArgb(226, 232, 240));
            layout.Controls.Add(originalText, 0, 0);

            translationText = CreateContentBox("准备就绪", translationFont, accent);
            layout.Controls.Add(translationText, 0, 1);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Padding = new Padding(18, 8, 18, 8);
            footer.BackColor = panelColor;
            rootLayout.Controls.Add(footer, 0, 2);

            TableLayoutPanel footerLayout = new TableLayoutPanel();
            footerLayout.Dock = DockStyle.Fill;
            footerLayout.ColumnCount = 2;
            footerLayout.RowCount = 1;
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(footerLayout);

            shortcutLabel = new Label();
            shortcutLabel.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            shortcutLabel.ForeColor = muted;
            shortcutLabel.Dock = DockStyle.Fill;
            shortcutLabel.TextAlign = ContentAlignment.MiddleLeft;
            shortcutLabel.AutoEllipsis = true;
            footerLayout.Controls.Add(shortcutLabel, 0, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.Margin = new Padding(4, 2, 0, 2);
            footerLayout.Controls.Add(actions, 1, 0);

            Button copyButton = MakeOverlayButton("复制译文");
            copyButton.Click += delegate { CopyCurrentTranslation(); };
            actions.Controls.Add(copyButton);

            favoriteButton = MakeOverlayButton("收藏");
            favoriteButton.Click += delegate { ToggleFavoriteCurrentTranslation(); };
            actions.Controls.Add(favoriteButton);

            Button historyButton = MakeOverlayButton("历史");
            historyButton.Click += delegate { OpenHistory(); };
            actions.Controls.Add(historyButton);

            debounceTimer = new WinFormsTimer();
            debounceTimer.Interval = 650;
            debounceTimer.Tick += DebounceTimerTick;

            fullscreenTimer = new WinFormsTimer();
            fullscreenTimer.Interval = 900;
            fullscreenTimer.Tick += FullscreenTimerTick;

            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.GlobalKeyDown += KeyboardHookGlobalKeyDown;
            keyboardHook.ToggleMonitoring += ToggleMonitoring;
            keyboardHook.TranslateClipboard += TranslateSelectionOrClipboard;
            keyboardHook.ShowHistoryRequested += OpenHistory;
            keyboardHook.ShowOverlayRequested += ShowOverlayAndRecenter;
            keyboardHook.ShowSettingsRequested += OpenSettings;
            keyboardHook.ConfigureHotkeys(
                settings.SettingsHotkey,
                settings.TranslateHotkey,
                settings.HistoryHotkey);
            focusChangedHandler = AutomationFocusChanged;

            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("显示并复位悬浮窗", delegate { ShowOverlayAndRecenter(); });
            pauseMenuItem = trayMenu.MenuItems.Add("暂停监听", delegate { ToggleMonitoring(); });
            trayMenu.MenuItems.Add("-");
            localOnlyMenuItem = trayMenu.MenuItems.Add("仅本地翻译", delegate { ToggleLocalOnly(); });
            clickThroughMenuItem = trayMenu.MenuItems.Add("点击穿透", delegate { ToggleClickThrough(); });
            lockPositionMenuItem = trayMenu.MenuItems.Add("锁定位置", delegate { ToggleLockPosition(); });
            trayMenu.MenuItems.Add("设置…", delegate { OpenSettings(); });
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("翻译选中/剪贴板", delegate { TranslateSelectionOrClipboard(); });
            trayMenu.MenuItems.Add("历史与收藏…", delegate { OpenHistory(); });
            trayMenu.MenuItems.Add("清空显示", delegate { ClearDisplay(); });
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("退出", delegate { ExitApplication(); });

            notifyIcon = new NotifyIcon();
            notifyIcon.Text = "中英对照悬浮窗 · F8 暂停/开启";
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Visible = true;
            notifyIcon.ContextMenu = trayMenu;
            notifyIcon.DoubleClick += delegate
            {
                if (formVisible)
                {
                    HideOverlay();
                }
                else
                {
                    ShowOverlay();
                }
            };

            Shown += OverlayFormShown;
            VisibleChanged += delegate { formVisible = Visible; };
            FormClosing += OverlayFormClosing;
            DoubleClick += delegate { HideOverlay(); };
            MouseDown += DragMouseDown;
            MouseMove += DragMouseMove;
            MouseUp += DragMouseUp;

            LoadDictionaryFile();
            ApplyTranslationSettings();
            UpdateSettingMenuChecks();
            UpdateShortcutLabel();
            SetStatus("●  监听中" + TranslationModeSuffix(), accent);
        }

        private static Button MakeOverlayButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.FromArgb(51, 65, 85);
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
            button.Margin = new Padding(4, 1, 0, 1);
            button.Padding = new Padding(6, 2, 6, 2);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private RichTextBox CreateContentBox(string text, Font font, Color color)
        {
            RichTextBox box = new RichTextBox();
            box.Text = text;
            box.Font = font;
            box.ForeColor = color;
            box.BackColor = background;
            box.BorderStyle = BorderStyle.None;
            box.Dock = DockStyle.Fill;
            box.ReadOnly = true;
            box.Multiline = true;
            box.WordWrap = true;
            box.ScrollBars = RichTextBoxScrollBars.Vertical;
            box.DetectUrls = false;
            box.HideSelection = true;
            box.ShortcutsEnabled = true;
            box.TabStop = false;
            box.Margin = new Padding(0, 4, 0, 4);
            box.MouseWheel += ContentMouseWheel;
            return box;
        }

        private static Font CreateContentFont(double scale)
        {
            return new Font(
                "Microsoft YaHei UI",
                (float)(14.0 * scale),
                FontStyle.Regular,
                GraphicsUnit.Point);
        }

        private static Font CreateTranslationFont(double scale)
        {
            return new Font(
                "Microsoft YaHei UI",
                (float)(19.0 * scale),
                FontStyle.Bold,
                GraphicsUnit.Point);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                if (settings != null && settings.ClickThrough)
                {
                    cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                    cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                }
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ClickThroughWindowStyle.Apply(Handle, settings != null && settings.ClickThrough);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_NCHITTEST && settings != null && settings.ClickThrough)
            {
                message.Result = new IntPtr(HTTRANSPARENT);
                return;
            }

            if (message.Msg == WM_DISPLAYCHANGE || message.Msg == WM_DPICHANGED)
            {
                base.WndProc(ref message);
                ScheduleDisplayRecovery();
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            int radius = 16;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(Width - radius - 1, 0, radius, radius, 270, 90);
            path.AddArc(Width - radius - 1, Height - radius - 1, radius, radius, 0, 90);
            path.AddArc(0, Height - radius - 1, radius, radius, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
            path.Dispose();
        }

        private void OverlayFormShown(object sender, EventArgs e)
        {
            keyboardHook.Install();
            try
            {
                Automation.AddAutomationFocusChangedEventHandler(focusChangedHandler);
            }
            catch
            {
            }
            CaptureBaseline();
            Point savedPosition;
            if (WindowPositionStore.TryLoad(out savedPosition) &&
                WindowPlacement.IsFullyVisible(savedPosition, Size, Screen.AllScreens))
            {
                Location = savedPosition;
                KeepWindowOnTop();
            }
            else
            {
                ResetWindowPosition();
            }
            if (demoMode)
            {
                ShowDemoContent();
            }
            fullscreenTimer.Start();
        }

        private void KeyboardHookGlobalKeyDown()
        {
            if (!monitoring || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(RestartDebounce));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void RestartDebounce()
        {
            if (!monitoring)
            {
                return;
            }
            debounceTimer.Stop();
            debounceTimer.Start();
        }

        private void DebounceTimerTick(object sender, EventArgs e)
        {
            debounceTimer.Stop();
            if (!monitoring)
            {
                return;
            }

            if (readingFocusedText)
            {
                readingPending = true;
                return;
            }

            SetStatus("◌  正在读取输入框", Color.FromArgb(148, 163, 184));
            readingFocusedText = true;
            List<string> excluded = new List<string>(settings.ExcludedProcesses);
            Task.Run(() =>
            {
                AutomationSnapshot snapshot = FocusedTextReader.TryRead(excluded);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() => ProcessSnapshot(snapshot)));
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }).ContinueWith(delegate
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(FinishRead));
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void FinishRead()
        {
            readingFocusedText = false;
            if (readingPending && monitoring)
            {
                readingPending = false;
                RestartDebounce();
            }
        }

        private void ProcessSnapshot(AutomationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                SetOriginal("此输入框没有向 Windows 提供文字");
                SetTranslation("选中文字后按 Ctrl+Alt+T", Color.FromArgb(251, 191, 36));
                SetStatus("!  未读取 · 可改用快捷翻译", Color.FromArgb(251, 191, 36));
                return;
            }

            if (snapshot.Blocked)
            {
                ShowPrivacyMessage(snapshot);
                return;
            }

            if (snapshot.IsPassword)
            {
                lastSnapshot = snapshot;
                SetOriginal("密码框内容已隐藏");
                SetTranslation("隐私保护：不读取、不翻译、不联网", Color.FromArgb(251, 191, 36));
                SetStatus("●  密码框已安全跳过", Color.FromArgb(251, 191, 36));
                return;
            }

            bool fieldChanged = lastSnapshot == null || lastSnapshot.Key != snapshot.Key;
            if (!fieldChanged &&
                string.Equals(lastSnapshot.Text, snapshot.Text, StringComparison.Ordinal))
            {
                return;
            }

            string inserted = TextDelta.ExtractInserted(
                fieldChanged ? null : lastSnapshot.Text,
                snapshot.Text);
            lastSnapshot = snapshot;

            if (fieldChanged)
            {
                // A baseline read can lose the race with a very fast first typing
                // burst. The debounce event means input happened, so show the
                // readable field tail instead of silently discarding that input.
                string currentField = CapturedText.Normalize(snapshot.Text);
                if (currentField.Length == 0)
                {
                    return;
                }
                ShowTranslation(CapturedText.Tail(currentField, 1600), "当前输入框");
                return;
            }

            string source = CapturedText.Normalize(inserted);
            if (source.Length == 0)
            {
                return;
            }

            ShowTranslation(CapturedText.Tail(source, 1600), "自动捕获");
        }

        private void ShowTranslation(string original, string sourceLabel)
        {
            if (string.Equals(original, completedTranslationSource, StringComparison.Ordinal))
            {
                SetStatus("✓  " + sourceLabel + " · 内容未变化", accent);
                return;
            }
            if (translationRunning && string.Equals(original, originalText.Text, StringComparison.Ordinal))
            {
                return;
            }

            SetOriginal(original);
            SetTranslation("正在翻译…", Color.FromArgb(203, 213, 225));
            currentOriginal = original;
            currentTranslation = string.Empty;
            currentProvider = string.Empty;
            currentTranslationIsError = false;
            UpdateFavoriteButton();
            SetStatus(
                "✓  已捕获 · " + (settings.LocalOnly ? "本地模式" : "正在翻译"),
                accent);
            translationRunning = true;
            completedTranslationSource = null;

            translationSequence++;
            int sequence = translationSequence;
            if (translationCancellation != null)
            {
                translationCancellation.Cancel();
                translationCancellation.Dispose();
            }
            translationCancellation = new CancellationTokenSource();
            CancellationToken token = translationCancellation.Token;

            TranslationService.TranslateAsync(original, token).ContinueWith(task =>
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (sequence != translationSequence)
                            {
                                return;
                            }

                            if (task.IsCanceled)
                            {
                                if (sequence == translationSequence)
                                {
                                    translationRunning = false;
                                }
                                return;
                            }

                            TranslationResult result = task.IsFaulted
                                ? TranslationResult.Failed("翻译失败，请稍后重试")
                                : task.Result;
                            SetTranslation(
                                result.Text,
                                result.IsError ? Color.FromArgb(251, 146, 60) : accent);
                            currentTranslation = result.IsError ? string.Empty : result.Text;
                            currentProvider = result.Provider ?? string.Empty;
                            currentTranslationIsError = result.IsError;
                            translationRunning = false;
                            if (!result.IsError)
                            {
                                completedTranslationSource = original;
                                RecordHistory(original, result.Text, result.Provider);
                            }
                            UpdateFavoriteButton();
                            SetStatus(
                                (result.IsError ? "!  " : "✓  ") + sourceLabel + " · " + result.Provider,
                                result.IsError ? Color.FromArgb(251, 146, 60) : accent);
                        }));
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void ToggleMonitoring()
        {
            monitoring = !monitoring;
            debounceTimer.Stop();
            pauseMenuItem.Text = monitoring ? "暂停监听" : "继续监听";

            if (monitoring)
            {
                SetStatus("●  监听中" + TranslationModeSuffix(), accent);
                notifyIcon.Text = "中英对照悬浮窗 · 正在监听";
                CaptureBaseline();
            }
            else
            {
                SetStatus("Ⅱ  监听已暂停", Color.FromArgb(251, 191, 36));
                notifyIcon.Text = "中英对照悬浮窗 · 已暂停";
            }
        }

        private void TranslateSelectionOrClipboard()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            ShowOverlay();
            SetStatus("◌  正在读取选中文字", Color.FromArgb(148, 163, 184));
            List<string> excluded = new List<string>(settings.ExcludedProcesses);
            Task.Run(() => FocusedTextReader.TryReadSelection(excluded)).ContinueWith(task =>
            {
                AutomationSnapshot selection =
                    task.IsFaulted || task.IsCanceled ? null : task.Result;
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (selection != null && selection.Blocked)
                            {
                                ShowOverlay();
                                ShowPrivacyMessage(selection);
                                return;
                            }

                            if (selection != null && selection.IsPassword)
                            {
                                ShowOverlay();
                                SetOriginal("密码框内容已隐藏");
                                SetTranslation("隐私保护：不读取、不翻译、不联网",
                                    Color.FromArgb(251, 191, 36));
                                SetStatus("●  密码框已安全跳过", Color.FromArgb(251, 191, 36));
                                return;
                            }

                            string selectedText = selection == null ? string.Empty : selection.Text;
                            string clipboardText = string.Empty;
                            try
                            {
                                clipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                            }
                            catch (ExternalException)
                            {
                                SetStatus("!  剪贴板正被其他程序占用", Color.FromArgb(251, 146, 60));
                            }

                            CaptureCandidate candidate = InputFallback.Choose(selectedText, clipboardText, 1600);
                            if (candidate == null)
                            {
                                SetOriginal("没有选中文字，剪贴板也是空的");
                                SetTranslation("先选中文字，或复制到剪贴板后再按 Ctrl+Alt+T",
                                    Color.FromArgb(251, 191, 36));
                                SetStatus("!  没有可翻译文字", Color.FromArgb(251, 191, 36));
                                return;
                            }

                            ShowOverlay();
                            ShowTranslation(candidate.Text, candidate.Source);
                        }));
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void ClearDisplay()
        {
            SetOriginal("在任意输入框里开始打字…");
            SetTranslation("准备就绪", accent);
            currentOriginal = null;
            currentTranslation = null;
            currentProvider = null;
            currentTranslationIsError = false;
            UpdateFavoriteButton();
            SetStatus(
                monitoring ? "●  监听中" + TranslationModeSuffix() : "Ⅱ  监听已暂停",
                monitoring ? accent : Color.FromArgb(251, 191, 36));
        }

        private void ShowDemoContent()
        {
            SetOriginal("今天天气很好");
            SetTranslation("The weather is nice today", accent);
            currentOriginal = "今天天气很好";
            currentTranslation = "The weather is nice today";
            currentProvider = "本地词库";
            currentTranslationIsError = false;
            UpdateFavoriteButton();
            SetStatus("✓  演示 · 本地词库", accent);
        }

        private void ShowPrivacyMessage(AutomationSnapshot snapshot)
        {
            lastSnapshot = snapshot;
            if (translationCancellation != null)
            {
                translationCancellation.Cancel();
            }
            translationSequence++;
            translationRunning = false;
            completedTranslationSource = null;
            currentOriginal = null;
            currentTranslation = null;
            currentProvider = null;
            currentTranslationIsError = true;
            UpdateFavoriteButton();
            SetOriginal("隐私保护：已跳过此窗口");
            SetTranslation("未读取文字，也未发送到网络", Color.FromArgb(251, 191, 36));
            SetStatus(
                "■  " + snapshot.BlockReason + " · " +
                (string.IsNullOrWhiteSpace(snapshot.ProcessName) ? "未知程序" : snapshot.ProcessName),
                Color.FromArgb(251, 191, 36));
        }

        private void SetOriginal(string text)
        {
            originalText.Text = text ?? string.Empty;
            originalText.SelectionStart = 0;
            originalText.SelectionLength = 0;
            originalText.ScrollToCaret();
        }

        private void SetTranslation(string text, Color color)
        {
            translationText.Text = text ?? string.Empty;
            translationText.ForeColor = color;
            translationText.SelectionStart = 0;
            translationText.SelectionLength = 0;
            translationText.ScrollToCaret();
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        private void CopyCurrentTranslation()
        {
            if (string.IsNullOrWhiteSpace(currentTranslation) || currentTranslationIsError)
            {
                SetStatus("!  当前没有可复制的译文", Color.FromArgb(251, 191, 36));
                return;
            }

            try
            {
                Clipboard.SetText(currentTranslation);
                SetStatus("✓  译文已复制到剪贴板", accent);
            }
            catch (ExternalException)
            {
                SetStatus("!  剪贴板正被其他程序占用", Color.FromArgb(251, 146, 60));
            }
            catch (Exception ex)
            {
                SetStatus("!  复制失败 · " + ex.Message, Color.FromArgb(251, 146, 60));
            }
        }

        private void ToggleFavoriteCurrentTranslation()
        {
            if (string.IsNullOrWhiteSpace(currentOriginal) ||
                string.IsNullOrWhiteSpace(currentTranslation) ||
                currentTranslationIsError)
            {
                SetStatus("!  当前没有可收藏的译文", Color.FromArgb(251, 191, 36));
                return;
            }

            TranslationRecord record = FindHistoryRecord(currentOriginal, currentTranslation);
            if (record == null)
            {
                if (settings.HistoryEnabled != true)
                {
                    SetStatus("!  历史记录已关闭，无法收藏", Color.FromArgb(251, 191, 36));
                    return;
                }
                record = RecordHistory(currentOriginal, currentTranslation, currentProvider);
            }
            if (record == null)
            {
                SetStatus("!  收藏保存失败", Color.FromArgb(251, 146, 60));
                return;
            }

            record.IsFavorite = !record.IsFavorite;
            TranslationHistoryStore.Save(history);
            UpdateFavoriteButton();
            SetStatus(
                record.IsFavorite ? "★  已收藏常用句" : "☆  已取消收藏",
                record.IsFavorite ? Color.FromArgb(250, 204, 21) : accent);
        }

        private TranslationRecord RecordHistory(
            string original,
            string translated,
            string provider)
        {
            if (settings.HistoryEnabled != true)
            {
                UpdateFavoriteButton();
                return null;
            }

            TranslationRecord record = TranslationHistoryStore.Add(
                history,
                original,
                translated,
                provider,
                DateTime.Now);
            if (record != null)
            {
                TranslationHistoryStore.Save(history);
            }
            UpdateFavoriteButton();
            return record;
        }

        private TranslationRecord FindHistoryRecord(string original, string translated)
        {
            string source = CapturedText.Normalize(original);
            string target = CapturedText.Normalize(translated);
            foreach (TranslationRecord record in history)
            {
                if (record != null &&
                    string.Equals(record.Original, source, StringComparison.Ordinal) &&
                    string.Equals(record.Translation, target, StringComparison.Ordinal))
                {
                    return record;
                }
            }
            return null;
        }

        private void UpdateFavoriteButton()
        {
            if (favoriteButton == null)
            {
                return;
            }
            TranslationRecord record = string.IsNullOrWhiteSpace(currentOriginal) ||
                string.IsNullOrWhiteSpace(currentTranslation)
                ? null
                : FindHistoryRecord(currentOriginal, currentTranslation);
            favoriteButton.Text = record != null && record.IsFavorite ? "★ 已收藏" : "☆ 收藏";
            favoriteButton.Enabled = settings == null || settings.HistoryEnabled == true;
        }

        private void OpenHistory()
        {
            using (HistoryForm dialog = new HistoryForm(history, settings.HistoryEnabled == true))
            {
                dialog.ShowDialog(this);
                history = TranslationHistoryStore.Load();
                UpdateFavoriteButton();
                SetStatus(
                    "✓  历史记录 · " + history.Count + " 条",
                    accent);
            }
        }

        private void UpdateShortcutLabel()
        {
            shortcutLabel.Text =
                "F8 暂停  ·  F9 复位  ·  " +
                settings.TranslateHotkey + " 翻译选中  ·  " +
                settings.HistoryHotkey + " 历史";
        }

        private void ScheduleDisplayRecovery()
        {
            if (displayRecoveryPending || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            displayRecoveryPending = true;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    displayRecoveryPending = false;
                    if (!IsDisposed && formVisible)
                    {
                        EnsureWindowVisible();
                    }
                    KeepWindowOnTop();
                }));
            }
            catch (InvalidOperationException)
            {
                displayRecoveryPending = false;
            }
        }

        private void EnsureWindowVisible()
        {
            if (!formVisible || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            Screen[] screens = Screen.AllScreens;
            Rectangle[] areas = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++)
            {
                areas[i] = screens[i].WorkingArea;
            }
            if (!WindowPlacement.IsFullyVisible(Location, Size, areas))
            {
                ResetWindowPosition();
            }
        }

        private void ShowOverlay()
        {
            if (!Visible)
            {
                Show();
            }
            BringToFront();
            formVisible = true;
        }

        private void ShowOverlayAndRecenter()
        {
            if (settings.ClickThrough)
            {
                settings.ClickThrough = false;
                SettingsStore.Save(settings);
                UpdateSettingMenuChecks();
            }
            hiddenForFullscreen = false;
            ResetWindowPosition();
            ShowOverlay();
        }

        private void ResetWindowPosition()
        {
            Screen screen = GetActiveScreen();
            Rectangle area = screen.WorkingArea;
            Location = WindowPlacement.DefaultRightPosition(area, Size);
            SaveWindowPosition();
            KeepWindowOnTop();
        }

        private static Screen GetActiveScreen()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            return foregroundWindow != IntPtr.Zero
                ? Screen.FromHandle(foregroundWindow)
                : Screen.FromPoint(Cursor.Position);
        }

        private void KeepWindowOnTop()
        {
            if (Handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                Handle,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private void SaveWindowPosition()
        {
            WindowPositionStore.Save(Location);
        }

        private void HideOverlay()
        {
            SaveWindowPosition();
            hiddenForFullscreen = false;
            Hide();
            formVisible = false;
        }

        private void FullscreenTimerTick(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (formVisible)
            {
                EnsureWindowVisible();
            }

            if (!settings.AutoHideFullScreen)
            {
                if (hiddenForFullscreen)
                {
                    hiddenForFullscreen = false;
                    ShowOverlay();
                }
                return;
            }

            bool fullscreen = FullscreenDetector.IsForegroundFullscreen(Handle);
            if (fullscreen && Visible && !hiddenForFullscreen)
            {
                hiddenForFullscreen = true;
                SaveWindowPosition();
                Hide();
                formVisible = false;
            }
            else if (!fullscreen && hiddenForFullscreen)
            {
                hiddenForFullscreen = false;
                ShowOverlay();
            }
        }

        private void ContentMouseWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0)
            {
                return;
            }

            double next = settings.FontScale + (e.Delta > 0 ? 0.1 : -0.1);
            next = Math.Round(Math.Min(Math.Max(next, 0.75), 1.8), 1);
            if (Math.Abs(next - settings.FontScale) < 0.001)
            {
                return;
            }
            settings.FontScale = next;
            ApplyFonts();
            SettingsStore.Save(settings);
            SetStatus("✓  字号 " + Math.Round(settings.FontScale * 100) + "%", accent);
        }

        private void ToggleLocalOnly()
        {
            settings.LocalOnly = !settings.LocalOnly;
            ApplyCurrentSettings(true);
        }

        private void ToggleClickThrough()
        {
            settings.ClickThrough = !settings.ClickThrough;
            ApplyCurrentSettings(true);
        }

        private void ToggleLockPosition()
        {
            settings.LockPosition = !settings.LockPosition;
            ApplyCurrentSettings(true);
        }

        private void OpenSettings()
        {
            using (SettingsForm dialog = new SettingsForm(settings.Clone()))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    settings = dialog.Settings;
                    ApplyCurrentSettings(true);
                }
            }
        }

        private void ApplyCurrentSettings(bool persist)
        {
            settings.Normalize();
            if (settings.LockPosition)
            {
                dragging = false;
            }
            if (settings.LocalOnly && translationRunning)
            {
                translationSequence++;
                translationRunning = false;
                if (translationCancellation != null)
                {
                    translationCancellation.Cancel();
                }
                SetTranslation("已切换到离线模式，未完成的联网请求已取消",
                    Color.FromArgb(251, 191, 36));
            }
            ClientSize = new Size(settings.WindowWidth, settings.WindowHeight);
            Rectangle area = Screen.FromPoint(Location).WorkingArea;
            Location = WindowPlacement.Clamp(Location, Size, area);
            Opacity = settings.Opacity;
            if (IsHandleCreated)
            {
                ClickThroughWindowStyle.Apply(Handle, settings.ClickThrough);
            }
            ApplyFonts();
            ApplyTranslationSettings();
            keyboardHook.ConfigureHotkeys(
                settings.SettingsHotkey,
                settings.TranslateHotkey,
                settings.HistoryHotkey);
            UpdateShortcutLabel();
            UpdateFavoriteButton();
            UpdateSettingMenuChecks();
            if (persist)
            {
                SettingsStore.Save(settings);
            }
            if (!settings.AutoHideFullScreen && hiddenForFullscreen)
            {
                hiddenForFullscreen = false;
                ShowOverlay();
            }
            KeepWindowOnTop();
            SetStatus(
                monitoring ? "●  监听中" + TranslationModeSuffix() : "Ⅱ  监听已暂停",
                monitoring ? accent : Color.FromArgb(251, 191, 36));
        }

        private void ApplyFonts()
        {
            Font nextContent = CreateContentFont(settings.FontScale);
            Font nextTranslation = CreateTranslationFont(settings.FontScale);
            Font oldContent = contentFont;
            Font oldTranslation = translationFont;
            contentFont = nextContent;
            translationFont = nextTranslation;
            originalText.Font = contentFont;
            translationText.Font = translationFont;
            if (oldContent != null)
            {
                oldContent.Dispose();
            }
            if (oldTranslation != null)
            {
                oldTranslation.Dispose();
            }
        }

        private void ApplyTranslationSettings()
        {
            TranslationService.Configure(
                settings.LocalOnly,
                settings.TimeoutSeconds,
                (TranslationDirection)settings.LanguageDirection);
            LoadGlossaryFile();
        }

        private void UpdateSettingMenuChecks()
        {
            localOnlyMenuItem.Checked = settings.LocalOnly;
            clickThroughMenuItem.Checked = settings.ClickThrough;
            lockPositionMenuItem.Checked = settings.LockPosition;
        }

        private string TranslationModeSuffix()
        {
            return settings.LocalOnly ? " · 离线" : " · 在线";
        }

        private void ExitApplication()
        {
            notifyIcon.Visible = false;
            Close();
            Application.Exit();
        }

        private void OverlayFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideOverlay();
                return;
            }

            SaveWindowPosition();
            try
            {
                Automation.RemoveAutomationFocusChangedEventHandler(focusChangedHandler);
            }
            catch
            {
            }
            keyboardHook.Dispose();
            debounceTimer.Dispose();
            fullscreenTimer.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            if (translationCancellation != null)
            {
                translationCancellation.Cancel();
                translationCancellation.Dispose();
            }
            titleFont.Dispose();
            captionFont.Dispose();
            contentFont.Dispose();
            translationFont.Dispose();
        }

        private void AutomationFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            if (!monitoring || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(CaptureBaseline));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void CaptureBaseline()
        {
            List<string> excluded = new List<string>(settings.ExcludedProcesses);
            Task.Run(() =>
            {
                AutomationSnapshot snapshot = FocusedTextReader.TryRead(excluded);
                if (snapshot == null || IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (lastSnapshot == null || lastSnapshot.Key != snapshot.Key)
                        {
                            lastSnapshot = snapshot;
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void DragMouseDown(object sender, MouseEventArgs e)
        {
            if (!WindowDragPolicy.CanStartDrag(e.Button, settings.LockPosition))
            {
                return;
            }
            dragging = true;
            dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
        }

        private void DragMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging || settings.LockPosition)
            {
                return;
            }
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = WindowDragPolicy.NextPosition(
                Cursor.Position,
                dragOffset,
                Size,
                area);
        }

        private void DragMouseUp(object sender, MouseEventArgs e)
        {
            dragging = false;
            SaveWindowPosition();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint flags);

        private static void LoadDictionaryFile()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dictionary.tsv");
                if (File.Exists(path))
                {
                    TranslationService.LoadDictionary(path);
                }
            }
            catch
            {
            }
        }

        private static void LoadGlossaryFile()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "glossary.tsv");
                TranslationService.LoadGlossary(path);
            }
            catch
            {
            }
        }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly List<TranslationRecord> history;
        private readonly DataGridView grid;
        private readonly CheckBox favoriteFilter;
        private readonly TextBox searchInput;
        private readonly Label statusLabel;
        private readonly bool recordingEnabled;

        public List<TranslationRecord> Records
        {
            get { return history; }
        }

        public HistoryForm(IEnumerable<TranslationRecord> records, bool isRecordingEnabled)
        {
            history = new List<TranslationRecord>();
            if (records != null)
            {
                foreach (TranslationRecord record in records)
                {
                    if (record != null)
                    {
                        history.Add(record.Clone());
                    }
                }
            }
            recordingEnabled = isRecordingEnabled;

            Text = "翻译历史与收藏";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(800, 570);
            MinimumSize = new Size(640, 450);
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(14);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Fill;
            filterRow.AutoSize = true;
            filterRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            filterRow.MinimumSize = new Size(0, 38);
            filterRow.ColumnCount = 4;
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));
            root.Controls.Add(filterRow, 0, 0);

            filterRow.Controls.Add(MakeFormLabel("搜索"), 0, 0);
            searchInput = new TextBox();
            searchInput.Dock = DockStyle.Fill;
            searchInput.Margin = new Padding(4, 5, 12, 5);
            searchInput.TextChanged += delegate { RefreshGrid(); };
            filterRow.Controls.Add(searchInput, 1, 0);

            favoriteFilter = new CheckBox();
            favoriteFilter.Text = "只看收藏";
            favoriteFilter.AutoSize = true;
            favoriteFilter.Anchor = AnchorStyles.Left;
            favoriteFilter.CheckedChanged += delegate { RefreshGrid(); };
            filterRow.Controls.Add(favoriteFilter, 2, 0);

            Label recordingLabel = new Label();
            recordingLabel.Text = recordingEnabled ? "● 正在记录" : "○ 记录已关闭";
            recordingLabel.ForeColor = recordingEnabled
                ? Color.FromArgb(5, 150, 105)
                : Color.FromArgb(180, 83, 9);
            recordingLabel.TextAlign = ContentAlignment.MiddleRight;
            recordingLabel.Dock = DockStyle.Fill;
            filterRow.Controls.Add(recordingLabel, 3, 0);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoGenerateColumns = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.MultiSelect = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.Padding = new Padding(5, 4, 5, 4);
            grid.ColumnHeadersDefaultCellStyle.Font =
                new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(226, 232, 240);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0)
                {
                    CopySelected();
                }
            };
            grid.Columns.Add(MakeTextColumn("收藏", "Favorite", 6f, 42));
            grid.Columns.Add(MakeTextColumn("时间", "Created", 12f, 96));
            grid.Columns.Add(MakeTextColumn("原文", "Original", 38f, 120));
            grid.Columns.Add(MakeTextColumn("译文", "Translation", 44f, 140));
            grid.Columns.Add(MakeTextColumn("来源", "Provider", 10f, 58));
            root.Controls.Add(grid, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = true;
            actions.AutoSize = true;
            actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actions.MinimumSize = new Size(0, 38);
            root.Controls.Add(actions, 0, 2);

            Button copyButton = MakeFormButton("复制译文");
            copyButton.Click += delegate { CopySelected(); };
            actions.Controls.Add(copyButton);

            Button favoriteButton = MakeFormButton("收藏 / 取消");
            favoriteButton.Click += delegate { ToggleSelectedFavorite(); };
            actions.Controls.Add(favoriteButton);

            Button deleteButton = MakeFormButton("删除");
            deleteButton.Click += delegate { DeleteSelected(); };
            actions.Controls.Add(deleteButton);

            Button exportButton = MakeFormButton("导出 CSV");
            exportButton.Click += delegate { ExportVisible(); };
            actions.Controls.Add(exportButton);

            Button clearButton = MakeFormButton("清空非收藏");
            clearButton.Click += delegate { ClearNonFavorites(); };
            actions.Controls.Add(clearButton);

            Button closeButton = MakeFormButton("关闭");
            closeButton.Click += delegate { Close(); };
            actions.Controls.Add(closeButton);

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoSize = true;
            statusLabel.MinimumSize = new Size(0, 26);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
            root.Controls.Add(statusLabel, 0, 3);

            CancelButton = closeButton;
            RefreshGrid();
        }

        private static DataGridViewTextBoxColumn MakeTextColumn(
            string header,
            string name,
            float fillWeight,
            int minimumWidth)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = header;
            column.Name = name;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.FillWeight = fillWeight;
            column.MinimumWidth = minimumWidth;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            return column;
        }

        private List<TranslationRecord> GetVisibleRecords()
        {
            string query = (searchInput == null ? string.Empty : searchInput.Text).Trim();
            List<TranslationRecord> visible = new List<TranslationRecord>();
            foreach (TranslationRecord record in history)
            {
                if (record == null)
                {
                    continue;
                }
                if (favoriteFilter != null && favoriteFilter.Checked && !record.IsFavorite)
                {
                    continue;
                }
                if (query.Length > 0 &&
                    (record.Original ?? string.Empty).IndexOf(
                        query,
                        StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    (record.Translation ?? string.Empty).IndexOf(
                        query,
                        StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    continue;
                }
                visible.Add(record);
            }
            return visible;
        }

        private void RefreshGrid()
        {
            if (grid == null)
            {
                return;
            }

            List<TranslationRecord> visible = GetVisibleRecords();
            grid.Rows.Clear();
            foreach (TranslationRecord record in visible)
            {
                int index = grid.Rows.Add(
                    record.IsFavorite ? "★" : "☆",
                    record.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    record.Original,
                    record.Translation,
                    record.Provider);
                grid.Rows[index].Tag = record;
                if (record.IsFavorite)
                {
                    grid.Rows[index].DefaultCellStyle.BackColor =
                        Color.FromArgb(254, 249, 195);
                }
            }

            int favorites = 0;
            foreach (TranslationRecord record in history)
            {
                if (record != null && record.IsFavorite)
                {
                    favorites++;
                }
            }
            statusLabel.Text =
                "显示 " + visible.Count + " 条 · 共 " + history.Count +
                " 条 · 收藏 " + favorites + " 条 · 双击可复制译文";
        }

        private TranslationRecord GetSelectedRecord()
        {
            if (grid.SelectedRows.Count == 0)
            {
                return null;
            }
            return grid.SelectedRows[0].Tag as TranslationRecord;
        }

        private void CopySelected()
        {
            TranslationRecord record = GetSelectedRecord();
            if (record == null)
            {
                SetStatus("请先选择一条记录。", false);
                return;
            }
            try
            {
                Clipboard.SetText(record.Translation ?? string.Empty);
                SetStatus("译文已复制到剪贴板。", true);
            }
            catch (ExternalException)
            {
                SetStatus("剪贴板正被其他程序占用，请稍后重试。", false);
            }
        }

        private void ToggleSelectedFavorite()
        {
            TranslationRecord record = GetSelectedRecord();
            if (record == null)
            {
                SetStatus("请先选择一条记录。", false);
                return;
            }
            if (!recordingEnabled && !record.IsFavorite)
            {
                SetStatus("历史记录已关闭；请先在设置中开启后再收藏。", false);
                return;
            }

            record.IsFavorite = !record.IsFavorite;
            TranslationHistoryStore.Save(history);
            SetStatus(record.IsFavorite ? "已加入收藏。" : "已取消收藏。", true);
            RefreshGrid();
            SelectRecord(record);
        }

        private void DeleteSelected()
        {
            TranslationRecord record = GetSelectedRecord();
            if (record == null)
            {
                SetStatus("请先选择一条记录。", false);
                return;
            }
            if (MessageBox.Show(
                this,
                "确定删除选中的这条历史记录吗？",
                "删除历史记录",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            history.Remove(record);
            TranslationHistoryStore.Save(history);
            SetStatus("已删除 1 条记录。", true);
            RefreshGrid();
        }

        private void ClearNonFavorites()
        {
            if (MessageBox.Show(
                this,
                "将清空所有未收藏的历史记录，收藏不会删除。是否继续？",
                "清空非收藏历史",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            int removed = TranslationHistoryStore.ClearHistoryKeepFavorites(history);
            TranslationHistoryStore.Save(history);
            SetStatus("已清空 " + removed + " 条非收藏历史。", true);
            RefreshGrid();
        }

        private void ExportVisible()
        {
            List<TranslationRecord> visible = GetVisibleRecords();
            if (visible.Count == 0)
            {
                SetStatus("当前列表没有可导出的记录。", false);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出翻译历史";
                dialog.Filter = "CSV 文件 (*.csv)|*.csv";
                dialog.FileName = "BilingualOverlay-history-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                dialog.AddExtension = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    File.WriteAllText(
                        dialog.FileName,
                        TranslationHistoryStore.BuildCsv(visible),
                        new UTF8Encoding(true));
                    SetStatus("已导出 " + visible.Count + " 条记录。", true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "导出失败：\r\n" + ex.Message,
                        "导出失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void SelectRecord(TranslationRecord record)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (object.ReferenceEquals(row.Tag, record))
                {
                    row.Selected = true;
                    grid.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }

        private void SetStatus(string text, bool success)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = success
                ? Color.FromArgb(5, 150, 105)
                : Color.FromArgb(180, 83, 9);
        }

        private static Label MakeFormLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            return label;
        }

        private static Button MakeFormButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(96, 30);
            button.Margin = new Padding(5, 3, 0, 3);
            return button;
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox localOnlyCheck;
        private readonly CheckBox autoHideCheck;
        private readonly CheckBox clickThroughCheck;
        private readonly CheckBox lockPositionCheck;
        private readonly CheckBox startWithWindowsCheck;
        private readonly CheckBox historyEnabledCheck;
        private readonly CheckBox clearHistoryOnStartupCheck;
        private readonly NumericUpDown timeoutInput;
        private readonly NumericUpDown fontScaleInput;
        private readonly NumericUpDown opacityInput;
        private readonly NumericUpDown widthInput;
        private readonly NumericUpDown heightInput;
        private readonly ComboBox directionInput;
        private readonly TextBox settingsHotkeyInput;
        private readonly TextBox translateHotkeyInput;
        private readonly TextBox historyHotkeyInput;
        private readonly TextBox excludedInput;
        private readonly TextBox updateManifestInput;
        private readonly Label serviceStatusLabel;
        private readonly Label updateStatusLabel;
        private readonly Label feedbackLabel;

        public OverlaySettings Settings { get; private set; }

        public SettingsForm(OverlaySettings initial)
        {
            Settings = (initial ?? OverlaySettings.CreateDefault()).Clone();
            Settings.Normalize();

            Text = "中英对照悬浮窗 · 设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(780, 520);
            MinimumSize = new Size(620, 430);
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(14);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            root.Controls.Add(header, 0, 0);

            Label titleLabel = new Label();
            titleLabel.Text = "中英对照悬浮窗";
            titleLabel.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(15, 23, 42);
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(2, 4);
            header.Controls.Add(titleLabel);

            Label versionLabel = new Label();
            versionLabel.Text = "版本 " + Program.AppVersion +
                " · 所有设置保存在本机，不会上传。";
            versionLabel.ForeColor = Color.FromArgb(71, 85, 105);
            versionLabel.AutoSize = true;
            versionLabel.Location = new Point(4, 34);
            header.Controls.Add(versionLabel);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(16, 7);
            root.Controls.Add(tabs, 0, 1);

            TabPage displayPage = new TabPage("显示与窗口");
            displayPage.BackColor = Color.FromArgb(248, 250, 252);
            displayPage.Padding = new Padding(16);
            displayPage.AutoScroll = true;
            tabs.TabPages.Add(displayPage);
            TableLayoutPanel display = MakePageLayout(2, 7);
            display.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170f));
            display.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            displayPage.Controls.Add(display);

            display.Controls.Add(MakeLabel("字号比例（%）"), 0, 0);
            fontScaleInput = MakeNumber(75, 180, (int)Math.Round(Settings.FontScale * 100));
            display.Controls.Add(WrapControl(fontScaleInput, "调整原文和译文字体"), 1, 0);
            display.Controls.Add(MakeLabel("窗口尺寸（像素）"), 0, 1);
            widthInput = MakeNumber(500, 1000, Settings.WindowWidth);
            heightInput = MakeNumber(240, 700, Settings.WindowHeight);
            FlowLayoutPanel sizeRow = MakeInlineRow();
            sizeRow.Controls.Add(widthInput);
            sizeRow.Controls.Add(MakeInlineLabel("宽 × 高"));
            sizeRow.Controls.Add(heightInput);
            display.Controls.Add(sizeRow, 1, 1);
            display.Controls.Add(MakeLabel("透明度（%）"), 0, 2);
            opacityInput = MakeNumber(35, 100, (int)Math.Round(Settings.Opacity * 100));
            display.Controls.Add(opacityInput, 1, 2);
            display.Controls.Add(MakeLabel("窗口行为"), 0, 3);
            autoHideCheck = MakeCheckBox("全屏应用时自动隐藏", Settings.AutoHideFullScreen);
            display.Controls.Add(autoHideCheck, 1, 3);
            display.Controls.Add(MakeLabel("鼠标穿透"), 0, 4);
            clickThroughCheck = MakeCheckBox("点击穿透（按 F9 或托盘可恢复）", Settings.ClickThrough);
            display.Controls.Add(clickThroughCheck, 1, 4);
            display.Controls.Add(MakeLabel("位置"), 0, 5);
            lockPositionCheck = MakeCheckBox("锁定悬浮窗位置", Settings.LockPosition);
            display.Controls.Add(lockPositionCheck, 1, 5);
            Label displayHint = MakeHint(
                "在 150% / 200% 缩放下会按显示器 DPI 调整尺寸。副屏拔插后，可见窗口会自动移回当前屏幕。");
            display.Controls.Add(displayHint, 0, 6);
            display.SetColumnSpan(displayHint, 2);

            TabPage translationPage = new TabPage("翻译服务");
            translationPage.BackColor = Color.FromArgb(248, 250, 252);
            translationPage.Padding = new Padding(16);
            translationPage.AutoScroll = true;
            tabs.TabPages.Add(translationPage);
            TableLayoutPanel translation = MakePageLayout(2, 9);
            translation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170f));
            translation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            translationPage.Controls.Add(translation);

            translation.Controls.Add(MakeLabel("翻译模式"), 0, 0);
            localOnlyCheck = MakeCheckBox("仅本地词库（完全禁止联网）", Settings.LocalOnly);
            translation.Controls.Add(localOnlyCheck, 1, 0);
            translation.Controls.Add(MakeLabel("翻译方向"), 0, 1);
            directionInput = new ComboBox();
            directionInput.DropDownStyle = ComboBoxStyle.DropDownList;
            directionInput.Width = 220;
            directionInput.Items.Add("自动识别");
            directionInput.Items.Add("中文 → 英文");
            directionInput.Items.Add("英文 → 中文");
            translation.Controls.Add(directionInput, 1, 1);
            translation.Controls.Add(MakeLabel("联网超时（秒）"), 0, 2);
            timeoutInput = MakeNumber(3, 30, Settings.TimeoutSeconds);
            translation.Controls.Add(timeoutInput, 1, 2);
            Label translationHint = MakeHint(
                "本地词库优先；未命中时使用 Google，失败后自动尝试 MyMemory。离线模式不会发起任何翻译网络请求。");
            translation.Controls.Add(translationHint, 0, 3);
            translation.SetColumnSpan(translationHint, 2);

            GroupBox serviceGroup = new GroupBox();
            serviceGroup.Text = "服务检查";
            serviceGroup.Dock = DockStyle.Fill;
            serviceGroup.Padding = new Padding(10);
            serviceGroup.MinimumSize = new Size(0, 104);
            translation.Controls.Add(serviceGroup, 0, 4);
            translation.SetColumnSpan(serviceGroup, 2);
            TableLayoutPanel serviceLayout = new TableLayoutPanel();
            serviceLayout.Dock = DockStyle.Fill;
            serviceLayout.ColumnCount = 2;
            serviceLayout.RowCount = 2;
            serviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125f));
            serviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            serviceGroup.Controls.Add(serviceLayout);
            Button serviceButton = MakeButton("检查在线服务");
            serviceButton.Click += ServiceCheckButtonClick;
            serviceLayout.Controls.Add(serviceButton, 0, 0);
            serviceStatusLabel = new Label();
            serviceStatusLabel.Text = "尚未检查";
            serviceStatusLabel.AutoEllipsis = true;
            serviceStatusLabel.Dock = DockStyle.Fill;
            serviceStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            serviceStatusLabel.ForeColor = Color.FromArgb(71, 85, 105);
            serviceLayout.Controls.Add(serviceStatusLabel, 1, 0);
            Label serviceHint = new Label();
            serviceHint.Text = "检查只发送固定测试句，不读取输入框、历史或剪贴板。";
            serviceHint.ForeColor = Color.FromArgb(71, 85, 105);
            serviceHint.AutoSize = true;
            serviceHint.Dock = DockStyle.Fill;
            serviceLayout.Controls.Add(serviceHint, 0, 1);
            serviceLayout.SetColumnSpan(serviceHint, 2);

            translation.Controls.Add(MakeLabel("更新清单 HTTPS 地址"), 0, 5);
            updateManifestInput = new TextBox();
            updateManifestInput.Dock = DockStyle.Fill;
            updateManifestInput.Text = Settings.UpdateManifestUrl ?? string.Empty;
            translation.Controls.Add(updateManifestInput, 1, 5);

            translation.Controls.Add(MakeLabel("版本更新"), 0, 6);
            FlowLayoutPanel updateRow = MakeInlineRow();
            Button updateButton = MakeButton("检查更新");
            updateButton.Click += UpdateCheckButtonClick;
            updateRow.Controls.Add(updateButton);
            updateStatusLabel = new Label();
            updateStatusLabel.Text = "当前版本 " + Program.AppVersion;
            updateStatusLabel.AutoSize = true;
            updateStatusLabel.ForeColor = Color.FromArgb(71, 85, 105);
            updateRow.Controls.Add(updateStatusLabel);
            translation.Controls.Add(updateRow, 1, 6);

            Label updateHint = MakeHint(
                "更新下载必须提供 SHA-256 校验值；校验一致后才允许启动安装程序。安装和升级都会保留设置、历史与收藏。");
            translation.Controls.Add(updateHint, 0, 7);
            translation.SetColumnSpan(updateHint, 2);
            translation.Controls.Add(MakeHint(""), 0, 8);
            translation.SetColumnSpan(translation.GetControlFromPosition(0, 8), 2);

            TabPage shortcutPage = new TabPage("快捷键与启动");
            shortcutPage.BackColor = Color.FromArgb(248, 250, 252);
            shortcutPage.Padding = new Padding(16);
            shortcutPage.AutoScroll = true;
            tabs.TabPages.Add(shortcutPage);
            TableLayoutPanel shortcuts = MakePageLayout(2, 7);
            shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
            shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            shortcutPage.Controls.Add(shortcuts);

            shortcuts.Controls.Add(MakeLabel("打开设置"), 0, 0);
            settingsHotkeyInput = MakeShortcutInput(Settings.SettingsHotkey);
            shortcuts.Controls.Add(WrapControl(settingsHotkeyInput, "可输入 F1-F24"), 1, 0);
            shortcuts.Controls.Add(MakeLabel("翻译选中 / 剪贴板"), 0, 1);
            translateHotkeyInput = MakeShortcutInput(Settings.TranslateHotkey);
            shortcuts.Controls.Add(translateHotkeyInput, 1, 1);
            shortcuts.Controls.Add(MakeLabel("打开历史"), 0, 2);
            historyHotkeyInput = MakeShortcutInput(Settings.HistoryHotkey);
            shortcuts.Controls.Add(historyHotkeyInput, 1, 2);
            shortcuts.Controls.Add(MakeLabel("固定快捷键"), 0, 3);
            shortcuts.Controls.Add(MakeHint("F8：暂停 / 继续监听　　F9：显示并复位悬浮窗"), 1, 3);
            shortcuts.Controls.Add(MakeLabel("Windows 启动"), 0, 4);
            startWithWindowsCheck = MakeCheckBox(
                "登录 Windows 后自动启动（当前用户）",
                Settings.StartWithWindows);
            shortcuts.Controls.Add(startWithWindowsCheck, 1, 4);
            Label shortcutHint = MakeHint(
                "快捷键格式：Ctrl+Alt+T、Ctrl+Shift+H 或 Alt+F10。必须包含 Ctrl、Alt、Shift 中至少一个。");
            shortcuts.Controls.Add(shortcutHint, 0, 5);
            shortcuts.SetColumnSpan(shortcutHint, 2);
            shortcuts.Controls.Add(MakeHint(""), 0, 6);
            shortcuts.SetColumnSpan(shortcuts.GetControlFromPosition(0, 6), 2);

            TabPage privacyPage = new TabPage("历史与隐私");
            privacyPage.BackColor = Color.FromArgb(248, 250, 252);
            privacyPage.Padding = new Padding(16);
            privacyPage.AutoScroll = true;
            tabs.TabPages.Add(privacyPage);
            TableLayoutPanel privacy = MakePageLayout(1, 7);
            privacyPage.Controls.Add(privacy);

            historyEnabledCheck = MakeCheckBox(
                "保存最近翻译与收藏（最多 500 条）",
                Settings.HistoryEnabled == true);
            privacy.Controls.Add(historyEnabledCheck, 0, 0);
            clearHistoryOnStartupCheck = MakeCheckBox(
                "每次启动时清空未收藏记录，收藏会保留",
                Settings.ClearHistoryOnStartup);
            privacy.Controls.Add(clearHistoryOnStartupCheck, 0, 1);
            Label excludedLabel = new Label();
            excludedLabel.Text = "程序排除列表（每行一个程序名，命中后不读取、不翻译、不联网）：";
            excludedLabel.AutoSize = true;
            excludedLabel.Dock = DockStyle.Fill;
            privacy.Controls.Add(excludedLabel, 0, 2);
            excludedInput = new TextBox();
            excludedInput.Multiline = true;
            excludedInput.ScrollBars = ScrollBars.Vertical;
            excludedInput.Dock = DockStyle.Fill;
            excludedInput.Text = string.Join(Environment.NewLine, Settings.ExcludedProcesses.ToArray());
            excludedInput.MinimumSize = new Size(0, 110);
            privacy.Controls.Add(excludedInput, 0, 3);

            FlowLayoutPanel privacyActions = MakeInlineRow();
            privacy.Controls.Add(privacyActions, 0, 4);
            Button openDataButton = MakeButton("打开数据目录");
            openDataButton.Click += delegate
            {
                try
                {
                    string directory = Path.GetDirectoryName(SettingsStore.DefaultPath);
                    Directory.CreateDirectory(directory);
                    Process.Start("explorer.exe", directory);
                }
                catch (Exception ex)
                {
                    ShowFeedback("无法打开数据目录：" + ex.Message, true);
                }
            };
            privacyActions.Controls.Add(openDataButton);
            Button openProgramButton = MakeButton("打开程序目录");
            openProgramButton.Click += delegate
            {
                try
                {
                    Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory);
                }
                catch (Exception ex)
                {
                    ShowFeedback("无法打开程序目录：" + ex.Message, true);
                }
            };
            privacyActions.Controls.Add(openProgramButton);
            Button clearCacheButton = MakeButton("清空翻译缓存");
            clearCacheButton.Click += delegate
            {
                TranslationService.ClearCache();
                ShowFeedback("翻译缓存已清空。", false);
            };
            privacyActions.Controls.Add(clearCacheButton);
            Label sensitiveHint = MakeHint(
                "密码框、密码管理器和标题含“密码 / Password / 无痕 / InPrivate”的窗口会自动跳过。");
            privacy.Controls.Add(sensitiveHint, 0, 5);
            privacy.Controls.Add(MakeHint(
                "历史与收藏保存到用户数据目录，也可在“历史”窗口导出 CSV。"), 0, 6);

            FlowLayoutPanel buttonRow = new FlowLayoutPanel();
            buttonRow.Dock = DockStyle.Fill;
            buttonRow.FlowDirection = FlowDirection.RightToLeft;
            buttonRow.WrapContents = true;
            buttonRow.AutoSize = true;
            buttonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonRow.MinimumSize = new Size(0, 48);
            root.Controls.Add(buttonRow, 0, 2);

            Button cancelButton = MakeButton("取消");
            cancelButton.DialogResult = DialogResult.Cancel;
            buttonRow.Controls.Add(cancelButton);

            Button saveButton = MakeButton("保存并应用");
            saveButton.Click += SaveButtonClick;
            buttonRow.Controls.Add(saveButton);

            Button defaultsButton = MakeButton("恢复默认");
            defaultsButton.Click += delegate { LoadControls(OverlaySettings.CreateDefault()); };
            buttonRow.Controls.Add(defaultsButton);

            feedbackLabel = new Label();
            feedbackLabel.AutoSize = true;
            feedbackLabel.MinimumSize = new Size(240, 48);
            feedbackLabel.MaximumSize = new Size(390, 0);
            feedbackLabel.Text = "设置保存在本机，不会上传。";
            feedbackLabel.ForeColor = Color.FromArgb(71, 85, 105);
            feedbackLabel.TextAlign = ContentAlignment.MiddleLeft;
            buttonRow.Controls.Add(feedbackLabel);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            LoadControls(Settings);
        }

        private void SaveButtonClick(object sender, EventArgs e)
        {
            if (!ShortcutGesture.IsValid(settingsHotkeyInput.Text, true))
            {
                ShowFeedback("打开设置的快捷键格式无效。示例：Ctrl+Alt+F10。", true);
                settingsHotkeyInput.Focus();
                return;
            }
            if (!ShortcutGesture.IsValid(translateHotkeyInput.Text, false))
            {
                ShowFeedback("翻译快捷键格式无效。示例：Ctrl+Alt+T。", true);
                translateHotkeyInput.Focus();
                return;
            }
            if (!ShortcutGesture.IsValid(historyHotkeyInput.Text, false))
            {
                ShowFeedback("历史快捷键格式无效。示例：Ctrl+Alt+H。", true);
                historyHotkeyInput.Focus();
                return;
            }

            ShortcutGesture settingsGesture =
                ShortcutGesture.Parse(settingsHotkeyInput.Text, true);
            ShortcutGesture translateGesture =
                ShortcutGesture.Parse(translateHotkeyInput.Text, false);
            ShortcutGesture historyGesture =
                ShortcutGesture.Parse(historyHotkeyInput.Text, false);
            if (ShortcutGesture.HasDuplicates(
                settingsHotkeyInput.Text,
                translateHotkeyInput.Text,
                historyHotkeyInput.Text,
                true,
                false))
            {
                ShowFeedback("三个快捷键不能重复，请修改后再保存。", true);
                return;
            }

            OverlaySettings next = Settings.Clone();
            next.LocalOnly = localOnlyCheck.Checked;
            next.TimeoutSeconds = Decimal.ToInt32(timeoutInput.Value);
            next.FontScale = Decimal.ToDouble(fontScaleInput.Value) / 100.0;
            next.Opacity = Decimal.ToDouble(opacityInput.Value) / 100.0;
            next.WindowWidth = Decimal.ToInt32(widthInput.Value);
            next.WindowHeight = Decimal.ToInt32(heightInput.Value);
            next.AutoHideFullScreen = autoHideCheck.Checked;
            next.ClickThrough = clickThroughCheck.Checked;
            next.LockPosition = lockPositionCheck.Checked;
            next.LanguageDirection = Math.Max(0, directionInput.SelectedIndex);
            next.HistoryEnabled = historyEnabledCheck.Checked;
            next.ClearHistoryOnStartup = clearHistoryOnStartupCheck.Checked;
            next.SettingsHotkey = settingsGesture.DisplayText;
            next.TranslateHotkey = translateGesture.DisplayText;
            next.HistoryHotkey = historyGesture.DisplayText;
            next.StartWithWindows = startWithWindowsCheck.Checked;
            next.UpdateManifestUrl = updateManifestInput.Text.Trim();
            next.ExcludedProcesses = new List<string>();
            foreach (string line in excludedInput.Lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    next.ExcludedProcesses.Add(line);
                }
            }
            next.Normalize();

            bool previousStartup = Settings.StartWithWindows;
            string startupError;
            if (!StartupManager.SetEnabled(
                next.StartWithWindows,
                Application.ExecutablePath,
                out startupError))
            {
                ShowFeedback("无法修改开机启动项：" + startupError, true);
                return;
            }
            if (!SettingsStore.Save(next))
            {
                string rollbackError;
                StartupManager.SetEnabled(
                    previousStartup,
                    Application.ExecutablePath,
                    out rollbackError);
                ShowFeedback("保存失败：用户目录和程序目录均不可写，请检查文件夹权限。", true);
                return;
            }
            Settings = next;
            ShowFeedback("已保存并应用：" + SettingsStore.LastSaveSummary, false);
            System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = 1300;
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                DialogResult = DialogResult.OK;
                Close();
            };
            closeTimer.Start();
        }

        private void LoadControls(OverlaySettings source)
        {
            source.Normalize();
            localOnlyCheck.Checked = source.LocalOnly;
            autoHideCheck.Checked = source.AutoHideFullScreen;
            clickThroughCheck.Checked = source.ClickThrough;
            lockPositionCheck.Checked = source.LockPosition;
            timeoutInput.Value = source.TimeoutSeconds;
            fontScaleInput.Value = (int)Math.Round(source.FontScale * 100);
            opacityInput.Value = (int)Math.Round(source.Opacity * 100);
            widthInput.Value = source.WindowWidth;
            heightInput.Value = source.WindowHeight;
            directionInput.SelectedIndex = Math.Min(Math.Max(source.LanguageDirection, 0), 2);
            historyEnabledCheck.Checked = source.HistoryEnabled == true;
            clearHistoryOnStartupCheck.Checked = source.ClearHistoryOnStartup;
            settingsHotkeyInput.Text = source.SettingsHotkey;
            translateHotkeyInput.Text = source.TranslateHotkey;
            historyHotkeyInput.Text = source.HistoryHotkey;
            startWithWindowsCheck.Checked = source.StartWithWindows;
            updateManifestInput.Text = source.UpdateManifestUrl ?? string.Empty;
            excludedInput.Text = string.Join(Environment.NewLine, source.ExcludedProcesses.ToArray());
            ShowFeedback("已填入默认值，点击“保存并应用”后生效。", false);
        }

        private async void ServiceCheckButtonClick(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                button.Enabled = false;
            }
            serviceStatusLabel.Text = "正在检查 Google / MyMemory…";
            serviceStatusLabel.ForeColor = Color.FromArgb(71, 85, 105);
            try
            {
                string result = await TranslationService.CheckOnlineServicesAsync(
                    Decimal.ToInt32(timeoutInput.Value));
                serviceStatusLabel.Text = result;
                serviceStatusLabel.ForeColor =
                    result.IndexOf("可用", StringComparison.Ordinal) >= 0
                        ? Color.FromArgb(5, 150, 105)
                        : Color.FromArgb(180, 83, 9);
            }
            catch (Exception ex)
            {
                serviceStatusLabel.Text = "检查失败：" + ex.Message;
                serviceStatusLabel.ForeColor = Color.FromArgb(180, 83, 9);
            }
            finally
            {
                if (button != null)
                {
                    button.Enabled = true;
                }
            }
        }

        private async void UpdateCheckButtonClick(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                button.Enabled = false;
            }
            updateStatusLabel.Text = "正在检查…";
            updateStatusLabel.ForeColor = Color.FromArgb(71, 85, 105);
            try
            {
                UpdateCheckResult result =
                    await UpdateService.CheckAsync(updateManifestInput.Text);
                updateStatusLabel.Text = result.Message;
                if (!result.Success)
                {
                    updateStatusLabel.ForeColor = Color.FromArgb(180, 83, 9);
                    return;
                }
                updateStatusLabel.ForeColor = Color.FromArgb(5, 150, 105);
                if (!result.HasUpdate || result.Manifest == null)
                {
                    return;
                }

                string notes = string.IsNullOrWhiteSpace(result.Manifest.Notes)
                    ? "（无更新说明）"
                    : result.Manifest.Notes;
                if (MessageBox.Show(
                    this,
                    "发现新版本 " + result.Manifest.Version + "\r\n\r\n" +
                    notes + "\r\n\r\n是否下载并校验更新安装包？",
                    "发现更新",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes)
                {
                    return;
                }

                updateStatusLabel.Text = "正在下载并校验 SHA-256…";
                UpdateDownloadResult download =
                    await UpdateService.DownloadAsync(result.Manifest);
                if (!download.Success || string.IsNullOrWhiteSpace(download.FilePath))
                {
                    updateStatusLabel.Text = "更新失败：" + download.Message;
                    updateStatusLabel.ForeColor = Color.FromArgb(180, 83, 9);
                    return;
                }

                updateStatusLabel.Text = "更新包已下载并通过 SHA-256 校验。";
                updateStatusLabel.ForeColor = Color.FromArgb(5, 150, 105);
                if (MessageBox.Show(
                    this,
                    "更新包已校验。是否立即关闭当前程序并运行安装程序？\r\n" +
                    "设置、历史和收藏会保留。",
                    "运行更新",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = download.FilePath,
                        Arguments = "--install",
                        UseShellExecute = true
                    });
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                updateStatusLabel.Text = "检查更新失败：" + ex.Message;
                updateStatusLabel.ForeColor = Color.FromArgb(180, 83, 9);
            }
            finally
            {
                if (button != null)
                {
                    button.Enabled = true;
                }
            }
        }

        private static Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            return label;
        }

        private static CheckBox MakeCheckBox(string text, bool isChecked)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = text;
            checkBox.Checked = isChecked;
            checkBox.AutoSize = true;
            checkBox.Anchor = AnchorStyles.Left;
            return checkBox;
        }

        private static NumericUpDown MakeNumber(int minimum, int maximum, int value)
        {
            NumericUpDown input = new NumericUpDown();
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = Math.Min(Math.Max(value, minimum), maximum);
            input.Width = 92;
            input.Anchor = AnchorStyles.Left;
            return input;
        }

        private static TextBox MakeShortcutInput(string value)
        {
            TextBox input = new TextBox();
            input.Width = 220;
            input.Text = value ?? string.Empty;
            input.Anchor = AnchorStyles.Left;
            return input;
        }

        private static FlowLayoutPanel WrapControl(Control control, string hint)
        {
            FlowLayoutPanel row = MakeInlineRow();
            row.Controls.Add(control);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                row.Controls.Add(MakeInlineLabel(hint));
            }
            return row;
        }

        private static FlowLayoutPanel MakeInlineRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = true;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.Margin = new Padding(0);
            return row;
        }

        private static Label MakeInlineLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = Color.FromArgb(71, 85, 105);
            label.Margin = new Padding(8, 7, 8, 0);
            return label;
        }

        private static Label MakeHint(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Color.FromArgb(71, 85, 105);
            label.AutoSize = true;
            label.MaximumSize = new Size(620, 0);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static TableLayoutPanel MakePageLayout(int columns, int rows)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = columns;
            layout.RowCount = rows;
            layout.Padding = new Padding(4);
            for (int i = 0; i < rows; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            return layout;
        }

        private void ShowFeedback(string text, bool error)
        {
            if (feedbackLabel == null)
            {
                return;
            }
            feedbackLabel.Text = text;
            feedbackLabel.ForeColor = error
                ? Color.FromArgb(190, 24, 93)
                : Color.FromArgb(5, 150, 105);
        }

        private static Button MakeButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(94, 30);
            button.Margin = new Padding(6, 3, 0, 3);
            return button;
        }
    }

    internal static class FullscreenDetector
    {
        private const int MonitorDefaultToNearest = 2;

        public static bool IsForegroundFullscreen(IntPtr overlayHandle)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == overlayHandle || !IsWindowVisible(foreground))
            {
                return false;
            }

            RECT windowRect;
            if (!GetWindowRect(foreground, out windowRect))
            {
                return false;
            }

            IntPtr monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            Rectangle window = Rectangle.FromLTRB(
                windowRect.Left,
                windowRect.Top,
                windowRect.Right,
                windowRect.Bottom);
            Rectangle monitorRectangle = Rectangle.FromLTRB(
                monitorInfo.rcMonitor.Left,
                monitorInfo.rcMonitor.Top,
                monitorInfo.rcMonitor.Right,
                monitorInfo.rcMonitor.Bottom);
            return CoversMonitor(window, monitorRectangle);
        }

        public static bool CoversMonitor(Rectangle window, Rectangle monitor)
        {
            const int tolerance = 3;
            if (window.Width <= 0 || window.Height <= 0 ||
                monitor.Width <= 0 || monitor.Height <= 0)
            {
                return false;
            }

            return Math.Abs(window.Left - monitor.Left) <= tolerance &&
                Math.Abs(window.Top - monitor.Top) <= tolerance &&
                Math.Abs(window.Right - monitor.Right) <= tolerance &&
                Math.Abs(window.Bottom - monitor.Bottom) <= tolerance;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);
    }

    internal sealed class AutomationSnapshot
    {
        public string Key;
        public string Text;
        public bool IsPassword;
        public bool Blocked;
        public string BlockReason;
        public string ProcessName;
        public string WindowTitle;
    }

    internal static class FocusedTextReader
    {
        private const int MaxAncestorDepth = 4;

        public static AutomationSnapshot TryRead()
        {
            return TryRead(null);
        }

        public static AutomationSnapshot TryRead(IEnumerable<string> excludedProcesses)
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null)
                {
                    return null;
                }

                int currentProcessId = Process.GetCurrentProcess().Id;
                if (focused.Current.ProcessId == currentProcessId)
                {
                    return null;
                }

                string processName = GetProcessName(focused.Current.ProcessId);
                string windowTitle = GetForegroundWindowTitle(focused.Current.ProcessId);
                string blockReason = PrivacyGuard.GetBlockReason(
                    processName,
                    windowTitle,
                    excludedProcesses);
                if (blockReason != null)
                {
                    return new AutomationSnapshot
                    {
                        Key = BuildKey(focused),
                        Text = string.Empty,
                        Blocked = true,
                        BlockReason = blockReason,
                        ProcessName = processName,
                        WindowTitle = windowTitle
                    };
                }

                string key = BuildKey(focused);
                AutomationElement candidate = focused;
                for (int depth = 0; candidate != null && depth <= MaxAncestorDepth; depth++)
                {
                    if (candidate.Current.IsPassword)
                    {
                        return new AutomationSnapshot
                        {
                            Key = key,
                            Text = string.Empty,
                            IsPassword = true,
                            ProcessName = processName,
                            WindowTitle = windowTitle
                        };
                    }

                    string text = TryValuePattern(candidate);
                    if (text == null)
                    {
                        text = TryTextPattern(candidate);
                    }
                    if (text != null)
                    {
                        if (text.Length > 12000)
                        {
                            text = CapturedText.Tail(text, 12000);
                        }

                        return new AutomationSnapshot
                        {
                            Key = key,
                            Text = text,
                            IsPassword = false,
                            ProcessName = processName,
                            WindowTitle = windowTitle
                        };
                    }

                    candidate = GetParent(candidate);
                }

                string selection = TryReadSelectionCore(focused);
                if (!string.IsNullOrWhiteSpace(selection))
                {
                    return new AutomationSnapshot
                    {
                        Key = key,
                        Text = selection,
                        IsPassword = false,
                        ProcessName = processName,
                        WindowTitle = windowTitle
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static AutomationSnapshot TryReadSelection(IEnumerable<string> excludedProcesses)
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null)
                {
                    return null;
                }

                int processId = focused.Current.ProcessId;
                if (processId == Process.GetCurrentProcess().Id)
                {
                    return null;
                }

                string processName = GetProcessName(processId);
                string windowTitle = GetForegroundWindowTitle(processId);
                string blockReason = PrivacyGuard.GetBlockReason(
                    processName,
                    windowTitle,
                    excludedProcesses);
                if (blockReason != null)
                {
                    return new AutomationSnapshot
                    {
                        Key = BuildKey(focused),
                        Blocked = true,
                        BlockReason = blockReason,
                        ProcessName = processName,
                        WindowTitle = windowTitle
                    };
                }

                AutomationElement candidate = focused;
                for (int depth = 0; candidate != null && depth <= MaxAncestorDepth; depth++)
                {
                    if (candidate.Current.IsPassword)
                    {
                        return new AutomationSnapshot
                        {
                            Key = BuildKey(focused),
                            IsPassword = true,
                            ProcessName = processName,
                            WindowTitle = windowTitle
                        };
                    }
                    candidate = GetParent(candidate);
                }

                string selection = TryReadSelectionCore(focused);
                if (string.IsNullOrWhiteSpace(selection))
                {
                    return null;
                }
                return new AutomationSnapshot
                {
                    Key = BuildKey(focused),
                    Text = CapturedText.Tail(selection, 12000),
                    ProcessName = processName,
                    WindowTitle = windowTitle
                };
            }
            catch
            {
                return null;
            }
        }

        private static string GetProcessName(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return process.ProcessName + ".exe";
                }
            }
            catch
            {
                return processId.ToString();
            }
        }

        private static string GetForegroundWindowTitle(int focusedProcessId)
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero)
                {
                    return string.Empty;
                }

                uint windowProcessId;
                GetWindowThreadProcessId(window, out windowProcessId);
                if (windowProcessId != focusedProcessId)
                {
                    return string.Empty;
                }

                int length = GetWindowTextLength(window);
                if (length <= 0)
                {
                    return string.Empty;
                }
                StringBuilder title = new StringBuilder(length + 1);
                GetWindowText(window, title, title.Capacity);
                return title.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static AutomationElement GetParent(AutomationElement element)
        {
            try
            {
                return TreeWalker.RawViewWalker.GetParent(element);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildKey(AutomationElement element)
        {
            try
            {
                int[] runtimeId = element.GetRuntimeId();
                if (runtimeId != null && runtimeId.Length > 0)
                {
                    return element.Current.ProcessId + ":" + string.Join(".", runtimeId);
                }
            }
            catch
            {
            }

            try
            {
                return element.Current.ProcessId + ":" + element.Current.ControlType.ProgrammaticName + ":" +
                    element.Current.AutomationId + ":" + element.Current.ClassName;
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        private static string TryValuePattern(AutomationElement element)
        {
            try
            {
                object patternObject;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out patternObject))
                {
                    ValuePattern pattern = (ValuePattern)patternObject;
                    return pattern.Current.Value;
                }
            }
            catch
            {
            }
            return null;
        }

        private static string TryTextPattern(AutomationElement element)
        {
            try
            {
                object patternObject;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out patternObject))
                {
                    TextPattern pattern = (TextPattern)patternObject;
                    return pattern.DocumentRange.GetText(-1);
                }
            }
            catch
            {
            }
            return null;
        }

        private static string TryReadSelectionCore(AutomationElement focused)
        {
            AutomationElement candidate = focused;
            for (int depth = 0; candidate != null && depth <= MaxAncestorDepth; depth++)
            {
                try
                {
                    object patternObject;
                    if (candidate.TryGetCurrentPattern(TextPattern.Pattern, out patternObject))
                    {
                        TextPattern pattern = (TextPattern)patternObject;
                        TextPatternRange[] ranges = pattern.GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            StringBuilder builder = new StringBuilder();
                            foreach (TextPatternRange range in ranges)
                            {
                                string part = range.GetText(-1);
                                if (!string.IsNullOrEmpty(part))
                                {
                                    if (builder.Length > 0)
                                    {
                                        builder.AppendLine();
                                    }
                                    builder.Append(part);
                                }
                            }
                            if (builder.Length > 0)
                            {
                                return builder.ToString();
                            }
                        }
                    }
                }
                catch
                {
                }
                candidate = GetParent(candidate);
            }
            return null;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
    }

    internal static class CapturedText
    {
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();
        }

        public static string Tail(string text, int maxCharacters)
        {
            if (string.IsNullOrEmpty(text) || maxCharacters <= 0 || text.Length <= maxCharacters)
            {
                return text ?? string.Empty;
            }

            int start = text.Length - maxCharacters;
            if (start > 0 && char.IsHighSurrogate(text[start - 1]) && char.IsLowSurrogate(text[start]))
            {
                start++;
            }
            return text.Substring(start);
        }
    }

    internal static class WindowPlacement
    {
        public static Point Clamp(Point position, Size size, Rectangle area)
        {
            int maxX = Math.Max(area.Left, area.Right - size.Width);
            int maxY = Math.Max(area.Top, area.Bottom - size.Height);
            int left = Math.Min(Math.Max(position.X, area.Left), maxX);
            int top = Math.Min(Math.Max(position.Y, area.Top), maxY);
            return new Point(left, top);
        }

        public static Point DefaultRightPosition(Rectangle area, Size size)
        {
            const int margin = 24;
            return Clamp(
                new Point(area.Right - size.Width - margin, area.Top + margin),
                size,
                area);
        }

        public static bool IsFullyVisible(Point position, Size size, Screen[] screens)
        {
            if (screens == null)
            {
                return false;
            }

            Rectangle[] areas = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++)
            {
                areas[i] = screens[i].WorkingArea;
            }
            return IsFullyVisible(position, size, areas);
        }

        public static bool IsFullyVisible(Point position, Size size, Rectangle[] areas)
        {
            if (areas == null)
            {
                return false;
            }

            Rectangle window = new Rectangle(position, size);
            foreach (Rectangle area in areas)
            {
                if (area.Contains(window))
                {
                    return true;
                }
            }
            return false;
        }

        public static Point RecoverVisible(
            Point desired,
            Size size,
            Rectangle[] areas,
            Rectangle fallbackArea)
        {
            if (IsFullyVisible(desired, size, areas))
            {
                return desired;
            }
            Rectangle fallback = fallbackArea.Width > 0 && fallbackArea.Height > 0
                ? fallbackArea
                : (areas != null && areas.Length > 0
                    ? areas[0]
                    : new Rectangle(0, 0, 1920, 1040));
            return DefaultRightPosition(fallback, size);
        }
    }

    internal static class UiLayoutMetrics
    {
        public static int ScaleForDpi(int logicalPixels, int dpi)
        {
            int effectiveDpi = Math.Min(Math.Max(dpi, 96), 480);
            return (int)Math.Round(
                logicalPixels * (effectiveDpi / 96.0),
                MidpointRounding.AwayFromZero);
        }

        public static Size ScaleForDpi(Size logicalSize, int dpi)
        {
            return new Size(
                ScaleForDpi(logicalSize.Width, dpi),
                ScaleForDpi(logicalSize.Height, dpi));
        }

        public static bool FitsWorkingArea(Size logicalSize, Size workingArea, int dpi)
        {
            Size scaled = ScaleForDpi(logicalSize, dpi);
            return scaled.Width <= workingArea.Width &&
                scaled.Height <= workingArea.Height;
        }
    }

    internal static class WindowDragPolicy
    {
        public static bool CanStartDrag(MouseButtons button, bool lockPosition)
        {
            return button == MouseButtons.Left && !lockPosition;
        }

        public static Point NextPosition(
            Point cursorPosition,
            Point dragOffset,
            Size size,
            Rectangle area)
        {
            Point proposed = new Point(
                cursorPosition.X - dragOffset.X,
                cursorPosition.Y - dragOffset.Y);
            return WindowPlacement.Clamp(proposed, size, area);
        }
    }

    internal static class WindowPositionStore
    {
        private static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BilingualOverlay",
                    "window-position.txt");
            }
        }

        public static bool TryLoad(out Point position)
        {
            position = Point.Empty;
            try
            {
                return File.Exists(FilePath) &&
                    TryParse(File.ReadAllText(FilePath, Encoding.UTF8), out position);
            }
            catch
            {
                return false;
            }
        }

        public static void Save(Point position)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, Format(position), Encoding.UTF8);
            }
            catch
            {
            }
        }

        public static string Format(Point position)
        {
            return position.X.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string text, out Point position)
        {
            position = Point.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Trim().Split(',');
            int x;
            int y;
            if (parts.Length != 2 ||
                !int.TryParse(
                    parts[0].Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out x) ||
                !int.TryParse(
                    parts[1].Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out y))
            {
                return false;
            }

            position = new Point(x, y);
            return true;
        }
    }

    internal static class TextDelta
    {
        public static string ExtractInserted(string oldText, string newText)
        {
            if (string.IsNullOrEmpty(newText))
            {
                return null;
            }
            if (oldText == null)
            {
                return newText;
            }
            if (string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                return null;
            }

            int prefix = 0;
            int maxPrefix = Math.Min(oldText.Length, newText.Length);
            while (prefix < maxPrefix && oldText[prefix] == newText[prefix])
            {
                prefix++;
            }

            int suffix = 0;
            int maxSuffix = Math.Min(oldText.Length - prefix, newText.Length - prefix);
            while (suffix < maxSuffix &&
                oldText[oldText.Length - suffix - 1] == newText[newText.Length - suffix - 1])
            {
                suffix++;
            }

            int insertedLength = newText.Length - prefix - suffix;
            if (insertedLength <= 0)
            {
                return null;
            }
            return newText.Substring(prefix, insertedLength);
        }
    }

    internal static class TextInputKeys
    {
        private const int VK_BACK = 0x08;
        private const int VK_RETURN = 0x0D;
        private const int VK_SPACE = 0x20;
        private const int VK_DELETE = 0x2E;
        private const int VK_OEM_1 = 0xBA;
        private const int VK_OEM_102 = 0xE2;
        private const int VK_PROCESSKEY = 0xE5;
        private const int VK_PACKET = 0xE7;

        public static bool MayChangeText(int virtualKey)
        {
            return
                (virtualKey >= 0x30 && virtualKey <= 0x39) ||
                (virtualKey >= 0x41 && virtualKey <= 0x5A) ||
                (virtualKey >= 0x60 && virtualKey <= 0x69) ||
                (virtualKey >= VK_OEM_1 && virtualKey <= 0xC0) ||
                virtualKey == VK_BACK ||
                virtualKey == VK_RETURN ||
                virtualKey == VK_SPACE ||
                virtualKey == VK_DELETE ||
                virtualKey == VK_OEM_102 ||
                virtualKey == VK_PROCESSKEY ||
                virtualKey == VK_PACKET;
        }
    }

    internal sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_F8 = 0x77;
        private const int VK_F9 = 0x78;
        private const int VK_V = 0x56;

        private readonly LowLevelKeyboardProc callback;
        private IntPtr hookId = IntPtr.Zero;
        private bool f8Held;
        private bool f9Held;
        private bool settingsHotkeyHeld;
        private bool translationHotkeyHeld;
        private bool historyHotkeyHeld;
        private ShortcutGesture settingsShortcut;
        private ShortcutGesture translationShortcut;
        private ShortcutGesture historyShortcut;

        public event Action GlobalKeyDown;
        public event Action ToggleMonitoring;
        public event Action TranslateClipboard;
        public event Action ShowHistoryRequested;
        public event Action ShowOverlayRequested;
        public event Action ShowSettingsRequested;

        public GlobalKeyboardHook()
        {
            callback = HookCallback;
            ConfigureHotkeys(
                OverlaySettings.DefaultSettingsHotkey,
                OverlaySettings.DefaultTranslateHotkey,
                OverlaySettings.DefaultHistoryHotkey);
        }

        public void ConfigureHotkeys(
            string settingsGesture,
            string translationGesture,
            string historyGesture)
        {
            settingsShortcut = ShortcutGesture.Parse(settingsGesture, true);
            translationShortcut = ShortcutGesture.Parse(translationGesture, false);
            historyShortcut = ShortcutGesture.Parse(historyGesture, false);
        }

        public void Install()
        {
            if (hookId != IntPtr.Zero)
            {
                return;
            }

            using (Process currentProcess = Process.GetCurrentProcess())
            using (ProcessModule currentModule = currentProcess.MainModule)
            {
                hookId = SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    callback,
                    GetModuleHandle(currentModule.ModuleName),
                    0);
            }
        }

        public void Dispose()
        {
            if (hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookId);
                hookId = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
            {
                return CallNextHookEx(hookId, nCode, wParam, lParam);
            }

            int message = wParam.ToInt32();
            KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            bool keyDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
            bool keyUp = message == WM_KEYUP || message == WM_SYSKEYUP;

            if (data.vkCode == VK_F8 && keyDown && !f8Held)
            {
                f8Held = true;
                Action handler = ToggleMonitoring;
                if (handler != null)
                {
                    handler();
                }
                return new IntPtr(1);
            }
            if (data.vkCode == VK_F8 && keyUp)
            {
                f8Held = false;
                return new IntPtr(1);
            }

            if (data.vkCode == VK_F9 && keyDown && !f9Held)
            {
                f9Held = true;
                Action handler = ShowOverlayRequested;
                if (handler != null)
                {
                    handler();
                }
                return new IntPtr(1);
            }
            if (data.vkCode == VK_F9 && keyUp)
            {
                f9Held = false;
                return new IntPtr(1);
            }

            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            if (settingsShortcut != null &&
                settingsShortcut.Matches(ctrl, alt, shift, (int)data.vkCode) &&
                keyDown &&
                !settingsHotkeyHeld)
            {
                settingsHotkeyHeld = true;
                Action handler = ShowSettingsRequested;
                if (handler != null)
                {
                    handler();
                }
                return new IntPtr(1);
            }
            if (settingsShortcut != null &&
                data.vkCode == settingsShortcut.VirtualKey &&
                keyUp &&
                settingsHotkeyHeld)
            {
                settingsHotkeyHeld = false;
                return new IntPtr(1);
            }

            if (translationShortcut != null &&
                translationShortcut.Matches(ctrl, alt, shift, (int)data.vkCode) &&
                keyDown &&
                !translationHotkeyHeld)
            {
                translationHotkeyHeld = true;
                Action handler = TranslateClipboard;
                if (handler != null)
                {
                    handler();
                }
                return new IntPtr(1);
            }
            if (translationShortcut != null &&
                data.vkCode == translationShortcut.VirtualKey &&
                keyUp &&
                translationHotkeyHeld)
            {
                translationHotkeyHeld = false;
                return new IntPtr(1);
            }

            if (historyShortcut != null &&
                historyShortcut.Matches(ctrl, alt, shift, (int)data.vkCode) &&
                keyDown &&
                !historyHotkeyHeld)
            {
                historyHotkeyHeld = true;
                Action handler = ShowHistoryRequested;
                if (handler != null)
                {
                    handler();
                }
                return new IntPtr(1);
            }
            if (historyShortcut != null &&
                data.vkCode == historyShortcut.VirtualKey &&
                keyUp &&
                historyHotkeyHeld)
            {
                historyHotkeyHeld = false;
                return new IntPtr(1);
            }

            if (keyDown)
            {
                bool mayChangeText = TextInputKeys.MayChangeText((int)data.vkCode);
                if (ctrl || alt)
                {
                    mayChangeText = data.vkCode == VK_V;
                }
                if (mayChangeText)
                {
                    Action handler = GlobalKeyDown;
                    if (handler != null)
                    {
                        handler();
                    }
                }
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }

    internal sealed class TranslationResult
    {
        public string Text;
        public string Provider;
        public bool IsError;

        public static TranslationResult Failed(string message)
        {
            return new TranslationResult
            {
                Text = message,
                Provider = "翻译服务不可用",
                IsError = true
            };
        }
    }

    internal static class TranslationService
    {
        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> LocalDictionary =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "你好", "Hello" },
                { "您好", "Hello" },
                { "早上好", "Good morning" },
                { "晚上好", "Good evening" },
                { "晚安", "Good night" },
                { "谢谢", "Thank you" },
                { "谢谢你", "Thank you" },
                { "不客气", "You're welcome" },
                { "再见", "Goodbye" },
                { "请", "Please" },
                { "对不起", "Sorry" },
                { "没关系", "It's okay" },
                { "是", "Yes" },
                { "不是", "No" },
                { "我不知道", "I don't know" },
                { "我明白", "I understand" },
                { "我不明白", "I don't understand" },
                { "你会说中文吗", "Do you speak Chinese?" },
                { "你会说英语吗", "Do you speak English?" },
                { "多少钱", "How much is it?" },
                { "在哪里", "Where is it?" },
                { "你在哪里", "Where are you?" },
                { "你是谁", "Who are you?" },
                { "你在做什么", "What are you doing?" },
                { "你能帮我吗", "Can you help me?" },
                { "请稍等", "Please wait a moment" },
                { "我马上回来", "I'll be right back" },
                { "我正在工作", "I'm working" },
                { "今天天气很好", "The weather is nice today" },
                { "我喜欢这个", "I like this" },
                { "我不喜欢这个", "I don't like this" },
                { "可以", "Sure" },
                { "不可以", "No, you can't" },
                { "没问题", "No problem" },
                { "好的", "Okay" },
                { "收到", "Received" },
                { "稍后联系", "Talk to you later" },
                { "请告诉我更多", "Please tell me more" },
                { "这个怎么用", "How do I use this?" },
                { "我听不懂", "I don't understand what I hear" },
                { "我打字很慢", "I type slowly" },
                { "请再说一次", "Please say that again" },
                { "你能翻译这个吗", "Can you translate this?" },
                { "Hello", "你好" },
                { "Hi", "你好" },
                { "Good morning", "早上好" },
                { "Good evening", "晚上好" },
                { "Good night", "晚安" },
                { "Thank you", "谢谢" },
                { "Thanks", "谢谢" },
                { "You're welcome", "不客气" },
                { "Goodbye", "再见" },
                { "Please", "请" },
                { "Sorry", "对不起" },
                { "It's okay", "没关系" },
                { "Yes", "是" },
                { "No", "不是" },
                { "I don't know", "我不知道" },
                { "I understand", "我明白" },
                { "I don't understand", "我不明白" },
                { "Do you speak Chinese?", "你会说中文吗？" },
                { "Do you speak English?", "你会说英语吗？" },
                { "How much is it?", "多少钱？" },
                { "Where is it?", "在哪里？" },
                { "Where are you?", "你在哪里？" },
                { "Who are you?", "你是谁？" },
                { "What are you doing?", "你在做什么？" },
                { "Can you help me?", "你能帮我吗？" },
                { "Please wait a moment", "请稍等" },
                { "I'll be right back", "我马上回来" },
                { "I'm working", "我正在工作" },
                { "The weather is nice today", "今天天气很好" },
                { "I like this", "我喜欢这个" },
                { "I don't like this", "我不喜欢这个" },
                { "Sure", "可以" },
                { "No problem", "没问题" },
                { "Okay", "好的" },
                { "Talk to you later", "稍后联系" },
                { "Please tell me more", "请告诉我更多" },
                { "How do I use this?", "这个怎么用？" },
                { "I type slowly", "我打字很慢" },
                { "Please say that again", "请再说一次" },
                { "Can you translate this?", "你能翻译这个吗？" }
            };

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Dictionary<string, string> Glossary =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();
        private static volatile bool localOnly;
        private static int timeoutSeconds = 9;
        private static int lastNetworkAttempted;
        private static int translationDirection = (int)TranslationDirection.Auto;

        public static bool LastNetworkAttempted
        {
            get { return Interlocked.CompareExchange(ref lastNetworkAttempted, 0, 0) != 0; }
        }

        public static void Configure(bool useLocalOnly, int networkTimeoutSeconds)
        {
            Configure(
                useLocalOnly,
                networkTimeoutSeconds,
                (TranslationDirection)translationDirection);
        }

        public static void Configure(
            bool useLocalOnly,
            int networkTimeoutSeconds,
            TranslationDirection direction)
        {
            localOnly = useLocalOnly;
            timeoutSeconds = Math.Min(Math.Max(networkTimeoutSeconds, 3), 30);
            translationDirection = Math.Min(Math.Max((int)direction, 0), 2);
        }

        public static void ClearCache()
        {
            Cache.Clear();
        }

        public static void ResetNetworkDiagnostics()
        {
            Interlocked.Exchange(ref lastNetworkAttempted, 0);
        }

        public static void LoadDictionary(string path)
        {
            KeyValuePair<string, string>[] entries = ReadPairs(path);
            lock (Sync)
            {
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    LocalDictionary[entry.Key] = entry.Value;
                    if (!LocalDictionary.ContainsKey(entry.Value))
                    {
                        LocalDictionary[entry.Value] = entry.Key;
                    }
                }
            }
        }

        public static void LoadGlossary(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            KeyValuePair<string, string>[] entries = ReadPairs(path);
            lock (Sync)
            {
                Glossary.Clear();
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    Glossary[entry.Key] = entry.Value;
                    LocalDictionary[entry.Key] = entry.Value;
                    if (!LocalDictionary.ContainsKey(entry.Value))
                    {
                        LocalDictionary[entry.Value] = entry.Key;
                    }
                }
            }
        }

        public static async Task<TranslationResult> TranslateAsync(string text, CancellationToken token)
        {
            string normalized = Normalize(text);
            if (normalized.Length == 0)
            {
                return new TranslationResult { Text = "没有可翻译的文字", Provider = "提示", IsError = true };
            }

            bool chineseSource;
            string source;
            string target;
            ResolveLanguagePair(
                normalized,
                (TranslationDirection)translationDirection,
                out chineseSource,
                out source,
                out target);
            string cacheKey = translationDirection + ":" + normalized;

            string cached;
            if (Cache.TryGetValue(cacheKey, out cached))
            {
                return new TranslationResult { Text = cached, Provider = "缓存", IsError = false };
            }

            string local;
            bool languageMatches = chineseSource
                ? ContainsCjk(normalized)
                : !ContainsCjk(normalized);
            if (languageMatches && LocalDictionary.TryGetValue(normalized, out local))
            {
                Cache[cacheKey] = local;
                return new TranslationResult { Text = local, Provider = "本地词库", IsError = false };
            }

            KeyValuePair<string, string>[] glossary = SnapshotGlossary();
            GlossaryProtection protection = ProtectGlossaryTerms(normalized, glossary, chineseSource);

            if (localOnly)
            {
                return TranslationResult.Failed(
                    "离线模式：本地词库没有匹配；可编辑 dictionary.tsv 或 glossary.tsv");
            }

            Interlocked.Exchange(ref lastNetworkAttempted, 1);
            bool timedOut = false;
            using (CancellationTokenSource timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    string google = await TryGoogle(
                        protection.Text,
                        source,
                        target,
                        timeoutCancellation.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(google))
                    {
                        string translated = Normalize(RestoreGlossaryTerms(google, protection.Replacements));
                        Cache[cacheKey] = translated;
                        return new TranslationResult
                        {
                            Text = translated,
                            Provider = "在线翻译 · Google",
                            IsError = false
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    timedOut = true;
                }
                catch
                {
                }

                if (!timedOut)
                {
                    try
                    {
                        string memory = await TryMyMemory(
                            protection.Text,
                            source,
                            target,
                            timeoutCancellation.Token).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(memory))
                        {
                            string translated = Normalize(
                                RestoreGlossaryTerms(memory, protection.Replacements));
                            Cache[cacheKey] = translated;
                            return new TranslationResult
                            {
                                Text = translated,
                                Provider = "在线翻译 · MyMemory",
                                IsError = false
                            };
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        timedOut = true;
                    }
                    catch
                    {
                    }
                }
            }

            return TranslationResult.Failed(
                timedOut
                    ? "在线翻译超时（" + timeoutSeconds + " 秒）；已停止本次请求"
                    : "联网翻译暂不可用；请检查网络，或把常用句加入 dictionary.tsv");
        }

        public static async Task<string> CheckOnlineServicesAsync(int networkTimeoutSeconds)
        {
            int timeout = Math.Min(Math.Max(networkTimeoutSeconds, 3), 30);
            Interlocked.Exchange(ref lastNetworkAttempted, 1);
            using (CancellationTokenSource cancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(timeout)))
            {
                string google;
                try
                {
                    string result = await TryGoogle(
                        "hello",
                        "en",
                        "zh-CN",
                        cancellation.Token).ConfigureAwait(false);
                    google = string.IsNullOrWhiteSpace(result)
                        ? "Google 未返回结果"
                        : "Google 可用";
                }
                catch (OperationCanceledException)
                {
                    google = "Google 超时";
                }
                catch
                {
                    google = "Google 不可用";
                }

                string memory;
                try
                {
                    string result = await TryMyMemory(
                        "hello",
                        "en",
                        "zh-CN",
                        cancellation.Token).ConfigureAwait(false);
                    memory = string.IsNullOrWhiteSpace(result)
                        ? "MyMemory 未返回结果"
                        : "MyMemory 可用";
                }
                catch (OperationCanceledException)
                {
                    memory = "MyMemory 超时";
                }
                catch
                {
                    memory = "MyMemory 不可用";
                }
                return google + "；" + memory + "。";
            }
        }

        public static string GetLanguagePairForTest(
            string text,
            TranslationDirection direction)
        {
            bool chineseSource;
            string source;
            string target;
            ResolveLanguagePair(
                Normalize(text),
                direction,
                out chineseSource,
                out source,
                out target);
            return source + "|" + target;
        }

        private static void ResolveLanguagePair(
            string text,
            TranslationDirection direction,
            out bool chineseSource,
            out string source,
            out string target)
        {
            chineseSource = direction == TranslationDirection.ChineseToEnglish ||
                (direction == TranslationDirection.Auto && ContainsCjk(text));
            source = chineseSource ? "zh-CN" : "en";
            target = chineseSource ? "en" : "zh-CN";
        }

        private static KeyValuePair<string, string>[] ReadPairs(string path)
        {
            List<KeyValuePair<string, string>> result =
                new List<KeyValuePair<string, string>>();
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    string left = parts[0].Trim();
                    string right = parts[1].Trim();
                    if (left.Length > 0 && right.Length > 0)
                    {
                        result.Add(new KeyValuePair<string, string>(left, right));
                    }
                }
            }
            return result.ToArray();
        }

        private static KeyValuePair<string, string>[] SnapshotGlossary()
        {
            lock (Sync)
            {
                KeyValuePair<string, string>[] entries =
                    new KeyValuePair<string, string>[Glossary.Count];
                int index = 0;
                foreach (KeyValuePair<string, string> entry in Glossary)
                {
                    entries[index++] = entry;
                }
                return entries;
            }
        }

        public static string SimulateGlossaryRoundTripForTest(string source, string translated)
        {
            KeyValuePair<string, string>[] glossary = SnapshotGlossary();
            GlossaryProtection protection = ProtectGlossaryTerms(source, glossary, ContainsCjk(source));
            StringBuilder markedTranslation = new StringBuilder(translated ?? string.Empty);
            foreach (string token in protection.Replacements.Keys)
            {
                markedTranslation.Append(' ');
                markedTranslation.Append(token);
            }
            return RestoreGlossaryTerms(markedTranslation.ToString(), protection.Replacements);
        }

        private static GlossaryProtection ProtectGlossaryTerms(
            string source,
            KeyValuePair<string, string>[] glossary,
            bool chineseSource)
        {
            List<KeyValuePair<string, string>> ordered =
                new List<KeyValuePair<string, string>>(glossary);
            ordered.Sort(delegate(KeyValuePair<string, string> left, KeyValuePair<string, string> right)
            {
                string leftTerm = chineseSource ? left.Key : left.Value;
                string rightTerm = chineseSource ? right.Key : right.Value;
                return rightTerm.Length.CompareTo(leftTerm.Length);
            });

            string text = source;
            Dictionary<string, string> replacements =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (KeyValuePair<string, string> entry in ordered)
            {
                string term = chineseSource ? entry.Key : entry.Value;
                string preferred = chineseSource ? entry.Value : entry.Key;
                int position = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                while (position >= 0)
                {
                    string token = "__TERM" + index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) + "__";
                    text = text.Substring(0, position) + token +
                        text.Substring(position + term.Length);
                    replacements[token] = preferred;
                    index++;
                    position = text.IndexOf(term, position + token.Length, StringComparison.OrdinalIgnoreCase);
                }
            }

            return new GlossaryProtection
            {
                Text = text,
                Replacements = replacements
            };
        }

        private static string RestoreGlossaryTerms(
            string translated,
            Dictionary<string, string> replacements)
        {
            string result = translated ?? string.Empty;
            foreach (KeyValuePair<string, string> replacement in replacements)
            {
                result = result.Replace(replacement.Key, replacement.Value);
            }
            return result;
        }

        private static async Task<string> TryGoogle(
            string text,
            string source,
            string target,
            CancellationToken token)
        {
            string url =
                "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t" +
                "&sl=" + Uri.EscapeDataString(source) +
                "&tl=" + Uri.EscapeDataString(target) +
                "&q=" + Uri.EscapeDataString(text);

            using (HttpResponseMessage response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                object[] root = serializer.DeserializeObject(json) as object[];
                if (root == null || root.Length == 0)
                {
                    return null;
                }

                object[] segments = root[0] as object[];
                if (segments == null)
                {
                    return null;
                }

                StringBuilder builder = new StringBuilder();
                foreach (object segmentObject in segments)
                {
                    object[] segment = segmentObject as object[];
                    if (segment != null && segment.Length > 0 && segment[0] != null)
                    {
                        builder.Append(Convert.ToString(segment[0]));
                    }
                }
                return builder.ToString();
            }
        }

        private static async Task<string> TryMyMemory(
            string text,
            string source,
            string target,
            CancellationToken token)
        {
            string url =
                "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) +
                "&langpair=" + Uri.EscapeDataString(source + "|" + target);

            using (HttpResponseMessage response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root =
                    serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    return null;
                }

                object dataObject;
                if (root.TryGetValue("responseData", out dataObject))
                {
                    Dictionary<string, object> data = dataObject as Dictionary<string, object>;
                    if (data != null)
                    {
                        object translatedObject;
                        if (data.TryGetValue("translatedText", out translatedObject) && translatedObject != null)
                        {
                            return Convert.ToString(translatedObject);
                        }
                    }
                }
            }
            return null;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            HttpClient client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BilingualOverlay/1.0");
            return client;
        }

        private static bool ContainsCjk(string text)
        {
            foreach (char c in text)
            {
                if ((c >= '\u3400' && c <= '\u4DBF') ||
                    (c >= '\u4E00' && c <= '\u9FFF') ||
                    (c >= '\uF900' && c <= '\uFAFF'))
                {
                    return true;
                }
            }
            return false;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private sealed class GlossaryProtection
        {
            public string Text;
            public Dictionary<string, string> Replacements;
        }
    }

    internal static class SelfTest
    {
        public static int Run(string reportPath)
        {
            StringBuilder report = new StringBuilder();
            int failures = 0;

            Check(report, "insert", TextDelta.ExtractInserted("hello", "hello world"), " world", ref failures);
            Check(report, "insert-middle", TextDelta.ExtractInserted("helo", "hello"), "l", ref failures);
            Check(report, "delete", TextDelta.ExtractInserted("hello", "hell"), null, ref failures);
            Check(report, "chinese", TextDelta.ExtractInserted("", "你好"), "你好", ref failures);
            Check(report, "replace", TextDelta.ExtractInserted("hello", "hallo"), "a", ref failures);
            Check(report, "multiline-normalize", CapturedText.Normalize("第一行\r\n第二行\r\n"), "第一行\n第二行", ref failures);
            Check(report, "tail", CapturedText.Tail("0123456789", 4), "6789", ref failures);
            Check(report, "tail-surrogate", CapturedText.Tail("a😀", 2), "😀", ref failures);
            Check(report, "unchanged", TextDelta.ExtractInserted("hello", "hello"), null, ref failures);
            Check(
                report,
                "long-text-preserved",
                CapturedText.Tail(new string('长', 2500), 1600).Length.ToString(),
                "1600",
                ref failures);

            OverlaySettings defaults = OverlaySettings.CreateDefault();
            Check(report, "settings-local-default", defaults.LocalOnly ? "yes" : "no", "no", ref failures);
            Check(
                report,
                "settings-default-exclusions",
                (defaults.ExcludedProcesses ?? new List<string>()).Count.ToString(),
                "5",
                ref failures);
            Check(report, "settings-history-default",
                defaults.HistoryEnabled == true ? "yes" : "no", "yes", ref failures);
            Check(report, "settings-clear-history-default",
                defaults.ClearHistoryOnStartup ? "yes" : "no", "no", ref failures);
            Check(report, "settings-startup-default",
                defaults.StartWithWindows ? "yes" : "no", "no", ref failures);
            Check(report, "settings-direction-default",
                defaults.LanguageDirection.ToString(),
                ((int)TranslationDirection.Auto).ToString(),
                ref failures);
            Check(report, "settings-hotkey-default",
                defaults.SettingsHotkey, "Ctrl+Alt+F10", ref failures);
            Check(report, "translate-hotkey-default",
                defaults.TranslateHotkey, "Ctrl+Alt+T", ref failures);
            Check(report, "history-hotkey-default",
                defaults.HistoryHotkey, "Ctrl+Alt+H", ref failures);

            ShortcutGesture shortcut = ShortcutGesture.Parse(" control + alt + t ", false);
            Check(report, "shortcut-normalize",
                shortcut == null ? null : shortcut.DisplayText,
                "Ctrl+Alt+T", ref failures);
            Check(report, "shortcut-match",
                shortcut != null && shortcut.Matches(true, true, false, 0x54)
                    ? "yes"
                    : "no",
                "yes", ref failures);
            Check(report, "shortcut-requires-modifier",
                ShortcutGesture.IsValid("T", false) ? "yes" : "no",
                "no", ref failures);
            Check(report, "shortcut-function-scope",
                ShortcutGesture.IsValid("Ctrl+F10", false) ? "yes" : "no",
                "no", ref failures);
            Check(report, "shortcut-settings-function",
                ShortcutGesture.IsValid("Ctrl+F10", true) ? "yes" : "no",
                "yes", ref failures);
            Check(report, "shortcut-invalid-fallback",
                ShortcutGesture.NormalizeOrDefault(
                    "bad gesture",
                    "Ctrl+Alt+T",
                    false),
                "Ctrl+Alt+T", ref failures);
            Check(report, "shortcut-duplicate",
                ShortcutGesture.HasDuplicates(
                    "Ctrl+Alt+T",
                    "Alt+Ctrl+T",
                    "Ctrl+Alt+H",
                    true,
                    false)
                    ? "yes"
                    : "no",
                "yes", ref failures);
            Check(report, "shortcut-unique",
                ShortcutGesture.HasDuplicates(
                    "Ctrl+Alt+F10",
                    "Ctrl+Alt+T",
                    "Ctrl+Alt+H",
                    true,
                    false)
                    ? "yes"
                    : "no",
                "no", ref failures);

            Check(report, "version-newer",
                VersionTools.IsNewer("1.2.0", "1.1.9") ? "yes" : "no",
                "yes", ref failures);
            Check(report, "version-equal",
                VersionTools.IsNewer("v1.1.0", "1.1") ? "yes" : "no",
                "no", ref failures);
            Check(report, "version-rollback",
                VersionTools.IsNewer("1.0.9", "1.1.0") ? "yes" : "no",
                "no", ref failures);
            Check(report, "startup-command-quote",
                StartupManager.BuildRunCommand(
                    @"C:\Program Files\Bilingual Overlay\BilingualOverlay.exe"),
                "\"C:\\Program Files\\Bilingual Overlay\\BilingualOverlay.exe\"",
                ref failures);
            Check(report, "update-https-required",
                UpdateService.CheckAsync("http://example.com/update.json")
                    .GetAwaiter()
                    .GetResult()
                    .Success
                    ? "yes"
                    : "no",
                "no", ref failures);
            Check(report, "update-hash-missing",
                UpdateService.DownloadAsync(new UpdateManifest
                {
                    Version = "1.2.0",
                    DownloadUrl = "https://example.com/BilingualOverlaySetup.exe"
                })
                    .GetAwaiter()
                    .GetResult()
                    .Success
                    ? "yes"
                    : "no",
                "no", ref failures);
            Check(report, "update-hash-valid",
                UpdateService.IsSha256(new string('A', 64)) ? "yes" : "no",
                "yes", ref failures);
            Check(report, "update-hash-invalid",
                UpdateService.IsSha256(new string('G', 64)) ? "yes" : "no",
                "no", ref failures);

            Check(report, "direction-cn-en",
                TranslationService.GetLanguagePairForTest(
                    "你好",
                    TranslationDirection.ChineseToEnglish),
                "zh-CN|en", ref failures);
            Check(report, "direction-en-cn",
                TranslationService.GetLanguagePairForTest(
                    "hello",
                    TranslationDirection.EnglishToChinese),
                "en|zh-CN", ref failures);
            Check(report, "direction-auto-cn",
                TranslationService.GetLanguagePairForTest(
                    "你好",
                    TranslationDirection.Auto),
                "zh-CN|en", ref failures);
            Check(report, "direction-auto-en",
                TranslationService.GetLanguagePairForTest(
                    "hello",
                    TranslationDirection.Auto),
                "en|zh-CN", ref failures);

            Size testWorkingArea = new Size(1920, 1040);
            Check(report, "dpi-150-scale",
                UiLayoutMetrics.ScaleForDpi(800, 144).ToString(),
                "1200", ref failures);
            Check(report, "dpi-200-scale",
                UiLayoutMetrics.ScaleForDpi(800, 192).ToString(),
                "1600", ref failures);
            Check(report, "dpi-settings-min-150",
                UiLayoutMetrics.FitsWorkingArea(
                    new Size(620, 430),
                    testWorkingArea,
                    144)
                    ? "yes"
                    : "no",
                "yes", ref failures);
            Check(report, "dpi-settings-min-200",
                UiLayoutMetrics.FitsWorkingArea(
                    new Size(620, 430),
                    testWorkingArea,
                    192)
                    ? "yes"
                    : "no",
                "yes", ref failures);
            Check(report, "dpi-history-min-200",
                UiLayoutMetrics.FitsWorkingArea(
                    new Size(640, 450),
                    testWorkingArea,
                    192)
                    ? "yes"
                    : "no",
                "yes", ref failures);
            Size overlaySize = new Size(590, 272);
            Rectangle currentMonitors = new Rectangle(0, 0, 1920, 1040);
            Check(report, "monitor-removal-recovery",
                PointText(WindowPlacement.RecoverVisible(
                    new Point(-1080, 140),
                    overlaySize,
                    new[] { currentMonitors },
                    currentMonitors)),
                "1306,24",
                ref failures);

            using (SettingsForm settingsProbe = new SettingsForm(defaults))
            using (HistoryForm historyProbe = new HistoryForm(
                new List<TranslationRecord>(),
                true))
            {
                settingsProbe.CreateControl();
                historyProbe.CreateControl();
                Check(report, "ui-settings-autoscale",
                    settingsProbe.AutoScaleMode == AutoScaleMode.Dpi ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "ui-history-autoscale",
                    historyProbe.AutoScaleMode == AutoScaleMode.Dpi ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "ui-settings-tabs",
                    CountTabPages(settingsProbe).ToString(),
                    "4", ref failures);
                Check(report, "ui-settings-scroll",
                    AllTabPagesScrollable(settingsProbe) ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "ui-history-fill-columns",
                    FindDataGrid(historyProbe) != null &&
                    FindDataGrid(historyProbe).AutoSizeColumnsMode ==
                        DataGridViewAutoSizeColumnsMode.Fill
                        ? "yes"
                        : "no",
                    "yes", ref failures);
            }

            OverlaySettings clamped = defaults.Clone();
            clamped.FontScale = 9;
            clamped.Opacity = 0.01;
            clamped.TimeoutSeconds = 1;
            clamped.WindowWidth = 100;
            clamped.WindowHeight = 9999;
            clamped.ExcludedProcesses = new List<string>
            {
                @"C:\Tools\Notepad.EXE",
                "notepad",
                " ",
                "Chrome.exe"
            };
            clamped.Normalize();
            Check(report, "settings-font-max", clamped.FontScale.ToString("0.00"), "1.80", ref failures);
            Check(report, "settings-opacity-min", clamped.Opacity.ToString("0.00"), "0.35", ref failures);
            Check(report, "settings-timeout-min", clamped.TimeoutSeconds.ToString(), "3", ref failures);
            Check(report, "settings-width-min", clamped.WindowWidth.ToString(), "500", ref failures);
            Check(report, "settings-height-max", clamped.WindowHeight.ToString(), "700", ref failures);
            Check(
                report,
                "settings-exclusion-dedupe",
                string.Join(",", clamped.ExcludedProcesses.ToArray()),
                "notepad.exe,chrome.exe",
                ref failures);

            Check(
                report,
                "process-normalize",
                ProcessIdentity.Normalize(@"C:\Tools\Notepad.EXE"),
                "notepad.exe",
                ref failures);
            Check(
                report,
                "exclude-case-insensitive",
                PrivacyGuard.IsExcluded("NOTEPAD", new string[] { "notepad.exe" })
                    ? "yes"
                    : "no",
                "yes",
                ref failures);
            Check(
                report,
                "sensitive-title",
                PrivacyGuard.GetBlockReason("chrome.exe", "New Incognito Tab", null),
                "敏感窗口",
                ref failures);
            Check(
                report,
                "normal-title",
                PrivacyGuard.GetBlockReason("notepad.exe", "未命名 - 记事本", null),
                null,
                ref failures);

            CaptureCandidate selected = InputFallback.Choose("选中内容", "剪贴板内容", 1600);
            Check(report, "fallback-selection-first", selected == null ? null : selected.Text,
                "选中内容", ref failures);
            Check(report, "fallback-selection-source", selected == null ? null : selected.Source,
                "选中文字", ref failures);
            CaptureCandidate clipboard = InputFallback.Choose("  ", "剪贴板内容", 1600);
            Check(report, "fallback-clipboard", clipboard == null ? null : clipboard.Source,
                "剪贴板", ref failures);
            Check(report, "fallback-empty", InputFallback.Choose("", "", 1600) == null ? "empty" : "text",
                "empty", ref failures);

            Rectangle monitor = new Rectangle(0, 0, 1920, 1080);
            Check(
                report,
                "fullscreen-covers",
                FullscreenDetector.CoversMonitor(new Rectangle(-1, 0, 1922, 1080), monitor)
                    ? "yes"
                    : "no",
                "yes",
                ref failures);
            Check(
                report,
                "windowed-not-fullscreen",
                FullscreenDetector.CoversMonitor(new Rectangle(0, 0, 1200, 900), monitor)
                    ? "yes"
                    : "no",
                "no",
                ref failures);

            Rectangle primaryArea = new Rectangle(0, 0, 1920, 1040);
            Check(
                report,
                "default-right",
                PointText(WindowPlacement.DefaultRightPosition(primaryArea, overlaySize)),
                "1306,24",
                ref failures);
            Check(
                report,
                "clamp-left",
                PointText(WindowPlacement.Clamp(new Point(-50, 160), overlaySize, primaryArea)),
                "0,160",
                ref failures);
            Check(
                report,
                "clamp-bottom-right",
                PointText(WindowPlacement.Clamp(new Point(1900, 1100), overlaySize, primaryArea)),
                "1330,768",
                ref failures);

            Point savedPosition = new Point(-1080, 140);
            Check(
                report,
                "position-roundtrip",
                WindowPositionStore.Format(savedPosition),
                "-1080,140",
                ref failures);
            Point parsedPosition;
            bool parsed = WindowPositionStore.TryParse(
                WindowPositionStore.Format(savedPosition),
                out parsedPosition);
            Check(
                report,
                "position-restore",
                parsed ? PointText(parsedPosition) : "<invalid>",
                "-1080,140",
                ref failures);
            Check(
                report,
                "position-visible",
                WindowPlacement.IsFullyVisible(
                    new Point(-1080, 140),
                    overlaySize,
                    new Rectangle[] { new Rectangle(-1920, 0, 1920, 1040), primaryArea })
                    ? "yes"
                    : "no",
                "yes",
                ref failures);
            Check(
                report,
                "position-offscreen",
                WindowPlacement.IsFullyVisible(
                    new Point(-1200, 140),
                    overlaySize,
                    new Rectangle[] { primaryArea })
                    ? "yes"
                    : "no",
                "no",
                ref failures);
            Check(
                report,
                "drag-locked",
                WindowDragPolicy.CanStartDrag(MouseButtons.Left, true) ? "yes" : "no",
                "no",
                ref failures);
            Check(
                report,
                "drag-unlocked",
                WindowDragPolicy.CanStartDrag(MouseButtons.Left, false) ? "yes" : "no",
                "yes",
                ref failures);
            Check(
                report,
                "drag-non-left",
                WindowDragPolicy.CanStartDrag(MouseButtons.Right, false) ? "yes" : "no",
                "no",
                ref failures);
            Check(
                report,
                "drag-clamp",
                PointText(WindowDragPolicy.NextPosition(
                    new Point(1900, 1100),
                    Point.Empty,
                    overlaySize,
                    primaryArea)),
                "1330,768",
                ref failures);
            Check(
                report,
                "fullscreen-exact",
                FullscreenDetector.CoversMonitor(
                    new Rectangle(0, 0, 1920, 1080),
                    new Rectangle(0, 0, 1920, 1080))
                    ? "yes"
                    : "no",
                "yes",
                ref failures);
            Check(
                report,
                "fullscreen-tolerance",
                FullscreenDetector.CoversMonitor(
                    new Rectangle(2, 1, 1917, 1078),
                    new Rectangle(0, 0, 1920, 1080))
                    ? "yes"
                    : "no",
                "yes",
                ref failures);
            Check(
                report,
                "fullscreen-windowed",
                FullscreenDetector.CoversMonitor(
                    new Rectangle(0, 0, 1920, 1000),
                    new Rectangle(0, 0, 1920, 1080))
                    ? "yes"
                    : "no",
                "no",
                ref failures);
            Check(
                report,
                "fullscreen-invalid",
                FullscreenDetector.CoversMonitor(
                    new Rectangle(0, 0, 0, 1080),
                    new Rectangle(0, 0, 1920, 1080))
                    ? "yes"
                    : "no",
                "no",
                ref failures);

            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "BilingualOverlaySelfTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(testDirectory);
                string settingsPath = Path.Combine(testDirectory, "settings.json");
                OverlaySettings persisted = defaults.Clone();
                persisted.LocalOnly = true;
                persisted.FontScale = 1.3;
                persisted.WindowWidth = 720;
                persisted.WindowHeight = 410;
                persisted.Opacity = 0.72;
                persisted.ClickThrough = true;
                persisted.LockPosition = true;
                persisted.AutoHideFullScreen = false;
                persisted.ExcludedProcesses = new List<string> { "Notepad.Exe", "chrome" };
                persisted.LanguageDirection = (int)TranslationDirection.EnglishToChinese;
                persisted.HistoryEnabled = false;
                persisted.ClearHistoryOnStartup = true;
                persisted.SettingsHotkey = "Ctrl+Alt+F11";
                persisted.TranslateHotkey = "Ctrl+Shift+T";
                persisted.HistoryHotkey = "Alt+H";
                persisted.StartWithWindows = true;
                persisted.UpdateManifestUrl = "https://example.com/update.json";
                Check(report, "settings-save", SettingsStore.Save(persisted, settingsPath) ? "yes" : "no",
                    "yes", ref failures);
                OverlaySettings restored = SettingsStore.Load(settingsPath);
                Check(report, "settings-restore-local", restored.LocalOnly ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "settings-restore-width", restored.WindowWidth.ToString(),
                    "720", ref failures);
                Check(report, "settings-restore-height", restored.WindowHeight.ToString(),
                    "410", ref failures);
                Check(report, "settings-restore-font", restored.FontScale.ToString("0.0"),
                    "1.3", ref failures);
                Check(report, "settings-restore-clickthrough",
                    restored.ClickThrough ? "yes" : "no", "yes", ref failures);
                Check(report, "settings-restore-lock", restored.LockPosition ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "settings-restore-autohide",
                    restored.AutoHideFullScreen ? "yes" : "no", "no", ref failures);
                Check(report, "settings-restore-exclusions",
                    string.Join(",", restored.ExcludedProcesses.ToArray()),
                    "notepad.exe,chrome.exe", ref failures);
                Check(report, "settings-restore-direction",
                    restored.LanguageDirection.ToString(),
                    ((int)TranslationDirection.EnglishToChinese).ToString(),
                    ref failures);
                Check(report, "settings-restore-history",
                    restored.HistoryEnabled == false ? "off" : "on",
                    "off", ref failures);
                Check(report, "settings-restore-clear-history",
                    restored.ClearHistoryOnStartup ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "settings-restore-settings-hotkey",
                    restored.SettingsHotkey, "Ctrl+Alt+F11", ref failures);
                Check(report, "settings-restore-translate-hotkey",
                    restored.TranslateHotkey, "Ctrl+Shift+T", ref failures);
                Check(report, "settings-restore-history-hotkey",
                    restored.HistoryHotkey, "Alt+H", ref failures);
                Check(report, "settings-restore-startup",
                    restored.StartWithWindows ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "settings-restore-update-url",
                    restored.UpdateManifestUrl,
                    "https://example.com/update.json",
                    ref failures);
                string missingSettingsPath = Path.Combine(testDirectory, "missing", "settings.json");
                OverlaySettings candidateRestored = SettingsStore.LoadFromCandidates(
                    new[] { missingSettingsPath, settingsPath });
                Check(report, "settings-candidate-load",
                    candidateRestored.WindowWidth.ToString(), "720", ref failures);
                string fallbackSettingsPath = Path.Combine(testDirectory, "fallback", "settings.json");
                string invalidSettingsPath = "\0invalid-settings.json";
                Check(report, "settings-candidate-save",
                    SettingsStore.SaveToCandidates(
                        persisted,
                        new[] { invalidSettingsPath, fallbackSettingsPath }) ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "settings-candidate-file",
                    File.Exists(fallbackSettingsPath) ? "yes" : "no", "yes", ref failures);
                string secondarySettingsPath = Path.Combine(
                    testDirectory,
                    "secondary",
                    "settings.json");
                Check(report, "settings-candidate-save-both",
                    SettingsStore.SaveToCandidates(
                        persisted,
                        new[] { fallbackSettingsPath, secondarySettingsPath }) ? "yes" : "no",
                    "yes", ref failures);
                Check(report, "settings-candidate-both-files",
                    File.Exists(fallbackSettingsPath) && File.Exists(secondarySettingsPath)
                        ? "yes"
                        : "no",
                    "yes", ref failures);
                string newerSettingsPath = Path.Combine(
                    testDirectory,
                    "newer",
                    "settings.json");
                OverlaySettings newer = persisted.Clone();
                newer.WindowWidth = 810;
                SettingsStore.Save(newer, newerSettingsPath);
                File.SetLastWriteTimeUtc(settingsPath, DateTime.UtcNow.AddMinutes(-2));
                File.SetLastWriteTimeUtc(newerSettingsPath, DateTime.UtcNow);
                Check(report, "settings-candidate-load-newest",
                    SettingsStore.LoadFromCandidates(
                        new[] { settingsPath, newerSettingsPath }).WindowWidth.ToString(),
                    "810", ref failures);
                Check(report, "settings-save-unwritable",
                    SettingsStore.SaveToCandidates(
                        persisted,
                        new[] { invalidSettingsPath }) ? "yes" : "no",
                    "no", ref failures);

                List<TranslationRecord> historyRecords = new List<TranslationRecord>();
                TranslationRecord firstHistory = TranslationHistoryStore.Add(
                    historyRecords,
                    "第一句",
                    "First sentence",
                    "测试",
                    new DateTime(2026, 11, 22, 10, 0, 0));
                firstHistory.IsFavorite = true;
                TranslationRecord duplicateHistory = TranslationHistoryStore.Add(
                    historyRecords,
                    "第一句",
                    "First sentence",
                    "测试",
                    new DateTime(2026, 11, 22, 10, 5, 0));
                Check(report, "history-add",
                    historyRecords.Count.ToString(), "1", ref failures);
                Check(report, "history-dedupe-newest",
                    duplicateHistory.CreatedAt.ToString("HH:mm"),
                    "10:05", ref failures);
                Check(report, "history-dedupe-keeps-favorite",
                    duplicateHistory.IsFavorite ? "yes" : "no",
                    "yes", ref failures);
                TranslationHistoryStore.Add(
                    historyRecords,
                    "第二句",
                    "Second sentence",
                    "测试",
                    new DateTime(2026, 11, 22, 10, 10, 0));
                Check(report, "history-clear-nonfavorites-count",
                    TranslationHistoryStore.ClearHistoryKeepFavorites(historyRecords)
                        .ToString(),
                    "1", ref failures);
                Check(report, "history-clear-nonfavorites-result",
                    historyRecords.Count == 1 &&
                    historyRecords[0].IsFavorite
                        ? "favorite"
                        : "invalid",
                    "favorite", ref failures);

                List<TranslationRecord> boundedHistory = new List<TranslationRecord>();
                for (int index = 0; index < TranslationHistoryStore.MaximumRecords + 5; index++)
                {
                    TranslationHistoryStore.Add(
                        boundedHistory,
                        "原文" + index,
                        "Translation " + index,
                        "测试",
                        new DateTime(2026, 11, 22).AddSeconds(index));
                }
                Check(report, "history-limit-count",
                    boundedHistory.Count.ToString(),
                    TranslationHistoryStore.MaximumRecords.ToString(),
                    ref failures);
                Check(report, "history-limit-newest",
                    boundedHistory[0].Original,
                    "原文504", ref failures);
                Check(report, "history-limit-oldest-kept",
                    boundedHistory[boundedHistory.Count - 1].Original,
                    "原文5", ref failures);

                string csv = TranslationHistoryStore.BuildCsv(new[]
                {
                    new TranslationRecord
                    {
                        CreatedAt = new DateTime(2026, 11, 22, 10, 0, 0),
                        Original = "a,b",
                        Translation = "say \"hi\"",
                        Provider = "line1\nline2",
                        IsFavorite = true
                    }
                });
                CheckContains(report, "history-csv-comma", csv, "\"a,b\"", ref failures);
                CheckContains(report, "history-csv-quote", csv, "\"say \"\"hi\"\"\"", ref failures);
                CheckContains(report, "history-csv-newline", csv, "\"line1\nline2\"", ref failures);

                string installSource = Path.Combine(testDirectory, "install-source");
                string installTarget = Path.Combine(testDirectory, "install-target");
                string missingInstallSource = Path.Combine(testDirectory, "install-missing");
                string missingInstallTarget = Path.Combine(testDirectory, "install-missing-target");
                Directory.CreateDirectory(installSource);
                Directory.CreateDirectory(installTarget);
                Directory.CreateDirectory(missingInstallSource);
                foreach (string name in new[]
                {
                    "BilingualOverlay.exe",
                    "dictionary.tsv",
                    "glossary.tsv",
                    "README-使用说明.txt"
                })
                {
                    File.WriteAllText(Path.Combine(installSource, name), "new:" + name);
                }
                File.WriteAllText(
                    Path.Combine(installTarget, "BilingualOverlay.exe"),
                    "old:program");
                File.WriteAllText(
                    Path.Combine(installTarget, "dictionary.tsv"),
                    "old:dictionary");
                File.WriteAllText(
                    Path.Combine(installTarget, "settings.json"),
                    "user-settings");
                File.WriteAllText(
                    Path.Combine(installTarget, "history.json"),
                    "user-history");
                int copiedProgramFiles =
                    InstallationManager.CopyProgramFiles(installSource, installTarget);
                Check(report, "install-copy-count",
                    copiedProgramFiles.ToString(), "4", ref failures);
                Check(report, "install-upgrade-overwrites",
                    File.ReadAllText(Path.Combine(installTarget, "BilingualOverlay.exe")),
                    "new:BilingualOverlay.exe",
                    ref failures);
                Check(report, "install-preserves-settings",
                    File.ReadAllText(Path.Combine(installTarget, "settings.json")),
                    "user-settings",
                    ref failures);
                Check(report, "install-preserves-history",
                    File.ReadAllText(Path.Combine(installTarget, "history.json")),
                    "user-history",
                    ref failures);
                File.WriteAllText(
                    Path.Combine(missingInstallSource, "dictionary.tsv"),
                    "dictionary");
                bool missingMainRejected = false;
                try
                {
                    InstallationManager.CopyProgramFiles(
                        missingInstallSource,
                        missingInstallTarget);
                }
                catch (FileNotFoundException)
                {
                    missingMainRejected = true;
                }
                Check(report, "install-rejects-missing-main",
                    missingMainRejected ? "yes" : "no",
                    "yes", ref failures);

                Check(
                    report,
                    "clickthrough-style-on",
                    (ClickThroughWindowStyle.WithClickThrough(0, true) & 0x00080020).ToString("X"),
                    "80020",
                    ref failures);
                Check(
                    report,
                    "clickthrough-style-off",
                    ClickThroughWindowStyle.WithClickThrough(0x00080020, false).ToString("X"),
                    "80000",
                    ref failures);
                using (Form styleProbe = new Form())
                {
                    styleProbe.ShowInTaskbar = false;
                    styleProbe.FormBorderStyle = FormBorderStyle.None;
                    IntPtr styleHandle = styleProbe.Handle;
                    bool appliedOn = ClickThroughWindowStyle.Apply(styleHandle, true);
                    bool enabledOn = ClickThroughWindowStyle.IsEnabled(styleHandle);
                    bool appliedOff = ClickThroughWindowStyle.Apply(styleHandle, false);
                    bool enabledOff = ClickThroughWindowStyle.IsEnabled(styleHandle);
                    Check(report, "clickthrough-handle-on",
                        appliedOn && enabledOn ? "yes" : "no", "yes", ref failures);
                    Check(report, "clickthrough-handle-off",
                        appliedOff && !enabledOff ? "yes" : "no", "yes", ref failures);
                }

                string glossaryPath = Path.Combine(testDirectory, "glossary.tsv");
                File.WriteAllText(
                    glossaryPath,
                    "# test glossary\n悬浮窗\toverlay\n隐私模式\tprivacy mode\n",
                    new UTF8Encoding(true));
                TranslationService.LoadGlossary(glossaryPath);
                TranslationService.ClearCache();
                TranslationResult glossaryDirect = TranslationService
                    .TranslateAsync("悬浮窗", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Check(report, "glossary-local-direct", glossaryDirect.Text,
                    "overlay", ref failures);
                Check(
                    report,
                    "glossary-protect-restore",
                    TranslationService.SimulateGlossaryRoundTripForTest(
                        "请打开悬浮窗",
                        "Please open"),
                    "Please open overlay",
                    ref failures);
                Check(
                    report,
                    "glossary-sensitive-term",
                    TranslationService.SimulateGlossaryRoundTripForTest(
                        "打开隐私模式",
                        "Open"),
                    "Open privacy mode",
                    ref failures);
            }
            catch (Exception ex)
            {
                report.AppendLine("FAIL settings/glossary: " + ex.Message);
                failures++;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(testDirectory))
                    {
                        foreach (string file in Directory.GetFiles(testDirectory))
                        {
                            File.Delete(file);
                        }
                        Directory.Delete(testDirectory);
                    }
                }
                catch
                {
                }
            }

            Check(
                report,
                "key-letter",
                TextInputKeys.MayChangeText(0x41) ? "yes" : "no",
                "yes",
                ref failures);
            Check(
                report,
                "key-ime",
                TextInputKeys.MayChangeText(0xE5) ? "yes" : "no",
                "yes",
                ref failures);
            Check(
                report,
                "key-shift-ignored",
                TextInputKeys.MayChangeText(0x10) ? "yes" : "no",
                "no",
                ref failures);
            Check(
                report,
                "key-arrow-ignored",
                TextInputKeys.MayChangeText(0x25) ? "yes" : "no",
                "no",
                ref failures);

            try
            {
                TranslationService.Configure(true, 3);
                TranslationService.ClearCache();
                TranslationService.ResetNetworkDiagnostics();
                TranslationResult local = TranslationService
                    .TranslateAsync("hello", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Check(report, "local-translation", local.Text, "你好", ref failures);
                Check(report, "local-mode-no-network",
                    TranslationService.LastNetworkAttempted ? "yes" : "no", "no", ref failures);

                TranslationResult offlineMiss = TranslationService
                    .TranslateAsync("这是一条只存在于在线服务中的独特测试句子", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                CheckContains(report, "offline-miss-message", offlineMiss.Text, "离线模式", ref failures);
                Check(report, "offline-miss-no-network",
                    TranslationService.LastNetworkAttempted ? "yes" : "no", "no", ref failures);
                Check(report, "offline-miss-error", offlineMiss.IsError ? "yes" : "no",
                    "yes", ref failures);

                TranslationResult cached = TranslationService
                    .TranslateAsync("hello", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Check(report, "translation-cache", cached.Text, "你好", ref failures);
                TranslationService.ClearCache();
                TranslationService.Configure(false, 9);
            }
            catch (Exception ex)
            {
                report.AppendLine("FAIL translation: " + ex.Message);
                failures++;
            }

            report.AppendLine(failures == 0 ? "PASS" : "FAILURES=" + failures);
            string content = report.ToString();
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                File.WriteAllText(reportPath, content, Encoding.UTF8);
            }
            return failures == 0 ? 0 : 1;
        }

        private static string PointText(Point point)
        {
            return point.X + "," + point.Y;
        }

        private static TabControl FindTabControl(Control root)
        {
            if (root == null)
            {
                return null;
            }
            TabControl direct = root as TabControl;
            if (direct != null)
            {
                return direct;
            }
            foreach (Control child in root.Controls)
            {
                TabControl found = FindTabControl(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static int CountTabPages(Control root)
        {
            TabControl tabs = FindTabControl(root);
            return tabs == null ? 0 : tabs.TabPages.Count;
        }

        private static bool AllTabPagesScrollable(Control root)
        {
            TabControl tabs = FindTabControl(root);
            if (tabs == null || tabs.TabPages.Count == 0)
            {
                return false;
            }
            foreach (TabPage page in tabs.TabPages)
            {
                if (!page.AutoScroll)
                {
                    return false;
                }
            }
            return true;
        }

        private static DataGridView FindDataGrid(Control root)
        {
            if (root == null)
            {
                return null;
            }
            DataGridView direct = root as DataGridView;
            if (direct != null)
            {
                return direct;
            }
            foreach (Control child in root.Controls)
            {
                DataGridView found = FindDataGrid(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void Check(
            StringBuilder report,
            string name,
            string actual,
            string expected,
            ref int failures)
        {
            bool pass = string.Equals(actual, expected, StringComparison.Ordinal);
            report.AppendLine((pass ? "PASS " : "FAIL ") + name + " | actual=" +
                (actual ?? "<null>") + " | expected=" + (expected ?? "<null>"));
            if (!pass)
            {
                failures++;
            }
        }

        private static void CheckContains(
            StringBuilder report,
            string name,
            string actual,
            string expectedPart,
            ref int failures)
        {
            string value = actual ?? string.Empty;
            bool pass = value.IndexOf(expectedPart, StringComparison.Ordinal) >= 0;
            report.AppendLine((pass ? "PASS " : "FAIL ") + name + " | actual=" +
                value + " | contains=" + expectedPart);
            if (!pass)
            {
                failures++;
            }
        }
    }
}
