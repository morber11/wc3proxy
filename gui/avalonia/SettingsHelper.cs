using System.Text.Json;

namespace wc3proxy.avalonia
{
    public sealed class SettingsHelper(string path)
    {
        public const string SettingsFileName = "wc3proxy-gui-settings.json";

        public record UserSettings(string Ip, string Version, bool IsTft);

        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        private readonly string _path = path ?? throw new ArgumentNullException(nameof(path));
        public string Path => _path;

        public UserSettings? Load()
        {
            if (!File.Exists(_path)) return null;

            var json = File.ReadAllText(_path);

            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize<UserSettings>(json, s_jsonOptions);
        }

        public void Save(UserSettings s)
        {
            ArgumentNullException.ThrowIfNull(s);

            var json = JsonSerializer.Serialize(s, s_jsonOptions);
            var tempPath = _path + ".tmp";

            var writeSucceeded = false;
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _path, overwrite: true);
                writeSucceeded = true;
            }
            finally
            {
                if (!writeSucceeded && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        public void EnsureExists(UserSettings s)
        {
            if (!File.Exists(_path)) Save(s);
        }
    }
}
