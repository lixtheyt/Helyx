using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Projects;
using Helyx.Settings;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx
{
    internal static class MainMenu
    {
        internal static void Display()
        {
            AnsiConsole.Write(new FigletText("Helyx")
                .Color(Color.FromHex("#26ABDF")));
            Console.WriteLine();

            if (!ConfigurationHandler.ConfigExists)
            {
                AnsiConsole.MarkupLine($"[{Color.Red}]{Strings.MainMenu_Config_NotFound}[/]\n[italic bold {Color.Green}]{Strings.MainMenu_Config_Creating}[/]");
                ConfigurationHandler.CreateConfig();
                Thread.Sleep(1000);
                AnsiConsole.MarkupLine($"[{Color.LightGreen}]{Strings.MainMenu_Config_Created}[/]");
            }

            ConfigurationMigrator.CheckAndMergeConfig();

            if (GitHubCalls.TokenRejected)
            {
                GitHubCalls.TokenRejected = false;

                ConfigurationHandler.ForgetGitHubAccessToken();
                ConfigurationHandler.Update(x => x.PreferredIdentity = PreferredIdentity.Git);
            }

            _tokenCheck ??= Task.Run(GitHubCalls.CheckGitHubTokenAndResolve);

            if (_tokenCheck.IsCompleted)
                _scopeChecked = true;

            WarnAboutOutdatedScopes();

            using var cts = new CancellationTokenSource();

            if (!_scopeChecked)
                _ = _tokenCheck.ContinueWith(_ =>
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }, TaskScheduler.Default);

            Action choice;

            try
            {
                choice = new SelectionPrompt<Action>()
                    .AddChoices(Enum.GetValues<Action>())
                    .UseConverter(x => x switch
                    {
                        Action.Projects => Strings.MainMenu_Projects,
                        Action.Settings => Strings.Common_Settings,
                        Action.Credits => Strings.MainMenu_Credits,
                        Action.Exit => Strings.MainMenu_Exit,
                        _ => x.ToString()
                    })
                    .ShowAsync(AnsiConsole.Console, cts.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.Clear();
                return;
            }

            AnsiConsole.Clear();

            switch (choice)
            {
                case Action.Projects:
                    ProjectsMenu.DisplayProjects();
                    break;
                case Action.Settings:
                    SettingsMenu.DisplaySettings();
                    break;
                case Action.Credits:
                    CreditsMenu.DisplayCredits();
                    break;
                case Action.Exit:
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static bool _scopeWarningShown;

        private static Task? _tokenCheck;

        private static bool _scopeChecked;

        private static void WarnAboutOutdatedScopes()
        {
            if (_scopeWarningShown || !GitHubCalls.HasOutdatedScopes)
                return;

            _scopeWarningShown = true;

            UI.Warning(
                Strings.Common_ScopesMissing + "\n" +
                $"[bold]{Markup.Escape(string.Join(", ", GitHubCalls.MissingScopes))}[/]\n\n" +
                string.Format(Strings.MainMenu_Scopes_Hint,
                    $"[bold]{Strings.Common_Settings}[/] [{Color.Grey}]>[/] [bold]{Strings.Settings_GitHubAuthorization}[/]"),
                Strings.Common_ScopesOutdated_Title);

            Console.WriteLine();
        }

        private enum Action
        {
            Projects,
            Settings,
            Credits,
            Exit
        }
    }
}
