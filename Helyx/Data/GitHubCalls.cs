using System.IO.Compression;
using System.Text.Json.Serialization;
using LibGit2Sharp;
using Spectre.Console;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Helyx.Projects;
using TextCopy;
using Helyx.Shared;

namespace Helyx.Data
{
    internal static class GitHubCalls
    {
        private static readonly HttpClient Client = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        internal static Identity MainIdentity(Configuration repoConfig)
        {
            var config = ConfigurationHandler.GetConfig();

            var email = config.PreferredIdentity == PreferredIdentity.Git
                ? repoConfig.Get<string>("user.email")?.Value
                : GetUserGitHubInfo(InfoType.Email).GetAwaiter().GetResult();

            var username = config.PreferredIdentity == PreferredIdentity.Git
                ? repoConfig.Get<string>("user.name")?.Value
                : GetCachedUsername().GetAwaiter().GetResult();

            email ??= repoConfig.Get<string>("user.email")?.Value;
            username ??= repoConfig.Get<string>("user.name")?.Value;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException(
                    Strings.GitHub_NoIdentity + "\n" + Strings.GitHub_NoIdentity_Hint);

            return new Identity(username, email);
        }

        internal static bool IsAuthorizedWithGitHub() =>
            !string.IsNullOrEmpty(ConfigurationHandler.GetGitHubAccessToken());

        private static readonly string[] RequiredScopes =
        [
            "repo",
            "workflow",
            "read:user",
            "user:email"
        ];

        private static readonly Dictionary<string, string[]> ScopeImplications =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["repo"] = ["public_repo", "repo:status", "repo_deployment", "repo:invite", "security_events"],
                ["user"] = ["read:user", "user:email", "user:follow"],
                ["admin:org"] = ["write:org", "read:org"],
                ["write:org"] = ["read:org"]
            };

        private static volatile string[]? _missingScopes;

        internal static volatile bool TokenRejected;

        private static string RequestedScopeString => string.Join(' ', RequiredScopes);

        internal static IReadOnlyList<string> MissingScopes => _missingScopes ?? [];

        internal static bool HasOutdatedScopes => _missingScopes is { Length: > 0 };

        internal static void ResetScopeState() => _missingScopes = null;

        private static bool IsScopeSatisfied(string required, HashSet<string> granted) =>
            granted.Contains(required) ||
            ScopeImplications.Any(pair =>
                granted.Contains(pair.Key) &&
                pair.Value.Contains(required, StringComparer.OrdinalIgnoreCase));

        private static void EvaluateGrantedScopes(string? grantedScopes)
        {
            if (grantedScopes is null)
            {
                ResetScopeState();
                return;
            }

            var granted = grantedScopes
                .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _missingScopes = RequiredScopes
                .Where(scope => !IsScopeSatisfied(scope, granted))
                .ToArray();
        }

        internal static async Task AuthorizeGitHub()
        {
            const string clientId = "Ov23li2Qtd1KypM5WXgm";
            using var client = new HttpClient();

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("scope", RequestedScopeString)
            ]);

            HttpResponseMessage response;
            string json;
            GitHubDeviceAuthResponse? objResponse;

            try
            {
                response = await client.PostAsync("https://github.com/login/device/code", content);
                json = await response.Content.ReadAsStringAsync();
                objResponse = JsonSerializer.Deserialize<GitHubDeviceAuthResponse>(json);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                UI.Error(Strings.GitHub_Unreachable + $"\n{Markup.Escape(ex.Message)}", Strings.GitHub_AuthFailed_Title);
                Console.ReadKey();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                UI.Error(Strings.GitHub_ReturnedError + $"\n{Markup.Escape(json)}", Strings.GitHub_AuthFailed_Title);
                Console.ReadKey();
                return;
            }

            if (objResponse == null ||
                string.IsNullOrEmpty(objResponse.VerificationUri) ||
                string.IsNullOrEmpty(objResponse.UserCode) ||
                string.IsNullOrEmpty(objResponse.DeviceCode))
            {
                UI.Error(Strings.GitHub_IncompleteResponse, Strings.GitHub_AuthFailed_Title);
                Console.ReadKey();
                return;
            }

            UI.Info(string.Format(Strings.GitHub_GoToAndEnter, UI.Link(objResponse.VerificationUri, objResponse.VerificationUri)) +
                    $"\n[italic grey]{Strings.Common_CtrlClick}[/]\n\n[bold yellow]{objResponse.UserCode}[/]\n[italic grey]{Strings.GitHub_CodeCopied}[/]", Strings.Settings_GitHubAuthorization);

            try
            {
                await ClipboardService.SetTextAsync(objResponse.UserCode);
            }
            catch (Exception ex)
            {
                UI.Warning(Strings.GitHub_ClipboardFailed + $"\n{Markup.Escape(ex.Message)}");
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = objResponse.VerificationUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UI.Warning(Strings.GitHub_BrowserFailed + $"\n{Markup.Escape(ex.Message)}");
            }

            int pollInterval = Math.Max(objResponse.Interval, 5);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(objResponse.ExpiresIn > 0 ? objResponse.ExpiresIn : 900);

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Strings.GitHub_Waiting, async ctx =>
                {
                    while (true)
                    {
                        if (DateTimeOffset.UtcNow >= deadline)
                        {
                            AnsiConsole.Clear();
                            UI.Error(Strings.GitHub_CodeExpired, Strings.GitHub_AuthFailed_Title);
                            return;
                        }

                        await Task.Delay(pollInterval * 1000);

                        var tokenContent = new FormUrlEncodedContent([
                            new KeyValuePair<string,string>("client_id", clientId),
                                new KeyValuePair<string,string>("device_code", objResponse.DeviceCode),
                                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
                        ]);

                        HttpResponseMessage tokenResponse;
                        string tokenJson;
                        GitHubTokenResponse? token;

                        try
                        {
                            tokenResponse = await client.PostAsync(
                                "https://github.com/login/oauth/access_token",
                                tokenContent
                            );

                            tokenJson = await tokenResponse.Content.ReadAsStringAsync();

                            token = JsonSerializer.Deserialize<GitHubTokenResponse>(tokenJson);
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                        {
                            AnsiConsole.Clear();
                            UI.Error(Strings.GitHub_Unreachable + $"\n{Markup.Escape(ex.Message)}", Strings.GitHub_AuthFailed_Title);
                            return;
                        }

                        switch (token?.Error)
                        {
                            case "authorization_pending":
                                continue;

                            case "slow_down":
                                pollInterval += 5;
                                continue;
                        }

                        if (tokenResponse.IsSuccessStatusCode && token is { AccessToken: not null and not "" })
                        {
                            AnsiConsole.Clear();

                            UI.Success(Strings.GitHub_AuthSuccess, Strings.GitHub_Authorized_Title);

                            ConfigurationHandler.SaveGitHubAccessToken(token.AccessToken);
                            EvaluateGrantedScopes(token.Scope);
                            ForgetGitHubUsername();
                            return;
                        }

                        UI.Error(Strings.GitHub_ReturnedError + $"\n{Markup.Escape(tokenJson)}", Strings.GitHub_AuthFailed_Title);
                        return;
                    }
                });
        }

        internal static async Task<string?> GetUserGitHubInfo(InfoType infoType)
        {
            var url = infoType == InfoType.Username
                ? "https://api.github.com/user"
                : "https://api.github.com/user/emails";

            using var document = await GetJsonAsync<JsonDocument>(url);

            if (document == null)
                return null;

            switch (infoType)
            {
                case InfoType.Username:
                    return document.RootElement.TryGetProperty("login", out var login)
                        ? login.GetString()
                        : null;
                case InfoType.Email:
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Array)
                            return null;

                        foreach (var entry in document.RootElement.EnumerateArray())
                        {
                            if (entry.TryGetProperty("primary", out var primary) &&
                                primary.GetBoolean() &&
                                entry.TryGetProperty("email", out var email))
                            {
                                return email.GetString();
                            }
                        }

                        return null;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(infoType), infoType, null);
            }
        }

        internal static async Task CheckGitHubTokenAndResolve()
        {
            var accessToken = ConfigurationHandler.GetGitHubAccessToken();

            if (string.IsNullOrEmpty(accessToken))
            {
                ResetScopeState();
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");

            request.Headers.UserAgent.ParseAdd("Helyx");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using var response = await Client.SendAsync(request);

                if (response.StatusCode is not System.Net.HttpStatusCode.Unauthorized)
                {
                    EvaluateGrantedScopes(
                        response.Headers.TryGetValues("X-OAuth-Scopes", out var scopeHeader)
                            ? string.Join(',', scopeHeader)
                            : null
                    );

                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return;
            }

            ResetScopeState();
            ForgetGitHubUsername();

            TokenRejected = true;
        }

        internal enum RepoLookup
        {
            Found,
            NotFound,
            CouldNotCheck
        }

        internal static string DescribeLookup(RepoLookup result) => result switch
        {
            RepoLookup.NotFound =>
                Strings.GitHub_RepoNotFound + "\n" + Strings.GitHub_RepoNotFound_Hint,
            RepoLookup.CouldNotCheck =>
                Strings.GitHub_CouldNotCheck + "\n" + Strings.GitHub_CouldNotCheck_Hint,
            _ => string.Empty
        };

        internal static async Task<RepoLookup> RepoExistsOnUsersGitHubProfile(Guid guid)
        {
            var username = await GetCachedUsername();
            var repoName = ConfigurationHandler.GetProject(guid).GitHubName;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(repoName))
                return RepoLookup.CouldNotCheck;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{username}/{repoName}");

                request.Headers.UserAgent.ParseAdd("Helyx");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigurationHandler.GetGitHubAccessToken());

                using var response = await Client.SendAsync(request);

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.NotFound => RepoLookup.NotFound,
                    _ when response.IsSuccessStatusCode => RepoLookup.Found,
                    _ => RepoLookup.CouldNotCheck
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return RepoLookup.CouldNotCheck;
            }
        }

        internal static async Task<GitHubRepository?> GetGitHubRepoStats(Guid guid, bool withActivity = true)
        {
            if (!HasGitHubName(guid))
                return null;

            var project = ConfigurationHandler.GetProject(guid);

            if (ProjectsMenu.GitHubRepositories.TryGetValue(guid, out var cachedRepo) &&
                string.Equals(cachedRepo?.Name, project.GitHubName, StringComparison.OrdinalIgnoreCase))
                return cachedRepo;

            var username = await GetCachedUsername();

            if (string.IsNullOrEmpty(username))
                return null;

            var repository = await GetJsonAsync<GitHubRepository>($"https://api.github.com/repos/{username}/{project.GitHubName}");

            if (repository == null)
                return null;

            var languages = await GetJsonAsync<Dictionary<string, long>>($"https://api.github.com/repos/{username}/{project.GitHubName}/languages");

            long totalBytes = languages?.Values.Sum() ?? 0;

            if (languages == null)
                repository.FailedParts.Add(Strings.GitHub_Part_Languages);

            if (languages != null && totalBytes > 0)
            {
                Dictionary<string, double> languagesPercentage = new();

                foreach (var language in languages.OrderByDescending(x => x.Value))
                {
                    double percentage = (double)language.Value / totalBytes * 100;

                    languagesPercentage.Add(language.Key, percentage);
                }

                repository.Languages = languagesPercentage;
            }

            if (withActivity)
            {
                var since = DateTime.UtcNow.AddYears(-1).Date.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var activity = new SortedSet<DateOnly>();

                for (int page = 1; ; page++)
                {
                    using var commits = await GetJsonAsync<JsonDocument>($"https://api.github.com/repos/{username}/{project.GitHubName}/commits?since={since}&per_page=100&page={page}");

                    if (commits == null || commits.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        repository.FailedParts.Add(Strings.GitHub_Part_CommitActivity);
                        break;
                    }

                    var count = 0;

                    foreach (var x in commits.RootElement.EnumerateArray())
                    {
                        count++;
                        activity.Add(DateOnly.FromDateTime(
                            x.GetProperty("commit")
                                .GetProperty("author")
                                .GetProperty("date")
                                .GetDateTime()
                        ));
                    }

                    if (count < 100) break;
                }

                repository.ActivityDays = activity.ToList();
            }

            using var topicsJson = await GetJsonAsync<JsonDocument>($"https://api.github.com/repos/{username}/{project.GitHubName}/topics");

            if (topicsJson != null && topicsJson.RootElement.TryGetProperty("names", out var names))
                repository.Topics = names
                    .EnumerateArray()
                    .Select(x => x.GetString()!)
                    .ToList();
            else
                repository.FailedParts.Add(Strings.GitHub_Part_Topics);

            if (repository.FailedParts.Count == 0)
                ProjectsMenu.GitHubRepositories[guid] = repository;

            return repository;
        }

        internal static async Task<List<GitHubRepository>?> GetAllRepos()
        {
            const string url = "https://api.github.com/user/repos?affiliation=owner&sort=updated&per_page=100&page=";

            List<GitHubRepository> repos = new();

            for (int page = 1; ; page++)
            {
                var batch = await GetJsonAsync<GitHubRepository[]>(url + page);

                if (batch == null)
                    return null;

                repos.AddRange(batch);

                if (batch.Length < 100)
                    return repos;
            }
        }

        internal static async Task<List<GitHubIssue>?> GetAllIssues(Guid guid)
        {
            var url = $"{await RepoUrl(guid)}/issues?state=all&per_page=100&page=";

            List<GitHubIssue> issues = new();

            for (int page = 1; ; page++)
            {
                var batch = await GetJsonAsync<GitHubIssue[]>(url + page);

                if (batch == null)
                    return null;

                issues.AddRange(batch.Where(x => x.PullRequest is null));

                if (batch.Length < 100)
                    return issues;
            }
        }

        internal static async Task<List<GitHubPullRequest>?> GetAllPullRequests(Guid guid)
        {
            var url = $"{await RepoUrl(guid)}/pulls?state=all&per_page=100&page=";

            List<GitHubPullRequest> pulls = new();

            for (int page = 1; ; page++)
            {
                var batch = await GetJsonAsync<GitHubPullRequest[]>(url + page);

                if (batch == null)
                    return null;

                pulls.AddRange(batch);

                if (batch.Length < 100)
                    return pulls;
            }
        }

        internal static async Task<List<GitHubComment>?> GetAllComments(Guid guid, int issueNumber)
        {
            var url = $"{await RepoUrl(guid)}/issues/{issueNumber}/comments?per_page=100&page=";

            List<GitHubComment> comments = new();

            for (int page = 1; ; page++)
            {
                var batch = await GetJsonAsync<GitHubComment[]>(url + page);

                if (batch == null)
                    return null;

                comments.AddRange(batch);

                if (batch.Length < 100)
                    return comments;
            }
        }

        internal static async Task<List<GitHubEvent>?> GetAllEvents(Guid guid, int issueNumber)
        {
            var url = $"{await RepoUrl(guid)}/issues/{issueNumber}/events?per_page=100&page=";

            List<GitHubEvent> events = new();

            for (int page = 1; ; page++)
            {
                var batch = await GetJsonAsync<GitHubEvent[]>(url + page);

                if (batch == null)
                    return null;

                events.AddRange(batch);

                if (batch.Length < 100)
                    return events;
            }
        }

        internal static async Task<(GitHubComment? Result, string? Error)> CommentOnIssue(Guid guid, int issueNumber, string content) =>
            await SendJsonAsync<GitHubComment>(HttpMethod.Post,
                $"{await RepoUrl(guid)}/issues/{issueNumber}/comments", new { body = content });

        internal static async Task<(GitHubComment? Result, string? Error)> EditIssueComment(Guid guid, long commentId, string content) =>
            await SendJsonAsync<GitHubComment>(HttpMethod.Patch,
                $"{await RepoUrl(guid)}/issues/comments/{commentId}", new { body = content });

        internal static async Task<(bool Result, string? Error)> DeleteIssueComment(Guid guid, long commentId) =>
            await SendJsonAsync<bool>(HttpMethod.Delete,
                $"{await RepoUrl(guid)}/issues/comments/{commentId}");

        internal static async Task<(GitHubIssue? Result, string? Error)> CloseIssue(Guid guid, int issueNumber, string stateReason) =>
            await SendJsonAsync<GitHubIssue>(HttpMethod.Patch,
                $"{await RepoUrl(guid)}/issues/{issueNumber}", new { state = "closed", state_reason = stateReason });

        internal static async Task<(GitHubIssue? Result, string? Error)> ReopenIssue(Guid guid, int issueNumber) =>
            await SendJsonAsync<GitHubIssue>(HttpMethod.Patch,
                $"{await RepoUrl(guid)}/issues/{issueNumber}", new { state = "open" });

        internal static async Task<(GitHubIssue? Result, string? Error)> EditIssue(Guid guid, int issueNumber, string title, string content) =>
            await SendJsonAsync<GitHubIssue>(HttpMethod.Patch,
                $"{await RepoUrl(guid)}/issues/{issueNumber}", new { title, body = content });

        internal static async Task<(bool Result, string? Error)> LockIssue(Guid guid, int issueNumber, string lockReason) =>
            await SendJsonAsync<bool>(HttpMethod.Put,
                $"{await RepoUrl(guid)}/issues/{issueNumber}/lock", new { lock_reason = lockReason });

        internal static async Task<(bool Result, string? Error)> UnlockIssue(Guid guid, int issueNumber) =>
            await SendJsonAsync<bool>(HttpMethod.Delete,
                $"{await RepoUrl(guid)}/issues/{issueNumber}/lock");

        internal static async Task<(GitHubIssue? Result, string? Error)> CreateIssue(Guid guid, string title, string content) =>
            await SendJsonAsync<GitHubIssue>(HttpMethod.Post,
                $"{await RepoUrl(guid)}/issues", new { title, body = content });

        private static string? _gitHubUsername;

        internal static void ForgetGitHubUsername() => _gitHubUsername = null;

        internal static async Task<string?> GetCachedUsername() =>
            _gitHubUsername ??= await GetUserGitHubInfo(InfoType.Username);

        internal static async Task<string> RepoUrl(Guid guid) =>
            $"https://api.github.com/repos/{await GetCachedUsername()}/{ConfigurationHandler.GetProject(guid).GitHubName}";

        internal static async Task<GitHubPullRequest?> GetPullRequest(Guid guid, int number) =>
            await GetJsonAsync<GitHubPullRequest>($"{await RepoUrl(guid)}/pulls/{number}");

        internal static async Task<List<GitHubPullRequestReview>?> GetPullRequestReviews(Guid guid, int number)
        {
            var url = $"{await RepoUrl(guid)}/pulls/{number}/reviews?per_page=100&page=";

            List<GitHubPullRequestReview> reviews = new();

            for (int page = 1; ; page++)
            {
                var batch = await GetJsonAsync<GitHubPullRequestReview[]>(url + page);

                if (batch == null)
                    return null;

                reviews.AddRange(batch);

                if (batch.Length < 100)
                    return reviews;
            }
        }
           
        internal static async Task<(GitHubMergeResult? Result, string? Error)> MergePullRequest(Guid guid, int number, string method) =>
            await SendJsonAsync<GitHubMergeResult>(HttpMethod.Put, $"{await RepoUrl(guid)}/pulls/{number}/merge", new { merge_method = method });

        internal static async Task<(GitHubPullRequest? Result, string? Error)> ClosePullRequest(Guid guid, int number) =>
            await SendJsonAsync<GitHubPullRequest>(HttpMethod.Patch, $"{await RepoUrl(guid)}/pulls/{number}", new { state = "closed" });

        internal static async Task<(GitHubPullRequest? Result, string? Error)> ReopenPullRequest(Guid guid, int number) =>
            await SendJsonAsync<GitHubPullRequest>(HttpMethod.Patch, $"{await RepoUrl(guid)}/pulls/{number}", new { state = "open" });

        internal static async Task<(GitHubPullRequest? Result, string? Error)> EditPullRequest(Guid guid, int number, string title, string content) =>
            await SendJsonAsync<GitHubPullRequest>(HttpMethod.Patch, $"{await RepoUrl(guid)}/pulls/{number}", new { title, body = content });

        internal static async Task<(GitHubPullRequestReview? Result, string? Error)> ReviewPullRequest(Guid guid, int number, string reviewEvent, string content) =>
            await SendJsonAsync<GitHubPullRequestReview>(HttpMethod.Post, $"{await RepoUrl(guid)}/pulls/{number}/reviews", new { @event = reviewEvent, body = content });

        internal static async Task<List<GitHubWorkflow>?> GetWorkflows(Guid guid) =>
            (await GetJsonAsync<GitHubWorkflowList>($"{await RepoUrl(guid)}/actions/workflows?per_page=100"))?.Workflows;

        internal static async Task<List<GitHubWorkflowRun>?> GetWorkflowRuns(Guid guid, long workflowId = 0)
        {
            var scope = workflowId == 0
                ? "runs"
                : $"workflows/{workflowId}/runs";

            return (await GetJsonAsync<GitHubWorkflowRunList>(
                $"{await RepoUrl(guid)}/actions/{scope}?per_page=50"))?.Runs;
        }

        internal static async Task<GitHubWorkflowRun?> GetWorkflowRun(Guid guid, long runId) =>
            await GetJsonAsync<GitHubWorkflowRun>($"{await RepoUrl(guid)}/actions/runs/{runId}");

        internal static async Task<List<GitHubWorkflowJob>?> GetRunJobs(Guid guid, long runId) =>
            (await GetJsonAsync<GitHubWorkflowJobList>(
                $"{await RepoUrl(guid)}/actions/runs/{runId}/jobs?per_page=100"))?.Jobs;

        internal static async Task<(bool Result, string? Error)> RerunRun(Guid guid, long runId) =>
            await SendJsonAsync<bool>(HttpMethod.Post, $"{await RepoUrl(guid)}/actions/runs/{runId}/rerun");

        internal static async Task<(bool Result, string? Error)> RerunFailedJobs(Guid guid, long runId) =>
            await SendJsonAsync<bool>(HttpMethod.Post, $"{await RepoUrl(guid)}/actions/runs/{runId}/rerun-failed-jobs");

        internal static async Task<(bool Result, string? Error)> CancelRun(Guid guid, long runId) =>
            await SendJsonAsync<bool>(HttpMethod.Post, $"{await RepoUrl(guid)}/actions/runs/{runId}/cancel");

        internal static async Task<(bool Result, string? Error)> DeleteRun(Guid guid, long runId) =>
            await SendJsonAsync<bool>(HttpMethod.Delete, $"{await RepoUrl(guid)}/actions/runs/{runId}");

        internal static async Task<(bool Result, string? Error)> DispatchWorkflow(Guid guid, long workflowId, string reference) =>
            await SendJsonAsync<bool>(HttpMethod.Post,
                $"{await RepoUrl(guid)}/actions/workflows/{workflowId}/dispatches", new { @ref = reference });

        internal static async Task<Dictionary<string, string>?> GetRunLogs(Guid guid, long runId)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{await RepoUrl(guid)}/actions/runs/{runId}/logs");

                request.Headers.UserAgent.ParseAdd("Helyx");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigurationHandler.GetGitHubAccessToken());

                using var response = await Client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return null;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                Dictionary<string, string> logs = new(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries.Where(x => x.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    using var reader = new StreamReader(entry.Open());
                    logs[entry.FullName] = await reader.ReadToEndAsync();
                }

                return logs;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static async Task<string?> GetGitHubRepoLatestVersion()
        {
            using var json = await GetJsonAsync<JsonDocument>("https://api.github.com/repos/LixTheYT/Helyx/releases/latest");

            if (json == null)
                return null;

            return json.RootElement.TryGetProperty("tag_name", out var tagName)
                ? tagName.GetString()
                : null;
        }

        internal static bool HasGitHubName(Guid guid)
            => !string.IsNullOrWhiteSpace(ConfigurationHandler.GetProject(guid).GitHubName);

        internal static bool EnsureGitHubRepoConnection(Guid guid, string title)
        {
            if (HasGitHubName(guid))
                return true;

            UI.Warning(Strings.GitHub_NotLinked, title);

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.GitHub_AssignNow)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return false;

            List<GitHubRepository>? repos = null;

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(Strings.GitHub_LoadingRepos, ctx =>
                    repos = GetAllRepos().GetAwaiter().GetResult()
                );

            if (repos == null)
            {
                UI.Error(Strings.GitHub_UnreachableSettings + "\n" + Strings.GitHub_CheckConnection, title);
                Console.ReadKey();
                return false;
            }

            if (repos.Count == 0)
            {
                UI.Info(Strings.GitHub_NoRepos, title);
                Console.ReadKey();
                return false;
            }

            repos.Add(null!);

            var repo = AnsiConsole.Prompt(
                new SelectionPrompt<GitHubRepository>()
                .Title(Strings.GitHub_SelectRepo)
                .AddChoices(repos)
                .UseConverter(x => x switch
                {
                    null => $"[Red3_1]{Strings.Common_Back}[/]",
                    _ => x.Name ?? string.Empty
                }));

            if (repo == null)
                return false;

            var config = ConfigurationHandler.GetConfig();
            var project = config.Projects[guid];

            project.GitHubName = repo.Name ?? string.Empty;

            config.Projects[guid] = project;
            ConfigurationHandler.EditConfig(config);

            AnsiConsole.Clear();

            return true;
        }

        private static async Task<(T? Result, string? Error)> SendJsonAsync<T>(HttpMethod method, string url, object? body = null)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);

                request.Headers.UserAgent.ParseAdd("Helyx");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigurationHandler.GetGitHubAccessToken());

                if (body != null)
                    request.Content = JsonContent.Create(body);

                using var response = await Client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return (default, await DescribeFailure(response));

                return typeof(T) == typeof(bool)
                    ? ((T)(object)true, null)
                    : (await response.Content.ReadFromJsonAsync<T>(), null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return (default, ex.Message);
            }
        }

        private static async Task<string> DescribeFailure(HttpResponseMessage response)
        {
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                return document.RootElement.TryGetProperty("message", out var message)
                    ? message.GetString() ?? response.ReasonPhrase ?? Strings.GitHub_UnknownError
                    : $"{(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
            {
                return $"{(int)response.StatusCode} {response.ReasonPhrase}";
            }
        }

        private static async Task<T?> GetJsonAsync<T>(string url)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.UserAgent.ParseAdd("Helyx");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigurationHandler.GetGitHubAccessToken());

                using var response = await Client.SendAsync(request);

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<T>();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return default;
            }
        }

        internal enum InfoType
        {
            Username,
            Email
        }
    }

    internal class GitHubDeviceAuthResponse
    {
        [JsonPropertyName("device_code")]
        public string? DeviceCode { get; init; }

        [JsonPropertyName("user_code")]
        public string? UserCode { get; init; }

        [JsonPropertyName("verification_uri")]
        public string? VerificationUri { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("interval")]
        public int Interval { get; init; }
    }

    internal class GitHubTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }

    internal class GitHubRepository
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("private")]
        public bool Private { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; init; }

        [JsonPropertyName("stargazers_count")]
        public int Stars { get; init; }

        [JsonPropertyName("forks_count")]
        public int Forks { get; init; }

        [JsonPropertyName("watchers_count")]
        public int Watchers { get; init; }

        public Dictionary<string, double>? Languages { get; set; }

        public List<DateOnly>? ActivityDays { get; set; }

        public List<string>? Topics { get; set; }

        [JsonIgnore]
        public List<string> FailedParts { get; } = new();
    }

    internal class GitHubIssue
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("state_reason")]
        public string? StateReason { get; set; }

        [JsonPropertyName("locked")]
        public bool Locked { get; set; }

        [JsonPropertyName("comments")]
        public int Comments { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("closed_at")]
        public DateTimeOffset? ClosedAt { get; set; }

        [JsonPropertyName("user")]
        public GitHubUser? User { get; set; }

        [JsonPropertyName("author_association")]
        public string? AuthorAssociation { get; set; }

        [JsonPropertyName("assignees")]
        public List<GitHubUser> Assignees { get; set; } = [];

        [JsonPropertyName("labels")]
        public List<GitHubLabel> Labels { get; set; } = [];

        [JsonPropertyName("milestone")]
        public GitHubMilestone? Milestone { get; set; }

        [JsonPropertyName("pull_request")]
        public GitHubPullRequestReference? PullRequest { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }

    internal class GitHubComment
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("user")]
        public GitHubUser? User { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("author_association")]
        public string? AuthorAssociation { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("event")]
        public string? Event { get; set; }

        [JsonPropertyName("actor")]
        public GitHubUser? Actor { get; set; }

        [JsonPropertyName("label")]
        public GitHubLabel? Label { get; set; }

        [JsonPropertyName("assignee")]
        public GitHubUser? Assignee { get; set; }
    }

    internal class GitHubEvent
    {
        [JsonPropertyName("event")]
        public string? Event { get; set; }

        [JsonPropertyName("actor")]
        public GitHubUser? Actor { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("label")]
        public GitHubLabel? Label { get; set; }

        [JsonPropertyName("assignee")]
        public GitHubUser? Assignee { get; set; }

        [JsonPropertyName("milestone")]
        public GitHubMilestone? Milestone { get; set; }

        [JsonPropertyName("rename")]
        public GitHubRename? Rename { get; set; }

        [JsonPropertyName("lock_reason")]
        public string? LockReason { get; set; }
    }

    internal class GitHubRename
    {
        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }
    }

    internal class GitHubUser
    {
        [JsonPropertyName("login")]
        public string? Login { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }

    internal class GitHubLabel
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    internal class GitHubMilestone
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("due_on")]
        public DateTimeOffset? DueOn { get; set; }
    }

    internal class GitHubPullRequestReference
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    internal class GitHubPullRequest
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("locked")]
        public bool Locked { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("user")]
        public GitHubUser? User { get; set; }

        [JsonPropertyName("assignees")]
        public List<GitHubUser> Assignees { get; set; } = [];

        [JsonPropertyName("requested_reviewers")]
        public List<GitHubUser> RequestedReviewers { get; set; } = [];

        [JsonPropertyName("labels")]
        public List<GitHubLabel> Labels { get; set; } = [];

        [JsonPropertyName("milestone")]
        public GitHubMilestone? Milestone { get; set; }

        [JsonPropertyName("head")]
        public GitHubPullRequestBranch? Head { get; set; }

        [JsonPropertyName("base")]
        public GitHubPullRequestBranch? Base { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("closed_at")]
        public DateTimeOffset? ClosedAt { get; set; }

        [JsonPropertyName("merged_at")]
        public DateTimeOffset? MergedAt { get; set; }

        [JsonPropertyName("merged")]
        public bool Merged { get; set; }

        [JsonPropertyName("mergeable")]
        public bool? Mergeable { get; set; }

        [JsonPropertyName("mergeable_state")]
        public string? MergeableState { get; set; }

        [JsonPropertyName("rebaseable")]
        public bool? Rebaseable { get; set; }

        [JsonPropertyName("commits")]
        public int Commits { get; set; }

        [JsonPropertyName("additions")]
        public int Additions { get; set; }

        [JsonPropertyName("deletions")]
        public int Deletions { get; set; }

        [JsonPropertyName("changed_files")]
        public int ChangedFiles { get; set; }

        [JsonPropertyName("comments")]
        public int Comments { get; set; }

        [JsonPropertyName("review_comments")]
        public int ReviewComments { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("author_association")]
        public string? AuthorAssociation { get; set; }
    }

    internal class GitHubPullRequestBranch
    {
        [JsonPropertyName("ref")]
        public string? Ref { get; set; }

        [JsonPropertyName("sha")]
        public string? Sha { get; set; }

        [JsonPropertyName("repo")]
        public GitHubPRRepository? Repository { get; set; }
    }

    internal class GitHubPRRepository
    {
        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }
    }

    internal class GitHubPullRequestReview
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("user")]
        public GitHubUser? User { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("submitted_at")]
        public DateTimeOffset? SubmittedAt { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("author_association")]
        public string? AuthorAssociation { get; set; }
    }

    internal class GitHubMergeResult
    {
        [JsonPropertyName("sha")]
        public string? Sha { get; set; }

        [JsonPropertyName("merged")]
        public bool Merged { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    internal class GitHubWorkflowList
    {
        [JsonPropertyName("workflows")]
        public List<GitHubWorkflow> Workflows { get; set; } = [];
    }

    internal class GitHubWorkflow
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }

    internal class GitHubWorkflowRunList
    {
        [JsonPropertyName("workflow_runs")]
        public List<GitHubWorkflowRun> Runs { get; set; } = [];
    }

    internal class GitHubWorkflowRun
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("run_number")]
        public int RunNumber { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("workflow_id")]
        public long WorkflowId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("conclusion")]
        public string? Conclusion { get; set; }

        [JsonPropertyName("event")]
        public string? Event { get; set; }

        [JsonPropertyName("head_branch")]
        public string? HeadBranch { get; set; }

        [JsonPropertyName("head_sha")]
        public string? HeadSha { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("actor")]
        public GitHubUser? Actor { get; set; }

        [JsonPropertyName("run_started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    internal class GitHubWorkflowJobList
    {
        [JsonPropertyName("jobs")]
        public List<GitHubWorkflowJob> Jobs { get; set; } = [];
    }

    internal class GitHubWorkflowJob
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("conclusion")]
        public string? Conclusion { get; set; }

        [JsonPropertyName("started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTimeOffset? CompletedAt { get; set; }

        [JsonPropertyName("steps")]
        public List<GitHubWorkflowStep> Steps { get; set; } = [];
    }

    internal class GitHubWorkflowStep
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("conclusion")]
        public string? Conclusion { get; set; }

        [JsonPropertyName("started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
