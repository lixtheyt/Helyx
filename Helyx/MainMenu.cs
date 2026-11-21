using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Projects;
using Helyx.Settings;
using Helyx.Shared;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Helyx
{
    internal static class MainMenu
    {
        internal static void Display()
        {
            AnsiConsole.Clear();
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

            if (_broadcasts == null && !ConfigurationHandler.GetConfig().HideBroadcasts)
                _broadcasts = Task.Run(GitHubCalls.GetBroadcasts);

            var choices = Enum.GetValues<Action>();
            var top = Console.CursorTop - Console.WindowTop;
            var language = Strings.Culture?.TwoLetterISOLanguageName ?? "en";
            var selected = 0;
            var warned = false;
            var announced = false;
            Action? choice = null;

            string? Localized(Dictionary<string, string?>? values) =>
                values?
                    .OrderBy(x => x.Key.Equals(language, StringComparison.OrdinalIgnoreCase) ? 0 : x.Key.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .Select(x => x.Value)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            IRenderable Build()
            {
                var options = RenderOptions.Create(AnsiConsole.Console, AnsiConsole.Console.Profile.Capabilities);
                var room = Console.WindowHeight - top - choices.Length - 1;
                List<(IRenderable Panel, bool Below, bool Broadcast)> notices = [];
                List<IRenderable> above = [];
                List<IRenderable> below = [];

                if (GitHubCalls.HasOutdatedScopes && !_scopeWarningShown)
                    notices.Add((UI.StyledPanel(UI.SafeMarkup(
                        Strings.Common_ScopesMissing + "\n" +
                        $"[bold]{Markup.Escape(string.Join(", ", GitHubCalls.MissingScopes))}[/]\n\n" +
                        string.Format(Strings.MainMenu_Scopes_Hint,
                            $"[bold]{Strings.Common_Settings}[/] [{Color.Grey}]>[/] [bold]{Strings.Settings_GitHubAuthorization}[/]"),
                        new Style(Color.White)), Strings.Common_ScopesOutdated_Title, UIKind.Warning), false, false));

                if (_broadcasts is { IsCompletedSuccessfully: true } && !_broadcastsShown)
                    foreach (var broadcast in _broadcasts.Result)
                        notices.Add((UI.StyledPanel(UI.SafeMarkup(
                            UI.MarkdownToMarkup(Localized(broadcast.Text)!, "https://github.com/LixTheYT/Helyx") +
                            (string.IsNullOrWhiteSpace(broadcast.Link)
                                ? string.Empty
                                : $"\n\n{UI.Link(broadcast.Link, $"[{Color.Grey}]{Markup.Escape(broadcast.Link)}[/]")}\n[italic {Color.Grey}]{Strings.Common_CtrlClick}[/]"),
                            new Style(Color.White)),
                            Localized(broadcast.Title) ?? Strings.Broadcast_Title,
                            Enum.TryParse<UIKind>(broadcast.Kind, true, out var kind) ? kind : UIKind.Info),
                            string.Equals(broadcast.Position, "below", StringComparison.OrdinalIgnoreCase), true));

                foreach (var (panel, isBelow, broadcast) in notices)
                {
                    var cost = Segment.SplitLines(panel.Render(options, AnsiConsole.Console.Profile.Width)).Count +
                               (isBelow || above.Count > 0 ? 1 : 2);

                    if (cost > room)
                        continue;

                    room -= cost;
                    (isBelow ? below : above).Add(panel);

                    if (broadcast)
                        announced = true;
                    else
                        warned = true;
                }

                List<IRenderable> rows = [];

                foreach (var panel in above)
                    rows.AddRange([panel, new Text(string.Empty)]);

                if (above.Count > 0)
                    rows.Add(new Text(string.Empty));

                rows.AddRange(choices.Select((x, i) =>
                {
                    var label = x switch
                    {
                        Action.Projects => Strings.MainMenu_Projects,
                        Action.Settings => Strings.Common_Settings,
                        Action.Credits => Strings.MainMenu_Credits,
                        Action.Exit => Strings.MainMenu_Exit,
                        _ => x.ToString()
                    };

                    return new Markup(i == selected ? $"[{Color.Blue}]> {label}[/]" : $"  {label}");
                }));

                foreach (var panel in below)
                    rows.AddRange([new Text(string.Empty), panel]);

                return new Rows(rows);
            }

            AnsiConsole.Live(Build())
                .Start(ctx =>
                {
                    var drawn = (GitHubCalls.HasOutdatedScopes, _broadcasts?.IsCompleted, Console.WindowWidth, Console.WindowHeight);

                    ctx.Refresh();

                    while (choice == null && !GitHubCalls.TokenRejected)
                    {
                        var state = (GitHubCalls.HasOutdatedScopes, _broadcasts?.IsCompleted, Console.WindowWidth, Console.WindowHeight);

                        if (Console.KeyAvailable)
                        {
                            switch (Console.ReadKey(true).Key)
                            {
                                case ConsoleKey.UpArrow:
                                    selected = Math.Max(0, selected - 1);
                                    break;
                                case ConsoleKey.DownArrow:
                                    selected = Math.Min(choices.Length - 1, selected + 1);
                                    break;
                                case ConsoleKey.Home or ConsoleKey.PageUp:
                                    selected = 0;
                                    break;
                                case ConsoleKey.End or ConsoleKey.PageDown:
                                    selected = choices.Length - 1;
                                    break;
                                case ConsoleKey.Enter or ConsoleKey.Spacebar:
                                    choice = choices[selected];
                                    break;
                                default:
                                    continue;
                            }
                        }
                        else if (state == drawn)
                        {
                            Thread.Sleep(25);
                            continue;
                        }

                        drawn = state;
                        ctx.UpdateTarget(Build());
                    }
                });

            if (choice == null)
            {
                AnsiConsole.Clear();
                return;
            }

            if (warned)
                _scopeWarningShown = true;

            if (announced)
                _broadcastsShown = true;

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

        private static bool _broadcastsShown;

        private static Task? _tokenCheck;

        private static Task<List<Broadcast>>? _broadcasts;

        private enum Action
        {
            Projects,
            Settings,
            Credits,
            Exit
        }
    }
}
