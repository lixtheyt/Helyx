using Color = Spectre.Console.Color;
using System.Globalization;
using Helyx.Data;
using Helyx.Shared;
using LibGit2Sharp;
using Spectre.Console;
using Spectre.Console.Rendering;
using static Helyx.Data.ConfigurationHandler;

namespace Helyx.Projects
{
    internal static class ProjectsMenu
    {
        internal static readonly Dictionary<string, string> CodingLanguages = new()
        {
            [".cs"] = "C#",
            [".java"] = "Java",
            [".kt"] = "Kotlin",
            [".py"] = "Python",
            [".js"] = "JavaScript",
            [".ts"] = "TypeScript",
            [".jsx"] = "React JSX",
            [".tsx"] = "React TSX",
            [".go"] = "Go",
            [".rs"] = "Rust",
            [".cpp"] = "C++",
            [".cc"] = "C++",
            [".cxx"] = "C++",
            [".c"] = "C",
            [".h"] = "C/C++ Header",
            [".hpp"] = "C++ Header",
            [".swift"] = "Swift",
            [".dart"] = "Dart",
            [".php"] = "PHP",
            [".rb"] = "Ruby",
            [".lua"] = "Lua",
            [".r"] = "R",
            [".scala"] = "Scala",
            [".fs"] = "F#",
            [".vb"] = "Visual Basic .NET",
            [".html"] = "HTML",
            [".css"] = "CSS",
            [".scss"] = "SCSS",
            [".sass"] = "Sass",
            [".less"] = "Less",
            [".vue"] = "Vue",
            [".svelte"] = "Svelte",
            [".sh"] = "Shell",
            [".bash"] = "Bash",
            [".zsh"] = "Zsh",
            [".ps1"] = "PowerShell",
            [".bat"] = "Batch",
            [".sql"] = "SQL",
            [".graphql"] = "GraphQL",
            [".gql"] = "GraphQL",
            [".json"] = "JSON",
            [".jsonc"] = "JSON with Comments",
            [".xml"] = "XML",
            [".yaml"] = "YAML",
            [".yml"] = "YAML",
            [".toml"] = "TOML",
            [".ini"] = "INI",
            [".env"] = "Environment Config",
            [".dockerfile"] = "Dockerfile",
            [".tf"] = "Terraform",
            [".hcl"] = "HCL",
            [".csproj"] = "C# Project",
            [".sln"] = "Visual Studio Solution",
            [".gradle"] = "Gradle",
            [".cmake"] = "CMake",
            [".cu"] = "CUDA",
            [".cuh"] = "CUDA Header",
            [".ino"] = "Arduino",
            [".asm"] = "Assembly",
            [".md"] = "Markdown",
            [".rst"] = "reStructuredText",
            [".tex"] = "LaTeX",
            [".csv"] = "CSV",
            [".ipynb"] = "Jupyter Notebook",
        };

        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, GitHubRepository?> GitHubRepositories = new();

        private static string GetLanguagesFromFolder(string folderPath)
        {
            var languages = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(folderPath))
                return string.Empty;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };

            try
            {
                foreach (var file in Directory.EnumerateFiles(folderPath, "*", options))
                {
                    var ext = Path.GetExtension(file);

                    if (string.IsNullOrWhiteSpace(ext))
                        continue;

                    if (CodingLanguages.TryGetValue(ext, out var language) &&
                        !string.IsNullOrWhiteSpace(language) &&
                        seen.Add(language))
                    {
                        languages.Add(language);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return string.Join(", ", languages);
        }

        private static string? PickFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Strings.Projects_SelectFolder,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            var owner = new Win32Window(NativeMethods.GetConsoleWindow());
            NativeMethods.SetForegroundWindow(owner.Handle);

            return dialog.ShowDialog(owner) == DialogResult.OK
                ? dialog.SelectedPath
                : null;
        }

        private static string RootCommitSha(string path)
        {
            if (!Repository.IsValid(path))
                return string.Empty;

            try
            {
                using var repo = new Repository(path);

                var commit = repo.Head.Tip;

                if (commit == null)
                    return string.Empty;

                while (commit.Parents.Any())
                    commit = commit.Parents.First();

                return commit.Sha;
            }
            catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static bool HasCommit(string path, string sha)
        {
            if (!Repository.IsValid(path))
                return false;

            try
            {
                using var repo = new Repository(path);

                return repo.Lookup<Commit>(sha) != null;
            }
            catch (Exception ex) when (ex is LibGit2SharpException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static void PrintHeader(Guid guid)
        {
            var grid = HeaderGrid(guid, out _, out var title);

            UI.Box(grid, title);
        }

        internal static IRenderable HeaderPanel(Guid guid, out int height)
        {
            var grid = HeaderGrid(guid, out _, out var title);

            var panel = UI.StyledPanel(grid, title, UIKind.Info);

            var options = RenderOptions.Create(AnsiConsole.Console, AnsiConsole.Profile.Capabilities);

            height = Segment.SplitLines(((IRenderable)panel).Render(options, Console.WindowWidth)).Count + 1;

            return panel;
        }

        private static Grid HeaderGrid(Guid guid, out int rows, out string title)
        {
            var project = GetProject(guid);

            title = project.HelyxName;

            var count = 0;

            var detailsGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                .AddColumn();

            void Row(string left, string right)
            {
                detailsGrid.AddRow(left, right);
                count++;
            }

            var statusDef = Tags.AllStatuses().TryGetValue(project.Status, out var status)
                ? status
                : Tags.BuiltInStatuses[BuiltInStatusIds.Active];

            Row($"[bold]{Strings.Projects_Row_Status}[/]", $"{Tags.Markup(statusDef, "●")} {Markup.Escape(statusDef.Name)}");

            Row($"[bold]{Strings.Projects_Row_LastModified}[/]", Markup.Escape(Directory.GetLastWriteTime(project.Path).ToString("G", CultureInfo.CurrentCulture)));

            if (project.Badges.Count > 0)
            {
                var allBadges = Tags.AllBadges();

                var badgeText = string.Join(" ", project.Badges
                    .Where(allBadges.ContainsKey)
                    .Select(b => Tags.Markup(allBadges[b], $"[[{Markup.Escape(allBadges[b].Name)}]]")));

                if (!string.IsNullOrEmpty(badgeText))
                    Row($"[bold]{Strings.Projects_Row_Badges}[/]", badgeText);
            }
            
            var linked = GitHubCalls.IsAuthorizedWithGitHub()
                         && GitHubCalls.HasGitHubName(guid)
                         && Repository.IsValid(project.Path);

            var wantsTopics = linked
                              && project.GitHubSyncSettings.TryGetValue(GitHubSync.FetchGitHubRepoTopics, out var fetchTopics)
                              && fetchTopics;

            var wantsLanguages = linked
                                 && project.GitHubSyncSettings.TryGetValue(GitHubSync.OverwriteUsedLanguagesByGitHub, out var overwrite)
                                 && overwrite;

            GitHubRepository? githubRepo = null;

            if (wantsTopics || wantsLanguages)
                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Arc)
                    .Start(Strings.Projects_RetrievingStats, ctx =>
                        githubRepo = GitHubCalls.GetGitHubRepoStats(guid, false).GetAwaiter().GetResult()
                    );

            if (wantsTopics)
                Row(
                    $"[bold]{Strings.Projects_Row_Topics}[/]",
                    githubRepo?.Topics is { } topics
                        ? topics.Count > 0
                            ? string.Join(", ", topics.Select(x => $"[{Color.LightSteelBlue}]<{Markup.Escape(x)}>[/]"))
                            : $"[{Color.Grey}]{Strings.Common_None}[/]"
                        : $"[{Color.Red3_1}]{Strings.Common_Unknown}[/]");

            Row(
                $"[bold]{Strings.Projects_Row_Languages}[/]",
                wantsLanguages
                    ? githubRepo?.Languages is { } languages
                        ? string.Join(", ", languages.Keys)
                        : $"[{Color.Red3_1}]{Strings.Common_Unknown}[/]"
                    : project.UsedLanguages.Count > 0
                        ? string.Join(", ", project.UsedLanguages)
                        : $"[{Color.Grey}]{Strings.Common_None}[/]");

            rows = count;

            return detailsGrid;
        }

        internal static void DisplayProjects()
        {
            while (true)
            {
                Back:
                AnsiConsole.Clear();
                AnsiConsole.Write(new Rule($"[bold {Color.Blue}]{Strings.Projects_Title}[/]").LeftJustified());
                AnsiConsole.WriteLine();

                var allStatuses = Tags.AllStatuses();

                var projectsByStatus = GetConfig().Projects.Values
                    .GroupBy(p => allStatuses.ContainsKey(p.Status) ? p.Status : BuiltInStatusIds.Active)
                    .OrderBy(g => StatusSortOrder(g.Key))
                    .ThenBy(g => allStatuses[g.Key].Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var addNewProject = new ProjectClass { HelyxName = Strings.Projects_AddNew };
                var headerSentinels = new HashSet<ProjectClass>();

                var prompt = new SelectionPrompt<ProjectClass?>()
                    .Title(Strings.Projects_Select)
                    .EnableSearch()
                    .SearchPlaceholderText(Strings.Projects_Filter)
                    .UseConverter(x => x switch
                    {
                        null => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ when ReferenceEquals(x, addNewProject) => $"[{Color.Aqua}]{Strings.Projects_AddNew}[/]",
                        _ when headerSentinels.Contains(x) =>
                            $"{Tags.Markup(allStatuses[x.Status], "●")} [{Color.White}]{Markup.Escape(allStatuses[x.Status].Name)}[/]",
                        _ => $"[{Color.Gray58}]{Markup.Escape(x.HelyxName)}[/]"
                    });

                foreach (var group in projectsByStatus)
                {
                    var header = new ProjectClass { Status = group.Key };
                    headerSentinels.Add(header);

                    prompt.AddChoiceGroup(header, group.OrderBy(p => p.HelyxName, StringComparer.OrdinalIgnoreCase));
                }

                prompt.AddChoices(addNewProject, null);

                var choice = AnsiConsole.Prompt(prompt);

                switch (choice)
                {
                    case null:
                        AnsiConsole.Clear();
                        return;

                    case var _ when ReferenceEquals(choice, addNewProject):
                        AddNewProject();
                        continue;

                    case var _ when headerSentinels.Contains(choice):
                        continue;
                }

                var guid = choice.Guid;

                var selected = GetProject(guid);

                if (Directory.Exists(selected.Path))
                {
                    var sha = RootCommitSha(selected.Path);

                    if (!string.IsNullOrWhiteSpace(sha) && sha != selected.RootCommit)
                    {
                        var shaConfig = GetConfig();
                        var shaProject = shaConfig.Projects[guid];

                        shaProject.RootCommit = sha;

                        shaConfig.Projects[guid] = shaProject;
                        EditConfig(shaConfig);
                    }
                }
                else
                {
                    AnsiConsole.Clear();

                    UI.Warning(string.Format(Strings.Projects_FolderNotFound, $"'{Markup.Escape(selected.HelyxName)}'") + $"\n[{Color.Grey}]{Markup.Escape(selected.Path)}[/]", Strings.Projects_Missing_Title);

                    var missing = AnsiConsole.Prompt(
                        new SelectionPrompt<MissingProjectAction>()
                            .Title(Strings.Projects_WhatToDo)
                            .AddChoices(Enum.GetValues<MissingProjectAction>())
                            .UseConverter(x => x switch
                            {
                                MissingProjectAction.Search => Strings.Projects_SearchAuto,
                                MissingProjectAction.Locate => Strings.Projects_LocateManually,
                                MissingProjectAction.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                                _ => x.ToString()
                            }));

                    if (missing == MissingProjectAction.Back)
                        continue;

                    string? newPath = null;

                    if (missing == MissingProjectAction.Locate)
                    {
                        newPath = PickFolder();

                        if (newPath == null)
                        {
                            UI.Error(Strings.Projects_NoFolderSelected, Strings.Common_Cancelled);
                            Console.ReadKey();
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(selected.RootCommit) && RootCommitSha(newPath) != selected.RootCommit)
                        {
                            UI.Warning(Strings.Projects_DifferentRepo + "\n" + Strings.Projects_LinkAnyway, Strings.Projects_DifferentRepo_Title);

                            var linkAnyway = AnsiConsole.Prompt(
                                new SelectionPrompt<Confirm>()
                                    .AddChoices(Enum.GetValues<Confirm>())
                                    .UseConverter(UI.ConfirmName));

                            if (linkAnyway == Confirm.No)
                                continue;
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(selected.RootCommit))
                        {
                            UI.Error(Strings.Projects_NoFingerprint + "\n" + Strings.Projects_LocateInstead, Strings.Projects_Missing_Title);
                            Console.ReadKey();
                            continue;
                        }

                        var parents = new List<string>();

                        foreach (var path in GetConfig().Projects.Values.Select(x => x.Path).Prepend(selected.Path))
                        {
                            var parent = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar));

                            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) && !parents.Contains(parent))
                                parents.Add(parent);
                        }

                        var grandParent = Path.GetDirectoryName(
                            Path.GetDirectoryName(selected.Path.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty);

                        if (!string.IsNullOrWhiteSpace(grandParent) && Directory.Exists(grandParent) && !parents.Contains(grandParent))
                            parents.Add(grandParent);

                        var taken = GetConfig().Projects.Values
                            .Where(x => x.Guid != guid)
                            .Select(x => x.Path)
                            .ToList();

                        var searchOptions = new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            MaxRecursionDepth = 2,
                            IgnoreInaccessible = true
                        };

                        AnsiConsole.Status()
                            .Spinner(Spinner.Known.Dots)
                            .Start(Strings.Common_Searching, ctx =>
                            {
                                foreach (var parent in parents)
                                {
                                    try
                                    {
                                        foreach (var candidate in Directory.EnumerateDirectories(parent, "*", searchOptions))
                                        {
                                            if (Path.GetFileName(candidate).StartsWith('.'))
                                                continue;

                                            if (taken.Any(x => x.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                                                continue;

                                            if (!HasCommit(candidate, selected.RootCommit))
                                                continue;

                                            newPath = candidate;
                                            return;
                                        }
                                    }
                                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                    {
                                    }
                                }
                            });

                        if (newPath == null)
                        {
                            UI.Error(Strings.Projects_NotFound + "\n" + Strings.Projects_LocateInstead, Strings.Projects_Missing_Title);
                            Console.ReadKey();
                            continue;
                        }
                    }

                    var config = GetConfig();
                    var project = config.Projects[guid];

                    project.Path = newPath;

                    var newSha = RootCommitSha(newPath);

                    if (!string.IsNullOrWhiteSpace(newSha))
                        project.RootCommit = newSha;

                    config.Projects[guid] = project;
                    EditConfig(config);

                    UI.Success(string.Format(Strings.Projects_Linked, $"'{Markup.Escape(selected.HelyxName)}'") + $"\n[{Color.Grey}]{Markup.Escape(newPath)}[/]", Strings.Projects_Found_Title);
                    Console.ReadKey();
                }

                while (true)
                {
                    AnsiConsole.Clear();

                    PrintHeader(guid);

                    var action = AnsiConsole.Prompt(
                        new SelectionPrompt<ProjectActionMenu>()
                            .Title(Strings.Common_SelectAction)
                            .AddChoices(Enum.GetValues<ProjectActionMenu>())
                            .UseConverter(x => x switch
                            {
                                ProjectActionMenu.IDE => Strings.Projects_Menu_IDE,
                                ProjectActionMenu.Git => Strings.Projects_Menu_Git,
                                ProjectActionMenu.GitHub => Strings.Projects_Menu_GitHub,
                                ProjectActionMenu.Manage => Strings.Projects_Menu_Manage,
                                ProjectActionMenu.Other => Strings.Other_Title,
                                ProjectActionMenu.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                                _ => x.ToString()
                            }));

                    switch (action)
                    {
                        case ProjectActionMenu.IDE:
                            IDEActions.Display(guid);
                            break;
                        case ProjectActionMenu.Git:
                            GitActions.Display(guid);
                            break;
                        case ProjectActionMenu.GitHub:
                            GitHubActions.Display(guid);
                            break;
                        case ProjectActionMenu.Manage:
                            ProjectActions.Display(guid);

                            if (GetConfig().Projects.ContainsKey(guid))
                                break;

                            return;
                        case ProjectActionMenu.Other:
                            OtherActions.Display(guid);
                            break;
                        case ProjectActionMenu.Back:
                            AnsiConsole.Clear();
                            goto Back;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    AnsiConsole.Clear();
                }
            }
        }

        private static int StatusSortOrder(Guid status) =>
            status == BuiltInStatusIds.Active ? 0
            : status == BuiltInStatusIds.Paused ? 1
            : status == BuiltInStatusIds.Inactive ? 2
            : status == BuiltInStatusIds.Archived ? 4
            : 3;

        private static void AddNewProject()
        {
            AnsiConsole.Clear();

            string? path = PickFolder();

            if (path == null)
            {
                UI.Error(Strings.Projects_NoFolderSelected, Strings.Common_Cancelled);
                Console.ReadKey();
                return;
            }

            if (!Directory.Exists(path))
            {
                UI.Error(string.Format(Strings.Projects_FolderGone, $"'{Markup.Escape(path)}'"), Strings.Projects_FolderNotFound_Title);
                Console.ReadKey();
                return;
            }

            var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));

            if (string.IsNullOrWhiteSpace(folderName))
            {
                UI.Error(Strings.Projects_DriveRoot, Strings.Projects_InvalidFolder_Title);
                Console.ReadKey();
                return;
            }

            string? Normalize(string value)
            {
                try
                {
                    return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return null;
                }
            }

            var picked = Normalize(path);

            var taken = GetConfig().Projects.Values
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Path)
                                     && Normalize(x.Path) is { } stored
                                     && string.Equals(stored, picked, StringComparison.OrdinalIgnoreCase));

            if (taken != null)
            {
                UI.Warning(string.Format(Strings.Projects_AlreadyAdded, $"'{Markup.Escape(taken.HelyxName)}'") + "\n" + Strings.Projects_AddAgain, Strings.Projects_DuplicateFolder_Title);

                var confirm0 = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                        .AddChoices(Enum.GetValues<Confirm>())
                        .UseConverter(UI.ConfirmName));

                AnsiConsole.Clear();

                if (confirm0 == Confirm.No)
                    return;
            }

            var projectName = AnsiConsole.Prompt(
                new TextPrompt<string>(Strings.Projects_EnterName)
                    .WithConverter(Markup.Escape)
                    .DefaultValue(folderName)).Trim();

            AnsiConsole.Clear();

            if (string.IsNullOrEmpty(projectName))
            {
                UI.Error(Strings.Projects_NameEmpty, Strings.Common_InvalidName);
                Console.ReadKey();
                return;
            }

            if (GetConfig().Projects.Any(p => p.Value.HelyxName.Equals(projectName, StringComparison.OrdinalIgnoreCase)))
            {
                UI.Warning(string.Format(Strings.Projects_NameExists, $"'{Markup.Escape(projectName)}'") + "\n" + Strings.Common_Continue, Strings.Projects_DuplicateProject_Title);

                var confirm1 = AnsiConsole.Prompt(
                    new SelectionPrompt<Confirm>()
                        .AddChoices(Enum.GetValues<Confirm>())
                        .UseConverter(UI.ConfirmName));

                if (confirm1 == Confirm.No)
                    return;
            }

            var language = string.Empty;

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start(Strings.Projects_DetectingLanguages, ctx => language = GetLanguagesFromFolder(path));

            AnsiConsole.Write(new Rule($"[bold {Color.Cyan}]{Strings.Projects_ConfirmDetails}[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var confirmGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                .AddColumn();

            confirmGrid.AddRow($"[bold]{Strings.Projects_Row_ProjectName}[/]", $"[{Color.Blue}]{Markup.Escape(projectName)}[/]");
            confirmGrid.AddRow($"[bold]{Strings.Common_Path}[/]", $"[{Color.Green}]{Markup.Escape(path)}[/]");
            confirmGrid.AddRow($"[bold]{Strings.Projects_Row_Languages}[/]", string.IsNullOrWhiteSpace(language)
                ? $"[{Color.Grey}]{Strings.Projects_NoneDetected}[/]"
                : $"[{Color.Yellow}]{language}[/]");

            UI.Box(confirmGrid, "");

            var confirm2 = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.Projects_AddConfirm)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            AnsiConsole.Clear();

            if (confirm2 == Confirm.No)
                return;

            var detectedLanguages = string.IsNullOrWhiteSpace(language)
                ? new List<string>()
                : new List<string>(language.Split(", "));

            var config = GetConfig();

            config.Projects.Add(Guid.NewGuid(), new ProjectClass(
                projectName,
                path,
                detectedLanguages
                )
                { RootCommit = RootCommitSha(path) }
                );

            EditConfig(config);
        }

        private enum ProjectActionMenu
        {
            IDE,
            Git,
            GitHub,
            Manage,
            Other,
            Back
        }

        private enum MissingProjectAction
        {
            Search,
            Locate,
            Back
        }
    }
}
