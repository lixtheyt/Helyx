using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using LibGit2Sharp;
using Spectre.Console;
using System.Diagnostics;
using static Helyx.Data.ConfigurationHandler;
using Panel = Spectre.Console.Panel;

namespace Helyx.Projects
{
    internal static class WorkflowActions
    {
        internal static async Task Display(Guid guid)
        {
            var project = GetProject(guid);

            if (!Repository.IsValid(project.Path))
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                UI.Error(Strings.Common_NotAGitRepo + "\n" + Strings.Common_InitGitHint, Strings.GH_Wf_Title_Short);
                Console.ReadKey();
                return;
            }

            if (!GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.GH_Wf_Title_Short))
                return;

            List<GitHubWorkflow>? workflows = null;
            List<GitHubWorkflowRun>? runs = null;

            var workflowId = 0L;

            while (true)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Line)
                    .StartAsync(Strings.GH_Wf_Retrieving, async ctx =>
                    {
                        workflows ??= await GitHubCalls.GetWorkflows(guid);
                        runs = await GitHubCalls.GetWorkflowRuns(guid);
                    });

                UI.FlushInput();

                if (runs == null)
                {
                    AnsiConsole.Clear();
                    ProjectsMenu.PrintHeader(guid);

                    UI.Error(Strings.GH_Wf_LoadFailed + "\n" + Strings.GH_CheckAndRetry, Strings.GH_Wf_Title_Short);
                    Console.ReadKey();
                    return;
                }

                if (runs.Count == 0)
                {
                    AnsiConsole.Clear();
                    ProjectsMenu.PrintHeader(guid);

                    UI.Info(Strings.GH_Wf_NoRuns, Strings.GH_Wf_Title_Short);
                    Console.ReadKey();
                    return;
                }

                AnsiConsole.Clear();

                var layout = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(1),
                        new Layout("Filters").Size(3),
                        new Layout("List"),
                        new Layout("Footer").Size(3));

                layout["Header"].Update(
                    new Rule($"[bold {Color.Blue}]{string.Format(Strings.GH_Wf_Title, Markup.Escape(GetProject(guid).GitHubName))}[/]")
                        .LeftJustified());

                var selectedIndex = 0;
                var pageSize = Math.Max(2, Console.WindowHeight - 12);

                GitHubWorkflowRun? opened = null;

                var dispatching = false;
                var refreshing = false;

                AnsiConsole.Live(layout)
                    .Start(ctx =>
                    {
                        var running = true;

                        while (running)
                        {
                            var visible = workflowId == 0
                                ? runs
                                : runs.Where(x => x.WorkflowId == workflowId).ToList();

                            selectedIndex = visible.Count == 0
                                ? 0
                                : Math.Clamp(selectedIndex, 0, visible.Count - 1);

                            var lastPage = Math.Max(1, (int)Math.Ceiling(visible.Count / (double)pageSize));

                            var currentPage = selectedIndex / pageSize;
                            var firstRow = currentPage * pageSize;
                            var lastRow = Math.Min(firstRow + pageSize, visible.Count);

                            var tabs = new Grid().AddColumn().Expand();

                            tabs.AddRow(string.Join("   ", new[] { 0L }
                                .Concat(workflows?.Select(x => x.Id) ?? [])
                                .Select(id =>
                                {
                                    var name = id == 0
                                        ? Strings.GH_Wf_AllWorkflows
                                        : workflows?.FirstOrDefault(x => x.Id == id)?.Name ?? Strings.Common_Unknown;

                                    return id == workflowId
                                        ? $"[bold {Color.Aqua}]{Markup.Escape(name)}[/]"
                                        : $"[{Color.Grey}]{Markup.Escape(name)}[/]";
                                })));

                            layout["Filters"].Update(new Panel(tabs)
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            var table = new Table()
                                .Border(TableBorder.Rounded)
                                .ShowRowSeparators()
                                .AddColumn(new TableColumn("").Width(2))
                                .AddColumn($"[{Color.Grey}]#[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Wf_Col_Workflow}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Wf_Col_Status}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Wf_Col_Event}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Wf_Col_Branch}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Wf_Col_Commit}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Wf_Col_When}[/]")
                                .Expand();

                            for (var i = firstRow; i < lastRow; i++)
                            {
                                var run = visible[i];
                                var state = StateOf(run.Status, run.Conclusion);

                                if (i == selectedIndex)
                                    table.AddRow(
                                        $"[{Color.Aqua}]▸[/]",
                                        $"[{Color.Aqua}]{run.RunNumber}[/]",
                                        $"[{Color.Aqua}]{Markup.Escape(run.Name ?? Strings.Common_Unknown)}[/]",
                                        $"[{Color.Aqua}]{state.Glyph} {state.Label}[/]",
                                        $"[{Color.Aqua}]{Markup.Escape(run.Event ?? "")}[/]",
                                        $"[{Color.Aqua}]{Markup.Escape(run.HeadBranch ?? "")}[/]",
                                        $"[{Color.Aqua}]{ShortSha(run.HeadSha)}[/]",
                                        $"[{Color.Aqua}]{GitHubActions.RelativeTime(run.UpdatedAt.ToLocalTime())}[/]");
                                else
                                    table.AddRow(
                                        "",
                                        run.RunNumber.ToString(),
                                        Markup.Escape(run.Name ?? Strings.Common_Unknown),
                                        $"[{state.Colour}]{state.Glyph} {state.Label}[/]",
                                        $"[{Color.Grey}]{Markup.Escape(run.Event ?? "")}[/]",
                                        $"[{Color.Green3_1}]{Markup.Escape(run.HeadBranch ?? "")}[/]",
                                        $"[{Color.DarkOrange3}]{ShortSha(run.HeadSha)}[/]",
                                        $"[{Color.CadetBlue}]{GitHubActions.RelativeTime(run.UpdatedAt.ToLocalTime())}[/]");
                            }

                            layout["List"].Update(visible.Count == 0
                                ? new Markup($"\n    [{Color.Red3_1}]{Strings.GH_Wf_NoRunsFiltered}[/]")
                                : table);

                            layout["Footer"].Update(new Panel(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                        $"[{Color.Grey}]{Strings.GH_Wf_Footer_List}" +
                                        $"{(workflows is { Count: > 0 } ? Strings.GH_Wf_Key_Dispatch : "")}" +
                                        $"{Strings.GH_Wf_Key_Refresh}{Strings.GH_Wf_Key_Browser}[/]",
                                        $"[{Color.Grey}]{string.Format(Strings.GH_Wf_Page, visible.Count == 0 ? 0 : currentPage + 1, visible.Count == 0 ? 0 : lastPage, visible.Count == 0 ? 0 : selectedIndex + 1, visible.Count)}[/]"))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            var key = Console.ReadKey(true);

                            switch (key.Key)
                            {
                                case ConsoleKey.UpArrow when visible.Count > 0:
                                    selectedIndex = selectedIndex == 0
                                        ? visible.Count - 1
                                        : selectedIndex - 1;
                                    break;

                                case ConsoleKey.DownArrow when visible.Count > 0:
                                    selectedIndex = selectedIndex == visible.Count - 1
                                        ? 0
                                        : selectedIndex + 1;
                                    break;

                                case ConsoleKey.LeftArrow when visible.Count > 0:
                                    selectedIndex = currentPage == 0
                                        ? (lastPage - 1) * pageSize
                                        : firstRow - pageSize;
                                    break;

                                case ConsoleKey.RightArrow when visible.Count > 0:
                                    selectedIndex = currentPage == lastPage - 1
                                        ? 0
                                        : firstRow + pageSize;
                                    break;

                                case ConsoleKey.Tab when workflows is { Count: > 0 }:
                                    var ids = new[] { 0L }.Concat(workflows.Select(x => x.Id)).ToList();

                                    workflowId = ids[(ids.IndexOf(workflowId) + 1) % ids.Count];
                                    selectedIndex = 0;
                                    break;

                                case ConsoleKey.R:
                                    refreshing = true;
                                    running = false;
                                    break;

                                case ConsoleKey.D when workflows is { Count: > 0 }:
                                    dispatching = true;
                                    running = false;
                                    break;

                                case ConsoleKey.B when visible.Count > 0:
                                    OpenInBrowser(visible[selectedIndex].HtmlUrl);
                                    break;

                                case ConsoleKey.Enter when visible.Count > 0:
                                    opened = visible[selectedIndex];
                                    running = false;
                                    break;

                                case ConsoleKey.Escape:
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }
                    });

                AnsiConsole.Clear();

                if (opened != null)
                {
                    await ShowRun(guid, opened);
                    continue;
                }

                if (dispatching)
                {
                    await DispatchWorkflow(guid, workflows!);
                    continue;
                }

                if (refreshing)
                    continue;

                AnsiConsole.Clear();
                return;
            }
        }

        private static async Task ShowRun(Guid guid, GitHubWorkflowRun run)
        {
            var runId = run.Id;

            while (true)
            {
                GitHubWorkflowRun? current = null;
                List<GitHubWorkflowJob>? jobs = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Line)
                    .StartAsync(Strings.GH_Wf_RetrievingJobs, async ctx =>
                    {
                        current = await GitHubCalls.GetWorkflowRun(guid, runId);
                        jobs = await GitHubCalls.GetRunJobs(guid, runId);
                    });

                UI.FlushInput();

                if (current == null || jobs == null)
                {
                    AnsiConsole.Clear();
                    ProjectsMenu.PrintHeader(guid);

                    UI.Error(Strings.GH_Wf_JobsLoadFailed + "\n" + Strings.GH_CheckAndRetry, Strings.GH_Wf_Title_Short);
                    Console.ReadKey();
                    return;
                }

                var lines = new List<string>();

                foreach (var job in jobs)
                {
                    var jobState = StateOf(job.Status, job.Conclusion);

                    lines.Add($"[{jobState.Colour}]{jobState.Glyph}[/] [bold]{Markup.Escape(job.Name ?? "")}[/]   " +
                              $"[{Color.CadetBlue}]{Elapsed(job.StartedAt, job.CompletedAt)}[/]");

                    foreach (var step in job.Steps)
                    {
                        var stepState = StateOf(step.Status, step.Conclusion);

                        lines.Add($"    [{stepState.Colour}]{stepState.Mark}[/] {Markup.Escape(step.Name ?? "")}   " +
                                  (step.Conclusion is "skipped" or "cancelled" or "neutral"
                                      ? $"[{Color.Grey}]{stepState.Label}[/]"
                                      : $"[{Color.CadetBlue}]{Elapsed(step.StartedAt, step.CompletedAt)}[/]"));
                    }

                    lines.Add("");
                }

                AnsiConsole.Clear();

                var conclusion = StateOf(current.Status, current.Conclusion);

                var details = new Grid()
                    .AddColumn(new GridColumn().NoWrap().PadRight(2))
                    .AddColumn(new GridColumn().PadRight(4))
                    .AddColumn(new GridColumn().NoWrap().PadRight(2))
                    .AddColumn();

                details.AddRow(
                    $"[bold]{Strings.GH_Wf_Row_Conclusion}[/]", $"[{conclusion.Colour}]{conclusion.Glyph} {conclusion.Label}[/]",
                    $"[bold]{Strings.GH_Wf_Row_Branch}[/]", $"[{Color.Green3_1}]{Markup.Escape(current.HeadBranch ?? "")}[/]");

                details.AddRow(
                    $"[bold]{Strings.GH_Wf_Row_Commit}[/]", $"[{Color.DarkOrange3}]{ShortSha(current.HeadSha)}[/]",
                    $"[bold]{Strings.GH_Wf_Row_Actor}[/]", $"[{Color.SkyBlue1}]{Markup.Escape(current.Actor?.Login ?? "")}[/]");

                details.AddRow(
                    $"[bold]{Strings.GH_Wf_Row_Started}[/]", $"[{Color.CadetBlue}]{(current.StartedAt?.ToLocalTime().ToString("g") ?? "")}[/]",
                    $"[bold]{Strings.GH_Wf_Row_Duration}[/]", $"[{Color.CadetBlue}]{Elapsed(current.StartedAt, current.UpdatedAt)}[/]");

                var layout = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(1),
                        new Layout("Details").Size(5),
                        new Layout("Jobs"),
                        new Layout("Footer").Size(3));

                layout["Header"].Update(
                    new Rule($"[bold {Color.Blue}]{string.Format(Strings.GH_Wf_RunTitle, current.RunNumber, Markup.Escape(current.Name ?? ""))}[/]")
                        .LeftJustified());

                layout["Details"].Update(new Panel(details)
                    .RoundedBorder()
                    .Expand()
                    .Padding(1, 0));

                var scroll = 0;
                var height = Math.Max(3, Console.WindowHeight - 12);

                var action = ConsoleKey.Escape;

                AnsiConsole.Live(layout)
                    .Start(ctx =>
                    {
                        var running = true;

                        while (running)
                        {
                            scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - height));

                            layout["Jobs"].Update(new Markup(string.Join("\n", lines.Skip(scroll).Take(height))));

                            var open = current.Status is "queued" or "in_progress";

                            layout["Footer"].Update(new Panel(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                        $"[{Color.Grey}]{Strings.GH_Wf_Footer_Run}[/]" +
                                        $"[{(open ? "grey50" : "grey")}]{Strings.GH_Wf_Key_Rerun}[/]  " +
                                        $"[{(open || current.Conclusion != "failure" ? "grey50" : "grey")}]{Strings.GH_Wf_Key_RerunFailed}[/]  " +
                                        $"[{(open ? "grey" : "grey50")}]{Strings.GH_Wf_Key_Cancel}[/]  " +
                                        $"[{Color.Grey}]{Strings.GH_Wf_Key_FullLog}{Strings.GH_Wf_Key_Refresh}{Strings.GH_Wf_Key_Browser}[/]",
                                        $"[{Color.Grey}]{string.Format(Strings.GH_Wf_Jobs_Count, jobs.Count)}[/]"))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            var key = Console.ReadKey(true);

                            switch (key.Key)
                            {
                                case ConsoleKey.UpArrow:
                                    scroll--;
                                    break;

                                case ConsoleKey.DownArrow:
                                    scroll++;
                                    break;

                                case ConsoleKey.PageUp:
                                    scroll -= height;
                                    break;

                                case ConsoleKey.PageDown:
                                    scroll += height;
                                    break;

                                case ConsoleKey.B:
                                    OpenInBrowser(current.HtmlUrl);
                                    break;

                                case ConsoleKey.R:
                                case ConsoleKey.E when !open:
                                case ConsoleKey.F when !open && current.Conclusion == "failure":
                                case ConsoleKey.C when open:
                                case ConsoleKey.L:
                                    action = key.Key;
                                    running = false;
                                    break;

                                case ConsoleKey.Escape:
                                    action = ConsoleKey.Escape;
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }
                    });

                AnsiConsole.Clear();

                if (action == ConsoleKey.Escape)
                    return;

                if (action == ConsoleKey.R)
                    continue;

                if (action == ConsoleKey.L)
                {
                    await ShowLog(guid, current);
                    continue;
                }

                ProjectsMenu.PrintHeader(guid);

                var question = action switch
                {
                    ConsoleKey.E => Strings.GH_Wf_Confirm_Rerun,
                    ConsoleKey.F => Strings.GH_Wf_Confirm_RerunFailed,
                    _ => Strings.GH_Wf_Confirm_Cancel
                };

                if (AnsiConsole.Prompt(
                        new SelectionPrompt<Confirm>()
                            .Title(question)
                            .AddChoices(Enum.GetValues<Confirm>())
                            .UseConverter(UI.ConfirmName)) == Confirm.No)
                {
                    AnsiConsole.Clear();
                    continue;
                }

                (bool Result, string? Error) outcome = default;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Line)
                    .StartAsync(Strings.GH_Wf_Working, async ctx =>
                        outcome = action switch
                        {
                            ConsoleKey.E => await GitHubCalls.RerunRun(guid, runId),
                            ConsoleKey.F => await GitHubCalls.RerunFailedJobs(guid, runId),
                            _ => await GitHubCalls.CancelRun(guid, runId)
                        });

                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                if (outcome.Error != null)
                    UI.Error(Markup.Escape(outcome.Error), Strings.GH_Wf_Title_Short);
                else
                    UI.Success(action == ConsoleKey.C
                        ? Strings.GH_Wf_Cancelled
                        : Strings.GH_Wf_Rerunning, Strings.GH_Wf_Title_Short);

                Console.ReadKey();
                AnsiConsole.Clear();
            }
        }

        private static async Task ShowLog(Guid guid, GitHubWorkflowRun run)
        {
            Dictionary<string, string>? logs = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Line)
                .StartAsync(Strings.GH_Wf_DownloadingLog, async ctx =>
                    logs = await GitHubCalls.GetRunLogs(guid, run.Id));

            UI.FlushInput();
            AnsiConsole.Clear();

            if (logs == null || logs.Count == 0)
            {
                ProjectsMenu.PrintHeader(guid);

                UI.Error(Strings.GH_Wf_LogFailed, Strings.GH_Wf_Title_Short);
                Console.ReadKey();
                return;
            }

            var lines = logs
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(x => new[] { "", $"[bold {Color.Aqua}]{Markup.Escape(x.Key)}[/]", "" }
                    .Concat(x.Value.Replace("\r", "").Split('\n')
                        .Select(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                            ? $"[{Color.Red3_1}]{Markup.Escape(line)}[/]"
                            : $"[{Color.Grey}]{Markup.Escape(line)}[/]")))
                .ToList();

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(1),
                    new Layout("Log"),
                    new Layout("Footer").Size(3));

            layout["Header"].Update(
                new Rule($"[bold {Color.Blue}]{string.Format(Strings.GH_Wf_LogTitle, run.RunNumber)}[/]").LeftJustified());

            var scroll = 0;
            var height = Math.Max(3, Console.WindowHeight - 8);

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    var running = true;

                    while (running)
                    {
                        scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - height));

                        layout["Log"].Update(new Markup(string.Join("\n", lines.Skip(scroll).Take(height))));

                        layout["Footer"].Update(new Panel(
                            new Grid()
                                .AddColumn()
                                .AddColumn(new GridColumn().RightAligned())
                                .Expand()
                                .AddRow(
                                    $"[{Color.Grey}]{Strings.GH_Wf_Footer_Log}[/]",
                                    $"[{Color.Grey}]{string.Format(Strings.GH_Wf_LogLines, scroll + 1, Math.Min(scroll + height, lines.Count), lines.Count)}[/]"))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        ctx.Refresh();

                        switch (Console.ReadKey(true).Key)
                        {
                            case ConsoleKey.UpArrow:
                                scroll--;
                                break;

                            case ConsoleKey.DownArrow:
                                scroll++;
                                break;

                            case ConsoleKey.PageUp:
                                scroll -= height;
                                break;

                            case ConsoleKey.PageDown:
                                scroll += height;
                                break;

                            case ConsoleKey.Home:
                                scroll = 0;
                                break;

                            case ConsoleKey.End:
                                scroll = lines.Count;
                                break;

                            case ConsoleKey.Escape:
                                running = false;
                                break;
                        }

                        UI.FlushInput();
                    }
                });

            AnsiConsole.Clear();
        }

        private static async Task DispatchWorkflow(Guid guid, List<GitHubWorkflow> workflows)
        {
            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<GitHubWorkflow?>()
                    .Title(Strings.GH_Wf_Dispatch_Pick)
                    .AddChoices(workflows.Where(x => x.State == "active").Append(null))
                    .UseConverter(x => x == null
                        ? $"[{Color.Red3_1}]{Strings.Common_Back}[/]"
                        : $"{Markup.Escape(x.Name ?? "")}   [{Color.Grey}]{Markup.Escape(x.Path ?? "")}[/]"));

            if (choice == null)
            {
                AnsiConsole.Clear();
                return;
            }

            var project = GetProject(guid);

            using var repo = GitHelper.OpenRepo(project.Path, Strings.GH_Wf_Title_Short);

            var branches = repo == null
                ? [repo?.Head.FriendlyName ?? "main"]
                : repo.Branches.Where(x => !x.IsRemote).Select(x => x.FriendlyName).ToList();

            var branch = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Strings.GH_Wf_Dispatch_Branch)
                    .AddChoices(branches)
                    .UseConverter(x => $"[{Color.Green3_1}]{Markup.Escape(x)}[/]"));

            (bool Result, string? Error) outcome = default;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Line)
                .StartAsync(Strings.GH_Wf_Working, async ctx =>
                    outcome = await GitHubCalls.DispatchWorkflow(guid, choice.Id, branch));

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            if (outcome.Error != null)
                UI.Error(Markup.Escape(outcome.Error), Strings.GH_Wf_Title_Short);
            else
                UI.Success(string.Format(Strings.GH_Wf_Dispatched,
                    $"[{Color.Aqua}]{Markup.Escape(choice.Name ?? "")}[/]",
                    $"[{Color.Green3_1}]{Markup.Escape(branch)}[/]"), Strings.GH_Wf_Title_Short);

            Console.ReadKey();
            AnsiConsole.Clear();
        }

        private static (string Colour, string Glyph, string Mark, string Label) StateOf(string? status, string? conclusion) =>
            status switch
            {
                "queued" or "waiting" or "pending" => ("Yellow3_1", "○", "○", Strings.GH_Wf_State_Queued),
                "in_progress" => ("Yellow3_1", "◐", "◐", Strings.GH_Wf_State_Running),
                _ => conclusion switch
                {
                    "success" => ("Green3_1", "●", "✓", Strings.GH_Wf_State_Success),
                    "failure" => ("Red3_1", "●", "✗", Strings.GH_Wf_State_Failure),
                    "cancelled" => ("grey", "○", "○", Strings.GH_Wf_State_Cancelled),
                    "skipped" => ("grey", "○", "○", Strings.GH_Wf_State_Skipped),
                    "timed_out" => ("Orange1", "●", "✗", Strings.GH_Wf_State_TimedOut),
                    "action_required" => ("Orange1", "●", "!", Strings.GH_Wf_State_ActionRequired),
                    "neutral" => ("grey", "○", "○", Strings.GH_Wf_State_Neutral),
                    _ => ("grey", "○", "○", Strings.Common_Unknown)
                }
            };

        private static string ShortSha(string? sha) =>
            sha == null ? "" : Markup.Escape(sha[..Math.Min(7, sha.Length)]);

        private static string Elapsed(DateTimeOffset? from, DateTimeOffset? to)
        {
            if (from == null || to == null || to < from)
                return "";

            var span = to.Value - from.Value;

            return span.TotalMinutes < 1
                ? string.Format(Strings.GH_Wf_Seconds, (int)span.TotalSeconds)
                : span.TotalHours < 1
                    ? string.Format(Strings.GH_Wf_Minutes, (int)span.TotalMinutes, span.Seconds)
                    : string.Format(Strings.GH_Wf_Hours, (int)span.TotalHours, span.Minutes);
        }

        private static void OpenInBrowser(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
            }
        }
    }
}
