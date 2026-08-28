using Helyx.Data;
using Helyx.Shared;
using LibGit2Sharp;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using static Helyx.Data.ConfigurationHandler;
using Calendar = Spectre.Console.Calendar;
using Color = Spectre.Console.Color;
using HorizontalAlignment = Spectre.Console.HorizontalAlignment;
using Panel = Spectre.Console.Panel;

namespace Helyx.Projects
{
    internal static class GitHubActions
    {
        public static void Display(Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                if (!GitHubCalls.IsAuthorizedWithGitHub())
                {
                    UI.Error(Strings.GH_NeedAuth, Strings.Projects_Menu_GitHub);
                    Console.ReadKey();
                    return;
                }

                var settings = GetProject(guid).GitHubSyncSettings;

                var choices = Enum.GetValues<GitHubAction>()
                    .Where(x => x != GitHubAction.ViewGitHubRepoStats
                        || (settings.TryGetValue(GitHubSync.FetchGitHubRepoStats, out var stats) && stats))
                    .Where(x => x != GitHubAction.WorkflowRuns
                        || (settings.TryGetValue(GitHubSync.FetchGitHubActions, out var actions) && actions));

                var choice = AnsiConsole.Prompt(
                new SelectionPrompt<GitHubAction>()
                    .Title(Strings.Common_SelectAction)
                    .AddChoices(choices)
                    .UseConverter(x => x switch
                    {
                        GitHubAction.ManageIssues => Strings.GH_ManageIssues,
                        GitHubAction.ManagePullRequests => Strings.GH_ManagePulls,
                        GitHubAction.GitHubSynchronization => Strings.GH_Synchronization,
                        GitHubAction.ViewGitHubRepoStats => Strings.GH_ViewStats,
                        GitHubAction.WorkflowRuns => Strings.GH_Wf_Menu,
                        GitHubAction.OpenWiki => Strings.GH_OpenWiki,
                        GitHubAction.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    })
            );

                switch (choice)
                {
                    case GitHubAction.ManageIssues:
                        ManageIssues(guid).GetAwaiter().GetResult();
                        break;
                    case GitHubAction.ManagePullRequests:
                        ManagePullRequests(guid).GetAwaiter().GetResult();
                        break;
                    case GitHubAction.GitHubSynchronization:
                        GitHubSynchronization(guid);
                        break;
                    case GitHubAction.ViewGitHubRepoStats:
                        ViewGitHubRepoStats(guid);
                        break;
                    case GitHubAction.WorkflowRuns:
                        WorkflowActions.Display(guid).GetAwaiter().GetResult();
                        break;
                    case GitHubAction.OpenWiki:
                        OpenWiki(guid);
                        break;
                    case GitHubAction.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static async Task ManageIssues(Guid guid)
        {
            while (true)
            {
                var project = GetProject(guid);

                if (!Repository.IsValid(project.Path))
                {
                    UI.Error(Strings.Common_NotAGitRepo + "\n" + Strings.Common_InitGitHint, Strings.GH_Issues);
                    Console.ReadKey();
                    return;
                }

                if (!GitHubCalls.IsAuthorizedWithGitHub())
                {
                    UI.Error(Strings.GH_NeedAuth, Strings.GH_Issues);
                    Console.ReadKey();
                    return;
                }

                if (!GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.GH_Issues))
                    return;

                var lookup = GitHubCalls.RepoExistsOnUsersGitHubProfile(guid)
                    .GetAwaiter().GetResult();

                if (lookup != GitHubCalls.RepoLookup.Found)
                {
                    UI.Error(GitHubCalls.DescribeLookup(lookup), Strings.GH_Issues);
                    Console.ReadKey();
                    return;
                }

                List<GitHubIssue>? issues = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Line)
                    .StartAsync(Strings.GH_RetrievingIssues, async ctx =>
                            issues = await GitHubCalls.GetAllIssues(guid)
                    );

                UI.FlushInput();

                if (issues == null)
                {
                    UI.Error(Strings.GH_IssuesLoadFailed + "\n" + Strings.GH_CheckAndRetry, Strings.GH_Issues);
                    Console.ReadKey();
                    return;
                }

                if (issues.Count == 0)
                {
                    UI.Info(Strings.GH_NoIssues, Strings.GH_Issues);
                    Console.ReadKey();
                    return;
                }

                AnsiConsole.Clear();

                var rootLayout1 = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(1),
                        new Layout("Filters").Size(3),
                        new Layout("List"),
                        new Layout("Footer").Size(3)
                    );

                var header1 = new Rule($"[bold {Color.Blue}]{Strings.GH_Issues} · {Markup.Escape(GetProject(guid).GitHubName)}[/]")
                    .LeftJustified();

                rootLayout1["Header"].Update(header1);

                var allFilters = Enum.GetValues<IssueFilter>();

                var filter = IssueFilter.All;
                var invertedFilters = new HashSet<IssueFilter>();
                var selectedIndex1 = 0;
                var pageSize1 = Math.Max(2, (Console.WindowHeight - 10) / 2);

                GitHubIssue? selectedIssue = null;
                var creating = false;

                AnsiConsole.Live(rootLayout1)
                    .Start(ctx =>
                    {
                        var running = true;

                        while (running)
                        {
                            var visible = filter == IssueFilter.All
                                ? issues
                                : issues.Where(x => filter switch
                                {
                                    IssueFilter.Commented => x.Comments > 0,
                                    IssueFilter.Assigned => x.Assignees.Count > 0,
                                    IssueFilter.Labels => x.Labels.Count > 0,
                                    _ => true
                                } != invertedFilters.Contains(filter))
                                .ToList();

                            selectedIndex1 = visible.Count == 0
                                ? 0
                                : Math.Clamp(selectedIndex1, 0, visible.Count - 1);

                            var lastPage = Math.Max(1, (int)Math.Ceiling(visible.Count / (double)pageSize1));

                            var currentPage = selectedIndex1 / pageSize1;
                            var firstRow = currentPage * pageSize1;
                            var lastRow = Math.Min(firstRow + pageSize1, visible.Count);

                            var cellsFilters = allFilters
                                .SelectMany(x =>
                                {
                                    var isInverted = invertedFilters.Contains(x);

                                    var name = isInverted
                                        ? x switch
                                        {
                                            IssueFilter.Commented => Strings.GH_Filter_NoComments,
                                            IssueFilter.Assigned => Strings.GH_Filter_Unassigned,
                                            IssueFilter.Labels => Strings.GH_Filter_Unlabeled,
                                            _ => Strings.GH_Filter_All
                                        }
                                        : x switch
                                        {
                                            IssueFilter.Commented => Strings.GH_Filter_Commented,
                                            IssueFilter.Assigned => Strings.GH_Filter_Assigned,
                                            IssueFilter.Labels => Strings.GH_Filter_Labels,
                                            _ => Strings.GH_Filter_All
                                        };

                                    var color = x != filter
                                        ? "grey"
                                        : isInverted
                                            ? "red"
                                            : "Aqua";

                                    return new[] { $"[{Color.Grey}]•[/]", $"[{color}]{name}[/]" };
                                })
                                .Skip(1)
                                .ToArray();

                            var filters = new Grid()
                                .AddColumns(cellsFilters
                                    .Select((_, i) => i % 2 == 0
                                        ? new GridColumn().Centered()
                                        : new GridColumn().Centered().Width(3))
                                    .ToArray())
                                .Expand()
                                .AddRow(cellsFilters);

                            rootLayout1["Filters"].Update(new Panel(filters)
                                .RoundedBorder()
                                .BorderColor(Color.Gray)
                                .Expand()
                                );

                            var list = new Table()
                                .Border(TableBorder.Rounded)
                                .ShowRowSeparators()
                                .AddColumn(new TableColumn("").Width(2))
                                .AddColumn($"[{Color.Grey}]#[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_State}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Locked}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Title}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Labels}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Comments}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Updated}[/]")
                                .Expand();

                            for (int i = firstRow; i < lastRow; i++)
                            {
                                var issue = visible[i];

                                if (i == selectedIndex1)
                                    list.AddRow(
                                        $"[{Color.Aqua}]▸[/]",
                                        $"[{Color.Aqua}]{issue.Number}[/]",
                                        (issue.State, issue.StateReason) switch
                                        {
                                            (null, null) => Strings.Common_None,
                                            ("open", _) => $"[{Color.Aqua}]● {Strings.GH_State_Open}[/]",
                                            ("closed", "completed") => $"[{Color.Aqua}]● {Strings.GH_State_Closed}[/]",
                                            ("closed", "duplicate") => $"[{Color.Aqua}]● {Strings.GH_State_Duplicate}[/]",
                                            ("closed", "not_planned") => $"[{Color.Aqua}]● {Strings.GH_State_NotPlanned}[/]",
                                            _ => Strings.Common_Unknown
                                        },
                                        $"[{Color.Aqua}]{(issue.Locked ? "✓" : "✗")}[/]",
                                        $"[{Color.Aqua}]{Markup.Escape(issue.Title ?? "")}[/]",
                                        issue.Labels.Count > 0
                                            ? string.Join(", ", issue.Labels.Select(x => $"[{Color.Aqua}]{Markup.Escape(x.Name!)}[/]"))
                                            : $"[{Color.Aqua}]{Strings.Common_None}[/]",
                                        $"[{Color.Aqua}]{issue.Comments}[/]",
                                        $"[{Color.Aqua}]{ConvertDateTimeOffsetToText(issue.UpdatedAt.ToLocalTime().DateTime)}[/]");
                                else
                                    list.AddRow(
                                        "",
                                        issue.Number.ToString(),
                                        (issue.State, issue.StateReason) switch
                                        {
                                            (null, null) => Strings.Common_None,
                                            ("open", _) => $"[{Color.Green3_1}]● {Strings.GH_State_Open}[/]",
                                            ("closed", "completed") => $"[{Color.Red3_1}]● {Strings.GH_State_Closed}[/]",
                                            ("closed", "duplicate") => $"[{Color.Yellow3_1}]● {Strings.GH_State_Duplicate}[/]",
                                            ("closed", "not_planned") => $"[{Color.LightSteelBlue}]● {Strings.GH_State_NotPlanned}[/]",
                                            _ => Strings.Common_Unknown
                                        },
                                        $"{(issue.Locked ? $"[{Color.Green3_1}]✓[/]" : $"[{Color.Red3_1}]✗[/]")}",
                                        Markup.Escape(issue.Title ?? ""),
                                        issue.Labels.Count > 0
                                            ? string.Join(", ", issue.Labels.Select(x => $"[#{Tags.SafeHex(x.Color)}]{Markup.Escape(x.Name!)}[/]"))
                                            : Strings.Common_None,
                                        issue.Comments.ToString(),
                                        ConvertDateTimeOffsetToText(issue.UpdatedAt.ToLocalTime().DateTime));
                            }

                            rootLayout1["List"].Update(visible.Count == 0
                                ? new Markup($"\n    [{Color.Red3_1}]{Strings.GH_NothingMatches}[/]")
                                : list);

                            rootLayout1["Footer"].Update(new Panel(
                                    new Grid()
                                        .AddColumn()
                                        .AddColumn(new GridColumn().RightAligned())
                                        .Expand()
                                        .AddRow(
                                            $"[{Color.Grey}]{Strings.GH_Footer_Issues}[/]",
                                            $"[{Color.Grey}]{string.Format(Strings.GH_Page, visible.Count == 0 ? 0 : currentPage + 1, visible.Count == 0 ? 0 : lastPage, visible.Count == 0 ? 0 : selectedIndex1 + 1, visible.Count)}[/]"))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            var key = Console.ReadKey(true);

                            switch (key.Key)
                            {
                                case ConsoleKey.Tab when key.Modifiers.HasFlag(ConsoleModifiers.Shift):
                                    if (filter == IssueFilter.All)
                                        break;

                                    if (!invertedFilters.Add(filter))
                                        invertedFilters.Remove(filter);

                                    selectedIndex1 = 0;
                                    break;
                                case ConsoleKey.UpArrow:
                                    selectedIndex1 = selectedIndex1 == 0
                                        ? visible.Count - 1
                                        : selectedIndex1 - 1;
                                    break;
                                case ConsoleKey.DownArrow:
                                    selectedIndex1 = selectedIndex1 == visible.Count - 1
                                        ? 0
                                        : selectedIndex1 + 1;
                                    break;
                                case ConsoleKey.LeftArrow:
                                    selectedIndex1 = currentPage == 0
                                        ? (lastPage - 1) * pageSize1
                                        : firstRow - pageSize1;
                                    break;
                                case ConsoleKey.RightArrow:
                                    selectedIndex1 = currentPage == lastPage - 1
                                        ? 0
                                        : firstRow + pageSize1;
                                    break;
                                case ConsoleKey.Tab:
                                    filter = allFilters[(Array.IndexOf(allFilters, filter) + 1) % allFilters.Length];
                                    selectedIndex1 = 0;
                                    break;
                                case ConsoleKey.Enter when visible.Count > 0:
                                    selectedIssue = visible[selectedIndex1];
                                    running = false;
                                    break;
                                case ConsoleKey.N:
                                    creating = true;
                                    running = false;
                                    break;
                                case ConsoleKey.Escape:
                                    selectedIndex1 = -1;
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }
                    });

                if (creating)
                {
                    AnsiConsole.Clear();
                    ProjectsMenu.PrintHeader(guid);

                    var newTitle = AnsiConsole.Ask<string>(Strings.GH_NewIssue_AskTitle).Trim();

                    if (string.IsNullOrWhiteSpace(newTitle))
                    {
                        UI.Error(Strings.GH_TitleEmpty, Strings.GH_NewIssue_Title);
                        Console.ReadKey();
                        return;
                    }

                    var newBody = AnsiConsole.Prompt(
                        new TextPrompt<string>(string.Format(Strings.GH_NewIssue_AskBody, $"[{Color.Grey}]{Strings.GH_NewIssue_BodyHint}[/]"))
                            .AllowEmpty());

                    GitHubIssue? created = null;
                    string? failure = null;

                    await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .StartAsync(Strings.GH_CreatingIssue, async ctx =>
                            (created, failure) = await GitHubCalls.CreateIssue(guid, newTitle, newBody)
                        );

                    UI.FlushInput();

                    if (created == null)
                        UI.Error($"{Strings.GH_IssueNotCreated}\n{Markup.Escape(failure ?? Strings.GitHub_UnknownError)}", Strings.GH_NewIssue_Title);
                    else
                        UI.Success(string.Format(Strings.GH_IssueCreated, $"[{Color.Aqua}]#{created.Number}[/]"), Strings.GH_NewIssue_Title);

                    Console.ReadKey();
                    AnsiConsole.Clear();
                    continue;
                }

                if (selectedIndex1 == -1 || selectedIssue == null)
                    return;

                AnsiConsole.Clear();

                var rootLayout2 = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(2),
                        new Layout("Details").Size(8),
                        new Layout("Divider1").Size(3),
                        new Layout("Comments"),
                        new Layout("Divider2").Size(3),
                        new Layout("Footer").Size(3)
                    );

                var header2 = new Rule($"[bold {Color.Blue}]{string.Format(Strings.GH_IssueTitle, selectedIssue.Number)}[/]").LeftJustified();

                rootLayout2["Header"].Update(new Padder(header2, new Spectre.Console.Padding(0, 0, 0, 1)));

                List<Panel> issueComments = new();
                List<string> commentBodies = new();
                List<int[]> commentLineHeights = new();
                List<GitHubComment?> commentData = new();
                List<GitHubEvent?> eventData = new();

                var originalBody = UI.MarkdownToMarkup(selectedIssue.Body, guid);

                var originalComment = new Panel(originalBody)
                    .Header($"\u2800[{Color.White}]{UI.Link(selectedIssue.User?.HtmlUrl ?? "https://github.com/404", selectedIssue.User?.Login ?? Strings.GH_UnknownAuthor)} • [{DetermineColor(selectedIssue.AuthorAssociation)}]{(selectedIssue.AuthorAssociation == "NONE" ? "USER" : selectedIssue.AuthorAssociation)}[/] • {ConvertDateTimeOffsetToText(selectedIssue.CreatedAt)} • [bold {Color.Red3_1}]{Strings.GH_OpeningPost}[/][/]\u2800")
                    .BorderColor(Color.SkyBlue1)
                    .RoundedBorder();

                originalComment.Width = Console.WindowWidth;

                issueComments.Add(originalComment);
                commentBodies.Add(originalBody);
                commentLineHeights.Add(MeasureLines(originalBody));
                commentData.Add(null);
                eventData.Add(null);

                List<GitHubComment>? comments = null;
                List<GitHubEvent>? events = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(Strings.GH_RetrievingComments, async ctx =>
                    {
                        comments = await GitHubCalls.GetAllComments(guid, selectedIssue.Number);
                        events = await GitHubCalls.GetAllEvents(guid, selectedIssue.Number);

                        (comments ?? [])
                            .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)x, Happened: (GitHubEvent?)null))
                            .Concat((events ?? [])
                                .Where(x => DescribeEvent(x) != null)
                                .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)null, Happened: (GitHubEvent?)x)))
                            .OrderBy(x => x.Date)
                            .ToList()
                            .ForEach(y =>
                        {
                            if (y.Happened is { } happened)
                            {
                                var described = DescribeEvent(happened)!;

                                issueComments.Add(BuildEventPanel(happened, described, false));
                                commentBodies.Add(described);
                                commentLineHeights.Add(MeasureLines(described));
                                commentData.Add(null);
                                eventData.Add(happened);

                                return;
                            }

                            var x = y.Comment!;

                            var body = UI.MarkdownToMarkup(x.Body, guid);

                            var panel = new Panel(body)
                                .Header($"\u2800[{Color.White}]{UI.Link(x.User?.HtmlUrl ?? "https://github.com/404", x.User?.Login ?? Strings.GH_UnknownAuthor)} • [{DetermineColor(x.AuthorAssociation)}]{(x.AuthorAssociation == "NONE" ? "USER" : x.AuthorAssociation)}[/] • {ConvertDateTimeOffsetToText(x.CreatedAt)}[/]\u2800")
                                .BorderColor(string.Equals(x.User?.Login, selectedIssue.User?.Login, StringComparison.OrdinalIgnoreCase) ? Color.SkyBlue1 : Color.Grey)
                                .RoundedBorder();

                            panel.Width = Console.WindowWidth;

                            issueComments.Add(panel);
                            commentBodies.Add(body);
                            commentLineHeights.Add(MeasureLines(body));
                            commentData.Add(x);
                            eventData.Add(null);
                        });
                    }
                    );

                UI.FlushInput();

                if (comments == null)
                {
                    UI.Error(Strings.GH_CommentsLoadFailed + "\n" + Strings.GH_CheckAndRetry, Strings.GH_Issues);
                    Console.ReadKey();
                    AnsiConsole.Clear();
                    continue;
                }

                var currentPage = 0;
                var selectedComment = 0;
                var lastWidth = Console.WindowWidth;

                await AnsiConsole.Live(rootLayout2)
                   .StartAsync(async ctx =>
                   {
                       RefreshDetails();

                       void RefreshDetails()
                       {
                           var details = new Grid()
                           .AddColumn(new GridColumn())
                           .AddColumn(new GridColumn())
                           .AddColumn(new GridColumn())
                           .AddColumn(new GridColumn())
                           .AddColumn(new GridColumn())
                           .AddRow($"[{Color.Grey}]{Strings.GH_Col_State}[/]", (selectedIssue.State, selectedIssue.StateReason) switch
                           {
                               (null, null) => Strings.Common_None,
                               ("open", _) => $"[{Color.Green3_1}]● {Strings.GH_State_Open}[/]",
                               ("closed", "completed") => $"[{Color.Red3_1}]● {Strings.GH_State_Closed}[/]",
                               ("closed", "duplicate") => $"[{Color.Yellow3_1}]● {Strings.GH_State_Duplicate}[/]",
                               ("closed", "not_planned") => $"[{Color.LightSteelBlue}]● {Strings.GH_State_NotPlanned}[/]",
                               _ => Strings.Common_Unknown
                           })
                           .AddRow($"[{Color.Grey}]{Strings.GH_Col_Locked}[/]", $"{(selectedIssue.Locked ? $"[{Color.Green3_1}]✓[/]" : $"[{Color.Red3_1}]✗[/]")}")
                           .AddRow($"[{Color.Grey}]{Strings.GH_Row_Author}[/]", $"{UI.Link(selectedIssue.User?.HtmlUrl ?? "https://github.com", selectedIssue.User?.Login ?? Strings.GH_UnknownAuthor)}")
                           .AddRow($"[{Color.Grey}]{Strings.GH_Row_Opened}[/]", selectedIssue.CreatedAt.ToString("g", CultureInfo.CurrentCulture))
                           .AddRow($"[{Color.Grey}]{Strings.GH_Col_Labels}[/]", selectedIssue.Labels.Count > 0
                               ? string.Join(", ", selectedIssue.Labels.Select(x => $"[#{Tags.SafeHex(x.Color)}]{Markup.Escape(x.Name!)}[/]"))
                               : Strings.Common_None)
                           .AddRow($"[{Color.Grey}]{Strings.GH_Row_Assignees}[/]", selectedIssue.Assignees.Count > 0
                               ? string.Join(", ", selectedIssue.Assignees.Select(x => $"{UI.Link(x.HtmlUrl, Markup.Escape(x.Login ?? ""))}"))
                               : Strings.Common_None);

                           rootLayout2["Details"].Update(new Panel(details)
                           .Header($"\u2800[bold {Color.SteelBlue1}]{UI.Link(selectedIssue.HtmlUrl, Markup.Escape(selectedIssue.Title ?? ""))}[/]\u2800")
                           .RoundedBorder()
                           .Expand());
                       }

                       rootLayout2["Divider1"].Update(
                           new Padder(new Rule().RuleStyle(Style.Parse("Turquoise2")), new Spectre.Console.Padding(0, 1, 0, 1)));

                       rootLayout2["Divider2"].Update(
                           new Padder(new Rule().RuleStyle(Style.Parse("Turquoise2")), new Spectre.Console.Padding(0, 1, 0, 1)));

                       var running = true;

                       while (running)
                       {
                           if (lastWidth != Console.WindowWidth)
                           {
                               lastWidth = Console.WindowWidth;
                               commentLineHeights = commentBodies.Select(MeasureLines).ToList();
                           }

                           var commentsHeight = Math.Max(3, Console.WindowHeight - 19);

                           var pages = new List<List<(int Index, int Offset, int Count, bool Continues)>>();
                           var rows = new List<(int Index, int Offset, int Count, bool Continues)>();
                           var used = 0;
                           var index = 0;
                           var offset = 0;

                           while (index < commentBodies.Count)
                           {
                               var heights = commentLineHeights[index];
                               var remaining = heights.Length - offset;

                               if (remaining <= 0)
                               {
                                   index++;
                                   offset = 0;
                                   continue;
                               }

                               var frame = 2 + (offset > 0 ? 1 : 0);
                               var rest = heights.Skip(offset).Sum();

                               if (used + frame + rest <= commentsHeight)
                               {
                                   rows.Add((index, offset, remaining, false));
                                   used += frame + rest;
                                   index++;
                                   offset = 0;
                                   continue;
                               }

                               var budget = commentsHeight - used - frame - 1;
                               var taken = 0;
                               var count = 0;

                               while (count < remaining && taken + heights[offset + count] <= budget)
                               {
                                   taken += heights[offset + count];
                                   count++;
                               }

                               if (count == 0 && rows.Count == 0)
                                   count = 1;

                               if (count > 0)
                                   rows.Add((index, offset, count, true));

                               pages.Add(rows);
                               rows = [];
                               used = 0;
                               offset += count;
                           }

                           if (rows.Count > 0 || pages.Count == 0)
                               pages.Add(rows);

                           var selected = Math.Clamp(selectedComment, 0, issueComments.Count - 1);
                           currentPage = Math.Clamp(currentPage, 0, pages.Count - 1);

                           if (pages[currentPage].All(x => x.Index != selected))
                               currentPage = Math.Max(0, pages.FindIndex(x => x.Any(y => y.Index == selected)));

                           var onPage = pages[currentPage].Select(x => x.Index).ToList();

                           if (!onPage.Contains(selected))
                               selected = onPage.Count > 0 ? onPage[0] : 0;

                           selectedComment = selected;

                           var page = pages[currentPage]
                               .Select(x => BuildPanel(x.Index, selected, commentBodies[x.Index].Split('\n').Skip(x.Offset).Take(x.Count).ToArray(), x.Offset > 0, x.Continues))
                               .ToList();

                           var selectedData = commentData[selectedComment];

                           rootLayout2["Comments"].Update(new Rows(page));

                           rootLayout2["Footer"].Update(new Panel(
                                   new Grid()
                                       .AddColumn()
                                       .AddColumn(new GridColumn().RightAligned())
                                       .Expand()
                                       .AddRow(
                                            $"[{Color.Grey}]{Strings.GH_Key_Refresh}" +
                                            $"{Strings.GH_Footer_Issue}" +
                                            $"[{(selectedData == null ? "grey50" : "grey")}]{Strings.GH_Key_Delete}[/]  " +
                                            $"{(selectedIssue.State == "open" ? Strings.GH_Key_Close : Strings.GH_Key_Reopen)}  " +
                                            $"{(selectedIssue.Locked ? Strings.GH_Key_Unlock : Strings.GH_Key_Lock)}  {Strings.GH_Key_Browser}[/]",
                                            $"[{Color.Grey}]{string.Format(Strings.GH_PageComment, currentPage + 1, pages.Count, selectedComment + 1, issueComments.Count)}[/]"))
                               .RoundedBorder()
                               .Expand()
                               .Padding(1, 0));

                           ctx.Refresh();

                           var key = Console.ReadKey(true);

                           switch (key.Key)
                           {
                               case ConsoleKey.DownArrow when onPage.Count > 0 && selectedComment < onPage[^1]:
                                   selectedComment++;
                                   break;
                               case ConsoleKey.UpArrow when onPage.Count > 0 && selectedComment > onPage[0]:
                                   selectedComment--;
                                   break;
                               case ConsoleKey.DownArrow or ConsoleKey.RightArrow when currentPage + 1 < pages.Count:
                                   currentPage++;
                                   selectedComment = pages[currentPage][0].Index;
                                   break;
                               case ConsoleKey.UpArrow or ConsoleKey.LeftArrow when currentPage > 0:
                                   currentPage--;
                                   selectedComment = pages[currentPage][^1].Index;
                                   break;
                               case ConsoleKey.F5:
                                   await Reload();
                                   break;

                               case ConsoleKey.C:
                                   {
                                       var text = ComposeText(Strings.GH_NewComment, "");

                                       if (text == null)
                                           break;

                                       Report($"[italic {Color.Grey}]{Strings.GH_SendingComment}[/]", false);

                                       var (posted, error) = await GitHubCalls.CommentOnIssue(guid, selectedIssue.Number, text);

                                       if (posted == null)
                                       {
                                           Report($"[{Color.Red3_1}]{Strings.GH_CommentNotPosted}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                           break;
                                       }

                                       AppendComment(posted);
                                       break;
                                   }
                               case ConsoleKey.E when selectedComment == 0 || selectedData != null:
                                   {
                                       var edited = ComposeText(
                                           selectedData == null ? Strings.GH_EditIssue : Strings.GH_EditComment,
                                           selectedData?.Body ?? selectedIssue.Body ?? "");

                                       if (edited == null)
                                           break;

                                       Report($"[italic {Color.Grey}]{Strings.GH_Saving}[/]", false);

                                       if (selectedData == null)
                                       {
                                           var (changed, error) = await GitHubCalls.EditIssue(guid, selectedIssue.Number, selectedIssue.Title ?? "", edited);

                                           if (changed == null)
                                           {
                                               Report($"[{Color.Red3_1}]{Strings.GH_IssueNotSaved}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                               break;
                                           }

                                           selectedIssue = changed;
                                           RefreshDetails();
                                           ReplaceBody(0, edited);
                                       }
                                       else
                                       {
                                           var (changed, error) = await GitHubCalls.EditIssueComment(guid, selectedData.Id, edited);

                                           if (changed == null)
                                           {
                                               Report($"[{Color.Red3_1}]{Strings.GH_CommentNotSaved}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                               break;
                                           }

                                           commentData[selectedComment] = changed;
                                           ReplaceBody(selectedComment, changed.Body ?? "");
                                       }

                                       break;
                                   }
                               case ConsoleKey.D when selectedData != null:
                                   {
                                       if (!Ask(Strings.GH_DeleteCommentConfirm))
                                           break;

                                       Report($"[italic {Color.Grey}]{Strings.GH_Deleting}[/]", false);

                                       var (deleted, error) = await GitHubCalls.DeleteIssueComment(guid, selectedData.Id);

                                       if (!deleted)
                                       {
                                           Report($"[{Color.Red3_1}]{Strings.GH_CommentNotDeleted}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                           break;
                                       }

                                       issueComments.RemoveAt(selectedComment);
                                       commentBodies.RemoveAt(selectedComment);
                                       commentLineHeights.RemoveAt(selectedComment);
                                       commentData.RemoveAt(selectedComment);
                                       eventData.RemoveAt(selectedComment);

                                       selectedComment = Math.Min(selectedComment, issueComments.Count - 1);
                                       break;
                                   }
                               case ConsoleKey.X:
                                   {
                                       var closing = selectedIssue.State == "open";

                                       var reason = !closing
                                           ? ""
                                           : Choose(Strings.GH_WhyClosed, ["completed", "not_planned", "duplicate"]);

                                       if (reason == null)
                                           break;

                                       Report($"[italic {Color.Grey}]{Strings.GH_Talking}[/]", false);

                                       var (changed, error) = closing
                                           ? await GitHubCalls.CloseIssue(guid, selectedIssue.Number, reason)
                                           : await GitHubCalls.ReopenIssue(guid, selectedIssue.Number);

                                       if (changed == null)
                                       {
                                           Report($"[{Color.Red3_1}]{Strings.GH_IssueNotChanged}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                           break;
                                       }

                                       selectedIssue = changed;
                                       RefreshDetails();
                                       break;
                                   }
                               case ConsoleKey.K:
                                   {
                                       var locking = !selectedIssue.Locked;

                                       var reason = !locking
                                           ? ""
                                           : Choose(Strings.GH_WhyLocked, ["resolved", "off-topic", "too heated", "spam"]);

                                       if (reason == null)
                                           break;

                                       Report($"[italic {Color.Grey}]{Strings.GH_Talking}[/]", false);

                                       var (done, error) = locking
                                           ? await GitHubCalls.LockIssue(guid, selectedIssue.Number, reason)
                                           : await GitHubCalls.UnlockIssue(guid, selectedIssue.Number);

                                       if (!done)
                                       {
                                           Report($"[{Color.Red3_1}]{Strings.GH_IssueNotChanged}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                           break;
                                       }

                                       selectedIssue.Locked = locking;
                                       RefreshDetails();
                                       break;
                                   }
                               case ConsoleKey.O:
                                   {
                                       try
                                       {
                                           System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                           {
                                               FileName = selectedIssue.HtmlUrl ?? "https://github.com",
                                               UseShellExecute = true
                                           });
                                       }
                                       catch (Exception ex)
                                       {
                                           Report($"[{Color.Red3_1}]{Strings.GH_BrowserFailed}[/] {Markup.Escape(ex.Message)}", true);
                                       }

                                       break;
                                   }
                               case ConsoleKey.Escape:
                                   running = false;
                                   break;
                           }

                           UI.FlushInput();
                       }

                       void Report(string message, bool wait)
                       {
                           rootLayout2["Footer"].Update(new Panel(
                                   new Markup(wait ? message + $"   [{Color.Grey}]{Strings.GH_AnyKey}[/]" : message))
                               .RoundedBorder()
                               .Expand()
                               .Padding(1, 0));

                           ctx.Refresh();

                           if (wait)
                               Console.ReadKey(true);
                       }

                       bool Ask(string question)
                       {
                           var yes = false;

                           while (true)
                           {
                               rootLayout2["Footer"].Update(new Panel(
                                       new Markup($"{Markup.Escape(question)}   " +
                                                  $"[{(yes ? "Red3_1" : "grey")}]{Strings.GH_Yes}[/]   [{(yes ? "grey" : "Aqua")}]{Strings.GH_No}[/]   " +
                                                  $"[{Color.Grey}]{Strings.GH_ChooseHint}[/]"))
                                   .RoundedBorder()
                                   .Expand()
                                   .Padding(1, 0));

                               ctx.Refresh();

                               switch (Console.ReadKey(true).Key)
                               {
                                   case ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Tab:
                                       yes = !yes;
                                       break;
                                   case ConsoleKey.Enter:
                                       return yes;
                                   case ConsoleKey.Escape:
                                       return false;
                               }
                           }
                       }

                       string? Choose(string question, string[] options)
                       {
                           var picked = 0;

                           while (true)
                           {
                               rootLayout2["Footer"].Update(new Panel(
                                       new Markup($"{Markup.Escape(question)}   " +
                                                  string.Join("   ", options.Select((x, i) =>
                                                      $"[{(i == picked ? "Aqua" : "grey")}]{Markup.Escape(x)}[/]")) +
                                                  $"   [{Color.Grey}]{Strings.GH_ChooseHint}[/]"))
                                   .RoundedBorder()
                                   .Expand()
                                   .Padding(1, 0));

                               ctx.Refresh();

                               switch (Console.ReadKey(true).Key)
                               {
                                   case ConsoleKey.LeftArrow:
                                       picked = picked == 0 ? options.Length - 1 : picked - 1;
                                       break;
                                   case ConsoleKey.RightArrow or ConsoleKey.Tab:
                                       picked = (picked + 1) % options.Length;
                                       break;
                                   case ConsoleKey.Enter:
                                       return options[picked];
                                   case ConsoleKey.Escape:
                                       return null;
                               }
                           }
                       }

                       void ReplaceBody(int index, string raw)
                       {
                           var body = UI.MarkdownToMarkup(raw, guid);

                           var rebuilt = new Panel(body)
                           {
                               Width = Console.WindowWidth,
                               Header = issueComments[index].Header,
                               BorderStyle = issueComments[index].BorderStyle
                           };

                           rebuilt.RoundedBorder();

                           issueComments[index] = rebuilt;
                           commentBodies[index] = body;
                           commentLineHeights[index] = MeasureLines(body);
                       }

                       async Task Reload()
                       {
                           rootLayout2["Comments"].Update(
                               new Markup($"\n    [{Color.Grey}]{Strings.GH_RetrievingComments}[/]"));

                           ctx.Refresh();

                           var fresh = await GitHubCalls.GetAllComments(guid, selectedIssue.Number);
                           var freshEvents = await GitHubCalls.GetAllEvents(guid, selectedIssue.Number);

                           if (fresh == null)
                               return;

                           issueComments.RemoveRange(1, issueComments.Count - 1);
                           commentBodies.RemoveRange(1, commentBodies.Count - 1);
                           commentLineHeights.RemoveRange(1, commentLineHeights.Count - 1);
                           commentData.RemoveRange(1, commentData.Count - 1);
                           eventData.RemoveRange(1, eventData.Count - 1);

                           fresh
                               .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)x, Happened: (GitHubEvent?)null))
                               .Concat((freshEvents ?? [])
                                   .Where(x => DescribeEvent(x) != null)
                                   .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)null, Happened: (GitHubEvent?)x)))
                               .OrderBy(x => x.Date)
                               .ToList()
                               .ForEach(x =>
                               {
                                   if (x.Happened is { } happened)
                                       AppendEvent(happened);
                                   else
                                       AppendComment(x.Comment!);
                               });

                           selectedComment = Math.Clamp(selectedComment, 0, issueComments.Count - 1);
                           currentPage = 0;
                       }

                       void AppendEvent(GitHubEvent happened)
                       {
                           var described = DescribeEvent(happened)!;

                           issueComments.Add(BuildEventPanel(happened, described, false));
                           commentBodies.Add(described);
                           commentLineHeights.Add(MeasureLines(described));
                           commentData.Add(null);
                           eventData.Add(happened);
                       }

                       void AppendComment(GitHubComment posted)
                       {
                           var body = UI.MarkdownToMarkup(posted.Body, guid);

                           var panel = new Panel(body)
                               .Header($"⠀[{Color.White}]{UI.Link(posted.User?.HtmlUrl ?? "https://github.com/404", Markup.Escape(posted.User?.Login ?? Strings.GH_UnknownAuthor))} • [{DetermineColor(posted.AuthorAssociation)}]{(posted.AuthorAssociation == "NONE" ? "USER" : posted.AuthorAssociation)}[/] • {ConvertDateTimeOffsetToText(posted.CreatedAt)}[/]⠀")
                               .BorderColor(Color.SkyBlue1)
                               .RoundedBorder();

                           panel.Width = Console.WindowWidth;

                           issueComments.Add(panel);
                           commentBodies.Add(body);
                           commentLineHeights.Add(MeasureLines(body));
                           commentData.Add(posted);
                           eventData.Add(null);

                           selectedComment = issueComments.Count - 1;
                       }

                       string? ComposeText(string title, string initial)
                       {
                           UI.FlushInput();

                           StringBuilder content = new(initial);
                           var cursorPos = content.Length;

                           var bold = false;
                           var italic = false;
                           var code = false;
                           var sending = false;
                           var writing = true;

                           while (writing)
                           {
                               rootLayout2["Comments"].Update(new Panel(content.Length == 0
                                        ? $"[italic {Color.Grey}]{Strings.GH_WriteHint}[/]"
                                        : Markup.Escape(content.ToString(0, cursorPos)) +
                                    $"[{Color.SkyBlue1}]|[/]" +
                                    Markup.Escape(content.ToString(cursorPos, content.Length - cursorPos)))
                               .Header($"⠀[{Color.White}]{Markup.Escape(title)}[/]⠀")
                               .BorderColor(Color.SkyBlue1)
                               .RoundedBorder()
                               .Expand());

                               rootLayout2["Footer"].Update(new Panel(
                                   new Grid()
                                       .AddColumn()
                                       .AddColumn(new GridColumn().RightAligned())
                                       .Expand()
                                       .AddRow(
                                           $"[{(bold ? "SkyBlue1" : "grey")}]{Strings.GH_Editor_Bold}[/]   " +
                                           $"[{(italic ? "SkyBlue1" : "grey")}]{Strings.GH_Editor_Italic}[/]   " +
                                           $"[{(code ? "SkyBlue1" : "grey")}]{Strings.GH_Editor_Code}[/]   " +
                                           $"[{Color.Grey}]{Strings.GH_Editor_Footer}[/]",
                                           $"[{Color.Grey}]{string.Format(Strings.GH_Characters, content.Length)}[/]"))
                               .RoundedBorder()
                               .Expand()
                               .Padding(1, 0));

                               ctx.Refresh();

                               var input = Console.ReadKey(true);

                               if (input.Modifiers.HasFlag(ConsoleModifiers.Control))
                               {
                                   switch (input.Key)
                                   {
                                       case ConsoleKey.B:
                                           content.Insert(cursorPos, "**");
                                           cursorPos += 2;
                                           bold = !bold;
                                           break;
                                       case ConsoleKey.E:
                                           content.Insert(cursorPos, '*');
                                           cursorPos += 1;
                                           italic = !italic;
                                           break;
                                       case ConsoleKey.K:
                                           content.Insert(cursorPos, '`');
                                           cursorPos += 1;
                                           code = !code;
                                           break;
                                       case ConsoleKey.L:
                                           {
                                               var insert = cursorPos == 0 || content[cursorPos - 1] == '\n' ? "- " : "\n- ";
                                               content.Insert(cursorPos, insert);
                                               cursorPos += insert.Length;
                                               break;
                                           }
                                       case ConsoleKey.Q:
                                           {
                                               var insert = cursorPos == 0 || content[cursorPos - 1] == '\n' ? "> " : "\n> ";
                                               content.Insert(cursorPos, insert);
                                               cursorPos += insert.Length;
                                               break;
                                           }
                                   }
                                   continue;
                               }

                               switch (input.Key)
                               {
                                   case ConsoleKey.LeftArrow:
                                       if (cursorPos > 0)
                                           cursorPos--;
                                       break;
                                   case ConsoleKey.RightArrow:
                                       if (cursorPos < content.Length)
                                           cursorPos++;
                                       break;
                                   case ConsoleKey.Home:
                                       cursorPos = 0;
                                       break;
                                   case ConsoleKey.End:
                                       cursorPos = content.Length;
                                       break;
                                   case ConsoleKey.Escape:
                                       writing = false;
                                       break;
                                   case ConsoleKey.F2 when content.Length > 0:
                                       sending = true;
                                       writing = false;
                                       break;
                                   case ConsoleKey.Enter:
                                       content.Insert(cursorPos, '\n');
                                       cursorPos++;
                                       break;
                                   case ConsoleKey.Backspace when cursorPos > 0:
                                       content.Remove(cursorPos - 1, 1);
                                       cursorPos--;
                                       break;
                                   case ConsoleKey.Delete when cursorPos < content.Length:
                                       content.Remove(cursorPos, 1);
                                       break;
                                   default:
                                       if (!char.IsControl(input.KeyChar))
                                       {
                                           content.Insert(cursorPos, input.KeyChar);
                                           cursorPos++;
                                       }
                                       break;
                               }
                           }

                           return sending ? content.ToString() : null;
                       }

                       Panel BuildPanel(int index, int selected, string[] lines, bool continued, bool continues)
                       {
                           if (eventData[index] is { } happened)
                               return BuildEventPanel(happened, commentBodies[index], index == selected);

                           var body = string.Join("\n", lines);

                           if (continued)
                               body = $"[italic {Color.Grey}]{Strings.GH_ContinuedFrom}[/]\n" + body;

                           if (continues)
                               body += $"\n[italic {Color.Grey}]{Strings.GH_ContinuesOn}[/]";

                           var panel = new Panel(body)
                           {
                               Width = Console.WindowWidth,
                               Header = issueComments[index].Header,
                               BorderStyle = issueComments[index].BorderStyle
                           };

                           if (index == selected)
                               panel.DoubleBorder();
                           else
                               panel.RoundedBorder();

                           return panel;
                       }
                   });
                AnsiConsole.Clear();
                continue;
            }
        }

        private static async Task ManagePullRequests(Guid guid)
        {
            while (true)
            {
                var project = GetProject(guid);

                if (!Repository.IsValid(project.Path))
                {
                    UI.Error(Strings.Common_NotAGitRepo + "\n" + Strings.Common_InitGitHint, Strings.GH_PullRequests);
                    Console.ReadKey();
                    return;
                }

                if (!GitHubCalls.IsAuthorizedWithGitHub())
                {
                    UI.Error(Strings.GH_NeedAuth, Strings.GH_PullRequests);
                    Console.ReadKey();
                    return;
                }

                if (!GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.GH_PullRequests))
                    return;

                var lookup = GitHubCalls.RepoExistsOnUsersGitHubProfile(guid)
                    .GetAwaiter().GetResult();

                if (lookup != GitHubCalls.RepoLookup.Found)
                {
                    UI.Error(GitHubCalls.DescribeLookup(lookup), Strings.GH_PullRequests);
                    Console.ReadKey();
                    return;
                }

                List<GitHubPullRequest>? pulls = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Line)
                    .StartAsync(Strings.GH_RetrievingPulls, async ctx =>
                        pulls = await GitHubCalls.GetAllPullRequests(guid)
                );

                UI.FlushInput();

                if (pulls == null)
                {
                    UI.Error(Strings.GH_PullsLoadFailed + "\n" + Strings.GH_CheckAndRetry, Strings.GH_PullRequests);
                    Console.ReadKey();
                    return;
                }

                if (pulls.Count == 0)
                {
                    UI.Info(Strings.GH_NoPulls, Strings.GH_PullRequests);
                    Console.ReadKey();
                    return;
                }

                AnsiConsole.Clear();

                var rootLayout1 = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(1),
                        new Layout("Filters").Size(3),
                        new Layout("List"),
                        new Layout("Footer").Size(3)
                    );

                var header1 = new Rule($"[bold {Color.Blue}]{Strings.GH_PullRequests} · {Markup.Escape(GetProject(guid).GitHubName)}[/]")
                    .LeftJustified();

                rootLayout1["Header"].Update(header1);

                var allFilters = Enum.GetValues<PullRequestFilter>();

                var filter = PullRequestFilter.Open;

                var invertedFilters = new HashSet<PullRequestFilter>();

                var selectedIndex1 = 0;

                var pageSize1 = Math.Max(2, (Console.WindowHeight - 10) / 2);

                GitHubPullRequest? selectedPull = null;

                AnsiConsole.Live(rootLayout1)
                    .Start(ctx =>
                    {
                        var running = true;

                        while (running)
                        {
                            var visible = filter == PullRequestFilter.All
                                ? pulls
                                : pulls.Where(x => filter switch
                                {
                                    PullRequestFilter.Open => x.State == "open",
                                    PullRequestFilter.Drafts => x.Draft,
                                    PullRequestFilter.Assigned => x.Assignees.Count > 0,
                                    PullRequestFilter.Reviewed => x.RequestedReviewers.Count > 0,
                                    _ => true
                                } != invertedFilters.Contains(filter))
                            .ToList();

                            selectedIndex1 = visible.Count == 0
                                ? 0
                                : Math.Clamp(selectedIndex1, 0, visible.Count - 1);

                            var lastPage = Math.Max(1, (int)Math.Ceiling(visible.Count / (double)pageSize1));

                            var currentPage = selectedIndex1 / pageSize1;
                            var firstRow = currentPage * pageSize1;
                            var lastRow = Math.Min(firstRow + pageSize1, visible.Count);

                            var cellsFilters = allFilters.SelectMany(x =>
                            {
                                var isInverted = invertedFilters.Contains(x);

                                var name = isInverted
                                    ? x switch
                                    {
                                        PullRequestFilter.Open => Strings.GH_Filter_Closed,
                                        PullRequestFilter.Drafts => Strings.GH_Filter_Ready,
                                        PullRequestFilter.Assigned => Strings.GH_Filter_Unassigned,
                                        PullRequestFilter.Reviewed => Strings.GH_Filter_NoReviewers,
                                        _ => Strings.GH_Filter_All
                                    }
                                    : x switch
                                    {
                                        PullRequestFilter.Open => Strings.GH_Filter_Open,
                                        PullRequestFilter.Drafts => Strings.GH_Filter_Drafts,
                                        PullRequestFilter.Assigned => Strings.GH_Filter_Assigned,
                                        PullRequestFilter.Reviewed => Strings.GH_Filter_Reviewed,
                                        _ => Strings.GH_Filter_All
                                    };

                                var color = x != filter
                                    ? "grey"
                                    : isInverted
                                        ? "red"
                                        : "Aqua";

                                return new[] { $"[{Color.Grey}]•[/]", $"[{color}]{name}[/]" };
                            })
                            .Skip(1)
                            .ToArray();

                            var filters = new Grid()
                                .AddColumns(cellsFilters
                                    .Select((_, i) => i % 2 == 0
                                        ? new GridColumn().Centered()
                                        : new GridColumn().Centered().Width(3))
                                    .ToArray())
                                .Expand()
                                .AddRow(cellsFilters);

                            rootLayout1["Filters"].Update(new Panel(filters)
                                .RoundedBorder()
                                .BorderColor(Color.Grey)
                                .Expand()
                                );

                            var list = new Table()
                                .Border(TableBorder.Rounded)
                                .ShowRowSeparators()
                                .AddColumn(new TableColumn("").Width(2))
                                .AddColumn($"[{Color.Grey}]#[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_State}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Title}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Branch}[/]")
                                .AddColumn($"[{Color.Grey}]{Strings.GH_Col_Updated}[/]")
                                .Expand();

                            for (int i = firstRow; i < lastRow; i++)
                            {
                                var pull = visible[i];

                                var head = pull.Head?.Ref ?? "";
                                var target = pull.Base?.Ref ?? "";
                                var branches = $"{head[(head.LastIndexOf('/') + 1)..]} → {target[(target.LastIndexOf('/') + 1)..]}";

                                if (i == selectedIndex1)
                                    list.AddRow(
                                        $"[{Color.Aqua}]▸[/]",
                                        $"[{Color.Aqua}]{pull.Number}[/]",
                                        (pull.MergedAt, pull.State, pull.Draft) switch
                                        {
                                            (not null, _, _) => $"[{Color.Aqua}]● {Strings.GH_State_Merged}[/]",
                                            (_, "open", true) => $"[{Color.Aqua}]● {Strings.GH_State_Draft}[/]",
                                            (_, "open", _) => $"[{Color.Aqua}]● {Strings.GH_State_Open}[/]",
                                            (_, "closed", _) => $"[{Color.Aqua}]● {Strings.GH_State_Closed}[/]",
                                            _ => Strings.Common_Unknown
                                        },
                                        $"[{Color.Aqua}]{Markup.Escape(pull.Title ?? "")}[/]",
                                        $"[{Color.Aqua}]{Markup.Escape(branches)}[/]",
                                        $"[{Color.Aqua}]{ConvertDateTimeOffsetToText(pull.UpdatedAt.ToLocalTime())}[/]");
                                else
                                    list.AddRow(
                                        "",
                                        pull.Number.ToString(),
                                        (pull.MergedAt, pull.State, pull.Draft) switch
                                        {
                                            (not null, _, _) => $"[{Color.MediumPurple1}]● {Strings.GH_State_Merged}[/]",
                                            (_, "open", true) => $"[{Color.Grey}]● {Strings.GH_State_Draft}[/]",
                                            (_, "open", _) => $"[{Color.Green3_1}]● {Strings.GH_State_Open}[/]",
                                            (_, "closed", _) => $"[{Color.Red3_1}]● {Strings.GH_State_Closed}[/]",
                                            _ => Strings.Common_Unknown
                                        },
                                        Markup.Escape(pull.Title ?? ""),
                                        Markup.Escape(branches),
                                        ConvertDateTimeOffsetToText(pull.UpdatedAt.ToLocalTime()));
                            }

                            rootLayout1["List"].Update(visible.Count == 0
                                ? new Markup($"\n    [{Color.Red3_1}]{Strings.GH_NothingMatches}[/]")
                                : list);

                            rootLayout1["Footer"].Update(new Panel(
                                new Grid()
                                .AddColumn()
                                .AddColumn(new GridColumn().RightAligned())
                                .Expand()
                                .AddRow(
                                    $"[{Color.Grey}]{Strings.GH_Footer_Pulls}[/]",
                                    $"[{Color.Grey}]{string.Format(Strings.GH_PagePull, visible.Count == 0 ? 0 : currentPage + 1, visible.Count == 0 ? 0 : lastPage, visible.Count == 0 ? 0 : selectedIndex1 + 1, visible.Count)}[/]"))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            var key = Console.ReadKey(true);

                            switch (key.Key)
                            {
                                case ConsoleKey.Tab when key.Modifiers.HasFlag(ConsoleModifiers.Shift):
                                    if (filter == PullRequestFilter.All)
                                        break;

                                    if (!invertedFilters.Add(filter))
                                        invertedFilters.Remove(filter);

                                    selectedIndex1 = 0;
                                    break;
                                case ConsoleKey.UpArrow:
                                    selectedIndex1 = selectedIndex1 == 0
                                        ? visible.Count - 1
                                        : selectedIndex1 - 1;
                                    break;
                                case ConsoleKey.DownArrow:
                                    selectedIndex1 = selectedIndex1 == visible.Count - 1
                                        ? 0
                                        : selectedIndex1 + 1;
                                    break;
                                case ConsoleKey.LeftArrow:
                                    selectedIndex1 = currentPage == 0
                                        ? (lastPage - 1) * pageSize1
                                        : firstRow - pageSize1;
                                    break;
                                case ConsoleKey.RightArrow:
                                    selectedIndex1 = currentPage == lastPage - 1
                                        ? 0
                                        : firstRow + pageSize1;
                                    break;
                                case ConsoleKey.Tab:
                                    filter = allFilters[(Array.IndexOf(allFilters, filter) + 1) % allFilters.Length];
                                    selectedIndex1 = 0;
                                    break;
                                case ConsoleKey.Enter when visible.Count > 0:
                                    selectedPull = visible[selectedIndex1];
                                    running = false;
                                    break;
                                case ConsoleKey.Escape:
                                    selectedIndex1 = -1;
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }
                    });

                if (selectedIndex1 == -1 || selectedPull == null)
                    return;

                AnsiConsole.Clear();

                var rootLayout2 = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(2),
                        new Layout("Details").Size(8),
                        new Layout("Divider1").Size(3),
                        new Layout("Comments"),
                        new Layout("Divider2").Size(3),
                        new Layout("Footer").Size(3)
                    );

                var header2 = new Rule($"[bold {Color.Blue}]{string.Format(Strings.GH_PullTitle, selectedPull.Number)}[/]")
                    .LeftJustified();

                rootLayout2["Header"].Update(new Padder(header2, new Spectre.Console.Padding(0, 0, 0, 1)));

                List<Panel> pullComments = new();
                List<string> commentBodies = new();
                List<int[]> commentLineHeights = new();
                List<GitHubComment?> commentData = new();
                List<GitHubEvent?> eventData = new();

                var me = await GitHubCalls.GetCachedUsername();

                List<GitHubPullRequestReview>? reviews = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(Strings.GH_RetrievingPull, async ctx =>
                    {
                        selectedPull = await GitHubCalls.GetPullRequest(guid, selectedPull.Number) ?? selectedPull;

                        reviews = await GitHubCalls.GetPullRequestReviews(guid, selectedPull.Number);
                    }
                );

                UI.FlushInput();

                if (reviews == null)
                {
                    AnsiConsole.Clear();
                    ProjectsMenu.PrintHeader(guid);

                    UI.Error(Strings.GH_ReviewsLoadFailed + "\n" + Strings.GH_CheckAndRetry, Strings.GH_PullRequests);
                    Console.ReadKey();
                    return;
                }

                var originalBody = UI.MarkdownToMarkup(selectedPull.Body, guid);

                var originalComment = new Panel(originalBody)
                    .Header($"\u2800[{Color.White}]{UI.Link(selectedPull.User?.HtmlUrl ?? "https://github.com/404", selectedPull.User?.Login ?? Strings.GH_UnknownAuthor)} • [{DetermineColor(selectedPull.AuthorAssociation)}]{(selectedPull.AuthorAssociation == "NONE" ? "USER" : selectedPull.AuthorAssociation)}[/] • {ConvertDateTimeOffsetToText(selectedPull.CreatedAt)} • [bold {Color.Red3_1}]{Strings.GH_OpeningPost}[/][/]\u2800")
                    .BorderColor(Color.SkyBlue1)
                    .RoundedBorder();

                originalComment.Width = Console.WindowWidth;

                pullComments.Add(originalComment);
                commentBodies.Add(originalBody);
                commentLineHeights.Add(MeasureLines(originalBody));
                commentData.Add(null);
                eventData.Add(null);

                List<GitHubComment>? comments = null;
                List<GitHubEvent>? events = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(Strings.GH_RetrievingComments, async ctx =>
                    {
                        comments = await GitHubCalls.GetAllComments(guid, selectedPull.Number);
                        events = await GitHubCalls.GetAllEvents(guid, selectedPull.Number);

                        (comments ?? [])
                            .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)x, Review: (GitHubPullRequestReview?)null, Happened: (GitHubEvent?)null))
                            .Concat(reviews
                                .Where(x => x.State != "PENDING")
                                .Select(x => (Date: x.SubmittedAt ?? DateTimeOffset.MinValue, Comment: (GitHubComment?)null, Review: (GitHubPullRequestReview?)x, Happened: (GitHubEvent?)null)))
                            .Concat((events ?? [])
                                .Where(x => DescribeEvent(x) != null)
                                .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)null, Review: (GitHubPullRequestReview?)null, Happened: (GitHubEvent?)x)))
                            .OrderBy(x => x.Date)
                            .ToList()
                            .ForEach(x =>
                            {
                                if (x.Happened is { } happened)
                                {
                                    var described = DescribeEvent(happened)!;

                                    pullComments.Add(BuildEventPanel(happened, described, false));
                                    commentBodies.Add(described);
                                    commentLineHeights.Add(MeasureLines(described));
                                    commentData.Add(null);
                                    eventData.Add(happened);

                                    return;
                                }

                                var body = x.Review == null
                                    ? UI.MarkdownToMarkup(x.Comment!.Body, guid)
                                    : $"[{x.Review.State switch
                                    {
                                        "APPROVED" => "Green3_1",
                                        "CHANGES_REQUESTED" => "Red3_1",
                                        _ => "grey"
                                    }}]{x.Review.State}[/]" + (string.IsNullOrEmpty(x.Review.Body)
                                        ? ""
                                        : $"\n{UI.MarkdownToMarkup(x.Review.Body, guid)}");

                                var author = x.Review == null
                                    ? x.Comment!.User
                                    : x.Review.User;

                                var association = x.Review == null
                                    ? x.Comment!.AuthorAssociation
                                    : x.Review.AuthorAssociation;

                                var panel = new Panel(body)
                                    .Header($"\u2800[{Color.White}]{UI.Link(author?.HtmlUrl ?? "https://github.com/404", author?.Login ?? Strings.GH_UnknownAuthor)} • [{DetermineColor(association)}]{(association == "NONE" ? "USER" : association)}[/] • {ConvertDateTimeOffsetToText(x.Date)}{(x.Review == null ? "" : $" • [bold {Color.Turquoise2}]{Strings.GH_Review}[/]")}[/]\u2800")
                                    .BorderColor(string.Equals(author?.Login, selectedPull.User?.Login, StringComparison.OrdinalIgnoreCase)
                                        ? Color.SkyBlue1
                                        : Color.Grey
                                    ).RoundedBorder();

                                panel.Width = Console.WindowWidth;

                                pullComments.Add(panel);
                                commentBodies.Add(body);
                                commentLineHeights.Add(MeasureLines(body));
                                commentData.Add(x.Comment);
                                eventData.Add(null);
                            });
                    }
                );

                UI.FlushInput();

                if (comments == null)
                {
                    UI.Error(Strings.GH_CommentsLoadFailed + "\n" + Strings.GH_CheckAndRetry, Strings.GH_PullRequests);
                    Console.ReadKey();
                    return;
                }

                var currentPage = 0;
                var selectedComment = 0;
                var lastWidth = Console.WindowWidth;

                await AnsiConsole.Live(rootLayout2)
                    .StartAsync(async ctx =>
                    {
                        RefreshDetails();

                        void RefreshDetails()
                        {
                            var reason = selectedPull.MergeableState switch
                            {
                                "dirty" => string.Format(Strings.GH_Reason_Dirty, selectedPull.Base?.Ref ?? Strings.GH_Reason_BaseBranch),
                                "blocked" => Strings.GH_Reason_Blocked,
                                "behind" => Strings.GH_Reason_Behind,
                                "unstable" => Strings.GH_Reason_Unstable,
                                "draft" => Strings.GH_Reason_Draft,
                                _ => selectedPull.MergeableState ?? Strings.Common_Unknown
                            };

                            var latest = reviews
                                .Where(x => x.State != "PENDING")
                                .GroupBy(x => x.User?.Login)
                                .Select(x => x.OrderByDescending(y => y.SubmittedAt).First())
                                .ToList();

                            var details = new Grid()
                                .AddColumn(new GridColumn())
                                .AddColumn(new GridColumn())
                                .AddRow($"[{Color.Grey}]{Strings.GH_Col_State}[/]", (selectedPull.MergedAt, selectedPull.State, selectedPull.Draft) switch
                                {
                                    (not null, _, _) => $"[{Color.MediumPurple1}]● {Strings.GH_State_Merged}[/]",
                                    (_, "open", true) => $"[{Color.Grey}]● {Strings.GH_State_Draft}[/]",
                                    (_, "open", _) => $"[{Color.Green3_1}]● {Strings.GH_State_Open}[/]",
                                    (_, "closed", _) => $"[{Color.Red3_1}]● {Strings.GH_State_Closed}[/]",
                                    _ => Strings.Common_Unknown
                                })
                                .AddRow($"[{Color.Grey}]{Strings.GH_Row_Author}[/]", $"{UI.Link(selectedPull.User?.HtmlUrl ?? "https://github.com", selectedPull.User?.Login ?? Strings.GH_UnknownAuthor)}")
                                .AddRow($"[{Color.Grey}]{Strings.GH_Row_Branches}[/]", $"{Markup.Escape(selectedPull.Head?.Ref ?? "")}  →  {Markup.Escape(selectedPull.Base?.Ref ?? "")}")
                                .AddRow($"[{Color.Grey}]{Strings.GH_Row_Changes}[/]", $"[{Color.Green3_1}]+{selectedPull.Additions}[/]  [{Color.Red3_1}]-{selectedPull.Deletions}[/] {string.Format(Strings.GH_ChangesDetail, selectedPull.ChangedFiles, selectedPull.Commits)}")
                                .AddRow($"[{Color.Grey}]{Strings.GH_Row_Mergeable}[/]", (selectedPull.MergedAt, selectedPull.Mergeable) switch
                                {
                                    (not null, _) => $"[{Color.MediumPurple1}]{Strings.GH_Merge_Merged}[/]",
                                    (_, null) => $"[{Color.Grey}]{Strings.GH_Merge_Computing}[/]",
                                    (_, true) => $"[{Color.Green3_1}]✓ {Strings.GH_Merge_Clean}[/]",
                                    _ => $"[{Color.Red3_1}]✗ {Markup.Escape(selectedPull.MergeableState ?? "")}[/] - {Markup.Escape(reason)}"
                                })
                                .AddRow($"[{Color.Grey}]{Strings.GH_Row_Reviews}[/]", latest.Count == 0
                                    ? Strings.Common_None
                                    : string.Join("   ", latest.Select(x => $"[{x.State switch
                                    {
                                        "APPROVED" => "Green3_1",
                                        "CHANGES_REQUESTED" => "Red3_1",
                                        _ => "grey"
                                    }}]{Markup.Escape(x.User?.Login ?? "")} {x.State}[/]"))
                                );

                            rootLayout2["Details"].Update(new Panel(details)
                                .Header($"\u2800[bold {Color.SteelBlue1}]{UI.Link(selectedPull.HtmlUrl, Markup.Escape(selectedPull.Title ?? ""))}[/]\u2800")
                                .RoundedBorder()
                                .Expand()
                            );
                        }

                        rootLayout2["Divider1"].Update(
                            new Padder(new Rule()
                                .RuleStyle(Style.Parse("Turquoise2")),
                                    new Spectre.Console.Padding(0, 1, 0, 1)));

                        rootLayout2["Divider2"].Update(
                            new Padder(new Rule()
                            .RuleStyle(Style.Parse("Turquoise2")),
                                new Spectre.Console.Padding(0, 1, 0, 1)));

                        var running = true;

                        while (running)
                        {
                            if (lastWidth != Console.WindowWidth)
                            {
                                lastWidth = Console.WindowWidth;
                                commentLineHeights = commentBodies.Select(MeasureLines).ToList();
                            }

                            var commentsHeight = Math.Max(3, Console.WindowHeight - 19);

                            var pages = new List<List<(int Index, int Offset, int Count, bool Continues)>>();
                            var rows = new List<(int Index, int Offset, int Count, bool Continues)>();
                            var used = 0;
                            var index = 0;
                            var offset = 0;

                            while (index < commentBodies.Count)
                            {
                                var heights = commentLineHeights[index];
                                var remaining = heights.Length - offset;

                                if (remaining <= 0)
                                {
                                    index++;
                                    offset = 0;
                                    continue;
                                }

                                var frame = 2 + (offset > 0 ? 1 : 0);
                                var rest = heights.Skip(offset).Sum();

                                if (used + frame + rest <= commentsHeight)
                                {
                                    rows.Add((index, offset, remaining, false));
                                    used += frame + rest;
                                    index++;
                                    offset = 0;
                                    continue;
                                }

                                var budget = commentsHeight - used - frame - 1;
                                var taken = 0;
                                var count = 0;

                                while (count < remaining && taken + heights[offset + count] <= budget)
                                {
                                    taken += heights[offset + count];
                                    count++;
                                }

                                if (count == 0 && rows.Count == 0)
                                    count = 1;

                                if (count > 0)
                                    rows.Add((index, offset, count, true));

                                pages.Add(rows);
                                rows = [];
                                used = 0;
                                offset += count;
                            }

                            if (rows.Count > 0 || pages.Count == 0)
                                pages.Add(rows);

                            var selected = Math.Clamp(selectedComment, 0, pullComments.Count - 1);
                            currentPage = Math.Clamp(currentPage, 0, pages.Count - 1);

                            if (pages[currentPage].All(x => x.Index != selected))
                                currentPage = Math.Max(0, pages.FindIndex(x => x.Any(y => y.Index == selected)));

                            var onPage = pages[currentPage].Select(x => x.Index).ToList();

                            if (!onPage.Contains(selected))
                                selected = onPage.Count > 0 ? onPage[0] : 0;

                            selectedComment = selected;

                            var page = pages[currentPage]
                                .Select(x => BuildPanel(x.Index, selected,
                                    commentBodies[x.Index].Split('\n').Skip(x.Offset).Take(x.Count).ToArray(),
                                    x.Offset > 0, x.Continues))
                                .ToList();

                            var selectedData = commentData[selectedComment];

                            var changesRequested = reviews
                                .Where(x => x.State != "PENDING")
                                .GroupBy(x => x.User?.Login)
                                .Select(x => x.OrderByDescending(y => y.SubmittedAt).First())
                                .Any(x => x.State == "CHANGES_REQUESTED");

                            var open = selectedPull.State == "open" && selectedPull.MergedAt == null;
                            var canMerge = open && selectedPull.Mergeable == true && !selectedPull.Draft && !changesRequested;
                            var mine = string.Equals(selectedPull.User?.Login, me, StringComparison.OrdinalIgnoreCase);

                            rootLayout2["Comments"].Update(new Rows(page));

                            rootLayout2["Footer"].Update(new Panel(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                            $"[{Color.Grey}]{Strings.GH_Key_Refresh}" +
                                            $"{Strings.GH_Footer_Pull}" +
                                            $"[{(selectedComment == 0 || selectedData != null ? "grey" : "grey50")}]{Strings.GH_Key_Edit}[/]  " +
                                            $"[{(selectedData == null ? "grey50" : "grey")}]{Strings.GH_Key_Delete}[/]  " +
                                            $"[{(canMerge ? "grey" : "grey50")}]{Strings.GH_Key_Merge}[/]  " +
                                            $"[{(mine || !open ? "grey50" : "grey")}]{Strings.GH_Key_Review}[/]  " +
                                            $"{(open ? Strings.GH_Key_Close : Strings.GH_Key_Reopen)}  {Strings.GH_Key_Browser}[/]",
                                            $"[{Color.Grey}]{string.Format(Strings.GH_PageComment, currentPage + 1, pages.Count, selectedComment + 1, pullComments.Count)}[/]"))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            var key = Console.ReadKey(true);

                            switch (key.Key)
                            {
                                case ConsoleKey.DownArrow when onPage.Count > 0 && selectedComment < onPage[^1]:
                                    selectedComment++;
                                    break;
                                case ConsoleKey.UpArrow when onPage.Count > 0 && selectedComment > onPage[0]:
                                    selectedComment--;
                                    break;
                                case ConsoleKey.DownArrow or ConsoleKey.RightArrow when currentPage + 1 < pages.Count:
                                    currentPage++;
                                    selectedComment = pages[currentPage][0].Index;
                                    break;
                                case ConsoleKey.UpArrow or ConsoleKey.LeftArrow when currentPage > 0:
                                    currentPage--;
                                    selectedComment = pages[currentPage][^1].Index;
                                    break;
                                case ConsoleKey.F5:
                                    await Reload();
                                    break;

                                case ConsoleKey.C:
                                    {
                                        var text = ComposeText(Strings.GH_NewComment, "");

                                        if (text == null)
                                            break;

                                        Report($"[italic {Color.Grey}]{Strings.GH_SendingComment}[/]", false);

                                        var (posted, error) = await GitHubCalls.CommentOnIssue(guid, selectedPull.Number, text);

                                        if (posted == null)
                                        {
                                            Report($"[{Color.Red3_1}]{Strings.GH_CommentNotPosted}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                            break;
                                        }

                                        AppendComment(posted);
                                        break;
                                    }
                                case ConsoleKey.E when selectedComment == 0 || selectedData != null:
                                    {
                                        var edited = ComposeText(selectedComment == 0
                                            ? Strings.GH_EditPull
                                            : Strings.GH_EditComment,
                                                selectedData?.Body ?? selectedPull.Body ?? "");

                                        if (edited == null)
                                            break;

                                        Report($"[italic {Color.Grey}]{Strings.GH_Saving}[/]", false);

                                        if (selectedComment == 0)
                                        {
                                            var (changed, error) = await GitHubCalls.EditPullRequest(guid, selectedPull.Number, selectedPull.Title ?? "", edited);

                                            if (changed == null)
                                            {
                                                Report($"[{Color.Red3_1}]{Strings.GH_PullNotSaved}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);

                                                break;
                                            }

                                            selectedPull = changed;
                                            RefreshDetails();
                                            ReplaceBody(0, edited);
                                        }
                                        else
                                        {
                                            var (changed, error) = await GitHubCalls.EditIssueComment(guid, selectedData!.Id, edited);

                                            if (changed == null)
                                            {
                                                Report($"[{Color.Red3_1}]{Strings.GH_CommentNotSaved}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                                break;
                                            }

                                            commentData[selectedComment] = changed;
                                            ReplaceBody(selectedComment, changed.Body ?? "");
                                        }

                                        break;
                                    }
                                case ConsoleKey.D when selectedData != null:
                                    {
                                        if (!Ask(Strings.GH_DeleteCommentConfirm))
                                            break;

                                        Report($"[italic {Color.Grey}]{Strings.GH_Deleting}[/]", false);

                                        var (deleted, error) = await GitHubCalls.DeleteIssueComment(guid, selectedData.Id);

                                        if (!deleted)
                                        {
                                            Report($"[{Color.Red3_1}]{Strings.GH_CommentNotDeleted}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                            break;
                                        }

                                        pullComments.RemoveAt(selectedComment);
                                        commentBodies.RemoveAt(selectedComment);

                                        commentLineHeights.RemoveAt(selectedComment);
                                        commentData.RemoveAt(selectedComment);
                                        eventData.RemoveAt(selectedComment);

                                        selectedComment = Math.Min(selectedComment, pullComments.Count - 1);
                                        break;
                                    }
                                case ConsoleKey.M when open:
                                    {
                                        if (selectedPull.Mergeable == null)
                                        {
                                            Report($"[italic {Color.Grey}]{Strings.GH_Recomputing}[/]", false);

                                            selectedPull = await GitHubCalls.GetPullRequest(guid, selectedPull.Number) ?? selectedPull;

                                            RefreshDetails();

                                            if (selectedPull.Mergeable == null)
                                                Report($"[{Color.Grey}]{Strings.GH_MergeNotReady}[/]", true);

                                            break;
                                        }

                                        if (!canMerge)
                                        {
                                            Report($"[{Color.Red3_1}]{Strings.GH_CannotMerge}[/] {Markup.Escape(selectedPull.Draft ? Strings.GH_Reason_Draft : changesRequested ? Strings.GH_ChangesRequested : selectedPull.MergeableState ?? Strings.Common_Unknown)}", true);
                                            break;
                                        }

                                        var method = Choose(Strings.GH_HowMerged, ["merge", "squash", "rebase"]);

                                        if (method == null)
                                            break;

                                        if (!Ask(string.Format(Strings.GH_MergeConfirm, selectedPull.Number, selectedPull.Base?.Ref ?? Strings.GH_Reason_BaseBranch)))
                                            break;

                                        Report($"[italic {Color.Grey}]{Strings.GH_Merging}[/]", false);

                                        var (merged, error) = await GitHubCalls.MergePullRequest(guid, selectedPull.Number, method);

                                        if (merged == null || !merged.Merged)
                                        {
                                            Report($"[{Color.Red3_1}]{Strings.GH_PullNotMerged}[/] {Markup.Escape(error ?? merged?.Message ?? Strings.GitHub_UnknownError)}", true);
                                            break;
                                        }

                                        selectedPull = await GitHubCalls.GetPullRequest(guid, selectedPull.Number) ?? selectedPull;

                                        RefreshDetails();
                                        break;
                                    }
                                case ConsoleKey.R when open && !mine:
                                    {
                                        var reviewEvent = Choose(Strings.GH_WhatReview, ["APPROVE", "REQUEST_CHANGES", "COMMENT"]);

                                        if (reviewEvent == null)
                                            break;

                                        var text = reviewEvent == "APPROVE" ? "" : ComposeText(Strings.GH_ReviewBody, "");

                                        if (text == null)
                                            break;

                                        Report($"[italic {Color.Grey}]{Strings.GH_SendingReview}[/]", false);

                                        var (submitted, error) = await GitHubCalls.ReviewPullRequest(guid, selectedPull.Number, reviewEvent, text);

                                        if (submitted == null)
                                        {
                                            Report($"[{Color.Red3_1}]{Strings.GH_ReviewNotSent}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                            break;
                                        }

                                        reviews.Add(submitted);
                                        AppendReview(submitted);
                                        RefreshDetails();
                                        break;
                                    }
                                case ConsoleKey.X when selectedPull.MergedAt == null:
                                    {
                                        if (!Ask(open ? Strings.GH_ClosePullConfirm : Strings.GH_ReopenPullConfirm))
                                            break;

                                        Report($"[italic {Color.Grey}]{Strings.GH_Talking}[/]", false);

                                        var (changed, error) = open
                                            ? await GitHubCalls.ClosePullRequest(guid, selectedPull.Number)
                                            : await GitHubCalls.ReopenPullRequest(guid, selectedPull.Number);

                                        if (changed == null)
                                        {
                                            Report($"[{Color.Red3_1}]{Strings.GH_PullNotChanged}[/] {Markup.Escape(error ?? Strings.GitHub_UnknownError)}", true);
                                            break;
                                        }

                                        selectedPull = changed;
                                        RefreshDetails();
                                        break;
                                    }
                                case ConsoleKey.O:
                                    {
                                        try
                                        {
                                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                            {
                                                FileName = selectedPull.HtmlUrl ?? "https://github.com",
                                                UseShellExecute = true
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            Report($"[{Color.Red3_1}]{Strings.GH_BrowserFailed}[/] {Markup.Escape(ex.Message)}", true);
                                        }

                                        break;
                                    }
                                case ConsoleKey.Escape:
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }

                        void AppendReview(GitHubPullRequestReview posted)
                        {
                            var body = $"[{posted.State switch
                            {
                                "APPROVED" => "Green3_1",
                                "CHANGES_REQUESTED" => "Red3_1",
                                _ => "grey"
                            }}]{posted.State}[/]" + (string.IsNullOrWhiteSpace(posted.Body)
                                ? ""
                                : $"\n{UI.MarkdownToMarkup(posted.Body, guid)}");

                            var panel = new Panel(body)
                                .Header($" [{Color.White}]{UI.Link(posted.User?.HtmlUrl ?? "https://github.com/404", Markup.Escape(posted.User?.Login ?? Strings.GH_UnknownAuthor))} • [{DetermineColor(posted.AuthorAssociation)}]{(posted.AuthorAssociation == "NONE" ? "USER" : posted.AuthorAssociation)}[/] • {ConvertDateTimeOffsetToText(posted.SubmittedAt ?? DateTimeOffset.Now)} • [bold {Color.Turquoise2}]{Strings.GH_Review}[/][/] ")
                                .BorderColor(Color.Grey)
                                .RoundedBorder();

                            panel.Width = Console.WindowWidth;

                            pullComments.Add(panel);
                            commentBodies.Add(body);
                            commentLineHeights.Add(MeasureLines(body));
                            commentData.Add(null);

                            selectedComment = pullComments.Count - 1;
                        }

                        void Report(string message, bool wait)
                        {
                            rootLayout2["Footer"].Update(new Panel(
                                    new Markup(wait ? message + $"   [{Color.Grey}]{Strings.GH_AnyKey}[/]" : message))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            if (wait)
                                Console.ReadKey(true);
                        }

                        bool Ask(string question)
                        {
                            var yes = false;

                            while (true)
                            {
                                rootLayout2["Footer"].Update(new Panel(
                                        new Markup($"{Markup.Escape(question)}   " +
                                                   $"[{(yes ? "Red3_1" : "grey")}]{Strings.GH_Yes}[/]   [{(yes ? "grey" : "Aqua")}]{Strings.GH_No}[/]   " +
                                                   $"[{Color.Grey}]{Strings.GH_ChooseHint}[/]"))
                                    .RoundedBorder()
                                    .Expand()
                                    .Padding(1, 0));

                                ctx.Refresh();

                                switch (Console.ReadKey(true).Key)
                                {
                                    case ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Tab:
                                        yes = !yes;
                                        break;
                                    case ConsoleKey.Enter:
                                        return yes;
                                    case ConsoleKey.Escape:
                                        return false;
                                }
                            }
                        }

                        string? Choose(string question, string[] options)
                        {
                            var picked = 0;

                            while (true)
                            {
                                rootLayout2["Footer"].Update(new Panel(
                                        new Markup($"{Markup.Escape(question)}   " +
                                                   string.Join("   ", options.Select((x, i) =>
                                                       $"[{(i == picked ? "Aqua" : "grey")}]{Markup.Escape(x)}[/]")) +
                                                   $"   [{Color.Grey}]{Strings.GH_ChooseHint}[/]"))
                                    .RoundedBorder()
                                    .Expand()
                                    .Padding(1, 0));

                                ctx.Refresh();

                                switch (Console.ReadKey(true).Key)
                                {
                                    case ConsoleKey.LeftArrow:
                                        picked = picked == 0 ? options.Length - 1 : picked - 1;
                                        break;
                                    case ConsoleKey.RightArrow or ConsoleKey.Tab:
                                        picked = (picked + 1) % options.Length;
                                        break;
                                    case ConsoleKey.Enter:
                                        return options[picked];
                                    case ConsoleKey.Escape:
                                        return null;
                                }
                            }
                        }

                        void ReplaceBody(int index, string raw)
                        {
                            var body = UI.MarkdownToMarkup(raw, guid);

                            var rebuilt = new Panel(body)
                            {
                                Width = Console.WindowWidth,
                                Header = pullComments[index].Header,
                                BorderStyle = pullComments[index].BorderStyle
                            };

                            rebuilt.RoundedBorder();

                            pullComments[index] = rebuilt;
                            commentBodies[index] = body;
                            commentLineHeights[index] = MeasureLines(body);
                        }

                        async Task Reload()
                        {
                            rootLayout2["Comments"].Update(
                                new Markup($"\n    [{Color.Grey}]{Strings.GH_RetrievingComments}[/]"));

                            ctx.Refresh();

                            var fresh = await GitHubCalls.GetAllComments(guid, selectedPull.Number);
                            var freshEvents = await GitHubCalls.GetAllEvents(guid, selectedPull.Number);

                            if (fresh == null)
                                return;

                            pullComments.RemoveRange(1, pullComments.Count - 1);
                            commentBodies.RemoveRange(1, commentBodies.Count - 1);
                            commentLineHeights.RemoveRange(1, commentLineHeights.Count - 1);
                            commentData.RemoveRange(1, commentData.Count - 1);
                            eventData.RemoveRange(1, eventData.Count - 1);

                            fresh
                                .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)x, Happened: (GitHubEvent?)null))
                                .Concat((freshEvents ?? [])
                                    .Where(x => DescribeEvent(x) != null)
                                    .Select(x => (Date: x.CreatedAt, Comment: (GitHubComment?)null, Happened: (GitHubEvent?)x)))
                                .OrderBy(x => x.Date)
                                .ToList()
                                .ForEach(x =>
                                {
                                    if (x.Happened is { } happened)
                                        AppendEvent(happened);
                                    else
                                        AppendComment(x.Comment!);
                                });

                            selectedComment = Math.Clamp(selectedComment, 0, pullComments.Count - 1);
                            currentPage = 0;
                        }

                        void AppendEvent(GitHubEvent happened)
                        {
                            var described = DescribeEvent(happened)!;

                            pullComments.Add(BuildEventPanel(happened, described, false));
                            commentBodies.Add(described);
                            commentLineHeights.Add(MeasureLines(described));
                            commentData.Add(null);
                            eventData.Add(happened);
                        }

                        void AppendComment(GitHubComment posted)
                        {
                            var body = UI.MarkdownToMarkup(posted.Body, guid);

                            var panel = new Panel(body)
                                .Header($"⠀[{Color.White}]{UI.Link(posted.User?.HtmlUrl ?? "https://github.com/404", Markup.Escape(posted.User?.Login ?? Strings.GH_UnknownAuthor))} • [{DetermineColor(posted.AuthorAssociation)}]{(posted.AuthorAssociation == "NONE" ? "USER" : posted.AuthorAssociation)}[/] • {ConvertDateTimeOffsetToText(posted.CreatedAt)}[/]⠀")
                                .BorderColor(Color.SkyBlue1)
                                .RoundedBorder();

                            panel.Width = Console.WindowWidth;

                            pullComments.Add(panel);
                            commentBodies.Add(body);
                            commentLineHeights.Add(MeasureLines(body));
                            commentData.Add(posted);
                            eventData.Add(null);

                            selectedComment = pullComments.Count - 1;
                        }

                        string? ComposeText(string title, string initial)
                        {
                            UI.FlushInput();

                            StringBuilder content = new(initial);
                            var cursorPos = content.Length;

                            var bold = false;
                            var italic = false;
                            var code = false;
                            var sending = false;
                            var writing = true;

                            while (writing)
                            {
                                rootLayout2["Comments"].Update(new Panel(content.Length == 0
                                         ? $"[italic {Color.Grey}]{Strings.GH_WriteHint}[/]"
                                         : Markup.Escape(content.ToString(0, cursorPos)) +
                                     $"[{Color.SkyBlue1}]|[/]" +
                                     Markup.Escape(content.ToString(cursorPos, content.Length - cursorPos)))
                                .Header($"⠀[{Color.White}]{Markup.Escape(title)}[/]⠀")
                                .BorderColor(Color.SkyBlue1)
                                .RoundedBorder()
                                .Expand());

                                rootLayout2["Footer"].Update(new Panel(
                                    new Grid()
                                        .AddColumn()
                                        .AddColumn(new GridColumn().RightAligned())
                                        .Expand()
                                        .AddRow(
                                            $"[{(bold ? "SkyBlue1" : "grey")}]{Strings.GH_Editor_Bold}[/]   " +
                                            $"[{(italic ? "SkyBlue1" : "grey")}]{Strings.GH_Editor_Italic}[/]   " +
                                            $"[{(code ? "SkyBlue1" : "grey")}]{Strings.GH_Editor_Code}[/]   " +
                                            $"[{Color.Grey}]{Strings.GH_Editor_Footer}[/]",
                                            $"[{Color.Grey}]{string.Format(Strings.GH_Characters, content.Length)}[/]"))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                                ctx.Refresh();

                                var input = Console.ReadKey(true);

                                if (input.Modifiers.HasFlag(ConsoleModifiers.Control))
                                {
                                    switch (input.Key)
                                    {
                                        case ConsoleKey.B:
                                            content.Insert(cursorPos, "**");
                                            cursorPos += 2;
                                            bold = !bold;
                                            break;
                                        case ConsoleKey.E:
                                            content.Insert(cursorPos, '*');
                                            cursorPos += 1;
                                            italic = !italic;
                                            break;
                                        case ConsoleKey.K:
                                            content.Insert(cursorPos, '`');
                                            cursorPos += 1;
                                            code = !code;
                                            break;
                                        case ConsoleKey.L:
                                            {
                                                var insert = cursorPos == 0 || content[cursorPos - 1] == '\n' ? "- " : "\n- ";
                                                content.Insert(cursorPos, insert);
                                                cursorPos += insert.Length;
                                                break;
                                            }
                                        case ConsoleKey.Q:
                                            {
                                                var insert = cursorPos == 0 || content[cursorPos - 1] == '\n' ? "> " : "\n> ";
                                                content.Insert(cursorPos, insert);
                                                cursorPos += insert.Length;
                                                break;
                                            }
                                    }
                                    continue;
                                }

                                switch (input.Key)
                                {
                                    case ConsoleKey.LeftArrow:
                                        if (cursorPos > 0)
                                            cursorPos--;
                                        break;
                                    case ConsoleKey.RightArrow:
                                        if (cursorPos < content.Length)
                                            cursorPos++;
                                        break;
                                    case ConsoleKey.Home:
                                        cursorPos = 0;
                                        break;
                                    case ConsoleKey.End:
                                        cursorPos = content.Length;
                                        break;
                                    case ConsoleKey.Escape:
                                        writing = false;
                                        break;
                                    case ConsoleKey.F2 when content.Length > 0:
                                        sending = true;
                                        writing = false;
                                        break;
                                    case ConsoleKey.Enter:
                                        content.Insert(cursorPos, '\n');
                                        cursorPos++;
                                        break;
                                    case ConsoleKey.Backspace when cursorPos > 0:
                                        content.Remove(cursorPos - 1, 1);
                                        cursorPos--;
                                        break;
                                    case ConsoleKey.Delete when cursorPos < content.Length:
                                        content.Remove(cursorPos, 1);
                                        break;
                                    default:
                                        if (!char.IsControl(input.KeyChar))
                                        {
                                            content.Insert(cursorPos, input.KeyChar);
                                            cursorPos++;
                                        }
                                        break;
                                }
                            }

                            return sending ? content.ToString() : null;
                        }

                        Panel BuildPanel(int index, int selected, string[] lines, bool continued, bool continues)
                        {
                            if (eventData[index] is { } happened)
                                return BuildEventPanel(happened, commentBodies[index], index == selected);

                            var body = string.Join("\n", lines);

                            if (continued)
                                body = $"[italic {Color.Grey}]{Strings.GH_ContinuedFrom}[/]\n" + body;

                            if (continues)
                                body += $"\n[italic {Color.Grey}]{Strings.GH_ContinuesOn}[/]";

                            var panel = new Panel(body)
                            {
                                Width = Console.WindowWidth,
                                Header = pullComments[index].Header,
                                BorderStyle = pullComments[index].BorderStyle
                            };

                            if (index == selected)
                                panel.DoubleBorder();
                            else
                                panel.RoundedBorder();

                            return panel;
                        }
                    });

                AnsiConsole.Clear();
                continue;
            }
        }

        private static Color DetermineColor(string? authorAssociation)
        {
            return authorAssociation?.ToLowerInvariant() switch
            {
                "owner" => Color.Gold1,
                "member" => Color.DeepSkyBlue1,
                "collaborator" => Color.MediumPurple1,
                "contributor" => Color.SpringGreen3,
                "first_time_contributor" => Color.PaleGreen3,
                "first_timer" => Color.PaleGreen3,
                "mannequin" => Color.Gray50,
                _ => Color.Grey
            };
        }

        private static string? DescribeEvent(GitHubEvent happened)
        {
            string Tag(string? text, string color) => $"[{color}]{Markup.Escape(text ?? "?")}[/]";

            return happened.Event?.ToLowerInvariant() switch
            {
                "labeled" => string.Format(Strings.GH_Event_Labeled, Tag(happened.Label?.Name, LabelColor(happened.Label))),
                "unlabeled" => string.Format(Strings.GH_Event_Unlabeled, Tag(happened.Label?.Name, LabelColor(happened.Label))),
                "closed" => Strings.GH_Event_Closed,
                "reopened" => Strings.GH_Event_Reopened,
                "assigned" => string.Format(Strings.GH_Event_Assigned, Tag(happened.Assignee?.Login, "SkyBlue1")),
                "unassigned" => string.Format(Strings.GH_Event_Unassigned, Tag(happened.Assignee?.Login, "SkyBlue1")),
                "renamed" => string.Format(Strings.GH_Event_Renamed, Tag(happened.Rename?.To, "Khaki1")),
                "milestoned" => string.Format(Strings.GH_Event_Milestoned, Tag(happened.Milestone?.Title, "Aqua")),
                "demilestoned" => string.Format(Strings.GH_Event_Demilestoned, Tag(happened.Milestone?.Title, "Aqua")),
                "locked" => Strings.GH_Event_Locked,
                "unlocked" => Strings.GH_Event_Unlocked,
                "merged" => Strings.GH_Event_Merged,
                "referenced" => Strings.GH_Event_Referenced,
                "pinned" => Strings.GH_Event_Pinned,
                "unpinned" => Strings.GH_Event_Unpinned,
                "review_requested" => Strings.GH_Event_ReviewRequested,
                "review_request_removed" => Strings.GH_Event_ReviewRequestRemoved,
                "head_ref_deleted" => Strings.GH_Event_HeadRefDeleted,
                "head_ref_force_pushed" => Strings.GH_Event_HeadRefForcePushed,
                "head_ref_restored" => Strings.GH_Event_HeadRefRestored,
                _ => null
            };
        }

        private static string LabelColor(GitHubLabel? label)
        {
            if (string.IsNullOrWhiteSpace(label?.Color))
                return "grey";

            return $"#{label.Color.TrimStart('#')}";
        }

        private static Panel BuildEventPanel(GitHubEvent happened, string text, bool selected)
        {
            var grid = new Grid()
                .AddColumn()
                .AddColumn(new GridColumn().RightAligned())
                .Expand()
                .AddRow(
                    $"[{(selected ? "Aqua" : "grey35")}]{(selected ? "▸" : "•")}[/]  " +
                    $"[{Color.White}]{UI.Link(happened.Actor?.HtmlUrl ?? "https://github.com/404", happened.Actor?.Login ?? Strings.GH_UnknownAuthor)}[/] " +
                    $"[{Color.Grey}]{text}[/]",
                    $"[{Color.Grey35}]{ConvertDateTimeOffsetToText(happened.CreatedAt)}[/]");

            var panel = new Panel(grid)
            {
                Width = Console.WindowWidth,
                Padding = new Spectre.Console.Padding(3, 0, 1, 0)
            };

            return panel.NoBorder();
        }

        private static int[] MeasureLines(string markup)
        {
            var options = RenderOptions.Create(AnsiConsole.Console, AnsiConsole.Profile.Capabilities);

            return markup.Split('\n')
                .Select(x => Math.Max(1, Segment.SplitLines(
                    UI.SafeMarkup(x).Render(options, Math.Max(20, Console.WindowWidth - 4))).Count))
                .ToArray();
        }

        internal static string RelativeTime(DateTimeOffset time) => ConvertDateTimeOffsetToText(time);

        private static string ConvertDateTimeOffsetToText(DateTimeOffset time)
        {
            var diff = DateTimeOffset.Now - time;
            return diff.TotalSeconds switch
            {
                < 1 => Strings.GH_JustNow,
                < 60 => string.Format(Strings.GH_SecondsAgo, (int)diff.TotalSeconds),
                _ when diff.TotalMinutes < 60 => string.Format(Strings.GH_MinutesAgo, (int)diff.TotalMinutes),
                _ when diff.TotalHours < 24 => string.Format(Strings.GH_HoursAgo, (int)diff.TotalHours),
                _ when diff.TotalDays < 30 => string.Format(Strings.GH_DaysAgo, (int)diff.TotalDays),
                _ when diff.TotalDays < 365 => string.Format(Strings.GH_MonthsAgo, (int)(diff.TotalDays / 30)),
                _ => string.Format(Strings.GH_YearsAgo, (int)(diff.TotalDays / 365))
            };
        }

        private static void GitHubSynchronization(Guid guid)
        {
            var project = GetProject(guid);

            if (!Repository.IsValid(project.Path))
            {
                UI.Error(Strings.Common_NotAGitRepo + "\n" + Strings.Common_InitGitHint, Strings.GH_Synchronization);
                Console.ReadKey();
                return;
            }

            if (!GitHubCalls.IsAuthorizedWithGitHub())
            {
                UI.Error(Strings.GH_NeedAuth, Strings.GH_Synchronization);
                Console.ReadKey();
                return;
            }

            if (!GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.GH_Synchronization))
                return;

            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var lookup = GitHubCalls.RepoExistsOnUsersGitHubProfile(guid)
                .GetAwaiter().GetResult();

            if (lookup != GitHubCalls.RepoLookup.Found)
            {
                UI.Error(GitHubCalls.DescribeLookup(lookup), Strings.GH_Synchronization);
                Console.ReadKey();
                return;
            }

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            var syncSettings = GetProject(guid).GitHubSyncSettings;

            var prompt = new MultiSelectionPrompt<GitHubSync>()
                .Title(Strings.GH_Sync_Select)
                .AddChoices(Enum.GetValues<GitHubSync>())
                .PageSize(15)
                .InstructionsText($"[{Color.Grey}]{Strings.Common_MultiSelectHint}[/]")
                .UseConverter(x => x switch
                {
                    GitHubSync.SyncStatusWithGitHubRepo => Strings.GH_Sync_Status,
                    GitHubSync.SyncBadgesWithGitHubRepo => Strings.GH_Sync_Badges,
                    GitHubSync.FetchGitHubRepoTopics => Strings.GH_Sync_Topics,
                    GitHubSync.FetchGitHubRepoStats => Strings.GH_Sync_Stats,
                    GitHubSync.OverwriteUsedLanguagesByGitHub => Strings.GH_Sync_Languages,
                    GitHubSync.FetchGitHubActions => Strings.GH_Sync_Actions,
                    _ => x.ToString()
                })
                .NotRequired();

            syncSettings
                .Where(kvp => kvp.Value)
                .ToList()
                .ForEach(kvp => prompt.Select(kvp.Key));

            var choices = AnsiConsole.Prompt(prompt);

            if (!UpdateProject(guid, x => x.GitHubSyncSettings = Enum.GetValues<GitHubSync>()
                .ToDictionary(key => key, choices.Contains)))
                return;

            if (!choices.Contains(GitHubSync.SyncBadgesWithGitHubRepo) ||
                (syncSettings.TryGetValue(GitHubSync.SyncBadgesWithGitHubRepo, out var wasOn) && wasOn))
                return;

            var definitions = Tags.AllBadges();
            var readmePath = Path.Combine(repo.Info.WorkingDirectory, "README.md");
            string readme;

            try
            {
                readme = File.Exists(readmePath) ? File.ReadAllText(readmePath) : string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                UI.Warning($"{Strings.GH_Markers_ReadFailed}\n\n{Markup.Escape(ex.Message)}", Strings.GH_BadgesSync);
                Console.ReadKey();
                return;
            }

            var pending = GetProject(guid).Badges
                .Where(definitions.ContainsKey)
                .Where(x => readme.IndexOf($"<!-- HELYX_BADGE_{x}_START -->", StringComparison.Ordinal) == -1)
                .ToList();

            if (pending.Count == 0)
                return;

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            UI.Info(Strings.GH_PendingBadges + "\n\n" +
                    string.Join("\n", pending.Select(x => Tags.Markup(definitions[x], $"[[{Markup.Escape(definitions[x].Name)}]]"))),
                    Strings.GH_BadgesSync);

            var add = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.GH_AddThemNow)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (add == Confirm.Yes)
                SyncBadges(guid);
        }

        private static void ViewGitHubRepoStats(Guid guid)
        {
            var project = GetProject(guid);

            if (!Repository.IsValid(project.Path))
            {
                UI.Error(Strings.Common_NotAGitRepo + "\n" + Strings.Common_InitGitHint, Strings.GH_Stats_Title);
                Console.ReadKey();
                return;
            }

            if (!GitHubCalls.IsAuthorizedWithGitHub())
            {
                UI.Error(Strings.GH_NeedAuth, Strings.GH_Stats_Title);
                Console.ReadKey();
                return;
            }

            if (!GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.GH_Stats_Title))
                return;

            var statsChart = new BarChart().Width(50);
            var languagesChart = new BreakdownChart();
            var panel1 = new Panel(statsChart).Expand().DoubleBorder();
            var panel2 = new Panel(languagesChart).Expand().DottedBorder();

            ProjectsMenu.GitHubRepositories.TryRemove(guid, out _);

            GitHubRepository? githubRepo = null;

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots).Start(Strings.Projects_RetrievingStats, ctx =>
                    githubRepo = GitHubCalls.GetGitHubRepoStats(guid).GetAwaiter().GetResult()
                );

            if (githubRepo == null)
            {
                UI.Error(Strings.GH_Stats_Failed, Strings.GH_Stats_Title);
                Console.ReadKey();
                return;
            }

            if (githubRepo.FailedParts.Count > 0)
            {
                UI.Warning(
                    Strings.GH_Stats_Incomplete + "\n" +
                    $"[bold]{Markup.Escape(string.Join(", ", githubRepo.FailedParts))}[/]\n\n" +
                    $"[{Color.Grey}]{Strings.GH_Stats_Retry}[/]",
                    Strings.GH_Stats_Incomplete_Title);

                Console.ReadKey();
            }

            List<Calendar> calendars = new();

            if (githubRepo.ActivityDays != null)
            {
                foreach (var (year, month) in githubRepo.ActivityDays.Select(x => (x.Year, x.Month)).Distinct())
                {
                    var calendar = new Calendar(year, month)
                    {
                        Culture = CultureInfo.CurrentCulture
                    };

                    calendar.HeaderStyle(new Style(foreground: Color.Red, decoration: Decoration.Bold))
                        .Border(TableBorder.Rounded)
                        .HighlightStyle(new Style(foreground: Color.Orange1, decoration: Decoration.RapidBlink));

                    calendars.Add(calendar);
                }
                foreach (var activityDay in githubRepo.ActivityDays)
                {
                    var calendar = calendars.FirstOrDefault(x => x.Year == activityDay.Year && x.Month == activityDay.Month);

                    calendar?.AddCalendarEvent(activityDay.ToDateTime(new TimeOnly(12, 0, 0)));
                }
            }

            statsChart
                .AddItem($":star: {Strings.GH_Stats_Stars}", githubRepo.Stars, Color.Gold1)
                .AddItem($":eye: {Strings.GH_Stats_Watchers}", githubRepo.Watchers, Color.Blue)
                .AddItem($":test_tube: {Strings.GH_Stats_Forks}", githubRepo.Forks, Color.Silver);

            if (githubRepo.Languages != null)
                foreach (var language in githubRepo.Languages)
                    languagesChart
                        .ShowPercentage()
                        .AddItem(language.Key, language.Value, UI.GetColor(language.Key));

            var panel3 = new Align(
                    new Rows(
                        new Markup($"[bold {Color.Blue}]{Strings.GH_Stats_Activity}[/]"),
                        new Panel(new Columns(calendars))
                    ), HorizontalAlignment.Center
                );

            AnsiConsole.Write(panel1);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(panel2);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(panel3);

            UI.FlushInput();

            Console.ReadKey();
        }

        private static void OpenWiki(Guid guid)
        {
            var username = GitHubCalls.GetCachedUsername().GetAwaiter().GetResult();
            var repoName = GetProject(guid).GitHubName;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(repoName))
            {
                UI.Error(Strings.GH_Wiki_Failed + "\n" + Strings.GH_Wiki_CheckAuth, Strings.GH_OpenWiki);
                Console.ReadKey();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://github.com/{username}/{repoName}/wiki",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UI.Error(Strings.GH_Wiki_OpenFailed + $"\n\n{Markup.Escape(ex.Message)}", Strings.GH_OpenWiki);
                Console.ReadKey();
            }
        }

        internal static void SyncStatus(Guid projectGuid, Guid statusGuid, bool place = true)
        {
            var project = GetProject(projectGuid);

            var status = Tags.AllStatuses().TryGetValue(statusGuid, out var definition)
                ? definition
                : Tags.BuiltInStatuses[BuiltInStatusIds.Active];

            if (!GitHubCalls.EnsureGitHubRepoConnection(projectGuid, Strings.GH_StatusSync))
                return;

            Exception? err = null;
            List<string> warning = new();

            using (var probe = GitHelper.OpenRepo(project.Path, Strings.GH_StatusSync))
            {
                if (probe == null)
                    return;

                if (probe.RetrieveStatus(GitHelper.FastStatus).Any(x => GitHelper.IsStagedInIndex(x.State)))
                {
                    UI.Warning(Strings.GH_StagedChanges_Status + "\n\n" +
                               $"[{Color.Grey}]{Strings.GH_CommitOrUnstage_Status}[/]", Strings.GH_StatusSync);
                    Console.ReadKey();

                    return;
                }
            }

            if (place)
            {
                string readmePath;

                using (var probe = new Repository(project.Path))
                    readmePath = Path.Combine(probe.Info.WorkingDirectory, "README.md");

                if (File.Exists(readmePath) && !EnsureMarkers(
                        readmePath,
                        [(Tags.Markup(status, Markup.Escape(status.Name)),
                            "<!-- HELYX_STATUS_START -->", "<!-- HELYX_STATUS_END -->")],
                        Strings.GH_StatusSync))
                    return;
            }

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(Strings.GitHub_Waiting, ctx =>
                {
                    try
                    {
                        using var repo = new Repository(project.Path);

                        if (repo.Info.CurrentOperation != CurrentOperation.None)
                        {
                            warning.Add(string.Format(Strings.GH_OperationInProgress_Status, repo.Info.CurrentOperation));
                            return;
                        }

                        if (repo.Head.TrackedBranch == null)
                        {
                            warning.Add(string.Format(Strings.GH_NoUpstream_Status, $"'{Markup.Escape(repo.Head.FriendlyName)}'") + "\n\n" +
                                        $"[{Color.Grey}]{Strings.GH_PushOnceFirst}[/]");
                            return;
                        }

                        const string start = "<!-- HELYX_STATUS_START -->";
                        const string end = "<!-- HELYX_STATUS_END -->";

                        string readmePath = Path.Combine(
                            repo.Info.WorkingDirectory,
                            "README.md"
                        );

                        if (!File.Exists(readmePath))
                        {
                            warning.Add(Strings.GH_ReadmeNotFound_Status);
                            return;
                        }

                        var encoding = TextFile.ReadWithBom(readmePath, out var content);

                        if (!TextFile.Decoded(content))
                        {
                            warning.Add(Strings.GH_ReadmeNotUtf8_Status + "\n\n" +
                                        $"[{Color.Grey}]{Strings.GH_SaveAsUtf8_Status}[/]");
                            return;
                        }

                        int startIndex = content.IndexOf(start, StringComparison.Ordinal);
                        int endIndex = content.IndexOf(end, StringComparison.Ordinal);

                        if (startIndex == -1 || endIndex == -1 || endIndex < startIndex)
                        {
                            warning.Add(Strings.GH_MarkersNotFound_Status + "\n\n" +
                                      $"[{Color.Grey}]{Strings.GH_ExpectedMarkers}\n\n{Markup.Escape(start)}\n...\n{Markup.Escape(end)}[/]");
                            return;
                        }

                        var newline = content.Contains("\r\n") ? "\r\n" : "\n";

                        var shield = $"![Status](https://img.shields.io/badge/status-{ShieldsEscape(Tags.ShieldName(statusGuid, status))}-{Tags.SafeHex(status.Hex)})";

                        var newContent = content[..(startIndex + start.Length)] + shield + content[endIndex..];

                        if (newContent == content)
                            return;

                        var signature = new Signature(GitHubCalls.MainIdentity(repo.Config), DateTimeOffset.Now);

                        if (repo.RetrieveStatus(GitHelper.FastStatus).Any(x => GitHelper.IsStagedInIndex(x.State)))
                        {
                            warning.Add(Strings.GH_StagedChanges_Status + "\n\n" +
                                        $"[{Color.Grey}]{Strings.GH_CommitOrUnstage_Status}[/]");
                            return;
                        }

                        File.WriteAllText(readmePath, newContent, encoding);

                        Commands.Stage(repo, readmePath);

                        repo.Commit("Updated status in README.md", signature, signature);

                        repo.Network.Push(repo.Head, new PushOptions
                        {
                            CredentialsProvider = (url, usernameFromUrl, types) =>
                                new UsernamePasswordCredentials
                                {
                                    Username = "x-access-token",
                                    Password = GetGitHubAccessToken()
                                }
                        });
                    }
                    catch (Exception ex)
                    {
                        err = ex;
                    }
                });

            UI.FlushInput();

            if (err != null)
            {
                UI.Error(Markup.Escape(err.Message), Strings.GH_StatusSyncFailed);
                Console.ReadKey();
            }
            else if (warning.Count > 0)
                warning.ForEach(x => { UI.Warning(x, Strings.GH_StatusSync); Console.ReadKey(); });
        }

        internal static List<Guid> ConfirmSync(IEnumerable<ProjectClass> projects, GitHubSync setting, string declineLabel)
        {
            if (!GitHubCalls.IsAuthorizedWithGitHub())
                return [];

            var syncing = projects
                .Where(x => !string.IsNullOrWhiteSpace(x.GitHubName)
                            && x.GitHubSyncSettings.TryGetValue(setting, out var on) && on
                            && Repository.IsValid(x.Path))
                .Select(x => x.Guid)
                .ToList();

            if (syncing.Count == 0)
                return [];

            var push = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(string.Format(syncing.Count == 1 ? Strings.GH_ConfirmSyncOne : Strings.GH_ConfirmSync, $"[bold]{syncing.Count}[/]"))
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(x => x switch
                    {
                        Confirm.Yes => Strings.GH_ContinueAction,
                        Confirm.No => declineLabel,
                        _ => x.ToString()
                    }));

            return push == Confirm.No ? [] : syncing;
        }

        internal static void SyncBadges(Guid projectGuid, IEnumerable<Guid>? removed = null, bool place = true, IEnumerable<Guid>? only = null)
        {
            var project = GetProject(projectGuid);

            var clear = removed?.ToHashSet() ?? [];
            var scope = only?.ToHashSet();

            var allBadges = project.Badges.Concat(clear).Distinct()
                .Where(x => scope == null || scope.Contains(x))
                .ToList();

            if (!GitHubCalls.EnsureGitHubRepoConnection(projectGuid, Strings.GH_BadgesSync))
                return;

            Exception? err = null;
            List<string> warning = new();

            using (var probe = GitHelper.OpenRepo(project.Path, Strings.GH_BadgesSync))
            {
                if (probe == null)
                    return;

                if (probe.RetrieveStatus(GitHelper.FastStatus).Any(x => GitHelper.IsStagedInIndex(x.State)))
                {
                    UI.Warning(Strings.GH_StagedChanges_Badges + "\n\n" +
                               $"[{Color.Grey}]{Strings.GH_CommitOrUnstage_Badges}[/]", Strings.GH_BadgesSync);
                    Console.ReadKey();

                    return;
                }
            }

            if (place)
            {
                var definitions = Tags.AllBadges();

                string readmePath;

                using (var probe = new Repository(project.Path))
                    readmePath = Path.Combine(probe.Info.WorkingDirectory, "README.md");

                var tags = allBadges
                    .Where(x => !clear.Contains(x))
                    .Where(definitions.ContainsKey)
                    .Select(x => (
                        Label: Tags.Markup(definitions[x], $"[[{Markup.Escape(definitions[x].Name)}]]"),
                        Start: $"<!-- HELYX_BADGE_{x}_START -->",
                        End: $"<!-- HELYX_BADGE_{x}_END -->"))
                    .ToList();

                if (tags.Count > 0 && File.Exists(readmePath) && !EnsureMarkers(readmePath, tags, Strings.GH_BadgesSync))
                    return;
            }

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(Strings.GitHub_Waiting, ctx =>
                {
                    try
                    {
                        using var repo = new Repository(project.Path);

                        if (repo.Info.CurrentOperation != CurrentOperation.None)
                        {
                            warning.Add(string.Format(Strings.GH_OperationInProgress_Badges, repo.Info.CurrentOperation));
                            return;
                        }

                        if (repo.Head.TrackedBranch == null)
                        {
                            warning.Add(string.Format(Strings.GH_NoUpstream_Badges, $"'{Markup.Escape(repo.Head.FriendlyName)}'") + "\n\n" +
                                        $"[{Color.Grey}]{Strings.GH_PushOnceFirst}[/]");
                            return;
                        }

                        string readmePath = Path.Combine(
                            repo.Info.WorkingDirectory,
                            "README.md"
                        );

                        if (!File.Exists(readmePath))
                        {
                            warning.Add(Strings.GH_ReadmeNotFound_Badges);
                            return;
                        }

                        var encoding = TextFile.ReadWithBom(readmePath, out var content);

                        if (!TextFile.Decoded(content))
                        {
                            warning.Add(Strings.GH_ReadmeNotUtf8_Badges + "\n\n" +
                                        $"[{Color.Grey}]{Strings.GH_SaveAsUtf8_Badges}[/]");
                            return;
                        }

                        string newContent = content;

                        var definitions = Tags.AllBadges();

                        var newline = content.Contains("\r\n") ? "\r\n" : "\n";

                        foreach (var badgeGuid in allBadges)
                        {
                            definitions.TryGetValue(badgeGuid, out var badge);

                            string start = $"<!-- HELYX_BADGE_{badgeGuid}_START -->";
                            string end = $"<!-- HELYX_BADGE_{badgeGuid}_END -->";

                            int startIndex = newContent.IndexOf(start, StringComparison.Ordinal);
                            int endIndex = newContent.IndexOf(end, StringComparison.Ordinal);

                            if (startIndex == -1 || endIndex == -1 || endIndex < startIndex)
                            {
                                warning.Add(string.Format(Strings.GH_BadgeMarkersNotFound, $"'{(badge == null ? Markup.Escape(badgeGuid.ToString()) : $"[#{Tags.SafeHex(badge.Hex)}][[{Markup.Escape(badge.Name)}]][/]")}'") + "\n\n" +
                                          $"[{Color.Grey}]{Strings.GH_ExpectedMarkers}\n\n{Markup.Escape(start)}\n...\n{Markup.Escape(end)}[/]");
                                continue;
                            }

                            var shield = badge == null || clear.Contains(badgeGuid)
                                ? string.Empty
                                : $"![Badge](https://img.shields.io/badge/{ShieldsEscape(badge.Name)}-{Tags.SafeHex(badge.Hex)})";

                            newContent = newContent[..(startIndex + start.Length)] + shield + newContent[endIndex..];
                        }

                        if (newContent == content)
                            return;

                        var signature = new Signature(GitHubCalls.MainIdentity(repo.Config), DateTimeOffset.Now);

                        if (repo.RetrieveStatus(GitHelper.FastStatus).Any(x => GitHelper.IsStagedInIndex(x.State)))
                        {
                            warning.Add(Strings.GH_StagedChanges_Badges + "\n\n" +
                                        $"[{Color.Grey}]{Strings.GH_CommitOrUnstage_Badges}[/]");
                            return;
                        }

                        File.WriteAllText(readmePath, newContent, encoding);

                        Commands.Stage(repo, readmePath);

                        repo.Commit("Updated badges in README.md", signature, signature);

                        repo.Network.Push(repo.Head, new PushOptions
                        {
                            CredentialsProvider = (url, usernameFromUrl, types) =>
                                new UsernamePasswordCredentials
                                {
                                    Username = "x-access-token",
                                    Password = GetGitHubAccessToken()
                                }
                        });
                    }
                    catch (Exception ex)
                    {
                        err = ex;
                    }
                });

            UI.FlushInput();

            if (err != null)
            {
                UI.Error(Markup.Escape(err.Message), Strings.GH_BadgesSyncFailed);
                Console.ReadKey();
            }
            else if (warning.Count > 0)
                warning.ForEach(x => { UI.Warning(x, Strings.GH_BadgesSync); Console.ReadKey(); });
        }

        private static bool EnsureMarkers(string readmePath, List<(string Label, string Start, string End)> tags, string title)
        {
            string content;
            Encoding encoding;

            try
            {
                encoding = TextFile.ReadWithBom(readmePath, out content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                UI.Error($"{Strings.GH_Markers_ReadFailed}\n\n{Markup.Escape(ex.Message)}", title);
                Console.ReadKey();

                return false;
            }

            var missing = tags
                .Where(x => content.IndexOf(x.Start, StringComparison.Ordinal) == -1 ||
                            content.IndexOf(x.End, StringComparison.Ordinal) == -1)
                .ToList();

            if (missing.Count == 0)
                return true;

            if (!TextFile.Decoded(content))
            {
                UI.Error(Strings.GH_Markers_NotUtf8 + "\n\n" +
                         $"[{Color.Grey}]{Strings.GH_SaveAsUtf8Retry}[/]", title);
                Console.ReadKey();

                return false;
            }

            AnsiConsole.Clear();

            UI.Warning(string.Format(Strings.GH_Markers_Missing, missing.Count), title);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<MarkerAction>()
                    .Title(Strings.GH_Markers_How)
                    .AddChoices(Enum.GetValues<MarkerAction>())
                    .UseConverter(x => x switch
                    {
                        MarkerAction.Place => Strings.GH_Markers_Place,
                        MarkerAction.Manually => Strings.GH_Markers_Manually,
                        MarkerAction.Skip => $"[{Color.Red3_1}]{Strings.GH_Markers_Skip}[/]",
                        _ => x.ToString()
                    }));

            switch (choice)
            {
                case MarkerAction.Manually:
                    UI.Info(Strings.GH_Markers_AddThese + "\n\n" +
                            string.Join("\n\n", missing.Select(x => $"[{Color.Grey}]{Markup.Escape(x.Start)}\n{Markup.Escape(x.End)}[/]")), title);
                    Console.ReadKey();
                    return false;

                case MarkerAction.Skip:
                    return false;
            }

            var crlf = content.Contains("\r\n");
            var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
            var definitions = Tags.AllBadges();

            var rows = new List<(int Tag, bool Added, string Markup, string[] Lines)>();

            for (var i = 0; i < lines.Count;)
            {
                var opening = lines[i].IndexOf("<!-- HELYX_", StringComparison.Ordinal);
                var closing = opening < 0 ? -1 : lines[i].IndexOf("_START -->", opening, StringComparison.Ordinal);

                var name = closing > 0 ? lines[i][(opening + 11)..closing] : null;

                var close = name == null
                    ? -1
                    : lines.FindIndex(i, x => x.Contains($"<!-- HELYX_{name}_END -->", StringComparison.Ordinal));

                if (name == null || close < 0)
                {
                    rows.Add((-1, false, Markup.Escape(lines[i]), [lines[i]]));
                    i++;
                    continue;
                }

                var before = lines[i][..opening].TrimEnd();

                var shield = ShieldMarkup(name, definitions, lines
                    .Skip(i)
                    .Take(close - i + 1)
                    .FirstOrDefault(x => x.Contains("img.shields.io/badge/"))) ?? $"[{Color.Grey35}]{Markup.Escape($"<!-- HELYX_{name} -->")}[/]";

                rows.Add((-1, false, before.Length > 0 ? $"{Markup.Escape(before)} {shield}" : shield,
                    lines.Skip(i).Take(close - i + 1).ToArray()));

                i = close + 1;
            }

            var current = 0;
            var selected = 0;
            var scroll = 0;
            var saved = false;

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(2),
                    new Layout("List"),
                    new Layout("Footer").Size(3));

            layout["Header"].Update(new Padder(
                new Rule($"[bold {Color.Blue}]{string.Format(Strings.GH_Markers_Title, Markup.Escape(title))}[/]").LeftJustified(),
                new Spectre.Console.Padding(0, 0, 0, 1)));

            AnsiConsole.Clear();

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    var running = true;

                    while (running)
                    {
                        var height = Math.Max(3, Console.WindowHeight - 9);
                        var numberWidth = rows.Count.ToString().Length;

                        var pending = Enumerable.Range(0, missing.Count)
                            .Where(t => rows.All(x => x.Tag != t))
                            .ToList();

                        if (!pending.Contains(current))
                            current = pending.Count > 0 ? pending[0] : -1;

                        selected = Math.Clamp(selected, 0, rows.Count - 1);

                        if (selected < scroll)
                            scroll = selected;

                        if (selected >= scroll + height)
                            scroll = selected - height + 1;

                        scroll = Math.Max(0, Math.Min(scroll, rows.Count - height));

                        var grid = new Grid().AddColumn();

                        for (var i = scroll; i < Math.Min(scroll + height, rows.Count); i++)
                            grid.AddRow($"{(i == selected ? $"[{Color.Aqua}]▸[/]" : " ")} [{Color.Grey}]{(i + 1).ToString().PadLeft(numberWidth)} │[/] {rows[i].Markup}");

                        layout["List"].Update(new Panel(grid)
                            .RoundedBorder()
                            .BorderColor(Color.Grey)
                            .Expand());

                        layout["Footer"].Update(new Panel(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                        $"[{Color.Grey}]{Strings.GH_Markers_Footer}[{(current >= 0 ? "grey" : "grey50")}]{Strings.GH_Markers_KeyPlace}[/]   " +
                                        $"[{(rows[selected].Tag >= 0 ? "grey" : "grey50")}]{Strings.GH_Markers_KeyRemove}[/]   " +
                                        $"[{(rows[selected].Added ? "grey" : "grey50")}]{Strings.GH_Markers_KeyDeleteLine}[/]   {Strings.GH_Markers_KeyNextTag}" +
                                        $"[{(pending.Count == 0 ? "Green3_1" : "grey50")}]{Strings.GH_Markers_KeySave}[/]   {Strings.GH_Markers_KeyCancel}[/]",
                                        current >= 0
                                            ? $"{string.Format(Strings.GH_Markers_Placing, missing[current].Label)} [{Color.Grey}]{string.Format(Strings.GH_Markers_Left, pending.Count)}[/]"
                                            : $"[{Color.Green3_1}]{Strings.GH_Markers_AllPlaced}[/]"))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        ctx.Refresh();

                        switch (Console.ReadKey(true).Key)
                        {
                            case ConsoleKey.UpArrow:
                                selected--;
                                break;

                            case ConsoleKey.DownArrow:
                                selected++;
                                break;

                            case ConsoleKey.PageUp:
                                selected -= height;
                                break;

                            case ConsoleKey.PageDown:
                                selected += height;
                                break;

                            case ConsoleKey.Home:
                                selected = 0;
                                break;

                            case ConsoleKey.End:
                                selected = rows.Count - 1;
                                break;

                            case ConsoleKey.Enter:
                                rows.Insert(selected + 1, (-1, true, string.Empty, [string.Empty]));
                                selected++;
                                break;

                            case ConsoleKey.Spacebar when current >= 0:
                                if (rows[selected].Tag < 0 && rows[selected].Lines.All(string.IsNullOrWhiteSpace))
                                    rows[selected] = (current, true, missing[current].Label, []);
                                else
                                    rows.Insert(selected, (current, true, missing[current].Label, []));

                                break;

                            case ConsoleKey.Delete when rows[selected].Tag >= 0:
                                rows[selected] = (-1, true, string.Empty, [string.Empty]);
                                break;

                            case ConsoleKey.Backspace when rows[selected].Added:
                                rows.RemoveAt(selected);
                                selected--;
                                break;

                            case ConsoleKey.Tab when pending.Count > 0:
                                current = pending[(pending.IndexOf(current) + 1) % pending.Count];
                                break;

                            case ConsoleKey.F2 when pending.Count == 0:
                                saved = true;
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

            if (!saved)
                return false;

            var rebuilt = new List<string>();

            foreach (var row in rows)
                if (row.Tag >= 0)
                {
                    var pair = missing[row.Tag].Start + missing[row.Tag].End;

                    if (rebuilt.Count > 0 && rebuilt[^1].Trim().Length > 0)
                        rebuilt[^1] += pair;
                    else
                        rebuilt.Add(pair);
                }
                else
                    rebuilt.AddRange(row.Lines);

            var joined = string.Join("\n", rebuilt);

            try
            {
                File.WriteAllText(readmePath, crlf ? joined.Replace("\n", "\r\n") : joined, encoding);
            }
            catch (Exception ex)
            {
                UI.Error($"{Strings.GH_Markers_WriteFailed}\n\n{Markup.Escape(ex.Message)}", title);
                Console.ReadKey();
                return false;
            }

            return true;
        }

        private enum MarkerAction
        {
            Place,
            Manually,
            Skip
        }

        private static string? ShieldMarkup(string name, Dictionary<Guid, TagDefinition> definitions, string? link)
        {
            if (name.StartsWith("BADGE_", StringComparison.Ordinal) &&
                Guid.TryParse(name[6..], out var badgeGuid) &&
                definitions.TryGetValue(badgeGuid, out var badge))
                return Tags.Markup(badge, $"[[{Markup.Escape(badge.Name)}]]");

            if (link == null)
                return null;

            var path = link[(link.IndexOf("/badge/", StringComparison.Ordinal) + 7)..];
            var stop = path.IndexOfAny([')', ' ', '"']);

            if (stop >= 0)
                path = path[..stop];

            var dash = path.LastIndexOf('-');

            if (dash <= 0 || path.Length - dash - 1 != 6 || !path[(dash + 1)..].All(Uri.IsHexDigit))
                return null;

            var label = path[..dash];

            if (name == "STATUS" && label.StartsWith("status-", StringComparison.Ordinal))
                label = label[7..];

            var text = new StringBuilder();

            for (var i = 0; i < label.Length; i++)
            {
                if (label[i] is '-' or '_' && i + 1 < label.Length && label[i + 1] == label[i])
                {
                    text.Append(label[i]);
                    i++;
                    continue;
                }

                text.Append(label[i] == '_' ? ' ' : label[i]);
            }

            var escaped = Markup.Escape(Uri.UnescapeDataString(text.ToString()));

            return $"[#{path[(dash + 1)..]}]{(name == "STATUS" ? escaped : $"[[{escaped}]]")}[/]";
        }

        private static string ShieldsEscape(string text) =>
            Uri.EscapeDataString(text.Replace("-", "--").Replace("_", "__").Replace(' ', '_'));

        private enum IssueFilter
        {
            All,
            Commented,
            Assigned,
            Labels
        }

        private enum PullRequestFilter
        {
            All,
            Open,
            Drafts,
            Assigned,
            Reviewed
        }

        private enum GitHubAction
        {
            ManageIssues,
            ManagePullRequests,
            GitHubSynchronization,
            ViewGitHubRepoStats,
            WorkflowRuns,
            OpenWiki,
            Back
        }
    }
}
