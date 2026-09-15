using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace Helyx.Projects
{
    internal static class HackatimeInfo
    {
        internal static void Display(Guid guid)
        {
            if (string.IsNullOrEmpty(ConfigurationHandler.GetHackatimeToken()))
            {
                UI.Error("Hackatime is not authorized!\nAuthorize with Hackatime in Settings.", "Hackatime Info");
                Console.ReadKey();
                return;
            }

            bool Assign()
            {
                string[]? names = null;

                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots).Start("Loading your Hackatime projects...", ctx =>
                        names = HackatimeCalls.GetProjects().GetAwaiter().GetResult()
                    );

                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                if (names == null || names.Length == 0)
                {
                    UI.Error("Hackatime returned no projects to pick from.", "Hackatime Info");
                    Console.ReadKey();
                    return false;
                }

                var back = $"[{Color.Red3_1}]{Strings.Common_Back}[/]";

                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                    .Title("Which Hackatime project is this?")
                    .PageSize(15)
                    .AddChoices([.. names, back])
                    .UseConverter(x => x == back ? x : Markup.Escape(x)));

                return selected != back && ConfigurationHandler.UpdateProject(guid, x => x.HackatimeName = selected);
            }

            while (true)
            {
                var project = ConfigurationHandler.GetProject(guid);

                if (string.IsNullOrEmpty(project.HackatimeName))
                {
                    if (!Assign())
                        return;

                    continue;
                }
                
                ProjectDetails? details = null;
                string? error = null;

                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots).Start("Retrieving Hackatime stats...", ctx =>
                        (details, error) = HackatimeCalls.GetProjectDetails(project.HackatimeName).GetAwaiter().GetResult()
                    );

                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                if (details == null)
                    UI.Error($"Could not load [{Color.SteelBlue1}]{Markup.Escape(project.HackatimeName)}[/] from Hackatime." +
                             $"\n\n[{Color.Grey}]{Markup.Escape(error ?? string.Empty)}[/]", "Hackatime Info");
                else
                {
                    var span = TimeSpan.FromSeconds(details.TotalSeconds);

                    var grid = new Grid().Expand();

                    grid.AddColumn(new GridColumn().NoWrap().PadRight(4));
                    grid.AddColumn();
                    
                    grid.AddRow("[bold]Project[/]", $"[{Color.SteelBlue1}]{Markup.Escape(details.Name ?? project.HackatimeName)}[/]");
                    grid.AddRow("[bold]Total time[/]", string.Format(Strings.GH_Wf_Hours, (int)span.TotalHours, span.Minutes));
                    grid.AddRow("[bold]Heartbeats[/]", details.TotalHeartbeats.ToString("N0"));

                    if (details.FirstHeartbeat != null)
                        grid.AddRow("[bold]First heartbeat[/]", $"[{Color.CadetBlue}]{details.FirstHeartbeat.Value.LocalDateTime:g}[/]");

                    if (details.LastHeartbeat != null)
                        grid.AddRow("[bold]Last heartbeat[/]", $"[{Color.CadetBlue}]{details.LastHeartbeat.Value.LocalDateTime:g}[/]");

                    if (!string.IsNullOrWhiteSpace(details.RepoUrl))
                        grid.AddRow("[bold]Repository[/]", $"[{Color.Grey}]{UI.Link(details.RepoUrl, Markup.Escape(details.RepoUrl))}[/]");

                    UI.Box(grid, "Hackatime");

                    if (details.Languages is { Count: > 0 })
                    {
                        var languages = new BreakdownChart().HideTagValues().Expand();

                        foreach (var language in details.Languages)
                            languages.AddItem(language, 1, UI.GetColor(language));

                        UI.Box(languages, "Languages");
                    }

                    if (details.Archived)
                        UI.Warning("This project is archived in Hackatime.", "Archived");
                }

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                    .AddChoices(Enum.GetValues<Action>())
                    .UseConverter(x => x switch
                    {
                        Action.Refresh => "Refresh",
                        Action.ChangeProject => "Change Hackatime project",
                        Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    }));

                switch (action)
                {
                    case Action.Refresh:
                        continue;
                    case Action.ChangeProject:
                        Assign();
                        continue;
                    case Action.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private enum Action
        {
            Refresh,
            ChangeProject,
            Back
        }
    }
}
