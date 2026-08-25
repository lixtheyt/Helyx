using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using LibGit2Sharp;
using Panel = Spectre.Console.Panel;
using Spectre.Console;
using static Helyx.Data.ConfigurationHandler;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Helyx.Projects
{
    internal static class GitActions
    {
        internal static void Display(Guid guid)
        {
            var project = GetProject(guid);

            while (true)
            {
                AnsiConsole.Clear();

                if (!Directory.Exists(project.Path))
                {
                    UI.Error($"{Strings.Git_FolderGone}\n[grey]{project.Path}[/]", Strings.Git_FolderGone_Title);
                    Console.ReadKey();
                    return;
                }

                if (!Repository.IsValid(project.Path))
                {
                    UI.Error(string.Format(Strings.Git_NotARepo, $"[SteelBlue1]{Markup.Escape(project.HelyxName)}[/]"), Strings.Git_NotARepo_Title);

                    var initConfirm = AnsiConsole.Prompt(
                        new SelectionPrompt<Confirm>()
                            .Title($"[green]{Strings.Git_InitAsk}[/]")
                            .AddChoices(Enum.GetValues<Confirm>())
                            .UseConverter(UI.ConfirmName));

                    if (initConfirm == Confirm.No)
                        return;

                    try
                    {
                        Repository.Init(project.Path);
                    }
                    catch (Exception ex)
                    {
                        UI.Error($"{Strings.Git_InitFailed}\n\n{Markup.Escape(ex.Message)}", Strings.Git_InitFailed_Title);
                        Console.ReadKey();
                        return;
                    }
                }

                AnsiConsole.Clear();

                ProjectsMenu.PrintHeader(guid);

                var arrow = new[]
                {
                    Strings.Git_Changes,
                    Strings.Git_Sync,
                    Strings.Git_Branches,
                    Strings.Git_Stashes
                }.Max(x => x.Length) + 1;

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Action1>()
                        .Title(Strings.Common_SelectAction)
                        .AddChoices(Enum.GetValues<Action1>())
                        .UseConverter(x => x switch
                        {
                            Action1.Status => Strings.Git_Status,
                            Action1.Changes => Strings.Git_Changes.PadRight(arrow) + "[grey]▸[/]",
                            Action1.Sync => Strings.Git_Sync.PadRight(arrow) + "[grey]▸[/]",
                            Action1.Branches => Strings.Git_Branches.PadRight(arrow) + "[grey]▸[/]",
                            Action1.Stashes => Strings.Git_Stashes.PadRight(arrow) + "[grey]▸[/]",
                            Action1.Log => Strings.Git_Log,
                            Action1.Diagnostics => Strings.Git_Diagnostics,
                            Action1.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => x.ToString()
                        })
                );

                switch (choice)
                {
                    case Action1.Status:
                        Status(guid);
                        break;
                    case Action1.Changes:
                        DisplayCategoryActions(Action1.Changes, guid);
                        break;
                    case Action1.Sync:
                        DisplayCategoryActions(Action1.Sync, guid);
                        break;
                    case Action1.Branches:
                        DisplayCategoryActions(Action1.Branches, guid);
                        break;
                    case Action1.Stashes:
                        DisplayCategoryActions(Action1.Stashes, guid);
                        break;
                    case Action1.Log:
                        Log(guid);
                        break;
                    case Action1.Diagnostics:
                        Diagnostics(guid);
                        break;
                    case Action1.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void DisplayCategoryActions(Action1 category, Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                var project = GetProject(guid);

                var choices = category switch
                {
                    Action1.Changes => new[] { Action2.Stage, Action2.Commit, Action2.Diff, Action2.UndoCommit, Action2.RedoCommit, Action2.Back },
                    Action1.Sync => new[] { Action2.Push, Action2.Pull, Action2.Fetch, Action2.Sync, Action2.Back },
                    Action1.Branches => new[] { Action2.CreateBranch, Action2.SwitchBranch, Action2.MergeBranch, Action2.DeleteBranch, Action2.Back },
                    Action1.Stashes => new[] { Action2.SaveStash, Action2.ApplyStash, Action2.PopStash, Action2.ListStashes, Action2.Back },
                    _ => Array.Empty<Action2>()
                };

                switch (category)
                {
                    case Action1.Branches:
                        {
                            using var repo = GitHelper.OpenRepo(project.Path, Strings.Git_Branches);

                            if (repo == null)
                                return;

                            UI.Info(repo.Head.FriendlyName, Strings.Git_CurrentBranch);
                            break;
                        }
                    case Action1.Sync when !GitHubCalls.IsAuthorizedWithGitHub():
                        UI.Error(Strings.GH_NeedAuth, Strings.Git_Sync);
                        Console.ReadKey();
                        return;
                }

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Action2>()
                        .Title($"Git › {category switch
                        {
                            Action1.Changes => Strings.Git_Changes,
                            Action1.Sync => Strings.Git_Sync,
                            Action1.Branches => Strings.Git_Branches,
                            Action1.Stashes => Strings.Git_Stashes,
                            _ => category.ToString()
                        }}")
                        .AddChoices(choices)
                        .UseConverter(x => x switch
                        {
                            Action2.Stage => Strings.Git_Stage,
                            Action2.Commit => Strings.Git_Commit,
                            Action2.Diff => "Diff",
                            Action2.UndoCommit => Strings.Git_UndoCommit,
                            Action2.RedoCommit => Strings.Git_RedoCommit,
                            Action2.Push => "Push",
                            Action2.Pull => "Pull",
                            Action2.Fetch => "Fetch",
                            Action2.Sync => Strings.Git_SyncAuto,
                            Action2.CreateBranch => Strings.Git_CreateBranch,
                            Action2.SwitchBranch => Strings.Git_SwitchBranch,
                            Action2.MergeBranch => Strings.Git_MergeBranch,
                            Action2.DeleteBranch => Strings.Git_DeleteBranch,
                            Action2.SaveStash => Strings.Git_SaveStash,
                            Action2.ApplyStash => Strings.Git_ApplyStash,
                            Action2.PopStash => Strings.Git_PopStash,
                            Action2.ListStashes => Strings.Git_ListStashes,
                            Action2.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => x.ToString()
                        }));

                switch (choice)
                {
                    case Action2.Stage: Stage(guid).GetAwaiter().GetResult(); break;
                    case Action2.Commit: Commit(guid); break;
                    case Action2.Diff: Diff(guid); break;
                    case Action2.UndoCommit: UndoCommit(guid); break;
                    case Action2.RedoCommit: RedoCommit(guid); break;
                    case Action2.Push: Push(guid); break;
                    case Action2.Pull: Pull(guid); break;
                    case Action2.Fetch: Fetch(guid); break;
                    case Action2.Sync: Sync(guid); break;
                    case Action2.CreateBranch: CreateBranch(guid); break;
                    case Action2.SwitchBranch: SwitchBranch(guid); break;
                    case Action2.MergeBranch: MergeBranch(guid); break;
                    case Action2.DeleteBranch: DeleteBranch(guid); break;
                    case Action2.SaveStash: SaveStash(guid); break;
                    case Action2.ApplyStash: ApplyStash(guid); break;
                    case Action2.PopStash: PopStash(guid); break;
                    case Action2.ListStashes: ListStashes(guid); break;
                    case Action2.Back: return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void Status(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;
            RepositoryStatus status;

            try
            {
                status = repo.RetrieveStatus(GitHelper.FastStatus);
            }
            catch (Exception ex)
            {
                UI.Error(ex.Message);
                Console.ReadKey();
                return;
            }

            AnsiConsole.Clear();

            AnsiConsole.Write(new Rule($"[blue bold]{string.Format(Strings.Git_StatusTitle, Markup.Escape(project.HelyxName))}[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn($"[bold]{Strings.Git_Col_File}[/]"))
                .AddColumn(new TableColumn($"[bold]{Strings.Git_Col_State}[/]"));

            if (status.Count(x => x.State == FileStatus.Unaltered) == status.Count())
            {
                table.AddRow($"[grey]{Strings.Git_WorkingTreeClean}[/]", "[grey]—[/]");
            }
            else
            {
                foreach (var file in status)
                {
                    var (color, label) = GitHelper.StatusColorLabel(file.State);
                    table.AddRow(Markup.Escape(file.FilePath), $"[{color}]{label}[/]");
                }
            }

            AnsiConsole.Write(table);

            Console.ReadKey();
        }

        #region Changes
        private static async Task Stage(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            RepositoryStatus status;

            try
            {
                status = repo.RetrieveStatus(GitHelper.FastStatus);
            }
            catch (Exception ex)
            {
                UI.Error(ex.Message);
                Console.ReadKey();
                return;
            }

            Exception? err = null;

            var changedEntries = status
                .Where(e =>
                    e.State.HasFlag(FileStatus.NewInIndex) ||
                    e.State.HasFlag(FileStatus.ModifiedInIndex) ||
                    e.State.HasFlag(FileStatus.DeletedFromIndex) ||
                    e.State.HasFlag(FileStatus.NewInWorkdir) ||
                    e.State.HasFlag(FileStatus.ModifiedInWorkdir) ||
                    e.State.HasFlag(FileStatus.DeletedFromWorkdir))
                .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (changedEntries.Count == 0)
            {
                UI.Info(Strings.Git_NothingToStage, Strings.Git_StageFiles);
                Console.ReadKey();
                return;
            }

            var entriesByPath = changedEntries.ToDictionary(e => e.FilePath);

            var prompt = new MultiSelectionPrompt<string>()
                .Title(Strings.Git_SelectFilesToStage)
                .PageSize(15)
                .InstructionsText($"[grey]{Strings.Git_ToggleHint}[/]")
                .UseConverter(x => entriesByPath.TryGetValue(x, out var entry)
                    ? $"{Markup.Escape(entry.FilePath)} " +
                      $"[{GitHelper.StatusColorLabel(entry.State).color}]" +
                      $"({GitHelper.StatusColorLabel(entry.State).label})[/]"
                    : $"[bold]{Markup.Escape(x)}[/]")
                .NotRequired();

            var all = prompt.AddChoice(Strings.Git_AllFiles);

            foreach (var group in changedEntries
                         .GroupBy(e => GitHelper.Group(e.State))
                         .OrderBy(g => g.Key))
            {
                var node = all.AddChild(GitHelper.GroupName(group.Key));

                foreach (var entry in group)
                {
                    node.AddChild(entry.FilePath);

                    if (GitHelper.IsStagedInIndex(entry.State))
                        prompt.Select(entry.FilePath);
                }
            }

            var selectedEntries = AnsiConsole.Prompt(prompt)
                                                        .Where(entriesByPath.ContainsKey)
                                                        .Select(path => entriesByPath[path])
                                                        .ToList();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots).StartAsync(Strings.Git_Staging, ctx =>
                {
                    try
                    {
                        var previouslyStaged = changedEntries.Where(e => GitHelper.IsStagedInIndex(e.State)).ToList();
                        var toUnstage = previouslyStaged.Where(e => !selectedEntries.Contains(e)).ToList();

                        if (toUnstage.Count > 0)
                        {
                            ctx.Status(Strings.Git_Unstaging);
                            Commands.Unstage(repo, toUnstage.Select(x => x.FilePath));
                        }

                        if (selectedEntries.Count > 0)
                        {
                            ctx.Status(Strings.Git_Staging);
                            Commands.Stage(repo, selectedEntries.Select(x => x.FilePath));
                        }
                        return Task.CompletedTask;
                    }
                    catch (Exception exception)
                    {
                        err = exception;
                        return Task.CompletedTask;
                    }
                });

            if (err != null)
            {
                UI.Error(err.Message);
                Console.ReadKey();
            }

            AnsiConsole.Clear();
        }
        private static void Commit(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            List<StatusEntry> stagedEntries;

            try
            {
                stagedEntries = repo.RetrieveStatus(GitHelper.FastStatus)
                   .Where(e => GitHelper.IsStagedInIndex(e.State))
                   .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
                   .ToList();
            }
            catch (Exception ex)
            {
                UI.Error(ex.Message);
                Console.ReadKey();
                return;
            }

            if (stagedEntries.Count == 0)
            {
                UI.Info(string.Format(Strings.Git_NothingStaged, $"[bold]{Strings.Git_Stage}[/]"), Strings.Git_Commit);
                Console.ReadKey();
                return;
            }

            var summaryGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                .AddColumn();

            foreach (var entry in stagedEntries)
            {
                var (color, label) = GitHelper.StatusColorLabel(entry.State);
                summaryGrid.AddRow($"[{color}]{label}[/]", Markup.Escape(entry.FilePath));
            }

            UI.Box(summaryGrid, string.Format(Strings.Git_FilesToCommit, stagedEntries.Count));

            var proceed = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.Git_ProceedCommit)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(x => x switch
                    {
                        Confirm.Yes => Strings.Common_Yes,
                        Confirm.No => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    }));

            if (proceed == Confirm.No)
            {
                AnsiConsole.Clear();
                return;
            }

            var message = AnsiConsole.Prompt(
                new TextPrompt<string>(Strings.Git_CommitMessage)
                .AllowEmpty());

            if (string.IsNullOrEmpty(message))
                return;

            AnsiConsole.WriteLine();

            Commit commit;

            try
            {
                var signature = new Signature(GitHubCalls.MainIdentity(repo.Config), DateTimeOffset.Now);

                commit = repo.Commit(message, signature, signature);

                UpdateProject(guid, x => x.UndoState = null);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message));
                Console.ReadKey();
                return;
            }

            UI.Success(string.Format(Strings.Git_CommittedAs, $"[DarkOrange3]{commit.Sha[..7]}[/]", Markup.Escape(message)), Strings.Git_Commit);
            Console.ReadKey();
        }
        private static void Diff(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            List<StatusEntry> changedEntries;

            try
            {
                changedEntries = repo.RetrieveStatus(GitHelper.FastStatus)
                    .Where(e => e.State != FileStatus.Ignored && e.State != FileStatus.Unaltered)
                    .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                UI.Error(ex.Message);
                Console.ReadKey();
                return;
            }

            if (changedEntries.Count == 0)
            {
                UI.Info(Strings.Git_NothingToDiff, "Diff");
                Console.ReadKey();
                return;
            }

            int selectedIndex = 0;
            int scrollOffset = 0;
            var focus = DiffFocus.Files;

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Panes")
                        .SplitColumns(
                            new Layout("Files").Ratio(1),
                            new Layout("Diff").Ratio(3)),
                    new Layout("Footer").Size(3));

            AnsiConsole.Clear();
            AnsiConsole.Cursor.Hide();

            try
            {
                AnsiConsole.Live(layout)
                    .Start(ctx =>
                    {
                        var diffLines = GetDiffLines(repo, changedEntries[selectedIndex].FilePath);
                        RefreshDiffView(layout, changedEntries, selectedIndex, diffLines, ref scrollOffset, focus);
                        ctx.Refresh();

                        while (true)
                        {
                            var key = Console.ReadKey(intercept: true).Key;

                            switch (key)
                            {
                                case ConsoleKey.Tab:
                                    focus = focus == DiffFocus.Files ? DiffFocus.Diff : DiffFocus.Files;
                                    break;

                                case ConsoleKey.UpArrow when focus == DiffFocus.Files:
                                    selectedIndex = Math.Max(0, selectedIndex - 1);
                                    scrollOffset = 0;
                                    diffLines = GetDiffLines(repo, changedEntries[selectedIndex].FilePath);
                                    break;

                                case ConsoleKey.DownArrow when focus == DiffFocus.Files:
                                    selectedIndex = Math.Min(changedEntries.Count - 1, selectedIndex + 1);
                                    scrollOffset = 0;
                                    diffLines = GetDiffLines(repo, changedEntries[selectedIndex].FilePath);
                                    break;

                                case ConsoleKey.UpArrow when focus == DiffFocus.Diff:
                                    scrollOffset = Math.Max(0, scrollOffset - 1);
                                    break;

                                case ConsoleKey.DownArrow when focus == DiffFocus.Diff:
                                    scrollOffset = Math.Min(Math.Max(0, diffLines.Count - 1), scrollOffset + 1);
                                    break;

                                case ConsoleKey.PageUp:
                                    scrollOffset = Math.Max(0, scrollOffset - GetDiffPaneHeight());
                                    break;

                                case ConsoleKey.PageDown:
                                    scrollOffset = Math.Min(Math.Max(0, diffLines.Count - 1), scrollOffset + GetDiffPaneHeight());
                                    break;

                                case ConsoleKey.Escape:
                                    return;
                            }

                            RefreshDiffView(layout, changedEntries, selectedIndex, diffLines, ref scrollOffset, focus);
                            ctx.Refresh();
                        }
                    });
            }
            finally
            {
                AnsiConsole.Cursor.Show();
                AnsiConsole.Clear();
            }
        }

        private static void Diff(Guid guid, string commitSha)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            Commit? commit = repo.Lookup<Commit>(commitSha);

            if (commit == null)
            {
                UI.Error(Strings.Git_CommitNotFound);
                Console.ReadKey();
                return;
            }

            Commit? parent = commit.Parents.FirstOrDefault();

            Patch patch;

            try
            {
                patch = repo.Diff.Compare<Patch>(
                    parent?.Tree,
                    commit.Tree);
            }
            catch (Exception ex)
            {
                UI.Error(ex.Message);
                Console.ReadKey();
                return;
            }

            var changedEntries = patch
                .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (changedEntries.Count == 0)
            {
                UI.Info(Strings.Git_NoChangesInCommit, "Diff");
                Console.ReadKey();
                return;
            }

            int selectedIndex = 0;
            int scrollOffset = 0;
            var focus = DiffFocus.Files;

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Panes")
                        .SplitColumns(
                            new Layout("Files").Ratio(1),
                            new Layout("Diff").Ratio(3)),
                    new Layout("Footer").Size(3));

            AnsiConsole.Clear();
            AnsiConsole.Cursor.Hide();

            try
            {
                AnsiConsole.Live(layout)
                    .Start(ctx =>
                    {
                        var diffLines = GetDiffLines(patch, changedEntries[selectedIndex].Path);

                        RefreshDiffView(
                            layout,
                            changedEntries,
                            selectedIndex,
                            diffLines,
                            ref scrollOffset,
                            focus);

                        ctx.Refresh();

                        while (true)
                        {
                            var key = Console.ReadKey(true).Key;

                            switch (key)
                            {
                                case ConsoleKey.Tab:
                                    focus = focus == DiffFocus.Files
                                        ? DiffFocus.Diff
                                        : DiffFocus.Files;
                                    break;

                                case ConsoleKey.UpArrow when focus == DiffFocus.Files:
                                    selectedIndex = Math.Max(0, selectedIndex - 1);
                                    scrollOffset = 0;
                                    diffLines = GetDiffLines(
                                        patch,
                                        changedEntries[selectedIndex].Path);
                                    break;

                                case ConsoleKey.DownArrow when focus == DiffFocus.Files:
                                    selectedIndex = Math.Min(
                                        changedEntries.Count - 1,
                                        selectedIndex + 1);

                                    scrollOffset = 0;

                                    diffLines = GetDiffLines(
                                        patch,
                                        changedEntries[selectedIndex].Path);
                                    break;

                                case ConsoleKey.UpArrow when focus == DiffFocus.Diff:
                                    scrollOffset = Math.Max(0, scrollOffset - 1);
                                    break;

                                case ConsoleKey.DownArrow when focus == DiffFocus.Diff:
                                    scrollOffset = Math.Min(
                                        Math.Max(0, diffLines.Count - 1),
                                        scrollOffset + 1);
                                    break;

                                case ConsoleKey.PageUp:
                                    scrollOffset = Math.Max(
                                        0,
                                        scrollOffset - GetDiffPaneHeight());
                                    break;

                                case ConsoleKey.PageDown:
                                    scrollOffset = Math.Min(
                                        Math.Max(0, diffLines.Count - 1),
                                        scrollOffset + GetDiffPaneHeight());
                                    break;

                                case ConsoleKey.Escape:
                                    return;
                            }

                            RefreshDiffView(
                                layout,
                                changedEntries,
                                selectedIndex,
                                diffLines,
                                ref scrollOffset,
                                focus);

                            ctx.Refresh();
                        }
                    });
            }
            finally
            {
                AnsiConsole.Cursor.Show();
                AnsiConsole.Clear();
            }
        }
        #region Diff Funcs
        private static void RefreshDiffView(Layout layout, List<StatusEntry> changedEntries, int selectedIndex, List<string> diffLines, ref int scrollOffset, DiffFocus focus)
        {
            var filesGrid = new Grid().AddColumn();

            for (int i = 0; i < changedEntries.Count; i++)
            {
                var entry = changedEntries[i];
                var (color, label) = GitHelper.StatusColorLabel(entry.State);

                bool isSelected = i == selectedIndex;
                string marker = isSelected ? "[Aqua]>[/] " : "  ";
                string name = isSelected ? $"[bold]{Markup.Escape(entry.FilePath)}[/]" : Markup.Escape(entry.FilePath);

                filesGrid.AddRow($"{marker}{name} [{color}]({label})[/]");
            }

            layout["Files"].Update(
                new Panel(filesGrid)
                    .Header($"[blue bold] {Strings.Git_ChangedFiles} [/][grey]({selectedIndex + 1}/{changedEntries.Count})[/]")
                    .RoundedBorder()
                    .BorderColor(focus == DiffFocus.Files ? Color.Aqua : Color.Grey)
                    .Padding(1, 1)
                    .Expand());

            int paneHeight = GetDiffPaneHeight();
            int maxScroll = Math.Max(0, diffLines.Count - paneHeight);
            scrollOffset = Math.Min(scrollOffset, maxScroll);

            var visibleLines = diffLines
                .Skip(scrollOffset)
                .Take(paneHeight)
                .ToList();

            string diffText = visibleLines.Count > 0
                ? string.Join("\n", visibleLines)
                : $"[grey]{Strings.Git_NoDiffForFile}[/]";

            int lastVisibleLine = Math.Min(scrollOffset + paneHeight, diffLines.Count);
            string scrollInfo = diffLines.Count > paneHeight
                ? $" ({scrollOffset + 1}-{lastVisibleLine}/{diffLines.Count})"
                : string.Empty;

            var selectedEntry = changedEntries[selectedIndex];

            layout["Diff"].Update(
                new Panel(diffText)
                    .Header($"[blue bold] Diff — {Markup.Escape(selectedEntry.FilePath)}{scrollInfo} [/]")
                    .RoundedBorder()
                    .BorderColor(focus == DiffFocus.Diff ? Color.Aqua : Color.Grey)
                    .Padding(1, 1)
                    .Expand());

            layout["Footer"].Update(new Panel($"[grey]{Strings.Git_Diff_Footer}[/]")
                .RoundedBorder()
                .BorderColor(Color.Grey)
                .Expand()
                .Padding(1, 0));
        }

        private static void RefreshDiffView(Layout layout, List<PatchEntryChanges> changedEntries, int selectedIndex, List<string> diffLines, ref int scrollOffset, DiffFocus focus)
        {
            var filesGrid = new Grid().AddColumn();

            for (int i = 0; i < changedEntries.Count; i++)
            {
                var entry = changedEntries[i];

                var (color, label) = entry.Status switch
                {
                    ChangeKind.Added => ("green", Strings.Git_Kind_Added),
                    ChangeKind.Deleted => ("red", Strings.Git_Kind_Deleted),
                    ChangeKind.Renamed => ("cyan", Strings.Git_Kind_Renamed),
                    _ => ("yellow", Strings.Git_Kind_Modified)
                };

                bool isSelected = i == selectedIndex;
                string marker = isSelected ? "[Aqua]>[/] " : "  ";
                string name = isSelected ? $"[bold]{Markup.Escape(entry.Path)}[/]" : Markup.Escape(entry.Path);

                filesGrid.AddRow(
                    $"{marker}{name} [{color}]({label})[/]");
            }

            layout["Files"].Update(
                new Panel(filesGrid)
                    .Header($"[blue bold] {Strings.Git_ChangedFiles} [/][grey]({selectedIndex + 1}/{changedEntries.Count})[/]")
                    .RoundedBorder()
                    .BorderColor(focus == DiffFocus.Files ? Color.Aqua : Color.Grey)
                    .Padding(1, 1)
                    .Expand());

            int paneHeight = GetDiffPaneHeight();
            int maxScroll = Math.Max(0, diffLines.Count - paneHeight);
            scrollOffset = Math.Min(scrollOffset, maxScroll);

            var visibleLines = diffLines
                .Skip(scrollOffset)
                .Take(paneHeight)
                .ToList();

            string diffText = visibleLines.Count > 0
                ? string.Join("\n", visibleLines)
                : $"[grey]{Strings.Git_NoDiffForFile}[/]";

            int lastVisibleLine = Math.Min(scrollOffset + paneHeight, diffLines.Count);

            string scrollInfo = diffLines.Count > paneHeight
                ? $" ({scrollOffset + 1}-{lastVisibleLine}/{diffLines.Count})"
                : string.Empty;

            var selectedEntry = changedEntries[selectedIndex];

            layout["Diff"].Update(
                new Panel(diffText)
                    .Header($"[blue bold] Diff — {Markup.Escape(selectedEntry.Path)}{scrollInfo} [/]")
                    .RoundedBorder()
                    .BorderColor(focus == DiffFocus.Diff ? Color.Aqua : Color.Grey)
                    .Padding(1, 1)
                    .Expand());

            layout["Footer"].Update(new Panel($"[grey]{Strings.Git_Diff_Footer}[/]")
                .RoundedBorder()
                .BorderColor(Color.Grey)
                .Expand()
                .Padding(1, 0));
        }

        private static int GetDiffPaneHeight() =>
            Math.Max(5, Console.WindowHeight - 9);

        private static List<string> GetDiffLines(Repository repo, string relativePath)
        {
            PatchEntryChanges? entry;

            try
            {
                var oldTree = repo.Head.Tip?.Tree;

                var patch = repo.Diff.Compare<Patch>(
                    oldTree,
                    DiffTargets.WorkingDirectory | DiffTargets.Index,
                    [relativePath]);

                entry = patch[relativePath];
            }
            catch (Exception ex)
            {
                return [$"[red]{Strings.Git_DiffFailed}[/] {Markup.Escape(ex.Message)}"];
            }

            if (entry == null || string.IsNullOrEmpty(entry.Patch))
                return new List<string>();

            return entry.Patch
                .Split('\n')
                .Select(line =>
                {
                    string escaped = Markup.Escape(line);

                    return line switch
                    {
                        _ when line.StartsWith("+++") || line.StartsWith("---") => $"[bold]{escaped}[/]",
                        _ when line.StartsWith("@@") => $"[cyan]{escaped}[/]",
                        _ when line.StartsWith("+") => $"[green]{escaped}[/]",
                        _ when line.StartsWith("-") => $"[red]{escaped}[/]",
                        _ => $"[grey]{escaped}[/]"
                    };
                })
                .ToList();
        }

        private static List<string> GetDiffLines(Patch patch, string relativePath)
        {
            PatchEntryChanges? entry;

            try
            {
                entry = patch.FirstOrDefault(x => x.Path == relativePath);
            }
            catch (Exception ex)
            {
                return [$"[red]{Strings.Git_DiffFailed}[/] {Markup.Escape(ex.Message)}"];
            }

            if (entry == null || string.IsNullOrEmpty(entry.Patch))
                return [];

            return entry.Patch
                .Split('\n')
                .Select(line =>
                {
                    string escaped = Markup.Escape(line);

                    return line switch
                    {
                        _ when line.StartsWith("+++") ||
                               line.StartsWith("---")
                            => $"[bold]{escaped}[/]",

                        _ when line.StartsWith("@@")
                            => $"[cyan]{escaped}[/]",

                        _ when line.StartsWith("+")
                            => $"[green]{escaped}[/]",

                        _ when line.StartsWith("-")
                            => $"[red]{escaped}[/]",

                        _
                            => $"[grey]{escaped}[/]"
                    };
                })
                .ToList();
        }

        private enum DiffFocus { Files, Diff }
        #endregion

        private static void UndoCommit(Guid guid)
        {
            UI.Warning(Strings.Git_UndoWarning, Strings.Common_Warning);
            var confirm1 = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.Common_Continue)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName)
            );

            if (confirm1 == Confirm.No)
                return;

            AnsiConsole.Clear();

            ProjectsMenu.PrintHeader(guid);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<UndoCommitAction>()
                .Title(Strings.Git_UndoHow)
                .AddChoices(Enum.GetValues<UndoCommitAction>())
                .UseConverter(x => x switch
                {
                    UndoCommitAction.KeepChangesStaged => Strings.Git_Undo_Soft,
                    UndoCommitAction.KeepChangesUnstaged => Strings.Git_Undo_Mixed,
                    UndoCommitAction.DeleteChangesCompletely => Strings.Git_Undo_Hard,
                    UndoCommitAction.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                    _ => x.ToString()
                })
            );

            if (choice == UndoCommitAction.Back)
                return;

            UI.Warning(choice switch
            {
                UndoCommitAction.KeepChangesStaged => Strings.Git_Undo_SoftInfo,
                UndoCommitAction.KeepChangesUnstaged => Strings.Git_Undo_MixedInfo,
                UndoCommitAction.DeleteChangesCompletely => Strings.Git_Undo_HardInfo,
                _ => Strings.Git_Undo_GenericInfo
            });

            var confirm2 = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.Git_UndoConfirm)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName)
                );

            AnsiConsole.Clear();

            if (confirm2 == Confirm.No)
                return;

            ProjectsMenu.PrintHeader(guid);

            try
            {
                var project = GetProject(guid);
                using var repo = GitHelper.OpenRepo(project.Path, Strings.Git_UndoCommit);

                if (repo == null)
                    return;

                var headCommit = repo.Head.Tip;

                if (headCommit == null)
                    throw new Exception(Strings.Git_NoCommits);

                if (!headCommit.Parents.Any())
                    throw new Exception(Strings.Git_FirstCommitUndo);

                if (!UpdateProject(guid, x => x.UndoState = new GitUndoState
                {
                    CommitSha = headCommit.Sha,
                    Branch = repo.Head.FriendlyName
                }))
                    return;

                var parentCommit = headCommit.Parents.First();

                repo.Reset(choice switch
                {
                    UndoCommitAction.KeepChangesStaged => ResetMode.Soft,
                    UndoCommitAction.KeepChangesUnstaged => ResetMode.Mixed,
                    UndoCommitAction.DeleteChangesCompletely => ResetMode.Hard,
                    _ => throw new ArgumentOutOfRangeException()
                }, parentCommit);

                UI.Success(Strings.Git_UndoDone);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message));
            }

            Console.ReadKey();
        }

        private static void RedoCommit(Guid guid)
        {
            try
            {
                UI.Warning(Strings.Git_RedoWarning, Strings.Common_Warning);
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                    .Title(Strings.Git_RedoAsk)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName)
                );

                if (confirm == Confirm.No)
                    return;

                AnsiConsole.Clear();

                ProjectsMenu.PrintHeader(guid);

                var project = GetProject(guid);
                using var repo = GitHelper.OpenRepo(project.Path, Strings.Git_RedoCommit);

                if (repo == null)
                    return;

                var undoState = project.UndoState;

                if (undoState == null)
                    throw new Exception(Strings.Git_Redo_NoUndo);

                if (repo.Head.FriendlyName != undoState.Branch)
                    throw new Exception(string.Format(Strings.Git_Redo_BranchChanged, undoState.Branch, repo.Head.FriendlyName));

                if (string.IsNullOrEmpty(undoState.CommitSha))
                    throw new Exception(Strings.Git_Redo_NoSha);

                if (repo.RetrieveStatus(GitHelper.FastStatus).IsDirty)
                    throw new Exception(Strings.Git_Redo_Dirty);

                var commit = repo.Lookup<Commit>(undoState.CommitSha);

                if (commit == null)
                    throw new Exception(Strings.Git_Redo_Gone);

                if (commit.Parents.FirstOrDefault()?.Sha != repo.Head.Tip?.Sha)
                {
                    UpdateProject(guid, x => x.UndoState = null);

                    throw new Exception(Strings.Git_Redo_NoUndo);
                }

                repo.Reset(ResetMode.Hard, commit);

                project.UndoState = null;

                var config = GetConfig();
                config.Projects[guid] = project;
                EditConfig(config);

                UI.Success(string.Format(Strings.Git_RedoDone, $"[DarkOrange3]{commit.Sha[..7]}[/]"));
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message));
            }

            Console.ReadKey();
        }
        #endregion
        #region Sync
        private static void Push(Guid guid, bool iAmSure = false)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            if (!GitHubCalls.EnsureGitHubRepoConnection(guid, "Push"))
                return;

            var lookup = GitHubCalls.RepoExistsOnUsersGitHubProfile(guid)
                .GetAwaiter().GetResult();

            if (lookup != GitHubCalls.RepoLookup.Found)
            {
                UI.Error(GitHubCalls.DescribeLookup(lookup), "Push");
                Console.ReadKey();
                return;
            }

            if (repo.Head.Tip == null)
            {
                UI.Info(Strings.Git_NoCommitsToPush, "Push");
                Console.ReadKey();
                return;
            }

            if (!iAmSure)
            {
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                    .Title(Strings.Scripts_ConfirmContinue)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

                if (confirm == Confirm.No)
                    return;
            }

            var origin = repo.Network.Remotes["origin"];

            if (origin == null)
            {
                var addRemote = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                        .Title(Strings.Git_NoOriginAdd)
                        .AddChoices(Enum.GetValues<Confirm>())
                        .UseConverter(UI.ConfirmName));

                if (addRemote == Confirm.No)
                    return;

                var username = GitHubCalls.GetCachedUsername().GetAwaiter().GetResult();

                if (string.IsNullOrWhiteSpace(username))
                {
                    UI.Error(Strings.Git_UsernameUnknown, Strings.Git_PushFailed_Title);
                    Console.ReadKey();
                    return;
                }

                try
                {
                    origin = repo.Network.Remotes.Add("origin", $"https://github.com/{username}/{GetProject(guid).GitHubName}.git");
                }
                catch (Exception addEx)
                {
                    UI.Error(Markup.Escape(addEx.Message), Strings.Git_PushFailed_Title);
                    Console.ReadKey();
                    return;
                }

                UI.Success(string.Format(Strings.Git_OriginAdded, "[Green3_1]origin[/]"), "Push");
                Console.ReadKey();
            }

            if (repo.Head.TrackedBranch == null)
            {
                Fetch(guid, true);

                var remoteBranches = repo.Branches
                    .Where(x => x.IsRemote && !x.FriendlyName.EndsWith("/HEAD") && x.FriendlyName.StartsWith($"{origin.Name}/", StringComparison.Ordinal))
                    .ToList();

                var matching = remoteBranches
                    .FirstOrDefault(x => x.FriendlyName == $"{origin.Name}/{repo.Head.FriendlyName}");

                if (matching == null && remoteBranches.Count == 1)
                {
                    var remoteName = remoteBranches[0].FriendlyName[(origin.Name.Length + 1)..];

                    var rename = AnsiConsole.Prompt(
                        new SelectionPrompt<Confirm>()
                            .Title(string.Format(Strings.Git_RenameToRemote, repo.Head.FriendlyName, remoteName))
                            .AddChoices(Enum.GetValues<Confirm>())
                            .UseConverter(UI.ConfirmName));

                    if (rename == Confirm.Yes)
                    {
                        try
                        {
                            repo.Branches.Rename(repo.Head, remoteName);
                            matching = remoteBranches[0];
                        }
                        catch (Exception renameEx)
                        {
                            UI.Error(Markup.Escape(renameEx.Message), Strings.Git_PushFailed_Title);
                            Console.ReadKey();
                            return;
                        }
                    }
                }

                var upstream = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                        .Title(string.Format(Strings.Git_SetUpstreamAsk, repo.Head.FriendlyName))
                        .AddChoices(Enum.GetValues<Confirm>())
                        .UseConverter(UI.ConfirmName));

                if (upstream == Confirm.No)
                    return;

                try
                {
                    if (matching == null)
                        repo.Network.Push(origin, $"{repo.Head.CanonicalName}:{repo.Head.CanonicalName}", new PushOptions
                        {
                            CredentialsProvider = (url, usernameFromUrl, types) => new UsernamePasswordCredentials
                            {
                                Username = "x-access-token",
                                Password = GetGitHubAccessToken()
                            }
                        });

                    repo.Branches.Update(repo.Head,
                        x => x.Remote = origin.Name,
                        x => x.UpstreamBranch = repo.Head.CanonicalName);
                }
                catch (Exception upstreamEx)
                {
                    UI.Error(Markup.Escape(upstreamEx.Message), Strings.Git_PushFailed_Title);
                    Console.ReadKey();
                    return;
                }

                UI.Success(string.Format(Strings.Git_NowTracks, $"[Green3_1]{repo.Head.FriendlyName}[/]", $"[Green3_1]{origin.Name}/{repo.Head.FriendlyName}[/]"), "Push");
                Console.ReadKey();
            }

            Exception? err = null;

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(Strings.Git_Pushing, ctx =>
                {
                    try
                    {
                        repo.Network.Push(repo.Head, new PushOptions
                        {
                            CredentialsProvider = (url, usernameFromUrl, types) => new UsernamePasswordCredentials
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

            if (err != null)
            {
                UI.Error(Markup.Escape(err.Message), Strings.Git_PushFailed_Title);
                Console.ReadKey();

                Fetch(guid, true);

                if ((repo.Head.TrackingDetails.BehindBy ?? 0) == 0)
                    return;

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                        .Title(Strings.Git_RebaseAsk)
                        .AddChoices(Enum.GetValues<Confirm>())
                        .UseConverter(UI.ConfirmName));

                if (choice == Confirm.No)
                    return;

                if (repo.Head.TrackedBranch == null)
                {
                    UI.Error(Strings.Git_NoUpstreamRebase, Strings.Git_RebaseFailed_Title);
                    Console.ReadKey();
                    return;
                }

                try
                {
                    var committer = GitHubCalls.MainIdentity(repo.Config);

                    var options = new RebaseOptions();

                    RebaseResult result = null;

                    AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .Start(Strings.Git_Rebasing, ctx =>
                                result = repo.Rebase.Start(repo.Head, repo.Head.TrackedBranch, null, committer, options)
                        );

                    while (true)
                    {
                        if (result.Status == RebaseStatus.Complete)
                        {
                            Push(guid, true);
                            return;
                        }

                        AnsiConsole.Clear();

                        var conflicts = repo.RetrieveStatus(GitHelper.FastStatus)
                            .Where(x => x.State
                            .HasFlag(FileStatus.Conflicted))
                            .ToList();

                        var stepInfo = repo.Rebase.GetCurrentStepInfo();
                        var steps = RebaseSteps(repo, out var stepIndex, out var stepCount);

                        var conflicted = string.Join("\n", conflicts
                            .Select(x => $"[{GitHelper.StatusColorLabel(x.State).color}]{GitHelper.StatusColorLabel(x.State).label}[/] {Markup.Escape(x.FilePath)}"));

                        var grid = new Grid()
                            .AddColumn()
                            .AddColumn()
                            .AddRow($"[bold]{Strings.Git_Row_Step}[/]", steps
                                ? string.Format(Strings.Git_StepOf, stepIndex + 1, stepCount)
                                : $"[grey]{Strings.Git_UnknownLower}[/]")
                            .AddRow($"[bold]{Strings.Git_Commit}[/]", stepInfo?.Commit == null
                                ? $"[grey]{Strings.Git_UnknownLower}[/]"
                                : $"[DarkOrange3]{stepInfo.Commit.Sha[..7]}[/] - {Markup.Escape(stepInfo.Commit.MessageShort)}")
                            .AddRow($"[bold]{Strings.Git_Row_Conflicts}[/]", conflicts.Count == 0
                                ? $"[green]{Strings.Git_NoneReadyContinue}[/]"
                                : conflicted);

                        UI.Box(grid, Strings.Git_RebaseInProgress, UIKind.Warning);

                        var action = AnsiConsole.Prompt(
                            new SelectionPrompt<RebaseFailedAction>()
                                .Title(Strings.Git_WhatDo)
                                .AddChoices(Enum.GetValues<RebaseFailedAction>()
                                    .Where(x => x != RebaseFailedAction.Resolve || conflicts.Count > 0))
                                .UseConverter(x => x switch
                                {
                                    RebaseFailedAction.Continue => conflicts.Count == 0
                                        ? Strings.Git_ContinueRebase
                                        : Strings.Git_MarkResolvedContinue,
                                    RebaseFailedAction.Resolve => Strings.Git_ResolveHere,
                                    RebaseFailedAction.OpenInIDE => Strings.Git_OpenFolder,
                                    RebaseFailedAction.Recheck => Strings.Git_RecheckFiles,
                                    RebaseFailedAction.Abort => $"[Red3_1]{Strings.Git_AbortRebase}[/]",
                                    RebaseFailedAction.Leave => $"[Red3_1]{Strings.Git_LeaveInProgress}[/]",
                                    _ => x.ToString()
                                }));

                        switch (action)
                        {
                            case RebaseFailedAction.Resolve:
                                {
                                    var file = AnsiConsole.Prompt(
                                        new SelectionPrompt<StatusEntry?>()
                                            .Title(Strings.Git_SelectFileResolve)
                                            .PageSize(15)
                                            .AddChoices(conflicts.Cast<StatusEntry?>().Append(null))
                                            .UseConverter(x => x == null
                                                ? $"[Red3_1]{Strings.Common_Back}[/]"
                                                : Markup.Escape(x.FilePath)));

                                    if (file != null)
                                        SolveConflict(Path.Combine(repo.Info.WorkingDirectory, file.FilePath));

                                    break;
                                }

                            case RebaseFailedAction.Continue:
                                {
                                    var stillMarked = StillMarked(repo, conflicts);

                                    if (stillMarked.Count > 0)
                                    {
                                        UI.Error(Strings.Git_StillMarked + "\n\n" +
                                            string.Join("\n", stillMarked.Select(x => $"[Red3_1]{Markup.Escape(x)}[/]")) +
                                            "\n\n" + Strings.Git_ResolveBeforeRebase,
                                            Strings.Git_ConflictsNotResolved);
                                        Console.ReadKey();
                                        break;
                                    }

                                    conflicts.ForEach(x => Commands.Stage(repo, x.FilePath));

                                    AnsiConsole.Status()
                                        .Spinner(Spinner.Known.Dots)
                                        .Start(Strings.Git_ContinuingRebase, ctx =>
                                            result = repo.Rebase.Continue(committer, options)
                                        );
                                    break;
                                }

                            case RebaseFailedAction.OpenInIDE:
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = project.Path,
                                        UseShellExecute = true
                                    });
                                }
                                catch (Exception ex)
                                {
                                    UI.Error(Markup.Escape(ex.Message), Strings.Git_RebaseInProgress);
                                    Console.ReadKey();
                                }
                                break;

                            case RebaseFailedAction.Recheck:
                                break;

                            case RebaseFailedAction.Abort:
                                repo.Rebase.Abort();
                                UI.Warning(Strings.Git_RebaseAborted, Strings.Git_RebaseAborted_Title);
                                Console.ReadKey();
                                return;

                            case RebaseFailedAction.Leave:
                                UI.Warning(Strings.Git_RebaseLeft, Strings.Git_RebaseInProgress);
                                Console.ReadKey();
                                return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    UI.Error(Markup.Escape(ex.Message), Strings.Git_RebaseFailed_Title);
                    Console.ReadKey();
                }

                return;
            }

            UI.Success(string.Format(Strings.Git_PushedOk, $"[Green3_1]{repo.Head.FriendlyName}[/]"), "Push");
            Console.ReadKey();
        }

        private static void Pull(Guid guid, bool iAmSure = false)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            if (!GitHubCalls.EnsureGitHubRepoConnection(guid, "Pull"))
                return;

            var lookup = GitHubCalls.RepoExistsOnUsersGitHubProfile(guid)
                .GetAwaiter().GetResult();

            if (lookup != GitHubCalls.RepoLookup.Found)
            {
                UI.Error(GitHubCalls.DescribeLookup(lookup), "Pull");
                Console.ReadKey();
                return;
            }

            if (!iAmSure)
            {
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                        .Title(Strings.Scripts_ConfirmContinue)
                        .AddChoices(Enum.GetValues<Confirm>())
                        .UseConverter(UI.ConfirmName));

                if (confirm == Confirm.No)
                    return;
            }

            try
            {
                var signature = new Signature(GitHubCalls.MainIdentity(repo.Config), DateTimeOffset.UtcNow);

                var mergeResult = Commands.Pull(repo, signature, new PullOptions
                {
                    FetchOptions = new FetchOptions
                    {
                        CredentialsProvider = (url, usernameFromUrl, types) => new UsernamePasswordCredentials
                        {
                            Username = "x-access-token",
                            Password = GetGitHubAccessToken()
                        }
                    },
                    MergeOptions = new MergeOptions()
                });

                string message = mergeResult.Status switch
                {
                    MergeStatus.UpToDate => string.Format(Strings.Git_Pull_UpToDate, repo.Head.FriendlyName),
                    MergeStatus.FastForward => string.Format(Strings.Git_Pull_FastForward, $"[green]'{repo.Head.FriendlyName}'[/]"),
                    MergeStatus.NonFastForward => string.Format(Strings.Git_Pull_Merged, $"[green]'{repo.Head.FriendlyName}'[/]"),
                    MergeStatus.Conflicts => string.Format(Strings.Git_Pull_Conflicts, $"[red]'{repo.Head.FriendlyName}'[/]"),
                    _ => string.Format(Strings.Git_Pull_Unexpected, mergeResult.Status)
                };

                bool hasConflicts = mergeResult.Status == MergeStatus.Conflicts;

                switch (mergeResult.Status)
                {
                    case MergeStatus.UpToDate:
                        UI.Info(message, "Pull");
                        break;
                    case MergeStatus.FastForward:
                    case MergeStatus.NonFastForward:
                        UI.Success(message);
                        break;
                    case MergeStatus.Conflicts:
                        UI.Error(message);
                        break;
                    default:
                        UI.Warning(message);
                        break;
                }

                Console.ReadKey();
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_PullFailed_Title);
                Console.ReadKey();

                if (!ex.Message.Contains("would be overwritten by merge", StringComparison.OrdinalIgnoreCase))
                    return;

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                    .Title(string.Format(Strings.Git_ForcePullAsk, $"[red]{Strings.Git_DiscardWarn}[/]"))
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

                if (choice == Confirm.No)
                    return;

                try
                {
                    var remote = repo.Network.Remotes["origin"];

                    if (remote == null)
                    {
                        UI.Info(Strings.Git_NoOriginConfigured, "Pull");
                        Console.ReadKey();
                        return;
                    }

                    var refSpecs = remote.FetchRefSpecs.Select(rs => rs.Specification);

                    Commands.Fetch(repo, remote.Name, refSpecs, new FetchOptions
                    {
                        CredentialsProvider = (url, usernameFromUrl, types) => new UsernamePasswordCredentials
                        {
                            Username = "x-access-token",
                            Password = GetGitHubAccessToken()
                        }
                    }, "Fetched via Helyx");

                    var remoteBranch = repo.Head.TrackedBranch;

                    if (remoteBranch?.Tip == null)
                    {
                        UI.Error(string.Format(Strings.Git_NoRemoteToReset, $"[Green3_1]{repo.Head.FriendlyName}[/]"), Strings.Git_PullFailed_Title);
                        Console.ReadKey();
                        return;
                    }

                    repo.Reset(
                        ResetMode.Hard,
                        remoteBranch.Tip
                    );

                    UI.Success(string.Format(Strings.Git_ResetTo, $"[Green3_1]{repo.Head.FriendlyName}[/]", $"[Green3_1]{remoteBranch.FriendlyName}[/]"), "Pull");
                }
                catch (Exception forceEx)
                {
                    UI.Error(Markup.Escape(forceEx.Message), Strings.Git_PullFailed_Title);
                }

                Console.ReadKey();
            }
        }

        private static void Fetch(Guid guid, bool iAmSure = false)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var remote = repo.Network.Remotes["origin"];

            if (remote == null)
            {
                UI.Info(Strings.Git_NoOriginConfigured, "Fetch");
                Console.ReadKey();
                return;
            }

            if (!iAmSure)
            {
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                        .Title(Strings.Scripts_ConfirmContinue)
                        .AddChoices(Enum.GetValues<Confirm>())
                        .UseConverter(UI.ConfirmName));

                if (confirm == Confirm.No)
                    return;
            }

            try
            {
                var refSpecs = remote.FetchRefSpecs.Select(rs => rs.Specification);

                Commands.Fetch(repo, remote.Name, refSpecs, new FetchOptions
                {
                    CredentialsProvider = (url, usernameFromUrl, types) => new UsernamePasswordCredentials
                    {
                        Username = "x-access-token",
                        Password = GetGitHubAccessToken()
                    }
                }, "Fetched via Helyx");

                if (!iAmSure)
                {
                    UI.Success(string.Format(Strings.Git_Fetched, $"[Green3_1]{remote.Name}[/]"), "Fetch");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_FetchFailed_Title);
                Console.ReadKey();
            }
        }

        private static void Sync(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var remote = repo.Network.Remotes["origin"];

            if (remote == null)
            {
                UI.Info(Strings.Git_NoOriginConfigured, Strings.Git_Sync);
                Console.ReadKey();
                return;
            }

            if (repo.RetrieveStatus(GitHelper.FastStatus).IsDirty)
            {
                UI.Warning(Strings.Git_SyncDirty + $"\n\n[grey]{Strings.Git_PressAnyKey}[/]", Strings.Git_Sync);
                Console.ReadKey();
            }

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.Scripts_ConfirmContinue)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            try
            {
                Fetch(guid, true);

                var localCommit = repo.Head.Tip;
                var remoteCommit = repo.Head.TrackedBranch?.Tip;

                if (localCommit == null)
                {
                    UI.Info(Strings.Git_NoCommitsToSync, Strings.Git_Sync);
                    Console.ReadKey();
                    return;
                }

                if (remoteCommit == null)
                {
                    UI.Error(string.Format(Strings.Git_NoUpstreamSync, $"[Green3_1]{repo.Head.FriendlyName}[/]"), Strings.Git_Sync);
                    Console.ReadKey();
                    return;
                }

                var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(
                    localCommit,
                 remoteCommit);

                if (divergence == null)
                {
                    UI.Error(Strings.Git_DivergenceUnknown, Strings.Git_SyncFailed_Title);
                    Console.ReadKey();
                    return;
                }

                switch (divergence)
                {
                    case { AheadBy: 0, BehindBy: 0 }:
                        AnsiConsole.MarkupLine($"[green]{Strings.Git_AlreadySynced}[/]\n");
                        break;

                    case { AheadBy: > 0, BehindBy: 0 }:
                        AnsiConsole.MarkupLine($"[blue]{string.Format(Strings.Git_LocalAhead, divergence.AheadBy)}[/]\n");
                        Push(guid, true);
                        break;

                    case { AheadBy: 0, BehindBy: > 0 }:
                        AnsiConsole.MarkupLine($"[blue]{string.Format(Strings.Git_RemoteAhead, divergence.BehindBy)}[/]\n");
                        Pull(guid, true);
                        break;

                    case { AheadBy: > 0, BehindBy: > 0 }:
                        AnsiConsole.MarkupLine($"[yellow]{Strings.Git_Diverged}[/]\n");

                        var side = Math.Max(Strings.Git_Local.Length, Strings.Git_Remote.Length) + 1;

                        var choice = AnsiConsole.Prompt(
                            new SelectionPrompt<PreferredCommit>()
                            .Title(Strings.Git_SelectVersion)
                            .AddChoices(Enum.GetValues<PreferredCommit>())
                            .UseConverter(x => x switch
                            {
                                PreferredCommit.Local => $"[SeaGreen1]{Strings.Git_Local.PadRight(side)}[/] - [[[CadetBlue]{localCommit.Author.When.ToString("G", CultureInfo.CurrentCulture)}[/]]]",
                                PreferredCommit.Remote => $"[Orange3]{Strings.Git_Remote.PadRight(side)}[/] - [[[CadetBlue]{remoteCommit.Author.When.ToString("G", CultureInfo.CurrentCulture)}[/]]]",
                                _ => x.ToString()
                            }));

                        switch (choice)
                        {
                            case PreferredCommit.Local:
                                Push(guid, true);
                                break;
                            case PreferredCommit.Remote:
                                Fetch(guid, true);
                                Pull(guid, true);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                        break;
                }

                if (divergence is { AheadBy: 0, BehindBy: 0 })
                    Console.ReadKey();

            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_SyncFailed_Title);
                Console.ReadKey();
            }

        }
        #endregion
        #region Branches
        private static void CreateBranch(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var branchName = AnsiConsole.Prompt(
                new TextPrompt<string>(Strings.Git_EnterBranchName)
                .AllowEmpty());

            if (string.IsNullOrEmpty(branchName))
                return;

            AnsiConsole.WriteLine();

            if (!Reference.IsValidName("refs/heads/" + branchName))
            {
                UI.Error(string.Format(Strings.Git_InvalidBranchName, $"[Green3_1]{Markup.Escape(branchName)}[/]"), Strings.Git_CreateBranch);
                Console.ReadKey();
                return;
            }

            if (repo.Branches.Any(b => b.FriendlyName == branchName))
            {
                UI.Error(string.Format(Strings.Git_BranchExists, $"[Green3_1]{Markup.Escape(branchName)}[/]"), Strings.Git_CreateBranch);
                Console.ReadKey();
                return;
            }

            if (repo.Head.Tip == null)
            {
                UI.Info(Strings.Git_NoBranchBeforeCommit, Strings.Git_CreateBranch);
                Console.ReadKey();
                return;
            }

            try
            {
                repo.CreateBranch(branchName);
                UI.Success(string.Format(Strings.Git_BranchCreated, $"[Green3_1]{Markup.Escape(branchName)}[/]"), Strings.Git_CreateBranch);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_CreateBranchFailed_Title);
            }

            Console.ReadKey();
        }

        private static void SwitchBranch(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var branches = repo.Branches
                .Where(b => !b.IsRemote && b.FriendlyName != repo.Head.FriendlyName)
                .ToList();

            if (branches.Count == 0)
            {
                UI.Info(Strings.Git_NoBranchesToSwitch, Strings.Git_SwitchBranch);
                Console.ReadKey();
                return;
            }

            branches.Add(null);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Branch>()
                    .Title(Strings.Git_SelectBranchSwitch)
                    .AddChoices(branches)
                    .UseConverter(x => x switch
                    {
                        null => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.FriendlyName
                    }));

            if (choice == null)
                return;

            var status = repo.RetrieveStatus(GitHelper.FastStatus);

            if (status.IsDirty)
            {
                UI.Error(Strings.Git_DirtySwitch, Strings.Git_SwitchBranch);

                Console.ReadKey();
                return;
            }

            try
            {
                Commands.Checkout(repo, choice);
                UI.Success(string.Format(Strings.Git_Switched, $"[Green3_1]{choice.FriendlyName}[/]"), Strings.Git_SwitchBranch);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_SwitchBranchFailed_Title);
            }

            Console.ReadKey();

        }

        private static void MergeBranch(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            if (repo.Head.Tip == null)
            {
                UI.Info(Strings.Git_NothingBeforeCommit, Strings.Git_MergeBranch);
                Console.ReadKey();
                return;
            }

            var branches = repo.Branches
                .Where(b => !b.IsRemote && b.FriendlyName != repo.Head.FriendlyName)
                .ToList();

            if (branches.Count == 0)
            {
                UI.Info(Strings.Git_NoBranchesToMerge, Strings.Git_MergeBranch);
                Console.ReadKey();
                return;
            }

            branches.Add(null);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Branch>()
                    .Title(string.Format(Strings.Git_SelectBranchMerge, $"[Green3_1]{repo.Head.FriendlyName}[/]"))
                    .AddChoices(branches)
                    .UseConverter(x => x switch
                    {
                        null => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.Tip == null
                            ? x.FriendlyName
                            : $"{x.FriendlyName} [DarkOrange3]{x.Tip.Sha[..7]}[/] [grey]- {Markup.Escape(x.Tip.MessageShort)}[/]"
                    }));

            if (choice == null)
                return;

            if (repo.RetrieveStatus(GitHelper.FastStatus).IsDirty)
            {
                UI.Error(Strings.Git_DirtyMerge, Strings.Git_MergeBranch);
                Console.ReadKey();
                return;
            }

            var incoming = repo.Commits
                .QueryBy(new CommitFilter { IncludeReachableFrom = choice, ExcludeReachableFrom = repo.Head })
                .Count();

            if (incoming == 0)
            {
                UI.Info(string.Format(Strings.Git_AlreadyMerged, $"[Green3_1]{choice.FriendlyName}[/]", $"[Green3_1]{repo.Head.FriendlyName}[/]"), Strings.Git_MergeBranch);
                Console.ReadKey();
                return;
            }

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(string.Format(Strings.Git_MergeConfirm, $"[Green3_1]{choice.FriendlyName}[/]", string.Format(incoming == 1 ? Strings.Git_CommitCountOne : Strings.Git_CommitCountMany, $"[bold]{incoming}[/]"), $"[Green3_1]{repo.Head.FriendlyName}[/]"))
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            var signature = new Signature(GitHubCalls.MainIdentity(repo.Config), DateTimeOffset.Now);

            MergeResult result = null;

            try
            {
                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .Start(Strings.Git_Merging, ctx =>
                        result = repo.Merge(choice, signature, new MergeOptions())
                    );
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_MergeFailed_Title);
                Console.ReadKey();
                return;
            }

            if (result.Status != MergeStatus.Conflicts)
            {
                var message = result.Status switch
                {
                    MergeStatus.UpToDate => string.Format(Strings.Git_Pull_UpToDate, repo.Head.FriendlyName),
                    MergeStatus.FastForward => string.Format(Strings.Git_Merge_FastForward, choice.FriendlyName, repo.Head.FriendlyName),
                    MergeStatus.NonFastForward => string.Format(Strings.Git_Merge_Done, choice.FriendlyName, repo.Head.FriendlyName),
                    _ => string.Format(Strings.Git_Merge_Unexpected, result.Status)
                };

                if (result.Status is MergeStatus.FastForward or MergeStatus.NonFastForward)
                    UI.Success(message, Strings.Git_MergeBranch);
                else
                    UI.Info(message, Strings.Git_MergeBranch);

                Console.ReadKey();
                return;
            }

            var mergedName = choice.FriendlyName;

            while (true)
            {
                AnsiConsole.Clear();

                var conflicts = repo.RetrieveStatus(GitHelper.FastStatus)
                    .Where(x => x.State
                    .HasFlag(FileStatus.Conflicted))
                    .ToList();

                var conflicted = string.Join("\n", conflicts
                    .Select(x => $"[{GitHelper.StatusColorLabel(x.State).color}]{GitHelper.StatusColorLabel(x.State).label}[/] {Markup.Escape(x.FilePath)}"));

                var grid = new Grid()
                    .AddColumn()
                    .AddColumn()
                    .AddRow($"[bold]{Strings.Git_Row_Merging}[/]", $"[Green3_1]{mergedName}[/] → [Green3_1]{repo.Head.FriendlyName}[/]")
                    .AddRow($"[bold]{Strings.Git_Row_Conflicts}[/]", conflicts.Count == 0
                        ? $"[green]{Strings.Git_NoneReadyCommit}[/]"
                        : conflicted);

                UI.Box(grid, Strings.Git_MergeInProgress, UIKind.Warning);

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<MergeFailedAction>()
                        .Title(Strings.Git_WhatDo)
                        .AddChoices(Enum.GetValues<MergeFailedAction>()
                            .Where(x => x != MergeFailedAction.Resolve || conflicts.Count > 0))
                        .UseConverter(x => x switch
                        {
                            MergeFailedAction.Continue => conflicts.Count == 0
                                ? Strings.Git_CommitMerge
                                : Strings.Git_MarkResolvedCommit,
                            MergeFailedAction.Resolve => Strings.Git_ResolveHere,
                            MergeFailedAction.OpenInIDE => Strings.Git_OpenFolder,
                            MergeFailedAction.Recheck => Strings.Git_RecheckFiles,
                            MergeFailedAction.Abort => $"[Red3_1]{Strings.Git_AbortMerge}[/]",
                            MergeFailedAction.Leave => $"[Red3_1]{Strings.Git_LeaveInProgress}[/]",
                            _ => x.ToString()
                        }));

                switch (action)
                {
                    case MergeFailedAction.Resolve:
                        {
                            var file = AnsiConsole.Prompt(
                                new SelectionPrompt<StatusEntry?>()
                                    .Title(Strings.Git_SelectFileResolve)
                                    .PageSize(15)
                                    .AddChoices(conflicts.Cast<StatusEntry?>().Append(null))
                                    .UseConverter(x => x == null
                                        ? $"[Red3_1]{Strings.Common_Back}[/]"
                                        : Markup.Escape(x.FilePath)));

                            if (file != null)
                                SolveConflict(Path.Combine(repo.Info.WorkingDirectory, file.FilePath));

                            break;
                        }

                    case MergeFailedAction.Continue:
                        var stillMarked = StillMarked(repo, conflicts);

                        if (stillMarked.Count > 0)
                        {
                            UI.Error(Strings.Git_StillMarked + "\n\n" +
                                string.Join("\n", stillMarked.Select(x => $"[Red3_1]{Markup.Escape(x)}[/]")) +
                                "\n\n" + Strings.Git_ResolveBeforeMerge,
                                Strings.Git_ConflictsNotResolved);
                            Console.ReadKey();
                            break;
                        }

                        conflicts.ForEach(x => Commands.Stage(repo, x.FilePath));

                        try
                        {
                            repo.Commit($"Merge branch '{mergedName}' into {repo.Head.FriendlyName}", signature, signature);
                            UI.Success(string.Format(Strings.Git_MergedInto, $"[Green3_1]{mergedName}[/]", $"[Green3_1]{repo.Head.FriendlyName}[/]"), Strings.Git_MergeBranch);
                        }
                        catch (Exception ex)
                        {
                            UI.Error(Markup.Escape(ex.Message), Strings.Git_MergeFailed_Title);
                        }

                        Console.ReadKey();
                        return;

                    case MergeFailedAction.OpenInIDE:
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = project.Path,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            UI.Error(Markup.Escape(ex.Message), Strings.Git_MergeInProgress);
                            Console.ReadKey();
                        }
                        break;

                    case MergeFailedAction.Recheck:
                        break;

                    case MergeFailedAction.Abort:
                        repo.Reset(ResetMode.Hard, repo.Head.Tip);
                        UI.Warning(Strings.Git_MergeAborted, Strings.Git_MergeAborted_Title);
                        Console.ReadKey();
                        return;

                    case MergeFailedAction.Leave:
                        UI.Warning(Strings.Git_MergeLeft, Strings.Git_MergeInProgress);
                        Console.ReadKey();
                        return;
                }
            }
        }

        private static void DeleteBranch(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var branches = repo.Branches
                .Where(b => !b.IsRemote && b.FriendlyName != repo.Head.FriendlyName)
                .ToList();

            if (branches.Count == 0)
            {
                UI.Info(Strings.Git_NoBranchesToDelete, Strings.Git_DeleteBranch);
                Console.ReadKey();
                return;
            }

            branches.Add(null);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Branch>()
                    .Title(Strings.Git_SelectBranchDelete)
                    .AddChoices(branches)
                    .UseConverter(x => x switch
                    {
                        null => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.FriendlyName
                    }));

            if (choice == null)
                return;

            var isMerged = choice.Tip == null || repo.Branches
                .Where(b => b != choice && !b.IsRemote && b.Tip != null)
                .Any(b => repo.ObjectDatabase.CalculateHistoryDivergence(choice.Tip, b.Tip).AheadBy == 0);

            if (!isMerged)
                UI.Warning(string.Format(Strings.Git_UnmergedWarn, $"[Green3_1]{choice.FriendlyName}[/]"), Strings.Git_UnmergedWarn_Title);

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(isMerged
                        ? string.Format(Strings.Git_DeleteBranchConfirm, $"[Green3_1]{choice.FriendlyName}[/]")
                        : Strings.Git_DeleteBranchStill)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            try
            {
                repo.Branches.Remove(choice);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_DeleteBranchFailed_Title);
                Console.ReadKey();
                return;
            }

            UI.Success(string.Format(Strings.Git_BranchDeleted, $"[Green3_1]{choice.FriendlyName}[/]"), Strings.Git_DeleteBranch);

            Console.ReadKey();
        }
        #endregion
        #region Stashes
        private static void SaveStash(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            if (!repo.RetrieveStatus(GitHelper.FastStatus).IsDirty)
            {
                UI.Info(Strings.Git_NothingToStash, Strings.Git_SaveStash);
                Console.ReadKey();
                return;
            }

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.Scripts_ConfirmContinue)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            var message = AnsiConsole.Prompt(
                new TextPrompt<string>(Strings.Git_StashMessage)
                .AllowEmpty());

            try
            {
                var signature = new Signature(GitHubCalls.MainIdentity(repo.Config), DateTimeOffset.Now);

                var stash = repo.Stashes.Add(signature, message, StashModifiers.Default);

                Console.WriteLine();

                if (stash == null)
                    UI.Info(Strings.Git_StashedNothing, Strings.Git_SaveStash);
                else
                    UI.Success(string.Format(Strings.Git_StashSaved, $"[yellow]{Markup.Escape(stash.FriendlyName)}[/]"), Strings.Git_SaveStash);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_SaveStashFailed_Title);
            }

            Console.ReadKey();
        }

        private static void ApplyStash(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var stashes = repo.Stashes.ToList();

            if (stashes.Count == 0)
            {
                UI.Warning(Strings.Git_NoStashes, Strings.Git_ApplyStash);

                Console.ReadKey();
                return;
            }

            AnsiConsole.Clear();

            var header = ProjectsMenu.HeaderPanel(guid, out var headerHeight);

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(headerHeight),
                    new Layout("Title").Size(1),
                    new Layout("List"),
                    new Layout("Footer").Size(3));

            layout["Header"].Update(header);
            layout["Title"].Update(new Rule($"[blue bold]{Strings.Git_ApplyStash}[/]").LeftJustified());

            int pageSize = Math.Max(3, Console.WindowHeight - headerHeight - 10);
            int selectedIndex = 0;

            int totalPages = (int)Math.Ceiling(stashes.Count / (double)pageSize);

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    bool running = true;

                    while (running)
                    {
                        int currentPage = selectedIndex / pageSize;
                        int firstRow = currentPage * pageSize;
                        int lastRow = Math.Min(firstRow + pageSize, stashes.Count);

                        var table = new Table()
                            .Border(TableBorder.Rounded)
                            .BorderColor(Color.Grey)
                            .Expand();

                        table.AddColumns(" ", Strings.Git_Col_Stash, Strings.Git_Col_Message);

                        for (int i = firstRow; i < lastRow; i++)
                        {
                            var stash = stashes[i];

                            string message = stash.Message.Replace("\n", " ");

                            if (message.Length > 80)
                                message = message[..77] + "...";

                            table.AddRow(
                                i == selectedIndex ? "[SpringGreen2_1]>[/]" : " ",
                                $"[yellow]{Markup.Escape(stash.FriendlyName)}[/]",
                                $"[Khaki1]{Markup.Escape(message)}[/]"
                            );
                        }

                        layout["List"].Update(table);

                        layout["Footer"].Update(new Panel(
                            new Grid()
                                .AddColumn()
                                .AddColumn(new GridColumn().RightAligned())
                                .Expand()
                                .AddRow(
                                    $"[grey]{Strings.Git_Nav_Apply}[/]",
                                    string.Format(Strings.Common_Page, currentPage + 1, totalPages, selectedIndex + 1, stashes.Count)))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        ctx.Refresh();

                        switch (Console.ReadKey(true).Key)
                        {
                            case ConsoleKey.UpArrow:
                                selectedIndex = selectedIndex == 0
                                    ? stashes.Count - 1
                                    : selectedIndex - 1;
                                break;

                            case ConsoleKey.DownArrow:
                                selectedIndex = selectedIndex == stashes.Count - 1
                                    ? 0
                                    : selectedIndex + 1;
                                break;

                            case ConsoleKey.LeftArrow:
                                selectedIndex = currentPage == 0
                                    ? (totalPages - 1) * pageSize
                                    : firstRow - pageSize;
                                break;

                            case ConsoleKey.RightArrow:
                                selectedIndex = currentPage == totalPages - 1
                                    ? 0
                                    : firstRow + pageSize;
                                break;

                            case ConsoleKey.Enter:
                                running = false;
                                break;

                            case ConsoleKey.Escape:
                                selectedIndex = -1;
                                running = false;
                                break;
                        }

                        UI.FlushInput();
                    }
                });

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            if (selectedIndex == -1)
                return;

            Console.WriteLine();

            try
            {
                var result = repo.Stashes.Apply(selectedIndex);

                switch (result)
                {
                    case StashApplyStatus.Applied:
                        UI.Success(string.Format(Strings.Git_StashApplied, $"[yellow]{Markup.Escape(stashes[selectedIndex].FriendlyName)}[/]"), Strings.Git_ApplyStash);
                        break;
                    case StashApplyStatus.Conflicts:
                        UI.Warning(
                            Strings.Git_StashConflicts, Strings.Git_Conflicts_Title);
                        break;
                    default:
                        UI.Error(string.Format(Strings.Git_StashApplyFailed, result), Strings.Git_ApplyStashFailed_Title);
                        break;
                }
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_ApplyStashFailed_Title);
            }

            Console.ReadKey();
        }

        private static void PopStash(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            var stashes = repo.Stashes.ToList();

            if (stashes.Count == 0)
            {
                UI.Warning(Strings.Git_NoStashes, Strings.Git_PopStash);

                Console.ReadKey();
                return;
            }

            AnsiConsole.Clear();

            var header = ProjectsMenu.HeaderPanel(guid, out var headerHeight);

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(headerHeight),
                    new Layout("Title").Size(1),
                    new Layout("List"),
                    new Layout("Footer").Size(3));

            layout["Header"].Update(header);
            layout["Title"].Update(new Rule($"[blue bold]{Strings.Git_PopStash}[/]").LeftJustified());

            int pageSize = Math.Max(3, Console.WindowHeight - headerHeight - 10);
            int selectedIndex = 0;

            int totalPages = (int)Math.Ceiling(stashes.Count / (double)pageSize);

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    bool running = true;

                    while (running)
                    {
                        int currentPage = selectedIndex / pageSize;
                        int firstRow = currentPage * pageSize;
                        int lastRow = Math.Min(firstRow + pageSize, stashes.Count);

                        var table = new Table()
                            .Border(TableBorder.Rounded)
                            .BorderColor(Color.Grey)
                            .Expand();

                        table.AddColumns(" ", Strings.Git_Col_Stash, Strings.Git_Col_Message);

                        for (int i = firstRow; i < lastRow; i++)
                        {
                            var stash = stashes[i];

                            string message = stash.Message.Replace("\n", " ");

                            if (message.Length > 80)
                                message = message[..77] + "...";

                            table.AddRow(
                                i == selectedIndex ? "[SpringGreen2_1]>[/]" : " ",
                                $"[yellow]{Markup.Escape(stash.FriendlyName)}[/]",
                                $"[Khaki1]{Markup.Escape(message)}[/]"
                            );
                        }

                        layout["List"].Update(table);

                        layout["Footer"].Update(new Panel(
                            new Grid()
                                .AddColumn()
                                .AddColumn(new GridColumn().RightAligned())
                                .Expand()
                                .AddRow(
                                    $"[grey]{Strings.Git_Nav_Pop}[/]",
                                    string.Format(Strings.Common_Page, currentPage + 1, totalPages, selectedIndex + 1, stashes.Count)))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        ctx.Refresh();

                        switch (Console.ReadKey(true).Key)
                        {
                            case ConsoleKey.UpArrow:
                                selectedIndex = selectedIndex == 0
                                    ? stashes.Count - 1
                                    : selectedIndex - 1;
                                break;

                            case ConsoleKey.DownArrow:
                                selectedIndex = selectedIndex == stashes.Count - 1
                                    ? 0
                                    : selectedIndex + 1;
                                break;

                            case ConsoleKey.LeftArrow:
                                selectedIndex = currentPage == 0
                                    ? (totalPages - 1) * pageSize
                                    : firstRow - pageSize;
                                break;

                            case ConsoleKey.RightArrow:
                                selectedIndex = currentPage == totalPages - 1
                                    ? 0
                                    : firstRow + pageSize;
                                break;

                            case ConsoleKey.Enter:
                                running = false;
                                break;

                            case ConsoleKey.Escape:
                                selectedIndex = -1;
                                running = false;
                                break;
                        }

                        UI.FlushInput();
                    }
                });

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            if (selectedIndex == -1)
                return;

            Console.WriteLine();

            try
            {
                var result = repo.Stashes.Pop(selectedIndex);

                switch (result)
                {
                    case StashApplyStatus.Applied:
                        UI.Success(string.Format(Strings.Git_StashPopped, $"[yellow]{Markup.Escape(stashes[selectedIndex].FriendlyName)}[/]"), Strings.Git_PopStash);
                        break;
                    case StashApplyStatus.Conflicts:
                        UI.Warning(
                            Strings.Git_StashConflicts, Strings.Git_Conflicts_Title);
                        break;
                    default:
                        UI.Error(string.Format(Strings.Git_StashPopFailed, result), Strings.Git_PopStashFailed_Title);
                        break;
                }
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_PopStashFailed_Title);
            }

            Console.ReadKey();
        }
        private static void ListStashes(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;
            var stashes = repo.Stashes.ToList();

            if (stashes.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]{Strings.Git_NoStashesFound}[/]");
                Console.ReadKey();
                return;
            }

            int selectedIndex = 0;

            while (true)
            {
                AnsiConsole.Clear();

                var header = ProjectsMenu.HeaderPanel(guid, out var headerHeight);

                var listLayout = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(headerHeight),
                        new Layout("Title").Size(1),
                        new Layout("List"),
                        new Layout("Footer").Size(3));

                listLayout["Header"].Update(header);
                listLayout["Title"].Update(new Rule($"[blue bold]{Strings.Git_Stashes}[/]").LeftJustified());

                int pageSize = Math.Max(3, Console.WindowHeight - headerHeight - 10);

                int totalPages = (int)Math.Ceiling(stashes.Count / (double)pageSize);
                AnsiConsole.Live(listLayout)
                    .Start(ctx =>
                    {
                        bool running = true;

                        while (running)
                        {
                            int currentPage = selectedIndex / pageSize;
                            int firstRow = currentPage * pageSize;
                            int lastRow = Math.Min(firstRow + pageSize, stashes.Count);

                            var table = new Table()
                                .Border(TableBorder.Rounded)
                                .BorderColor(Color.Grey)
                                .Expand();

                            table.AddColumns(" ", Strings.Git_Col_Stash, Strings.Git_Col_Message);

                            for (int i = firstRow; i < lastRow; i++)
                            {
                                var stash = stashes[i];

                                string message = stash.Message.Replace("\n", " ");

                                if (message.Length > 80)
                                    message = message[..77] + "...";

                                table.AddRow(
                                    i == selectedIndex ? "[SpringGreen2_1]>[/]" : " ",
                                    $"[DarkOrange3]stash@{{{i}}}[/]",
                                    $"[Khaki1]{Markup.Escape(message)}[/]"
                                );
                            }

                            listLayout["List"].Update(table);

                            listLayout["Footer"].Update(new Panel(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                        $"[grey]{Strings.Git_Nav_View}[/]",
                                        string.Format(Strings.Common_Page, currentPage + 1, totalPages, selectedIndex + 1, stashes.Count)))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            switch (Console.ReadKey(true).Key)
                            {
                                case ConsoleKey.UpArrow:
                                    selectedIndex = selectedIndex == 0
                                        ? stashes.Count - 1
                                        : selectedIndex - 1;
                                    break;

                                case ConsoleKey.DownArrow:
                                    selectedIndex = selectedIndex == stashes.Count - 1
                                        ? 0
                                        : selectedIndex + 1;
                                    break;

                                case ConsoleKey.LeftArrow:
                                    selectedIndex = currentPage == 0
                                        ? (totalPages - 1) * pageSize
                                        : firstRow - pageSize;
                                    break;

                                case ConsoleKey.RightArrow:
                                    selectedIndex = currentPage == totalPages - 1
                                        ? 0
                                        : firstRow + pageSize;
                                    break;

                                case ConsoleKey.Enter:
                                    running = false;
                                    break;

                                case ConsoleKey.Escape:
                                    selectedIndex = -1;
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }
                    });

                AnsiConsole.Clear();

                if (selectedIndex == -1)
                    break;

                var selectedStash = stashes[selectedIndex];
                var baseCommit = selectedStash.WorkTree.Parents.FirstOrDefault();
                var patch = repo.Diff.Compare<Patch>(baseCommit?.Tree, selectedStash.WorkTree.Tree);

                var fileEntries = new List<(string Path, string Label, string Color, int Added, int Deleted)>();

                foreach (var entry in patch)
                {
                    string label = entry.Status switch
                    {
                        ChangeKind.Added => Strings.Git_Status_New,
                        ChangeKind.Deleted => Strings.Git_Status_Deleted,
                        ChangeKind.Modified => Strings.Git_Status_Modified,
                        ChangeKind.Renamed => Strings.Git_Status_Renamed,
                        ChangeKind.TypeChanged => Strings.Git_Status_TypeChanged,
                        _ => entry.Status.ToString().ToUpperInvariant()
                    };

                    string color = entry.Status switch
                    {
                        ChangeKind.Added => "green",
                        ChangeKind.Deleted => "Orange1",
                        ChangeKind.Modified => "yellow",
                        ChangeKind.Renamed => "blue",
                        _ => "grey"
                    };

                    fileEntries.Add((entry.Path, label, color, entry.LinesAdded, entry.LinesDeleted));
                }

                var diffLines = new List<string>();

                foreach (var line in patch.Content.Split('\n'))
                {
                    string escaped = Markup.Escape(line);

                    string colored = line switch
                    {
                        _ when line.StartsWith("+++") || line.StartsWith("---") => $"[bold]{escaped}[/]",
                        _ when line.StartsWith("@@") => $"[cyan]{escaped}[/]",
                        _ when line.StartsWith("+") => $"[green]{escaped}[/]",
                        _ when line.StartsWith("-") => $"[red]{escaped}[/]",
                        _ => $"[grey]{escaped}[/]"
                    };

                    diffLines.Add(colored);
                }

                int scrollOffset = 0;

                var filesSize = Math.Min(fileEntries.Count + 4, 10);

                var layout = new Layout("Root")
                    .SplitRows(
                        new Layout("Info").Size(6),
                        new Layout("Files").Size(filesSize),
                        new Layout("Diff"),
                        new Layout("Footer").Size(3));

                AnsiConsole.Clear();

                AnsiConsole.Live(layout)
                    .Start(ctx =>
                    {
                        bool running = true;

                        while (running)
                        {
                            var infoGrid = new Grid()
                                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                                .AddColumn();

                            var message = selectedStash.Message.Replace("\r", string.Empty).Replace("\n", " ");
                            var messageWidth = Math.Max(20, Console.WindowWidth - 20);

                            if (message.Length > messageWidth)
                                message = message[..(messageWidth - 3)] + "...";

                            infoGrid.AddRow($"[grey]{Strings.Git_Row_Index}[/]", $"[yellow]stash@{{{selectedIndex}}}[/]");
                            infoGrid.AddRow($"[grey]{Strings.Git_Col_Message}[/]", $"[Khaki1]{Markup.Escape(message)}[/]");
                            infoGrid.AddRow($"[grey]{Strings.Git_Row_BasedOn}[/]", baseCommit != null ? $"[DarkOrange3]{baseCommit.Sha[..7]}[/]" : $"[grey]{Strings.Git_UnknownLower}[/]");

                            layout["Info"].Update(
                                new Panel(infoGrid)
                                    .Header($"[blue bold] {Strings.Git_StashInfo} [/]")
                                    .RoundedBorder()
                                    .BorderColor(Color.Grey)
                                    .Padding(1, 1)
                                    .Expand());

                            var filesGrid = new Grid()
                                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                                .AddColumn(new GridColumn().PadRight(2))
                                .AddColumn(new GridColumn().NoWrap());

                            if (fileEntries.Count == 0)
                            {
                                filesGrid.AddRow($"[grey]{Strings.Git_NoFileChanges}[/]", string.Empty, string.Empty);
                            }
                            else
                            {
                                foreach (var (path, label, color, added, deleted) in fileEntries)
                                    filesGrid.AddRow(
                                        $"[{color}]{label}[/]",
                                        $"[Khaki1]{Markup.Escape(path)}[/]",
                                        $"[green]+{added}[/] [red]-{deleted}[/]");
                            }

                            layout["Files"].Update(
                                new Panel(filesGrid)
                                    .Header($"[blue bold] {string.Format(Strings.Git_Files, fileEntries.Count)} [/]")
                                    .RoundedBorder()
                                    .BorderColor(Color.Grey)
                                    .Padding(1, 1)
                                    .Expand());

                            int paneHeight = Math.Max(5, Console.WindowHeight - filesSize - 12);
                            int maxScroll = Math.Max(0, diffLines.Count - paneHeight);
                            scrollOffset = Math.Min(scrollOffset, maxScroll);

                            var visibleLines = diffLines
                                .Skip(scrollOffset)
                                .Take(paneHeight)
                                .ToList();

                            string diffText = visibleLines.Count > 0
                                ? string.Join("\n", visibleLines)
                                : $"[grey]{Strings.Git_NoChangesInStash}[/]";

                            int lastVisibleLine = Math.Min(scrollOffset + paneHeight, diffLines.Count);
                            string scrollInfo = diffLines.Count > paneHeight
                                ? $" ({scrollOffset + 1}-{lastVisibleLine}/{diffLines.Count})"
                                : string.Empty;

                            layout["Diff"].Update(
                                new Panel(diffText)
                                    .Header($"[blue bold] Diff{scrollInfo} [/]")
                                    .RoundedBorder()
                                    .BorderColor(Color.Grey)
                                    .Padding(1, 1)
                                    .Expand());

                            layout["Footer"].Update(new Panel($"[grey]{Strings.Git_Stash_Footer}[/]")
                                .RoundedBorder()
                                .BorderColor(Color.Grey)
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            var key = Console.ReadKey(true);

                            switch (key.Key)
                            {
                                case ConsoleKey.UpArrow:
                                    scrollOffset = Math.Max(0, scrollOffset - 1);
                                    break;
                                case ConsoleKey.DownArrow:
                                    scrollOffset = Math.Min(maxScroll, scrollOffset + 1);
                                    break;
                                case ConsoleKey.PageUp:
                                    scrollOffset = Math.Max(0, scrollOffset - paneHeight);
                                    break;
                                case ConsoleKey.PageDown:
                                    scrollOffset = Math.Min(maxScroll, scrollOffset + paneHeight);
                                    break;
                                case ConsoleKey.Escape:
                                case ConsoleKey.Q:
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }
                    });

                AnsiConsole.Clear();
            }

            AnsiConsole.Clear();
        }
        #endregion

        private static void Log(Guid guid)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path, Strings.Git_Log);

            if (repo == null)
                return;

            List<Commit> commits = [];

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(Strings.Git_ReadingLog, ctx => commits = repo.Commits.ToList());

            if (commits.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]{Strings.Git_NoCommitsFound}[/]");
                Console.ReadKey();
                return;
            }

            int selectedIndex = 0;

            while (true)
            {
                AnsiConsole.Clear();

                var header = ProjectsMenu.HeaderPanel(guid, out var headerHeight);

                var layout = new Layout("Root")
                    .SplitRows(
                        new Layout("Header").Size(headerHeight),
                        new Layout("Title").Size(1),
                        new Layout("List"),
                        new Layout("Footer").Size(3));

                layout["Header"].Update(header);
                layout["Title"].Update(new Rule($"[blue bold]{Strings.Git_Log}[/]").LeftJustified());

                int pageSize = Math.Max(3, Console.WindowHeight - headerHeight - 10);

                int totalPages = (int)Math.Ceiling(commits.Count / (double)pageSize);

                AnsiConsole.Live(layout)
                    .Start(ctx =>
                    {
                        bool running = true;

                        while (running)
                        {
                            int currentPage = selectedIndex / pageSize;
                            int firstRow = currentPage * pageSize;
                            int lastRow = Math.Min(firstRow + pageSize, commits.Count);

                            var table = new Table()
                                .Border(TableBorder.Rounded)
                                .BorderColor(Color.Grey)
                                .Expand();

                            table.AddColumns(" ", "SHA", Strings.Git_Col_Message, Strings.Git_Col_Author, Strings.Git_Col_Date);

                            for (int i = firstRow; i < lastRow; i++)
                            {
                                var commit = commits[i];

                                string message = commit.MessageShort.Length > 40
                                    ? commit.MessageShort[..37] + "..."
                                    : commit.MessageShort;

                                table.AddRow(
                                    i == selectedIndex ? "[SpringGreen2_1]>[/]" : " ",
                                    $"[DarkOrange3]{commit.Sha[..7]}[/]",
                                    $"[Khaki1]{Markup.Escape(message)}[/]",
                                    $"[SkyBlue1]{Markup.Escape(commit.Author.Name)}[/]",
                                    $"[CadetBlue]{commit.Author.When.ToString("G", CultureInfo.CurrentCulture)}[/]"
                                );
                            }

                            layout["List"].Update(table);

                            layout["Footer"].Update(new Panel(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                        $"[grey]{Strings.Common_NavSelect}[/]",
                                        string.Format(Strings.Common_Page, currentPage + 1, totalPages, selectedIndex + 1, commits.Count)))
                                .RoundedBorder()
                                .Expand()
                                .Padding(1, 0));

                            ctx.Refresh();

                            switch (Console.ReadKey(true).Key)
                            {
                                case ConsoleKey.UpArrow:
                                    selectedIndex = selectedIndex == 0
                                        ? commits.Count - 1
                                        : selectedIndex - 1;
                                    break;
                                case ConsoleKey.DownArrow:
                                    selectedIndex = selectedIndex == commits.Count - 1
                                        ? 0
                                        : selectedIndex + 1;
                                    break;
                                case ConsoleKey.LeftArrow:
                                    selectedIndex = currentPage == 0
                                        ? (totalPages - 1) * pageSize
                                        : firstRow - pageSize;
                                    break;
                                case ConsoleKey.RightArrow:
                                    selectedIndex = currentPage == totalPages - 1
                                        ? 0
                                        : firstRow + pageSize;
                                    break;
                                case ConsoleKey.Enter:
                                    running = false;
                                    break;
                                case ConsoleKey.Escape:
                                    selectedIndex = -1;
                                    running = false;
                                    break;
                            }

                            UI.FlushInput();
                        }
                    });

                AnsiConsole.Clear();

                if (selectedIndex == -1)
                    return;

                ShowCommit(guid, commits[selectedIndex]);
            }
        }
        #region Log Core
        private static void ShowCommit(Guid guid, Commit commit)
        {
            var project = GetProject(guid);
            using var repo = GitHelper.OpenRepo(project.Path);

            if (repo == null)
                return;

            while (true)
            {
                AnsiConsole.Clear();

                var parent = commit.Parents.FirstOrDefault();

                Patch patch;

                try
                {
                    patch = repo.Diff.Compare<Patch>(
                        parent?.Tree,
                        commit.Tree
                    );
                }
                catch (Exception ex)
                {
                    AnsiConsole.Clear();
                    ProjectsMenu.PrintHeader(guid);

                    UI.Error(Markup.Escape(ex.Message), Strings.Git_CommitDetails);
                    Console.ReadKey();
                    return;
                }

                var changes = patch.ToList();

                int additions = changes.Sum(x => x.LinesAdded);
                int deletions = changes.Sum(x => x.LinesDeleted);

                var branches = repo.Branches
                    .Where(b => b.Tip?.Sha == commit.Sha)
                    .Select(b => b.FriendlyName)
                    .ToList();

                var title = commit.MessageShort.Length > 60
                    ? commit.MessageShort[..57] + "..."
                    : commit.MessageShort;

                AnsiConsole.Write(new Rule($"[DarkOrange3]{commit.Sha[..7]}[/] [blue bold]{Markup.Escape(title)}[/]").LeftJustified());
                AnsiConsole.WriteLine();

                var changed = additions + deletions;
                var addedBar = additions == 0 ? 0 : Math.Max(1, (int)Math.Round(additions / (double)changed * 24.0));
                var deletedBar = deletions == 0 ? 0 : Math.Max(1, Math.Min(24 - addedBar, (int)Math.Round(deletions / (double)changed * 24.0)));

                var info = new Grid()
                    .AddColumn(new GridColumn().NoWrap().PadRight(2))
                    .AddColumn();

                info.AddRow("[grey]SHA[/]", $"[DarkOrange3]{commit.Sha[..7]}[/][Grey35]{commit.Sha[7..]}[/]");
                info.AddRow($"[grey]{Strings.Git_Col_Author}[/]", $"[SkyBlue1]{Markup.Escape(commit.Author.Name)}[/]");
                info.AddRow($"[grey]{Strings.Git_Col_Date}[/]", $"[CadetBlue]{commit.Author.When.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)}[/]");

                info.AddRow($"[grey]{Strings.Git_Row_Branches}[/]", branches.Count > 0
                    ? string.Join(", ", branches.Select(x => $"[Aqua]{x}[/]"))
                    : $"[grey]{Strings.Common_None}[/]");

                info.AddRow($"[grey]{Strings.Git_Row_Parents}[/]", commit.Parents.Any()
                    ? string.Join(", ", commit.Parents.Select(x => $"[DarkOrange3]{x.Sha[..7]}[/]"))
                    : $"[grey]{Strings.Analyze_FirstCommit}[/]");

                var counterWidth = Math.Max($"+{additions}".Length, $"-{deletions}".Length) + 2;

                info.AddRow($"[grey]{Strings.Git_Row_Insertions}[/]", $"[green]{$"+{additions}".PadRight(counterWidth)}{new string('█', addedBar)}[/]");
                info.AddRow($"[grey]{Strings.Git_Row_Deletions}[/]", $"[red]{$"-{deletions}".PadRight(counterWidth)}{new string('█', deletedBar)}[/]");

                UI.Box(info, Strings.Git_CommitInformation);

                var files = new Table().Border(TableBorder.None);

                files.AddColumn(new TableColumn($"[bold]{Strings.Git_Col_Status}[/]").NoWrap().PadRight(2));
                files.AddColumn(new TableColumn($"[bold]{Strings.Git_Col_File}[/]"));
                files.AddColumn(new TableColumn("[bold]+/-[/]").RightAligned().NoWrap());

                int fileLimit = Math.Max(3, Console.WindowHeight - 24);

                foreach (var change in changes.Take(fileLimit))
                {
                    var (color, kind, sign) = change.Status switch
                    {
                        ChangeKind.Added => ("green", Strings.Git_Kind_Added, "+"),
                        ChangeKind.Deleted => ("red", Strings.Git_Kind_Deleted, "-"),
                        ChangeKind.Renamed => ("cyan", Strings.Git_Kind_Renamed, "»"),
                        ChangeKind.Modified => ("yellow", Strings.Git_Kind_Modified, "~"),
                        _ => ("grey", change.Status.ToString(), "?")
                    };

                    files.AddRow(
                        $"[{color}]{sign} {kind}[/]",
                        $"[Khaki1]{Markup.Escape(change.Path)}[/]",
                        $"[green]+{change.LinesAdded}[/] [red]-{change.LinesDeleted}[/]");
                }

                if (changes.Count > fileLimit)
                    files.AddRow(string.Empty, string.Format(Strings.Git_MoreFiles, changes.Count - fileLimit), string.Empty);

                UI.Box(files, $"{Strings.Git_ChangedFiles} ({changes.Count})");

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<ShowCommitAction>()
                        .Title($"[cyan]{Strings.Git_CommitActions}[/]")
                        .PageSize(5)
                        .AddChoices(Enum.GetValues<ShowCommitAction>())
                        .UseConverter(x => x switch
                        {
                            ShowCommitAction.ViewDiff => Strings.Git_ViewDiff,
                            ShowCommitAction.ViewFullMessage => Strings.Git_ViewFullMessage,
                            ShowCommitAction.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => x.ToString()
                        })
                );

                switch (action)
                {
                    case ShowCommitAction.ViewDiff:
                        Diff(guid, commit.Sha);
                        break;
                    case ShowCommitAction.ViewFullMessage:
                        AnsiConsole.Clear();

                        AnsiConsole.Write(
                            new Panel(Markup.Escape(commit.Message.Trim()))
                                .Header($"[cyan]{Strings.Git_CommitMessageTitle}[/]")
                                .Expand()
                                .RoundedBorder()
                        );

                        AnsiConsole.MarkupLine(
                            $"\n[grey]{Strings.Git_PressAnyKeyReturn}[/]"
                        );

                        Console.ReadKey();
                        break;
                    case ShowCommitAction.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        #endregion

        private static void Diagnostics(Guid guid)
        {
            var project = GetProject(guid);

            while (true)
            {
                AnsiConsole.Clear();

                using var repo = GitHelper.OpenRepo(project.Path, Strings.Git_Diagnostics);

                if (repo == null)
                    return;

                var problems = new List<Diagnose>();

                var table = new Table()
                    .AddColumn(new TableColumn(Strings.Git_Col_State))
                    .AddColumn(new TableColumn(Strings.Git_Col_Check))
                    .AddColumn(new TableColumn(Strings.Git_Col_Details))
                    .Expand();

                var lockPath = Path.Combine(repo.Info.Path, "index.lock");

                var gitProcesses = Process.GetProcessesByName("git");
                var gitRunning = gitProcesses.Length > 0;

                Array.ForEach(gitProcesses, x => x.Dispose());

                if (!File.Exists(lockPath))
                    table.AddRow("[green]OK[/]", Strings.Git_Check_Index, $"[grey]{Strings.Git_NotLocked}[/]");
                else if (gitRunning)
                    table.AddRow($"[Orange1]{Strings.Git_State_Warn}[/]", Strings.Git_Check_Index, Strings.Git_LockedGitRunning);
                else if (new FileInfo(lockPath).Length > 0)
                    table.AddRow($"[Orange1]{Strings.Git_State_Warn}[/]", Strings.Git_Check_Index, Strings.Git_LockedNotEmpty);
                else
                {
                    table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", Strings.Git_Check_Index, Strings.Git_LockedCrashed);
                    problems.Add(Diagnose.RemoveIndexLock);
                }

                var rebasing = repo.Info.CurrentOperation is CurrentOperation.Rebase or CurrentOperation.RebaseInteractive or CurrentOperation.RebaseMerge;

                var stuckConflicts = repo.Info.CurrentOperation == CurrentOperation.None
                    ? 0
                    : repo.RetrieveStatus(GitHelper.FastStatus).Count(x => x.State.HasFlag(FileStatus.Conflicted));

                if (repo.Info.CurrentOperation == CurrentOperation.None)
                    table.AddRow("[green]OK[/]", Strings.Git_Check_Operation, $"[grey]{Strings.Git_NothingInProgress}[/]");
                else if (rebasing)
                {
                    var steps = RebaseSteps(repo, out var stepIndex, out var stepCount);

                    table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", Strings.Git_Check_Operation,
                        (steps
                            ? string.Format(Strings.Git_RebaseUnfinished, stepIndex + 1, stepCount)
                            : Strings.Git_RebaseUnfinishedByGit) +
                        (stuckConflicts > 0
                            ? string.Format(stuckConflicts == 1 ? Strings.Git_StillConflictedOne : Strings.Git_StillConflictedMany, $"[Red3_1]{stuckConflicts}[/]")
                            : $"[green]{Strings.Git_NoConflictsLeft}[/]") +
                        (steps ? string.Empty : $"\n[grey]{Strings.Git_RebaseFinishInTerminal}[/]"));

                    if (stuckConflicts > 0)
                        problems.Add(Diagnose.ResolveConflicts);
                    else if (steps)
                        problems.Add(Diagnose.ContinueRebase);

                    if (steps)
                        problems.Add(Diagnose.AbortOperation);
                }
                else
                {
                    table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", Strings.Git_Check_Operation,
                        string.Format(Strings.Git_OperationUnfinished, repo.Info.CurrentOperation) +
                        (stuckConflicts > 0 ? " " + string.Format(stuckConflicts == 1 ? Strings.Git_StillConflictedOne : Strings.Git_StillConflictedMany, $"[Red3_1]{stuckConflicts}[/]") : string.Empty));

                    if (stuckConflicts > 0)
                        problems.Add(Diagnose.ResolveConflicts);

                    problems.Add(Diagnose.AbortOperation);
                }

                if (!repo.Info.IsHeadDetached)
                    table.AddRow("[green]OK[/]", "HEAD", string.Format(Strings.Git_OnBranch, $"[Green3_1]{repo.Head.FriendlyName}[/]"));
                else
                {
                    table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", "HEAD", Strings.Git_Detached);
                    problems.Add(Diagnose.AttachHead);
                }

                if (repo.Head.Tip != null)
                    table.AddRow("[green]OK[/]", Strings.Git_Check_LastCommit, $"[DarkOrange3]{repo.Head.Tip.Sha[..7]}[/] - {Markup.Escape(repo.Head.Tip.MessageShort)}");
                else
                    table.AddRow($"[Orange1]{Strings.Git_State_Warn}[/]", Strings.Git_Check_LastCommit, $"[grey]{Strings.Git_NoCommitsYet}[/]");

                var origin = repo.Network.Remotes["origin"];

                var originName = origin?.Url
                    .Split('/', ':')
                    .LastOrDefault()
                    ?.Trim();

                if (originName != null && originName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                    originName = originName[..^4];

                var gitHubName = GetProject(guid).GitHubName;

                if (!string.IsNullOrWhiteSpace(gitHubName))
                    table.AddRow("[green]OK[/]", Strings.Git_Check_GitHubName, $"[grey]{Markup.Escape(gitHubName)}[/]");
                else
                {
                    table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", Strings.Git_Check_GitHubName,
                        Strings.Git_GitHubNameMissing +
                        (originName != null ? string.Format(Strings.Git_RemoteSuggests, Markup.Escape(originName)) : string.Empty));

                    problems.Add(Diagnose.SetGitHubName);
                }

                if (!string.IsNullOrWhiteSpace(gitHubName))
                {
                    if (origin == null)
                    {
                        table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", Strings.Git_Check_Remote, Strings.Git_NoOriginNothing);
                        problems.Add(Diagnose.AddRemote);
                    }
                    else if (!string.IsNullOrWhiteSpace(originName) &&
                             !originName.Equals(gitHubName, StringComparison.OrdinalIgnoreCase))
                    {
                        table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", Strings.Git_Check_Remote,
                            string.Format(Strings.Git_RemoteMismatch, Markup.Escape(originName), Markup.Escape(gitHubName)) + "\n" +
                            $"[grey]{Strings.Git_RemoteMismatchHint}[/]");

                        problems.Add(Diagnose.SetGitHubName);
                    }
                    else
                        table.AddRow("[green]OK[/]", Strings.Git_Check_Remote, $"[grey]{Markup.Escape(origin.Url)}[/]");
                }

                if (repo.Head.TrackedBranch != null)
                {
                    table.AddRow("[green]OK[/]", "Upstream", $"[Green3_1]{repo.Head.TrackedBranch.FriendlyName}[/]");

                    var ahead = repo.Head.TrackingDetails.AheadBy ?? 0;
                    var behind = repo.Head.TrackingDetails.BehindBy ?? 0;

                    table.AddRow(ahead == 0 && behind == 0 ? "[green]OK[/]" : $"[Orange1]{Strings.Git_State_Warn}[/]", Strings.Git_Check_SyncState,
                            ahead == 0 && behind == 0
                                ? $"[grey]{Strings.Git_UpToDateRemote}[/]"
                                : string.Format(Strings.Git_AheadBehind, ahead, behind)
                        );
                }
                else if (origin == null)
                    table.AddRow($"[grey]{Strings.Git_State_Skip}[/]", "Upstream", $"[grey]{Strings.Git_NoRemoteNoCheck}[/]");
                else
                {
                    table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", "Upstream", string.Format(Strings.Git_TracksNothing, repo.Head.FriendlyName));
                    problems.Add(Diagnose.SetUpstream);

                    var remoteBranches = repo.Branches
                        .Where(x => x.IsRemote && !x.FriendlyName.EndsWith("/HEAD") && x.FriendlyName.StartsWith($"{origin.Name}/", StringComparison.Ordinal))
                        .ToList();

                    if (remoteBranches.Count == 1 && remoteBranches[0].FriendlyName != $"{origin.Name}/{repo.Head.FriendlyName}")
                    {
                        table.AddRow($"[Orange1]{Strings.Git_State_Warn}[/]", Strings.Git_Check_BranchName, string.Format(Strings.Git_LocalRemoteNameDiff, repo.Head.FriendlyName, remoteBranches[0].FriendlyName[(origin.Name.Length + 1)..]));

                        problems.Add(Diagnose.RenameBranch);
                    }
                }

                RepositoryStatus? status = null;

                AnsiConsole.Status()
                    .Spinner(Spinner.Known.DotsCircle)
                    .Start(Strings.Git_RetrievingStatus, ctx => status = repo.RetrieveStatus(GitHelper.FastStatus)
                );

                table.AddRow(status.IsDirty
                    ? $"[Orange1]{Strings.Git_State_Warn}[/]"
                    : "[green]OK[/]", Strings.Git_Check_WorkingTree,
                            status.IsDirty
                    ? string.Format(Strings.Git_ChangedFileCount, status.Count(x => x.State != FileStatus.Ignored))
                    : $"[grey]{Strings.Git_Clean}[/]");

                List<IndexEntry> tracked = [];

                AnsiConsole.Status()
                    .Spinner(Spinner.Known.DotsCircle)
                    .Start(Strings.Git_MeasuringFiles, ctx => tracked = repo.Index.ToList());

                var sizes = tracked
                    .Select(x => (x.Path, Info: new FileInfo(Path.Combine(repo.Info.WorkingDirectory, x.Path))))
                    .Where(x => x.Info.Exists)
                    .ToList();

                var oversized = sizes
                    .Where(x => x.Info.Length > 100L * 1024 * 1024)
                    .OrderByDescending(x => x.Info.Length)
                    .ToList();

                if (oversized.Count == 0)
                    table.AddRow("[green]OK[/]", Strings.Git_Check_FileSizes, $"[grey]{Strings.Git_NothingOverLimit}[/]");
                else
                    table.AddRow($"[Red3_1]{Strings.Git_State_Problem}[/]", Strings.Git_Check_FileSizes,
                        string.Format(Strings.Git_OversizedFiles, oversized.Count) + "\n" +
                        string.Join("\n", oversized.Take(3).Select(x =>
                            $"[grey]{Markup.Escape(x.Path)} — {x.Info.Length / 1048576} MB[/]")) +
                        (oversized.Count > 3 ? $"\n[grey]{string.Format(Strings.Git_AndMore, oversized.Count - 3)}[/]" : string.Empty) +
                        $"\n[grey]{Strings.Git_UntrackHint}[/]");

                var buildOutput = tracked
                    .Where(x => x.Path.Split('/').Any(y => y is "bin" or "obj" or ".vs" or "node_modules" or "target" or "__pycache__"))
                    .ToList();

                if (buildOutput.Count == 0)
                    table.AddRow("[green]OK[/]", Strings.Git_Check_BuildOutput, $"[grey]{Strings.Git_NotTracked}[/]");
                else
                {
                    var paths = buildOutput.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var bytes = sizes.Where(x => paths.Contains(x.Path)).Sum(x => x.Info.Length);

                    table.AddRow($"[Orange1]{Strings.Git_State_Warn}[/]", Strings.Git_Check_BuildOutput,
                        string.Format(Strings.Git_BuildOutputTracked, $"[Orange1]{buildOutput.Count}[/]", bytes / 1048576));

                    problems.Add(Diagnose.UntrackBuildOutput);
                }

                try
                {
                    var identity = GitHubCalls.MainIdentity(repo.Config);

                    table.AddRow("[green]OK[/]", Strings.Git_Check_Identity, $"[grey]{Markup.Escape(identity.Name)} <{Markup.Escape(identity.Email)}>[/]");
                }
                catch (Exception identityEx)
                {
                    table.AddRow($"[Orange1]{Strings.Git_State_Warn}[/]", Strings.Git_Check_Identity, Markup.Escape(identityEx.Message.Replace("\n", " ")));
                }

                UI.Box(table, Strings.Git_Diagnostics);

                if (problems.Count == 0)
                {
                    UI.Success(Strings.Git_NoProblems, Strings.Git_Diagnostics);
                    Console.ReadKey();
                    return;
                }

                problems.Add(Diagnose.Back);

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Diagnose>()
                    .Title(Strings.Git_SelectRepair)
                    .AddChoices(problems)
                    .UseConverter(x => x switch
                    {
                        Diagnose.RemoveIndexLock => Strings.Git_Fix_RemoveLock,
                        Diagnose.ContinueRebase => Strings.Git_Fix_ContinueRebase,
                        Diagnose.ResolveConflicts => Strings.Git_ResolveHere,
                        Diagnose.UntrackBuildOutput => Strings.Git_Fix_UntrackBuild,
                        Diagnose.AbortOperation => $"[Red3_1]{string.Format(Strings.Git_Fix_AbortOperation, repo.Info.CurrentOperation)}[/]",
                        Diagnose.AttachHead => Strings.Git_Fix_AttachHead,
                        Diagnose.AddRemote => Strings.Git_Fix_AddRemote,
                        Diagnose.SetGitHubName => Strings.Git_Fix_SetGitHubName,
                        Diagnose.SetUpstream => Strings.Git_Fix_SetUpstream,
                        Diagnose.RenameBranch => Strings.Git_Fix_RenameBranch,
                        Diagnose.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    })
                );

                if (choice == Diagnose.Back)
                    return;

                try
                {
                    switch (choice)
                    {
                        case Diagnose.RemoveIndexLock:
                            File.Delete(lockPath);
                            UI.Success(Strings.Git_LockRemoved, Strings.Git_Diagnostics);
                            break;
                        case Diagnose.UntrackBuildOutput:
                            {
                                UI.Warning(
                                    string.Format(Strings.Git_UntrackWarn, $"[bold]{buildOutput.Count}[/]") +
                                    $"[grey]{Strings.Git_UntrackWarnHistory}[/]",
                                    Strings.Git_UntrackTitle);

                                var untrack = AnsiConsole.Prompt(
                                    new SelectionPrompt<Confirm>()
                                        .Title(Strings.Common_Continue)
                                        .AddChoices(Enum.GetValues<Confirm>())
                                        .UseConverter(UI.ConfirmName));

                                if (untrack == Confirm.No)
                                    continue;

                                foreach (var entry in buildOutput)
                                    Commands.Remove(repo, entry.Path, false);

                                UI.Success(
                                    string.Format(Strings.Git_UntrackDone, buildOutput.Count, "[bold].gitignore[/]"),
                                    Strings.Git_Diagnostics);
                                break;
                            }
                        case Diagnose.ResolveConflicts:
                            {
                                var conflicts = repo.RetrieveStatus(GitHelper.FastStatus)
                                    .Where(x => x.State.HasFlag(FileStatus.Conflicted))
                                    .ToList();

                                var file = AnsiConsole.Prompt(
                                    new SelectionPrompt<StatusEntry?>()
                                        .Title(Strings.Git_SelectFileResolve)
                                        .PageSize(15)
                                        .AddChoices(conflicts.Cast<StatusEntry?>().Append(null))
                                        .UseConverter(x => x == null
                                            ? $"[Red3_1]{Strings.Common_Back}[/]"
                                            : Markup.Escape(x.FilePath)));

                                if (file == null)
                                    continue;

                                if (!SolveConflict(Path.Combine(repo.Info.WorkingDirectory, file.FilePath)))
                                    continue;

                                Commands.Stage(repo, file.FilePath);

                                UI.Success(string.Format(Strings.Git_MarkedResolved, $"[Green3_1]{Markup.Escape(file.FilePath)}[/]"), Strings.Git_Diagnostics);
                                break;
                            }
                        case Diagnose.ContinueRebase:
                            {
                                var committer = GitHubCalls.MainIdentity(repo.Config);
                                var options = new RebaseOptions();

                                RebaseResult result = null;

                                AnsiConsole.Status()
                                    .Spinner(Spinner.Known.Dots)
                                    .Start(Strings.Git_ContinuingRebase, ctx =>
                                        result = repo.Rebase.Continue(committer, options)
                                    );

                                while (result.Status != RebaseStatus.Complete)
                                {
                                    if (repo.RetrieveStatus(GitHelper.FastStatus).Any(x => x.State.HasFlag(FileStatus.Conflicted)))
                                    {
                                        UI.Warning(Strings.Git_RebaseStoppedAgain, Strings.Git_RebaseInProgress);
                                        break;
                                    }

                                    if (result.Status == RebaseStatus.Stop)
                                    {
                                        UI.Warning(Strings.Git_RebaseNeedsHand, Strings.Git_RebaseInProgress);
                                        break;
                                    }

                                    AnsiConsole.Status()
                                        .Spinner(Spinner.Known.Dots)
                                        .Start(string.Format(Strings.Git_ContinuingRebaseStep, repo.Rebase.GetCurrentStepIndex() + 1, repo.Rebase.GetTotalStepCount()), ctx =>
                                            result = repo.Rebase.Continue(committer, options)
                                        );
                                }

                                if (result.Status == RebaseStatus.Complete)
                                    UI.Success(string.Format(Strings.Git_RebaseFinished, $"[Green3_1]{repo.Head.FriendlyName}[/]"), Strings.Git_Diagnostics);

                                break;
                            }
                        case Diagnose.AbortOperation:
                            if (repo.Info.CurrentOperation is CurrentOperation.Rebase or CurrentOperation.RebaseInteractive or CurrentOperation.RebaseMerge)
                                repo.Rebase.Abort();
                            else
                            {
                                repo.Reset(ResetMode.Hard, repo.Head.Tip);

                                foreach (var state in new[] { "MERGE_HEAD", "MERGE_MSG", "CHERRY_PICK_HEAD", "REVERT_HEAD" })
                                    File.Delete(Path.Combine(repo.Info.Path, state));
                            }

                            UI.Success(Strings.Git_OperationAborted, Strings.Git_Diagnostics);
                            break;
                        case Diagnose.AttachHead:
                            {
                                var localBranches = repo.Branches.Where(x => !x.IsRemote).ToList();

                                if (localBranches.Count == 0)
                                {
                                    UI.Error(Strings.Git_NoLocalBranch, Strings.Git_RepairFailed_Title);
                                    break;
                                }

                                var branch = AnsiConsole.Prompt(
                                    new SelectionPrompt<Branch>()
                                    .Title(Strings.Git_SelectBranch)
                                    .AddChoices(localBranches)
                                    .UseConverter(x => x.FriendlyName)
                                );

                                Commands.Checkout(repo, branch);

                                UI.Success(string.Format(Strings.Git_SwitchedTo, $"[Green3_1]{branch.FriendlyName}[/]"));
                                break;
                            }
                        case Diagnose.SetGitHubName:
                            {
                                if (string.IsNullOrWhiteSpace(originName))
                                {
                                    GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.Git_Diagnostics);
                                    break;
                                }

                                var useOriginName = AnsiConsole.Prompt(
                                    new SelectionPrompt<Confirm>()
                                        .Title(string.Format(Strings.Git_UseOriginName, Markup.Escape(originName)))
                                        .AddChoices(Enum.GetValues<Confirm>())
                                        .UseConverter(UI.ConfirmName));

                                if (useOriginName == Confirm.No)
                                {
                                    var clearConfig = GetConfig();
                                    var clearProject = clearConfig.Projects[guid];

                                    clearProject.GitHubName = string.Empty;

                                    clearConfig.Projects[guid] = clearProject;
                                    EditConfig(clearConfig);

                                    GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.Git_Diagnostics);
                                    break;
                                }

                                var nameConfig = GetConfig();
                                var nameProject = nameConfig.Projects[guid];

                                nameProject.GitHubName = originName;

                                nameConfig.Projects[guid] = nameProject;
                                EditConfig(nameConfig);

                                UI.Success(string.Format(Strings.Git_GitHubNameSet, $"[Green3_1]{Markup.Escape(originName)}[/]"), Strings.Git_Diagnostics);
                                break;
                            }
                        case Diagnose.AddRemote:
                            {
                                if (!GitHubCalls.EnsureGitHubRepoConnection(guid, Strings.Git_Diagnostics))
                                    break;

                                var username = GitHubCalls.GetCachedUsername().GetAwaiter().GetResult();

                                if (string.IsNullOrWhiteSpace(username))
                                {
                                    UI.Error(Strings.Git_UsernameUnknown);
                                    break;
                                }

                                repo.Network.Remotes.Add("origin", $"https://github.com/{username}/{GetProject(guid).GitHubName}.git");

                                UI.Success(string.Format(Strings.Git_OriginAdded, "[Green3_1]origin[/]"), Strings.Git_Diagnostics);
                                break;
                            }
                        case Diagnose.SetUpstream:
                            {
                                var remote = repo.Network.Remotes["origin"];

                                if (remote == null)
                                {
                                    UI.Error(Strings.Git_OriginFirst, Strings.Git_RepairFailed_Title);
                                    break;
                                }

                                Fetch(guid, true);

                                var match = repo.Branches
                                    .FirstOrDefault(x => x.IsRemote && x.FriendlyName == $"{remote.Name}/{repo.Head.FriendlyName}");

                                if (match == null)
                                    repo.Network.Push(remote, $"{repo.Head.CanonicalName}:{repo.Head.CanonicalName}", new PushOptions
                                    {
                                        CredentialsProvider = (url, usernameFromUrl, types) => new UsernamePasswordCredentials
                                        {
                                            Username = "x-access-token",
                                            Password = GetGitHubAccessToken()
                                        }
                                    });

                                repo.Branches.Update(repo.Head,
                                    x => x.Remote = remote.Name,
                                    x => x.UpstreamBranch = repo.Head.CanonicalName);

                                UI.Success(string.Format(Strings.Git_NowTracks, $"[Green3_1]{repo.Head.FriendlyName}[/]", $"[Green3_1]{remote.Name}/{repo.Head.FriendlyName}[/]"), Strings.Git_Diagnostics);
                                break;
                            }
                        case Diagnose.RenameBranch:
                            {
                                var remoteBranches = repo.Branches
                                    .Where(x => x.IsRemote && !x.FriendlyName.EndsWith("/HEAD") && x.FriendlyName.StartsWith($"{origin?.Name}/", StringComparison.Ordinal))
                                    .ToList();

                                if (remoteBranches.Count != 1)
                                {
                                    UI.Error(Strings.Git_RenameTargetUnknown);
                                    break;
                                }

                                var newName = remoteBranches[0].FriendlyName[(remoteBranches[0].FriendlyName.IndexOf('/') + 1)..];

                                repo.Branches.Rename(repo.Head, newName);

                                UI.Success(string.Format(Strings.Git_BranchRenamed, $"[Green3_1]{newName}[/]"), Strings.Git_Diagnostics);
                                break;
                            }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                }
                catch (Exception ex)
                {
                    UI.Error(Markup.Escape(ex.Message), Strings.Git_RepairFailed_Title);
                }

                Console.ReadKey();
            }

        }

        private static bool RebaseSteps(Repository repo, out long index, out long count)
        {
            try
            {
                index = repo.Rebase.GetCurrentStepIndex();
                count = repo.Rebase.GetTotalStepCount();

                return true;
            }
            catch (LibGit2SharpException)
            {
                index = 0;
                count = 0;

                return false;
            }
        }

        private static List<string> StillMarked(Repository repo, List<StatusEntry> conflicts)
        {
            List<string> marked = [];

            foreach (var file in conflicts)
            {
                try
                {
                    if (File.ReadLines(Path.Combine(repo.Info.WorkingDirectory, file.FilePath))
                        .Any(line => line.StartsWith("<<<<<<<", StringComparison.Ordinal) ||
                                     line.StartsWith(">>>>>>>", StringComparison.Ordinal)))
                        marked.Add(file.FilePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    marked.Add(file.FilePath);
                }
            }

            return marked;
        }

        private static bool SolveConflict(string path)
        {
            string content;
            Encoding encoding;

            try
            {
                encoding = TextFile.ReadWithBom(path, out content);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_ConflictResolution);
                Console.ReadKey();
                return false;
            }

            if (!TextFile.Decoded(content))
            {
                UI.Error(Strings.Git_ConflictNotUtf8 + "\n\n" +
                         $"[grey]{Strings.Git_ResolveInEditor}[/]", Strings.Git_ConflictResolution);
                Console.ReadKey();
                return false;
            }

            var crlf = content.Contains("\r\n");

            content = content.Replace("\r\n", "\n");

            var first = content.IndexOf("<<<<<<<", StringComparison.Ordinal);
            var last = content.LastIndexOf(">>>>>>>", StringComparison.Ordinal);

            if (first < 0 || last < first)
            {
                UI.Error(Strings.Git_NoMarkersFound, Strings.Git_ConflictResolution);
                Console.ReadKey();
                return false;
            }

            var closing = content.IndexOf('\n', last);
            var end = closing < 0 ? content.Length : closing + 1;

            var text = new StringBuilder(content[first..end]);

            try
            {
                AnsiConsole.AlternateScreen(() => UI.EditText(text));
            }
            catch (NotSupportedException)
            {
                UI.Error(Strings.Git_NoAltScreen, Strings.Git_ConflictResolution);
                Console.ReadKey();
                return false;
            }

            var resolved = content[..first] + text + content[end..];

            try
            {
                File.WriteAllText(path, crlf ? resolved.Replace("\n", "\r\n") : resolved, encoding);
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message), Strings.Git_ConflictResolution);
                Console.ReadKey();

                return false;
            }

            return true;
        }

        private enum PreferredCommit
        {
            Local,
            Remote
        }

        private enum Action2
        {
            Stage,
            Commit,
            Diff,
            UndoCommit,
            RedoCommit,
            Push,
            Pull,
            Fetch,
            Sync,
            CreateBranch,
            SwitchBranch,
            MergeBranch,
            DeleteBranch,
            SaveStash,
            ApplyStash,
            PopStash,
            ListStashes,
            Back
        }

        private enum Action1
        {
            Status,
            Changes,
            Sync,
            Branches,
            Stashes,
            Log,
            Diagnostics,
            Back
        }

        private enum Diagnose
        {
            RemoveIndexLock,
            ContinueRebase,
            ResolveConflicts,
            UntrackBuildOutput,
            AbortOperation,
            AttachHead,
            AddRemote,
            SetGitHubName,
            SetUpstream,
            RenameBranch,
            Back
        }

        private enum ShowCommitAction
        {
            ViewDiff,
            ViewFullMessage,
            Back
        }

        private enum UndoCommitAction
        {
            KeepChangesStaged,
            KeepChangesUnstaged,
            DeleteChangesCompletely,
            Back
        }

        private enum RebaseFailedAction
        {
            Continue,
            Resolve,
            OpenInIDE,
            Recheck,
            Abort,
            Leave
        }

        private enum MergeFailedAction
        {
            Continue,
            Resolve,
            OpenInIDE,
            Recheck,
            Abort,
            Leave
        }
    }
}
