using Spectre.Console;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Helyx.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helyx.Data
{
    internal static class HackatimeCalls
    {
        private static readonly HttpClient Client = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const string CurrentUserUrl = "https://hackatime.hackclub.com/api/hackatime/v1/users/current";
        private const string StatsUrl = "https://hackatime.hackclub.com/api/v1/users/my/stats?features=projects";

        internal static async Task AuthorizeHackatime()
        {
            AnsiConsole.Clear();

            var token = AnsiConsole.Prompt(new TextPrompt<string>("Enter your Hackatime token:")
                .AllowEmpty());

            var (name, identifier, error) = await CurrentUser(token);

            AnsiConsole.Clear();

            if (name == null)
                UI.Error("Failed to authorize Hackatime. Please check your token and try again." + $"\n\n{Markup.Escape(error ?? string.Empty)}", "Hackatime Authorization");
            else if (ConfigurationHandler.SaveHackatimeToken(token))
            {
                _hackatimeName = name;
                _hackatimeIdentifier = identifier;

                UI.Info("Successfully authorized Hackatime.", "Hackatime Authorization");
            }

            Console.ReadKey();
            AnsiConsole.Clear();
        }

        private static string? _hackatimeName;

        private static string? _hackatimeIdentifier;

        private static string? _hackatimeSummary;

        internal static void ForgetHackatimeUser()
        {
            _hackatimeName = null;
            _hackatimeIdentifier = null;
            _hackatimeSummary = null;
        }

        internal static async Task<string?> GetUsername()
        {
            if (_hackatimeName == null)
                (_hackatimeName, _hackatimeIdentifier, _) = await CurrentUser(ConfigurationHandler.GetHackatimeToken());

            return _hackatimeName;
        }

        internal static async Task<string?> GetSummary() =>
            _hackatimeSummary ??= await Summary(ConfigurationHandler.GetHackatimeToken());

        internal static async Task<string[]?> GetProjects() =>
            await Projects(ConfigurationHandler.GetHackatimeToken());

        internal static async Task<(ProjectDetails? Details, string? Error)> GetProjectDetails(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return (null, "This project has no Hackatime name set.");

            if (_hackatimeIdentifier == null)
                (_hackatimeName, _hackatimeIdentifier, _) = await CurrentUser(ConfigurationHandler.GetHackatimeToken());

            if (string.IsNullOrWhiteSpace(_hackatimeIdentifier))
                return (null, "Could not resolve the Hackatime account behind the stored key.");

            return await GetJsonAsync<ProjectDetails>(
                $"https://hackatime.hackclub.com/api/v1/users/{Uri.EscapeDataString(_hackatimeIdentifier)}/project/{Uri.EscapeDataString(project)}",
                ConfigurationHandler.GetHackatimeToken());
        }

        private static async Task<(string? Name, string? Identifier, string? Error)> CurrentUser(string token)
        {
            var (document, error) = await GetJsonAsync<JsonDocument>(CurrentUserUrl, token);

            using (document)
            {
                if (document == null)
                    return (null, null, error);

                if (!document.RootElement.TryGetProperty("data", out var data))
                    return (null, null, "Unexpected response from Hackatime.");

                var name = data.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String
                    ? display.GetString()
                    : null;

                var identifier = data.TryGetProperty("username", out var username) && username.ValueKind == JsonValueKind.String
                    ? username.GetString()
                    : data.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                        ? id.GetString()
                        : null;

                return (name, identifier, name == null ? "Unexpected response from Hackatime." : null);
            }
        }

        private static async Task<string?> Summary(string token)
        {
            using var document = (await GetJsonAsync<JsonDocument>(StatsUrl, token)).Result;

            if (document == null || !document.RootElement.TryGetProperty("data", out var data))
                return null;

            var span = TimeSpan.FromSeconds(data.TryGetProperty("total_seconds", out var total) && total.ValueKind == JsonValueKind.Number
                ? total.GetDouble()
                : 0);

            var projects = data.TryGetProperty("projects", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.GetArrayLength()
                : 0;

            var streak = data.TryGetProperty("streak", out var days) && days.ValueKind == JsonValueKind.Number && days.TryGetInt32(out var value)
                ? value
                : 0;

            return string.Format(Strings.GH_Wf_Hours, (int)span.TotalHours, span.Minutes)
                + $" across {projects} project{(projects == 1 ? string.Empty : "s")}"
                + (streak > 0 ? $" · {streak} day streak" : string.Empty);
        }

        private static async Task<string[]?> Projects(string token)
        {
            using var document = (await GetJsonAsync<JsonDocument>(StatsUrl, token)).Result;

            if (document == null || !document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("projects", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;

            return [.. list.EnumerateArray()
                .Select(x => x.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
                .OfType<string>()
                .Where(x => !string.IsNullOrWhiteSpace(x))];
        }

        private static async Task<(T? Result, string? Error)> GetJsonAsync<T>(string url, string token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.UserAgent.ParseAdd("Helyx");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await Client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return (default, $"{(int)response.StatusCode} {response.ReasonPhrase}");

                return (await response.Content.ReadFromJsonAsync<T>(), null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
            {
                return (default, ex.Message);
            }
        }
    }

    internal class ProjectDetails
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("total_seconds")]
        public double TotalSeconds { get; init; }

        [JsonPropertyName("languages")]
        public List<string>? Languages { get; init; }

        [JsonPropertyName("repo_url")]
        public string? RepoUrl { get; init; }

        [JsonPropertyName("total_heartbeats")]
        public int TotalHeartbeats { get; init; }

        [JsonPropertyName("first_heartbeat")]
        public DateTimeOffset? FirstHeartbeat { get; init; }

        [JsonPropertyName("last_heartbeat")]
        public DateTimeOffset? LastHeartbeat { get; init; }

        [JsonPropertyName("most_recent_heartbeat")]
        public DateTimeOffset? MostRecentHeartbeat { get; init; }

        [JsonPropertyName("archived")]
        public bool Archived { get; init; }
    }
}