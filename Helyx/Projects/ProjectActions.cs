using Helyx.Data;
using Helyx.Shared;
using LibGit2Sharp;
using Spectre.Console;
using static Helyx.Data.ConfigurationHandler;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Helyx.Projects
{
    internal static class ProjectActions
    {
        public static void Display(Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Action>()
                    .Title(Strings.Common_SelectAction)
                    .AddChoices(Enum.GetValues<Action>())
                    .UseConverter(x => x switch
                    {
                        Action.ChangeStatus => Strings.Manage_ChangeStatus,
                        Action.AssignBadges => Strings.Manage_AssignBadges,
                        Action.AnalyzeProject => Strings.Manage_Analyze,
                        Action.ChangeName => Strings.Manage_ChangeName,
                        Action.RemoveProject => Strings.Manage_RemoveProject,
                        Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    })
                );

                switch (choice)
                {
                    case Action.ChangeStatus:
                        ChangeStatus(guid);
                        break;
                    case Action.AssignBadges:
                        AssignBadges(guid);
                        break;
                    case Action.AnalyzeProject:
                        AnalyzeProject(guid);
                        break;
                    case Action.ChangeName:
                        ChangeName(guid);
                        break;
                    case Action.RemoveProject:
                        RemoveProject(guid);

                        if (!GetConfig().Projects.ContainsKey(guid))
                            return;

                        break;
                    case Action.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void ChangeStatus(Guid guid)
        {
            var allStatuses = Tags.AllStatuses();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Guid?>()
                    .Title(Strings.Manage_SelectNewStatus)
                    .AddChoices(allStatuses.Keys.Cast<Guid?>().Append(null))
                    .UseConverter(x => x switch
                    {
                        null => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => Tags.Markup(allStatuses[(Guid)x], Markup.Escape(allStatuses[(Guid)x].Name))
                    })
            );

            if (choice == null)
                return;

            if (!UpdateProject(guid, x => x.Status = (Guid)choice))
                return;

            var project = GetProject(guid);

            if (!GitHubCalls.IsAuthorizedWithGitHub()
                || !project.GitHubSyncSettings.TryGetValue(GitHubSync.SyncStatusWithGitHubRepo, out var sync) || !sync
                || !Repository.IsValid(project.Path))
                return;

            GitHubActions.SyncStatus(guid, (Guid)choice);
        }

        private static void AssignBadges(Guid guid)
        {
            var project = GetProject(guid);

            var allBadges = Tags.AllBadges();

            if (allBadges.Count == 0)
            {
                UI.Info(Strings.Manage_NoBadgesToAssign, Strings.Badges_None_Title);
                Console.ReadKey();
                return;
            }

            var prompt = new MultiSelectionPrompt<Guid>()
                .Title(Strings.Manage_SelectBadges)
                .AddChoices(allBadges.Keys)
                .UseConverter(x => Tags.Markup(allBadges[x], $"[[{Markup.Escape(allBadges[x].Name)}]]"))
                .PageSize(15)
                .InstructionsText($"[{Color.Grey}]{Strings.Common_MultiSelectHint}[/]")
                .NotRequired();

            foreach (var badge in project.Badges.Where(allBadges.ContainsKey))
                prompt.Select(badge);

            var before = project.Badges.ToList();
            var after = AnsiConsole.Prompt(prompt);

            if (!UpdateProject(guid, x => x.Badges = after))
                return;

            if (!GitHubCalls.IsAuthorizedWithGitHub()
                || !GetProject(guid).GitHubSyncSettings.TryGetValue(GitHubSync.SyncBadgesWithGitHubRepo, out var sync) || !sync
                || !Repository.IsValid(project.Path))
                return;

            GitHubActions.SyncBadges(guid, before.Except(after));
        }

        private static void AnalyzeProject(Guid guid)
        {
            var project = GetProject(guid);

            if (!Directory.Exists(project.Path))
            {
                UI.Error(Strings.Common_ProjectFolderMissing + $"\n[{Color.Grey}]{Markup.Escape(project.Path)}[/]", Strings.Manage_Analyze);
                Console.ReadKey();
                return;
            }

            AnsiConsole.Clear();

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };

            Again:

            var again = false;

            var perLanguage = new Dictionary<string, (int Lines, int Files)>();
            List<(string Path, long Size)> largest = [];

            var filesCount = 0;
            var foldersCount = 0;
            var trackedCount = 0;

            long totalBytes = 0;
            long trackedBytes = 0;
            long gitBytes = 0;

            var commits = 0;
            var changes = 0;
            var branches = 0;

            DateTimeOffset? firstCommit = null;
            string? branchName = null;
            string? lastCommit = null;

            var hasRepo = Repository.IsValid(project.Path);

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(Strings.Analyze_Running, ctx =>
                {
                    using var repo = hasRepo ? new Repository(project.Path) : null;

                    var tracked = repo?.Index.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(project.Path, "*", options))
                        {
                            long size;

                            try
                            {
                                size = new FileInfo(file).Length;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                continue;
                            }

                            filesCount++;
                            totalBytes += size;
                            largest.Add((file, size));

                            if (tracked.Contains(Path.GetRelativePath(project.Path, file).Replace('\\', '/')))
                            {
                                trackedCount++;
                                trackedBytes += size;
                            }

                            if (!ProjectsMenu.CodingLanguages.TryGetValue(Path.GetExtension(file), out var language))
                                continue;

                            try
                            {
                                var lines = File.ReadLines(file).Count();

                                perLanguage[language] = perLanguage.TryGetValue(language, out var current)
                                    ? (current.Lines + lines, current.Files + 1)
                                    : (lines, 1);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }

                    try
                    {
                        foldersCount = Directory.EnumerateDirectories(project.Path, "*", options).Count();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }

                    if (repo == null)
                        return;

                    ctx.Status(Strings.Analyze_ReadingRepo);

                    try
                    {
                        gitBytes = Directory.EnumerateFiles(repo.Info.Path, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = 0
                        }).Sum(x => new FileInfo(x).Length);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }

                    commits = repo.Commits.Count();

                    firstCommit = repo.Commits
                        .QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Reverse | CommitSortStrategies.Time })
                        .FirstOrDefault()?.Author.When;

                    changes = repo.RetrieveStatus(GitHelper.FastStatus).Count(x => x.State != FileStatus.Ignored);
                    branches = repo.Branches.Count(x => !x.IsRemote);
                    branchName = repo.Head.FriendlyName;

                    lastCommit = repo.Head.Tip == null
                        ? null
                        : $"[{Color.DarkOrange3}]{repo.Head.Tip.Sha[..7]}[/]  [{Color.White}]{Markup.Escape(repo.Head.Tip.MessageShort)}[/]";
                });

            var totalLines = perLanguage.Values.Sum(x => x.Lines);
            var ranked = perLanguage.OrderByDescending(x => x.Value.Lines).ToList();
            var top = largest.OrderByDescending(x => x.Size).Take(5).ToList();

            var scroll = 0;

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(2),
                    new Layout("Body"),
                    new Layout("Footer").Size(3));

            layout["Header"].Update(new Padder(
                new Rule($"[bold {Color.Blue}]{Strings.Manage_Analyze} · {Markup.Escape(project.HelyxName)}[/]").LeftJustified(),
                new Spectre.Console.Padding(0, 0, 0, 1)));

            AnsiConsole.Clear();

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    var running = true;

                    var labels = new[]
                    {
                        Strings.Analyze_Files,
                        Strings.Analyze_Folders,
                        Strings.Analyze_SizeOnDisk,
                        Strings.Analyze_Lines,
                        Strings.Analyze_Branch,
                        Strings.Analyze_Commits,
                        Strings.Analyze_FirstCommit,
                        Strings.Analyze_LastCommit,
                        Strings.Analyze_WorkingTree,
                        Strings.Analyze_GitSize
                    };

                    var labelWidth = labels.Max(x => x.Length) + 2;

                    while (running)
                    {
                        var width = Math.Max(30, Console.WindowWidth - 6);
                        var height = Math.Max(3, Console.WindowHeight - 9);
                        var label = Math.Min(labelWidth, width / 3);
                        var detail = width >= label + 46;
                        var bar = Math.Clamp(Math.Min(width - label - (detail ? 40 : 12), width - 23), 0, 30);
                        var nameWidth = Math.Max(8, width - bar - 15);

                        List<string> lines = [$"[{Color.Grey50}]{Markup.Escape(Shorten(project.Path, width))}[/]", string.Empty];

                        Row(Strings.Analyze_Files, $"{filesCount:N0}", hasRepo ? string.Format(Strings.Analyze_Tracked, $"{trackedCount:N0}") : null);
                        Row(Strings.Analyze_Folders, $"{foldersCount:N0}", null);
                        Row(Strings.Analyze_SizeOnDisk, FormatBytes(totalBytes), hasRepo ? string.Format(Strings.Analyze_Tracked, FormatBytes(trackedBytes)) : null);
                        Row(Strings.Analyze_Lines, perLanguage.Count == 0 ? "—" : $"{totalLines:N0}",
                            perLanguage.Count == 0 ? Strings.Analyze_NoSourceFiles : string.Format(Strings.Analyze_InFiles, $"{perLanguage.Values.Sum(x => x.Files):N0}"));

                        if (totalLines > 0)
                        {
                            Section(Strings.Analyze_Languages);

                            foreach (var language in ranked.Take(8))
                                lines.Add($"[{Color.Grey}]{Markup.Escape(Fit(language.Key, label)).PadRight(label)}[/]" +
                                          $"{Bar((double)language.Value.Lines / totalLines, bar, UI.GetColor(language.Key).ToMarkup())}  " +
                                          $"[{Color.White}]{(double)language.Value.Lines / totalLines * 100,5:0.0}%[/]" +
                                          (detail ? $"  [{Color.Grey50}]{string.Format(Strings.Analyze_LinesDetail, $"{language.Value.Lines:N0}")}[/]" : string.Empty));

                            if (ranked.Count > 8)
                            {
                                var rest = ranked.Skip(8).Sum(x => x.Value.Lines);

                                lines.Add($"[{Color.Grey}]{Strings.Analyze_Other.PadRight(label)}[/]" +
                                          $"{Bar((double)rest / totalLines, bar, "grey35")}  " +
                                          $"[{Color.White}]{(double)rest / totalLines * 100,5:0.0}%[/]" +
                                          (detail ? $"  [{Color.Grey50}]{string.Format(Strings.Analyze_LinesDetail, $"{rest:N0}")}[/]" : string.Empty));
                            }
                        }

                        if (top.Count > 0)
                        {
                            Section(Strings.Analyze_LargestFiles);

                            for (var i = 0; i < top.Count; i++)
                            {
                                var color = ProjectsMenu.CodingLanguages.TryGetValue(Path.GetExtension(top[i].Path), out var language)
                                    ? UI.GetColor(language).ToMarkup()
                                    : "grey35";

                                lines.Add($"[{Color.Grey35}]{i + 1}.[/] [{Color.White}]{Markup.Escape(Fit(Path.GetFileName(top[i].Path), nameWidth)).PadRight(nameWidth)}[/]  " +
                                          $"{Bar((double)top[i].Size / Math.Max(1, top[0].Size), bar, color)}  " +
                                          $"[{Color.Grey}]{FormatBytes(top[i].Size)}[/]");

                                var folder = Path.GetDirectoryName(Path.GetRelativePath(project.Path, top[i].Path));

                                if (!string.IsNullOrEmpty(folder))
                                    lines.Add($"   [{Color.Grey50}]{Markup.Escape(Shorten(folder.Replace('\\', '/'), width - 3))}[/]");
                            }
                        }

                        if (hasRepo)
                        {
                            Section("Git");

                            Row(Strings.Analyze_Branch, Markup.Escape(branchName ?? "—"), string.Format(branches == 1 ? Strings.Analyze_LocalBranchOne : Strings.Analyze_LocalBranches, branches));
                            Row(Strings.Analyze_Commits, $"{commits:N0}", null);

                            if (firstCommit != null)
                            {
                                var days = (int)(DateTimeOffset.Now - firstCommit.Value).TotalDays;

                                Row(Strings.Analyze_FirstCommit, $"{firstCommit.Value.ToLocalTime():d MMM yyyy}", days switch
                                {
                                    < 1 => Strings.Analyze_Today,
                                    < 60 => string.Format(Strings.Analyze_DaysAgo, days),
                                    < 730 => string.Format(Strings.Analyze_MonthsAgo, days / 30),
                                    _ => string.Format(Strings.Analyze_YearsAgo, days / 365)
                                });
                            }

                            if (lastCommit != null)
                                lines.Add($"[{Color.Grey}]{Fit(Strings.Analyze_LastCommit, label).PadRight(label)}[/]{lastCommit}");

                            Row(Strings.Analyze_WorkingTree, changes == 0 ? $"[{Color.Green3_1}]{Strings.Analyze_Clean}[/]" : $"[{Color.Orange1}]{changes:N0}[/] {Strings.Analyze_Changed}", null);
                            Row(Strings.Analyze_GitSize, FormatBytes(gitBytes), null);
                        }

                        scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - height));

                        layout["Body"].Update(new Panel(new Markup(string.Join("\n", lines.Skip(scroll).Take(height))))
                            .RoundedBorder()
                            .BorderColor(Color.Grey)
                            .Padding(1, 0)
                            .Expand());

                        layout["Footer"].Update(new Panel(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                        $"[{Color.Grey}]{Strings.Analyze_Footer}[/]",
                                        lines.Count > height
                                            ? $"[{Color.Grey}]{scroll + 1}-{Math.Min(scroll + height, lines.Count)}/{lines.Count}[/]"
                                            : $"[{Color.Grey}]{Strings.Analyze_All}[/]"))
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

                            case ConsoleKey.R:
                                again = true;
                                running = false;
                                break;

                            case ConsoleKey.Escape:
                                running = false;
                                break;
                        }

                        UI.FlushInput();
                        continue;

                        void Row(string name, string value, string? note) =>
                            lines.Add($"[{Color.Grey}]{Fit(name, label).PadRight(label)}[/][{Color.White}]{value}[/]" +
                                      (note == null || !detail ? string.Empty : $"  [{Color.Grey50}]{Markup.Escape(note)}[/]"));

                        void Section(string title) =>
                            lines.AddRange([string.Empty, $"[bold {Color.Blue}]{title}[/] [{Color.Grey30}]{new string('─', Math.Max(1, width - title.Length - 1))}[/]"]);
                    }
                });

            AnsiConsole.Clear();

            if (again)
                goto Again;

            return;

            static string Bar(double fraction, int cells, string color)
            {
                var filled = Math.Clamp((int)Math.Round(fraction * cells), fraction > 0 ? 1 : 0, cells);

                return $"[{color}]{new string('█', filled)}[/][{Color.Grey27}]{new string('░', cells - filled)}[/]";
            }

            static string Fit(string text, int max) =>
                text.Length <= max ? text : text[..Math.Max(1, max - 1)] + "…";

            static string Shorten(string text, int max) =>
                text.Length <= max ? text : "…" + text[^Math.Max(1, max - 1)..];
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];

            double size = bytes;
            int unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }

        private static void ChangeName(Guid guid)
        {
            var project = GetProject(guid);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<ChangeNameOptions>()
                .Title(Strings.Manage_ChangeNameOf)
                .AddChoices(Enum.GetValues<ChangeNameOptions>())
                .UseConverter(x => x switch 
                {
                    ChangeNameOptions.Helyx => "Helyx",
                    ChangeNameOptions.GitHub => "GitHub",
                    ChangeNameOptions.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                    _ => x.ToString()
                })
            );

            switch (choice)
            {
                case ChangeNameOptions.Helyx:
                    var newName = AnsiConsole.Prompt(
                        new TextPrompt<string>(string.Format(Strings.Manage_EnterNewNameFor, choice.ToString()))
                            .WithConverter(Markup.Escape)
                            .DefaultValue(project.HelyxName)
                    ).Trim();

                    AnsiConsole.Clear();
                    ProjectsMenu.PrintHeader(guid);

                    if (string.IsNullOrEmpty(newName))
                    {
                        UI.Error(Strings.Projects_NameEmpty, Strings.Common_InvalidName);
                        Console.ReadKey();
                        return;
                    }

                    if (GetConfig().Projects.Any(x => x.Key != guid && x.Value.HelyxName.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        UI.Warning(string.Format(Strings.Projects_NameExists, $"'{Markup.Escape(newName)}'") + "\n" + Strings.Common_Continue, Strings.Projects_DuplicateProject_Title);

                        var keep = AnsiConsole.Prompt(
                            new SelectionPrompt<Confirm>()
                                .AddChoices(Enum.GetValues<Confirm>())
                                .UseConverter(UI.ConfirmName));

                        AnsiConsole.Clear();
                        ProjectsMenu.PrintHeader(guid);

                        if (keep == Confirm.No)
                            return;
                    }

                    UpdateProject(guid, x => x.HelyxName = newName);
                    break;
                case ChangeNameOptions.GitHub:
                    List<GitHubRepository>? repos = null;

                    AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .Start(Strings.GitHub_LoadingRepos, ctx =>
                            repos = GitHubCalls.GetAllRepos().GetAwaiter().GetResult()
                        );

                    if (repos == null)
                    {
                        UI.Error(Strings.GitHub_UnreachableSettings + "\n" + Strings.GitHub_CheckConnection, Strings.Manage_ChangeName);
                        Console.ReadKey();
                        return;
                    }

                    if (repos.Count == 0)
                    {
                        UI.Info(Strings.GitHub_NoRepos, Strings.Manage_ChangeName);
                        Console.ReadKey();
                        return;
                    }

                    repos.Add(null!);

                    var newRepo = AnsiConsole.Prompt(
                        new SelectionPrompt<GitHubRepository>()
                        .Title(Strings.GitHub_SelectRepo)
                        .AddChoices(repos)
                        .UseConverter(x => x switch
                        {
                            null => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                            _ => x.Name ?? string.Empty
                        }));

                    if (newRepo == null)
                        return;

                    UpdateProject(guid, x => x.GitHubName = newRepo.Name ?? string.Empty);
                    break;
                case ChangeNameOptions.Back:
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void RemoveProject(Guid guid)
        {
            var project = GetProject(guid);
            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(string.Format(Strings.Manage_RemoveConfirm, $"[bold]{Markup.Escape(project.HelyxName)}[/]") + "\n" +
                           $"[bold underline]{Strings.Manage_CannotBeUndone}[/]\n\n" +
                           $"[bold {Color.Red}]{Strings.Manage_AlsoDeletes}[/]")
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(x => x switch
                    {
                        Confirm.Yes => Strings.Common_Yes,
                        Confirm.No => Strings.Common_No,
                        _ => x.ToString()
                    })
            );

            AnsiConsole.Clear();

            if (confirm == Confirm.No)
                return;

            try
            {
                Backups.DeleteAllBackups(guid);
                UserScripts.DeleteAllScripts(guid);
            }
            catch (Exception ex)
            {
                UI.Warning(Strings.Manage_CleanupFailed + $"\n\n{Markup.Escape(ex.Message)}", Strings.Manage_CleanupIncomplete);
                Console.ReadKey();
            }

            if (!Update(x => x.Projects.Remove(guid)))
                return;

            UI.Success(string.Format(Strings.Manage_Removed, $"[{Color.SteelBlue1}]'{Markup.Escape(project.HelyxName)}'[/]"), Strings.Manage_Removed_Title);
            Console.ReadKey();
            AnsiConsole.Clear();
        }

        private enum ChangeNameOptions
        {
            Helyx,
            GitHub,
            Back
        }

        private enum Action
        {
            ChangeStatus,
            AssignBadges,
            AnalyzeProject,
            ChangeName,
            RemoveProject,
            Back
        }
    }
}
