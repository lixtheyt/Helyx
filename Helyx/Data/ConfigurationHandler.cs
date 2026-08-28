using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Helyx.Shared;
using Color = Spectre.Console.Color;


namespace Helyx.Data
{
    internal static class ConfigurationHandler
    {
        internal static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static bool ConfigExists => File.Exists(GetConfigPath());

        internal static string GetConfigPath()
        {
            string appData =
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string dir = Path.Combine(appData, "Helyx");

            return Path.Combine(dir, "config.json");
        }

        internal static string GetSecretsPath()
        {
            string appData =
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string secretsDir = Path.Combine(appData, "Helyx");

            return Path.Combine(secretsDir, "secrets.json");
        }

        internal static ProjectClass GetProject(Guid guid) =>
            GetConfig().Projects.TryGetValue(guid, out var project)
            ? project
            : throw new KeyNotFoundException($"Project {guid} not found in configuration file.");

        internal static ConfigurationFile GetConfig()
        {
            if (!ConfigExists)
                CreateConfig();

            ConfigurationFile? config;

            try
            {
                config = JsonSerializer.Deserialize<ConfigurationFile>(ReadConfigText(), Options);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                string? kept = GetConfigPath() + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}";

                try
                {
                    File.Move(GetConfigPath(), kept);
                }
                catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                {
                    kept = null;
                }

                UI.Error(Strings.Config_ReadFailed + $"\n\n{ex.Message}" +
                         (kept == null
                             ? string.Empty
                             : $"\n\n[{Color.Grey}]" + string.Format(Strings.Config_KeptAs, Path.GetFileName(kept)) + "[/]"));

                config = null;
            }

            if (config == null)
            {
                CreateConfig();
                return new ConfigurationFile();
            }

            config.Projects ??= [];
            config.CustomStatuses ??= [];
            config.Badges ??= [];
            config.IDEExecutables ??= [];

            foreach (var (guid, project) in config.Projects)
            {
                project.Guid = guid;
                project.GitHubSyncSettings ??= [];
                project.Badges ??= [];
                project.UsedLanguages ??= [];
                project.GitHubName ??= string.Empty;
                project.HelyxName ??= string.Empty;
                project.Path ??= string.Empty;
                project.RootCommit ??= string.Empty;
                project.Notes ??= string.Empty;
            }

            return config;
        }

        private sealed record ConfigText(string Path, DateTime Stamp, long Length, string Text);

        private static ConfigText? _configText;

        private static string ReadConfigText()
        {
            var path = GetConfigPath();

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var info = new FileInfo(path);
                    var cached = _configText;

                    if (cached != null && cached.Path == path &&
                        cached.Stamp == info.LastWriteTimeUtc && cached.Length == info.Length)
                        return cached.Text;

                    var text = File.ReadAllText(path);

                    _configText = new ConfigText(path, info.LastWriteTimeUtc, info.Length, text);

                    return text;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        internal static void CreateConfig()
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Helyx"
            );

            try
            {
                Directory.CreateDirectory(configDir);

                File.WriteAllText(GetConfigPath(),
                    JsonSerializer.Serialize(new ConfigurationFile(), Options));

                _configText = null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    string.Format(Strings.Config_CreateFailed, configDir) + $"\n{ex.Message}", ex);
            }
        }

        internal static void CreateSecrets()
        {
            string secretsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Helyx"
            );

            try
            {
                Directory.CreateDirectory(secretsDir);

                File.WriteAllText(
                    GetSecretsPath(),
                    JsonSerializer.Serialize(
                        new SecretsFile(),
                        Options
                    )
                );
            }
            catch (Exception ex)
            {
                UI.Error(Strings.Secrets_CreateFailed + $"\n\n{ex.Message}");
            }
        }

        private static SecretsFile GetSecrets()
        {
            if (!File.Exists(GetSecretsPath()))
                CreateSecrets();

            try
            {
                return JsonSerializer.Deserialize<SecretsFile>(
                    File.ReadAllText(GetSecretsPath()), Options
                ) ?? new SecretsFile();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                UI.Error(Strings.Secrets_ReadFailed + $"\n\n{ex.Message}");
                return new SecretsFile();
            }
        }

        public static bool EditConfig(ConfigurationFile config)
        {
            var path = GetConfigPath();
            var staged = path + ".tmp";

            try
            {
                File.WriteAllText(staged, JsonSerializer.Serialize(config, Options));

                if (File.Exists(path))
                    File.Replace(staged, path, null);
                else
                    File.Move(staged, path);

                _configText = null;

                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    File.Delete(staged);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                }

                UI.Error(Strings.Config_SaveFailed + $"\n\n{ex.Message}");
                Console.ReadKey();

                return false;
            }
        }

        internal static bool Update(Action<ConfigurationFile> change)
        {
            var config = GetConfig();

            change(config);

            return EditConfig(config);
        }

        internal static bool UpdateProject(Guid guid, Action<ProjectClass> change)
        {
            var config = GetConfig();

            if (!config.Projects.TryGetValue(guid, out var project))
                return false;

            change(project);

            return EditConfig(config);
        }

        internal static bool TryReadNotes(string stored, out string plain)
        {
            plain = stored;

            byte[] data;

            try
            {
                data = Convert.FromBase64String(stored);
            }
            catch (FormatException)
            {
                return true;
            }

            if (data is not [1, 0, 0, 0, ..])
                return true;

            try
            {
                plain = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser));

                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        internal static string? ProtectNotes(string plain)
        {
            try
            {
                return Convert.ToBase64String(
                    ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(plain),
                        null,
                        DataProtectionScope.CurrentUser
                    )
                );
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static void EditSecrets(SecretsFile secrets)
        {
            var path = GetSecretsPath();
            var temporary = path + ".tmp";

            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(secrets, Options));

                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            catch (Exception ex)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                }

                UI.Error(Strings.Secrets_SaveFailed + $"\n\n{ex.Message}");
                Console.ReadKey();
            }
        }

        public static void SaveGitHubAccessToken(string accessToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accessToken))
                    throw new ArgumentException(Strings.Token_Empty, nameof(accessToken));

                byte[] data = Encoding.UTF8.GetBytes(accessToken);

                byte[] encrypted = ProtectedData.Protect(
                    data,
                    null,
                    DataProtectionScope.CurrentUser
                );

                SecretsFile secrets = GetSecrets();

                secrets.GitHubAccessToken =
                    Convert.ToBase64String(encrypted);

                EditSecrets(secrets);
            }
            catch (Exception ex)
            {
                UI.Error(Strings.Token_SaveFailed + $"\n\n{ex.Message}");
            }
        }

        public static string GetGitHubAccessToken()
        {
            try
            {
                SecretsFile secrets = GetSecrets();
            
                if (string.IsNullOrEmpty(secrets.GitHubAccessToken))
                    return string.Empty;

                byte[] data = Convert.FromBase64String(
                    secrets.GitHubAccessToken
                );

                byte[] decrypted = ProtectedData.Unprotect(
                    data,
                    null,
                    DataProtectionScope.CurrentUser
                );

                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return string.Empty;
            }
        }

        public static void ForgetGitHubAccessToken()
        {
            SecretsFile secrets = GetSecrets();

            secrets.GitHubAccessToken = string.Empty;

            EditSecrets(secrets);
        }
    }
}
