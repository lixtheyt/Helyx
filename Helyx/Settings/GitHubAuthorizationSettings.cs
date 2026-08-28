using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class GitHubAuthorizationSettings
    {
        internal static void Display()
        {
            while (true)
            {
                var authorized = GitHubCalls.IsAuthorizedWithGitHub();

                List<Action> choices = [];

                if (authorized)
                {
                    if (GitHubCalls.HasOutdatedScopes)
                        choices.Add(Action.ReauthorizeWithGitHub);

                    choices.Add(Action.UnauthorizeFromGitHub);
                }
                else
                {
                    choices.Add(Action.AuthorizeWithGitHub);
                }

                choices.Add(Action.Back);

                if (authorized)
                {
                    UI.Info(GitHubCalls.GetUserGitHubInfo(GitHubCalls.InfoType.Username).GetAwaiter().GetResult() ?? $"[{Color.Red3_1}]{Strings.Common_Unknown}[/]", Strings.GitHubAuth_Username);

                    if (GitHubCalls.HasOutdatedScopes)
                        UI.Warning(
                            Strings.Common_ScopesMissing + "\n" +
                            $"[bold]{Markup.Escape(string.Join(", ", GitHubCalls.MissingScopes))}[/]\n\n" +
                            Strings.GitHubAuth_Outdated_Hint,
                            Strings.Common_ScopesOutdated_Title);
                }

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                    .Title($"[{Color.Blue}]{Strings.Settings_GitHubAuthorization}[/]")
                    .AddChoices(choices)
                    .UseConverter(x => x switch
                    {
                        Action.AuthorizeWithGitHub => Strings.GitHubAuth_Authorize,
                        Action.ReauthorizeWithGitHub => $"[{Color.Yellow}]{Strings.GitHubAuth_Reauthorize}[/]",
                        Action.UnauthorizeFromGitHub => Strings.GitHubAuth_Unauthorize,
                        Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    }));

                AnsiConsole.Clear();

                if (action is Action.Back)
                    return;

                switch (action)
                {
                    case Action.AuthorizeWithGitHub:
                    case Action.ReauthorizeWithGitHub:
                        GitHubCalls.AuthorizeGitHub().GetAwaiter().GetResult();
                        break;
                    case Action.UnauthorizeFromGitHub:
                        ConfigurationHandler.ForgetGitHubAccessToken();
                        GitHubCalls.ResetScopeState();
                        GitHubCalls.ForgetGitHubUsername();

                        var config = ConfigurationHandler.GetConfig();
                        config.PreferredIdentity = PreferredIdentity.Git;
                        ConfigurationHandler.EditConfig(config);

                        UI.Success(Strings.GitHubAuth_Unauthorized_Message, Strings.GitHubAuth_Unauthorized_Title);
                        break;
                    case Action.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                Console.ReadKey();
                AnsiConsole.Clear();
            }
        }

        private enum Action
        {
            AuthorizeWithGitHub,
            ReauthorizeWithGitHub,
            UnauthorizeFromGitHub,
            Back
        }
    }
}
