using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace wc3proxy.avalonia
{
    public partial class MainWindow : Window
    {
        private Process? proc;
        private LogWindow? logWindow;
        private CancellationTokenSource? outputCts;
        private readonly string SettingsPath;

        private record UserSettings(string Ip, string Version, bool IsTft);

        public MainWindow()
        {
            InitializeComponent();

            IpBox!.Text = "1.0.0.1";
            VersionBox!.Text = "1.29";

            StartBtn!.Click += StartBtn_Click;
            StopBtn!.Click += StopBtn_Click;
            ShowLogBtn!.Click += ShowLogBtn_Click;
            OpenSettingsBtn!.Click += OpenSettingsBtn_Click;

            var exeDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            SettingsPath = Path.Combine(exeDir, "wc3proxy-gui-settings.json");

            LoadSettings();

            this.Closing += (sender, e) =>
            {
                try
                {
                    if (proc != null && !proc.HasExited)
                    {
                        StopProcess();
                        try { proc?.WaitForExit(3000); } catch { }
                    }

                    if (logWindow != null && !logWindow.IsClosed)
                    {
                        try { logWindow.Close(); } catch { }
                    }
                }
                catch { }

                try { SaveSettings(); } catch { }
            };

            IpBox!.LostFocus += (_, __) => SaveSettings();
            VersionBox!.LostFocus += (_, __) => SaveSettings();
            TFT!.IsCheckedChanged += (_, __) => SaveSettings();
            RoC!.IsCheckedChanged += (_, __) => SaveSettings();
        }

        private void OpenSettingsBtn_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    var s = new UserSettings(IpBox!.Text ?? string.Empty, VersionBox!.Text ?? string.Empty, TFT!.IsChecked == true);
                    var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SettingsPath, json);
                }

                var psi = new ProcessStartInfo { FileName = SettingsPath, UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowMessage("Failed to open settings file: " + ex.Message);
            }
        }

        private void ShowLogBtn_Click(object? sender, RoutedEventArgs? e)
        {
            if (logWindow == null || logWindow.IsClosed)
            {
                logWindow = new LogWindow();
            }
            logWindow.Show();
            logWindow.Activate();
        }

        private void StopBtn_Click(object? sender, RoutedEventArgs e)
        {
            StopProcess();

            try
            {
                var lw = logWindow;
                if (lw != null && !lw.IsClosed)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { lw.Close(); } catch { }
                    });
                }
            }
            catch { }
        }

        private void StartBtn_Click(object? sender, RoutedEventArgs e)
        {
            var ipText = IpBox?.Text ?? string.Empty;
            var versionText = VersionBox?.Text ?? string.Empty;

            if (!IsValidIP(ipText)) { ShowMessage("Invalid IP"); return; }
            if (!IsValidVersion(versionText)) { ShowMessage("Invalid Version"); return; }

            var expansion = (TFT?.IsChecked == true) ? "TFT" : "RoC";

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
                        continue;

                    // skip any resource that appears to be the GUI assembly
                    if (!string.IsNullOrEmpty(asmName) && lower.Contains(asmName))
                    {
                        continue;
                    }

                    if (!lower.Contains("wc3proxy") || !lower.EndsWith(".exe"))
                    {
                        continue;
                    }

                    string fileName;
                    var tokens = name.Split('.');
                    var idx = Array.FindIndex(tokens, t => string.Equals(t, "wc3proxy", StringComparison.OrdinalIgnoreCase));
                    var ext = tokens.Last();

                    if (idx >= 0)
                    {
                        fileName = tokens[idx] + "." + ext;
                    }
                    else if (tokens.Length >= 2)
                    {
                        fileName = tokens[tokens.Length - 2] + "." + ext;
                    }
                    else
                    {
                        fileName = name;
                    }

                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = "wc3proxy.exe";
                    }

                    var outPath = Path.Combine(Path.GetTempPath(), fileName);

                    using (var res = asm.GetManifestResourceStream(name))
                    {
                        if (res == null)
                        {
                            continue;
                        }

                        using (var fs = File.Create(outPath))
                        {
                            res.CopyTo(fs);
                        }
                    }

                    if (!OperatingSystem.IsWindows())
                    {
                        try
                        {
                            var chmod = new ProcessStartInfo { FileName = "chmod", Arguments = $"+x \"{outPath}\"", UseShellExecute = false, CreateNoWindow = true };
                            Process.Start(chmod)?.WaitForExit();
                        }
                        catch { }
                    }

                    return outPath;
                }
            }
            catch { }
            return null;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var s = JsonSerializer.Deserialize<UserSettings>(json);

                    if (s != null)
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
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var s = new UserSettings(IpBox!.Text ?? string.Empty, VersionBox!.Text ?? string.Empty, TFT!.IsChecked == true);
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        private async Task ReadStreamAsync(TextReader reader, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();

                    if (line == null) 
                    {
                        break;
                    }
                    
                    AppendLogLine(line + Environment.NewLine);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppendLogLine("[reader error] " + ex.Message + Environment.NewLine);
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

                try { outputCts?.Cancel(); } catch { }

                outputCts = new CancellationTokenSource();
                var token = outputCts.Token;

                var outReader = proc.StandardOutput;
                var errReader = proc.StandardError;

                var outTask = ReadStreamAsync(outReader, token);
                var errTask = ReadStreamAsync(errReader, token);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAll(outTask, errTask);
                    }
                    catch (Exception ex)
                    {
                        AppendLogLine("[reader tasks error] " + ex.Message + Environment.NewLine);
                    }
                });

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
                try { outputCts?.Cancel(); } catch { }

                if (proc != null && !proc.HasExited)
                {
                    proc.Kill(true);
                    try { proc.WaitForExit(3000); } catch { }
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
            if (lw == null || lw.IsClosed) 
            {
                return;
            }

            Dispatcher.UIThread.Post(() => lw.AppendText(s));
        }

        bool IsValidIP(string? s)
        {
            if (string.IsNullOrEmpty(s)) 
            {
                return false;
            }

            var ipnum = "(\\d|[1-9]\\d|1\\d\\d|2[0-4]\\d|25[0-5])";
            var fullExp = $"^{ipnum}\\.{ipnum}\\.{ipnum}\\.{ipnum}$";

            return Regex.IsMatch(s, fullExp);
        }

        bool IsValidVersion(string? s)
        {
            if (string.IsNullOrEmpty(s)) 
            {
                return false;
            }

            var full = new Regex("^1\\.[23]\\d$");

            return full.IsMatch(s);
        }
    }
}



