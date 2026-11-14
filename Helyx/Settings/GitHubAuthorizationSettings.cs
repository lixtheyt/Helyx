using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;
using System.Security.Cryptography;

namespace Helyx.Settings
{
    internal static class GitHubAuthorizationSettings
    {
        internal static void Display()
        {
            while (true)
            {
                var authorized = GitHubCalls.IsAuthorizedWithGitHub();
                var signing = ConfigurationHandler.GetConfig().SignCommits;

                List<Action> choices = new();

                if (authorized)
                {
                    if (GitHubCalls.HasOutdatedScopes)
                        choices.Add(Action.ReauthorizeWithGitHub);

                    choices.Add(signing ? Action.DisableCommitSigning : Action.EnableCommitSigning);
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

                    UI.Info(signing
                        ? $"[{Color.Green}]{Strings.Common_Enabled}[/]"
                        : $"[{Color.Red}]{Strings.Common_Disabled}[/]"
                        , Strings.Signing_Title);
                }

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                    .Title($"[{Color.Blue}]{Strings.Settings_GitHubAuthorization}[/]")
                    .AddChoices(choices)
                    .UseConverter(x => x switch
                    {
                        Action.AuthorizeWithGitHub => Strings.GitHubAuth_Authorize,
                        Action.ReauthorizeWithGitHub => $"[{Color.Yellow}]{Strings.GitHubAuth_Reauthorize}[/]",
                        Action.EnableCommitSigning => Strings.Signing_Enable,
                        Action.DisableCommitSigning => Strings.Signing_Disable,
                        Action.UnauthorizeFromGitHub => Strings.GitHubAuth_Unauthorize,
                        Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    }));

                AnsiConsole.Clear();

                switch (action)
                {
                    case Action.AuthorizeWithGitHub:
                    case Action.ReauthorizeWithGitHub:
                        GitHubCalls.AuthorizeGitHub().GetAwaiter().GetResult();
                        break;
                    case Action.EnableCommitSigning:
                        {
                            if (GitHubCalls.HasOutdatedScopes)
                            {
                                UI.Error(Strings.Signing_NeedsScope, Strings.Signing_Title);
                                break;
                            }

                            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

                            if (ConfigurationHandler.GetSigningKey() is { } stored)
                                key.ImportPkcs8PrivateKey(stored, out _);
                            else if (!ConfigurationHandler.SaveSigningKey(key.ExportPkcs8PrivateKey()))
                            {
                                AnsiConsole.Clear();
                                continue;
                            }

                            (bool Result, string? Error) outcome = default;

                            AnsiConsole.Status()
                                .Spinner(Spinner.Known.Dots)
                                .Start(Strings.Signing_Registering, ctx =>
                                    outcome = GitHubCalls.AddSigningKey($"ecdsa-sha2-nistp256 {Convert.ToBase64String(Signing.PublicKey(key))}")
                                        .GetAwaiter().GetResult()
                                );

                            if (outcome.Error != null)
                            {
                                UI.Error(Strings.Signing_RegisterFailed + $"\n\n{Markup.Escape(outcome.Error)}", Strings.Signing_Title);
                                break;
                            }

                            if (!ConfigurationHandler.Update(x => x.SignCommits = true))
                            {
                                AnsiConsole.Clear();
                                continue;
                            }

                            UI.Success(Strings.Signing_Enabled_Message + $"\n\n[{Color.Grey}]{Strings.Signing_EmailHint}[/]", Strings.Signing_Title);
                            break;
                        }
                    case Action.DisableCommitSigning:
                        if (!ConfigurationHandler.Update(x => x.SignCommits = false))
                        {
                            AnsiConsole.Clear();
                            continue;
                        }

                        UI.Success(Strings.Signing_Disabled_Message, Strings.Signing_Title);
                        break;
                    case Action.UnauthorizeFromGitHub:
                        ConfigurationHandler.ForgetGitHubAccessToken();
                        ConfigurationHandler.ForgetSigningKey();
                        GitHubCalls.ResetScopeState();
                        GitHubCalls.ForgetGitHubUsername();

                        var config = ConfigurationHandler.GetConfig();
                        config.PreferredIdentity = PreferredIdentity.Git;
                        config.SignCommits = false;
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
            EnableCommitSigning,
            DisableCommitSigning,
            UnauthorizeFromGitHub,
            Back
        }
    }
}
