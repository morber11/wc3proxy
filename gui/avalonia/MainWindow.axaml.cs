using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;

namespace wc3proxy.avalonia
{
    public partial class MainWindow : Window
    {
        private Process? proc;
        private LogWindow? logWindow;
        private CancellationTokenSource? outputCts;
        private readonly SettingsHelper settingsHelper;

        private const string DefaultIp = "1.0.0.1";
        private const string DefaultVersion = "1.29";
        private const string DefaultCliFileName = "wc3proxy.exe";
        private const string ExpansionTft = "TFT";
        private const string ExpansionRoc = "RoC";
        private const string Wc3proxyToken = "wc3proxy";


        private static readonly Regex VersionRegex = new(@"^1\.[23]\d$", RegexOptions.Compiled);

        public MainWindow()
        {
            InitializeComponent();

            IpBox!.Text = DefaultIp;
            VersionBox!.Text = DefaultVersion;

            StartBtn!.Click += StartBtn_Click;
            StopBtn!.Click += StopBtn_Click;
            ShowLogBtn!.Click += ShowLogBtn_Click;
            OpenSettingsBtn!.Click += OpenSettingsBtn_Click;

            var exeDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            var settingsPath = Path.Combine(exeDir, SettingsHelper.SettingsFileName);
            settingsHelper = new SettingsHelper(settingsPath);

            try
            {
                var s = settingsHelper.Load();
                if (s is not null)
                {
                    IpBox!.Text = s.Ip ?? IpBox.Text;
                    VersionBox!.Text = s.Version ?? VersionBox.Text;

                    if (s.IsTft)
                    {
                        TFT!.IsChecked = true;
                    }
                    else
                    {
                        RoC!.IsChecked = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("load settings", ex);
            }

            Closing += OnMainWindowClosing;

            IpBox!.LostFocus += (_, __) => SaveSettings();
            VersionBox!.LostFocus += (_, __) => SaveSettings();
            TFT!.IsCheckedChanged += (_, __) => SaveSettings();
            RoC!.IsCheckedChanged += (_, __) => SaveSettings();
        }

        private void OpenSettingsBtn_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                var s = new SettingsHelper.UserSettings(IpBox!.Text ?? string.Empty, VersionBox!.Text ?? string.Empty, TFT!.IsChecked == true);
                settingsHelper.EnsureExists(s);
                var psi = new ProcessStartInfo { FileName = settingsHelper.Path, UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowMessage("Failed to open settings file: " + ex.Message);
            }
        }

        private void ShowLogBtn_Click(object? sender, RoutedEventArgs? e)
        {
            if (logWindow is null || logWindow.IsClosed)
            {
                logWindow = new LogWindow();
            }

            logWindow.Show();
            logWindow.Activate();
        }

        private void StopBtn_Click(object? sender, RoutedEventArgs e)
        {
            StopProcess();
            CloseLogWindow();
        }

        private void StartBtn_Click(object? sender, RoutedEventArgs e)
        {
            var ipText = IpBox?.Text ?? string.Empty;
            var versionText = VersionBox?.Text ?? string.Empty;

            if (!IsValidIP(ipText)) { ShowMessage("Invalid IP"); return; }
            if (!IsValidVersion(versionText)) { ShowMessage("Invalid Version"); return; }

            var expansion = (TFT?.IsChecked == true) ? ExpansionTft : ExpansionRoc;

            var binary = TryExtractEmbeddedCli();
            if (string.IsNullOrEmpty(binary) || !File.Exists(binary))
            {
                ShowMessage("Embedded CLI not found.");
                return;
            }

            StartProcess(binary, ipText.Trim(), versionText.Trim(), expansion);
            ShowLogBtn_Click(null, null);
        }

        private void ShowMessage(string text)
        {
            _ = ConfirmDialog.ShowAlert(this, "Message", text);
        }

        private string? TryExtractEmbeddedCli()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var asmName = asm.GetName().Name?.ToLowerInvariant() ?? string.Empty;

                foreach (var name in asm.GetManifestResourceNames())
                {
                    var lower = name.ToLowerInvariant();

                    if (!(lower.Contains("embeddedcli") || lower.EndsWith(".exe")))
                    {
                        continue;
                    }

                    // skip any resource that appears to be the GUI assembly
                    if (!string.IsNullOrEmpty(asmName) && lower.Contains(asmName))
                    {
                        continue;
                    }

                    if (!lower.Contains(Wc3proxyToken) || !lower.EndsWith(".exe"))
                    {
                        continue;
                    }

                    string fileName;
                    var tokens = name.Split('.');
                    var idx = Array.FindIndex(tokens, t => string.Equals(t, Wc3proxyToken, StringComparison.OrdinalIgnoreCase));
                    var ext = tokens.Last();

                    if (idx >= 0)
                    {
                        fileName = tokens[idx] + "." + ext;
                    }
                    else if (tokens.Length >= 2)
                    {
                        fileName = tokens[^2] + "." + ext;
                    }
                    else
                    {
                        fileName = name;
                    }

                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = DefaultCliFileName;
                    }

                    var outPath = Path.Combine(Path.GetTempPath(), fileName);

                    using (var res = asm.GetManifestResourceStream(name))
                    {
                        if (res is null)
                        {
                            continue;
                        }

                        using var fs = File.Create(outPath);
                        res.CopyTo(fs);
                    }

                    return outPath;
                }
            }
            catch (Exception ex)
            {
                LogError("extract embedded cli", ex);
            }

            return null;
        }

        private void SaveSettings()
        {
            try
            {
                var s = new SettingsHelper.UserSettings(IpBox!.Text ?? string.Empty, VersionBox!.Text ?? string.Empty, TFT!.IsChecked == true);
                settingsHelper.Save(s);
            }
            catch (Exception ex)
            {
                LogError("save settings", ex);
            }
        }

        private async Task ReadStreamAsync(TextReader reader, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);

                    if (line is null)
                    {
                        break;
                    }

                    AppendLogLine(line + Environment.NewLine);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogError("reader", ex);
            }
        }

        private void StartProcess(string binary, string ip, string version, string expansion)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = $"\"{ip}\" \"{version}\" {expansion}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.Exited += (s, e) =>
                {
                    AppendLogLine("[process exited]");
                    Dispatcher.UIThread.Post(() => { StartBtn.IsEnabled = true; StopBtn.IsEnabled = false; });
                };

                proc.Start();
                CancelOutputReaders();

                outputCts = new CancellationTokenSource();
                var token = outputCts.Token;

                var outReader = proc.StandardOutput;
                var errReader = proc.StandardError;

                var outTask = ReadStreamAsync(outReader, token);
                var errTask = ReadStreamAsync(errReader, token);

                _ = Task.WhenAll(outTask, errTask);

                Dispatcher.UIThread.Post(() => { StartBtn!.IsEnabled = false; StopBtn!.IsEnabled = true; ShowLogBtn!.IsEnabled = true; });
            }
            catch (Exception ex)
            {
                ShowMessage("Failed to start process: " + ex.Message);
            }
        }

        private void StopProcess()
        {
            try
            {
                CancelOutputReaders();

                if (proc is not null && !proc.HasExited)
                {
                    proc.Kill(true);
                    WaitForExit(proc, 3000);
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Failed to stop process: " + ex.Message);
            }
        }

        private void AppendLogLine(string s)
        {
            var lw = logWindow;
            if (lw is null || lw.IsClosed)
            {
                return;
            }

            Dispatcher.UIThread.Post(() => lw.AppendText(s));
        }

        private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            StopProcess();
            CloseLogWindow();
            SaveSettings();
        }

        private void CloseLogWindow()
        {
            var lw = logWindow;
            if (lw is null || lw.IsClosed)
            {
                return;
            }

            try
            {
                lw.Close();
            }
            catch (Exception ex)
            {
                LogError("log window close", ex);
            }
        }

        private void CancelOutputReaders()
        {
            try
            {
                outputCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed - ignore
            }
        }

        private void WaitForExit(Process process, int milliseconds)
        {
            try
            {
                process.WaitForExit(milliseconds);
            }
            catch (Exception ex)
            {
                LogError("wait-for-exit", ex);
            }
        }

        private void LogError(string context, Exception ex)
        {
            AppendLogLine($"[{context} error] {ex.Message}{Environment.NewLine}");
        }

        static bool IsValidIP(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            if (IPAddress.TryParse(s.Trim(), out var addr))
            {
                return addr.AddressFamily == AddressFamily.InterNetwork; // IPv4 only
            }

            return false;
        }

        static bool IsValidVersion(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }

            return VersionRegex.IsMatch(s);
        }
    }
}
