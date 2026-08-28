using Helyx.Data;
using System.Globalization;
using System.IO.Compression;
using Helyx.Shared;
using LibGit2Sharp;
using Spectre.Console;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Helyx.Projects
{
    internal static class Backups
    {
        private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Helyx", "backups");

        private static bool BackupsFolderExists => Directory.Exists(Dir);

        private const string StampFormat = "ddMMyyyy~HHmmss";

        private static string ProjectMissingMessage =>
            Strings.Backups_ProjectMissing + "\n" + Strings.Backups_ProjectMissing_Hint;

        private static List<string> GetBackupStamps(Guid guid)
        {
            if (!BackupsFolderExists)
                return [];

            List<string> files;

            try
            {
                files = Directory.EnumerateFiles(Dir, $"{guid}!*.zip").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }

            var stamps = new List<(string Stamp, DateTime Date)>();

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);

                if (string.IsNullOrEmpty(name))
                    continue;

                var parts = name.Split('!');

                if (parts.Length != 2)
                    continue;

                if (!DateTime.TryParseExact(
                        parts[1],
                        StampFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var date))
                    continue;

                stamps.Add((parts[1], date));
            }

            return stamps
                .OrderByDescending(x => x.Date)
                .Select(x => x.Stamp)
                .ToList();
        }

        private static string BackupPath(Guid guid, string stamp) =>
            Path.Combine(Dir, $"{guid}!{stamp}.zip");

        public static void Display(Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                switch (AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                    .Title(Strings.Backups_Title)
                    .AddChoices(Enum.GetValues<Action>())
                    .UseConverter(x => x switch
                    {
                        Action.CreateBackup => Strings.Backups_Create,
                        Action.RestoreBackup => Strings.Backups_Restore,
                        Action.DeleteBackup => Strings.Backups_Delete,
                        Action.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    })))
                {
                    case Action.CreateBackup:
                        CreateBackup(guid).GetAwaiter().GetResult();
                        break;
                    case Action.RestoreBackup:
                        RestoreBackup(guid).GetAwaiter().GetResult();
                        break;
                    case Action.DeleteBackup:
                        DeleteBackup(guid).GetAwaiter().GetResult();
                        break;
                    case Action.Back:
                        return;
                }
            }
        }

        private static async Task CreateBackup(Guid guid)
        {
            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                .Title(Strings.Backups_CreateConfirm)
                .AddChoices(Enum.GetValues<Confirm>())
                .UseConverter(UI.ConfirmName)
            );

            if (confirm is Confirm.No)
                return;

            if (!ConfigurationHandler.GetConfig().Projects.TryGetValue(guid, out var project))
            {
                UI.Error(ProjectMissingMessage, Strings.Backups_Create);
                Console.ReadKey();
                return;
            }

            if (!Directory.Exists(project.Path))
            {
                UI.Error(Strings.Common_ProjectFolderMissing + $"\n[grey]{Markup.Escape(project.Path)}[/]", Strings.Backups_Create);
                Console.ReadKey();
                return;
            }

            try
            {
                Directory.CreateDirectory(Dir);
            }
            catch (Exception ex)
            {
                UI.Error(Strings.Backups_FolderCreateFailed + $"\n\n{Markup.Escape(ex.Message)}", Strings.Backups_Create);
                Console.ReadKey();
                return;
            }

            var scope = Repository.IsValid(project.Path)
                ? AnsiConsole.Prompt(
                    new SelectionPrompt<BackupScope>()
                        .Title(Strings.Backups_ScopeQuestion)
                        .AddChoices(Enum.GetValues<BackupScope>())
                        .UseConverter(x => x switch
                        {
                            BackupScope.SkipIgnored => Strings.Backups_ScopeSkipIgnored,
                            BackupScope.Everything => Strings.Backups_ScopeEverything,
                            _ => x.ToString()
                        }))
                : BackupScope.Everything;

            using var ignoreRepo = scope == BackupScope.SkipIgnored
                ? new Repository(project.Path)
                : null;

            Exception? err = null;
            List<string> skipped = [];
            var created = false;
            string absPath = BackupPath(guid, $"{DateTimeOffset.UtcNow:ddMMyyyy~HHmmss}");

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Strings.Backups_Running, async ctx =>
                {
                    try
                    {
                        await using FileStream fs = new FileStream(absPath, FileMode.CreateNew);

                        created = true;

                        await using var archive = await ZipArchive.CreateAsync(fs, ZipArchiveMode.Create, false, null);

                        foreach (var file in Directory.EnumerateFiles(project.Path, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = 0
                        }))
                        {
                            var entryName = Path.GetRelativePath(project.Path, file)
                                .Replace('\\', '/');

                            if (ignoreRepo != null &&
                                !entryName.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) &&
                                ignoreRepo.Ignore.IsPathIgnored(entryName))
                                continue;

                            ctx.Status(string.Format(Strings.Backups_BackingUpFile, entryName));

                            FileStream input;

                            try
                            {
                                input = File.OpenRead(file);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                skipped.Add(entryName);
                                continue;
                            }

                            await using (input)
                            {
                                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

                                await using var output = await entry.OpenAsync();

                                await input.CopyToAsync(output);
                            }
                        }

                        foreach (var folder in Directory.EnumerateDirectories(project.Path, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = 0
                        }))
                        {
                            var entryName = Path.GetRelativePath(project.Path, folder)
                                .Replace('\\', '/') + "/";

                            if (ignoreRepo != null &&
                                !entryName.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) &&
                                ignoreRepo.Ignore.IsPathIgnored(entryName))
                                continue;

                            archive.CreateEntry(entryName);
                        }
                    }
                    catch (Exception ex)
                    {
                        err = ex;
                    }
                });

            if (err != null)
            {
                UI.Error(Markup.Escape(err.Message), Strings.Backups_Failed_Title);

                try
                {
                    if (created && File.Exists(absPath))
                        File.Delete(absPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
            else if (skipped.Count > 0)
                UI.Warning(
                    string.Format(Strings.Backups_CreatedWithSkipped, skipped.Count) + "\n\n" +
                    string.Join("\n", skipped.Take(10).Select(x => $"[grey]{Markup.Escape(x)}[/]")) +
                    (skipped.Count > 10 ? $"\n[grey]{string.Format(Strings.Backups_AndMore, skipped.Count - 10)}[/]" : string.Empty),
                    Strings.Backups_Create);
            else
                UI.Success(Strings.Backups_Created, Strings.Backups_Create);

            Console.ReadKey();
        }

        private static async Task RestoreBackup(Guid guid)
        {
            if (!BackupsFolderExists)
            {
                UI.Warning(Strings.Backups_NoneAvailable, Strings.Backups_Restore);
                Console.ReadKey();
                return;
            }

            var orderedBackups = GetBackupStamps(guid);

            if (orderedBackups.Count == 0)
            {
                UI.Warning(Strings.Backups_NoneToRestore, Strings.Backups_Restore);
                Console.ReadKey();
                return;
            }

            string projectName;

            if (ConfigurationHandler.GetConfig().Projects.TryGetValue(guid, out var project))
                projectName = project.HelyxName;
            else
            {
                UI.Error(ProjectMissingMessage, Strings.Backups_Restore);
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
            layout["Title"].Update(new Rule($"[blue bold]{Strings.Backups_Header}[/]").LeftJustified());

            int pageSize = Math.Max(3, Console.WindowHeight - headerHeight - 10);
            int selectedIndex = 0;

            int totalPages = (int)Math.Ceiling(orderedBackups.Count / (double)pageSize);

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    bool running = true;

                    while (running)
                    {
                        int currentPage = selectedIndex / pageSize;
                        int firstRow = currentPage * pageSize;
                        int lastRow = Math.Min(firstRow + pageSize, orderedBackups.Count);

                        var table = new Table()
                            .Border(TableBorder.Rounded)
                            .BorderColor(Color.Grey)
                            .Expand();

                        table.AddColumns(" ", Strings.Backups_Col_Project, Strings.Backups_Col_Date);

                        for (int i = firstRow; i < lastRow; i++)
                        {
                            var date = DateTime.ParseExact(orderedBackups[i], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                                .ToLocalTime();

                            table.AddRow(
                                i == selectedIndex ? "[SpringGreen2_1]>[/]" : " ",
                                $"[DarkOrange3]{Markup.Escape(projectName)}[/]",
                                $"[CadetBlue]{date}[/]"
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
                                    $"[grey]{string.Format(Strings.Common_Page, currentPage + 1, totalPages, selectedIndex + 1, orderedBackups.Count)}[/]"))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        ctx.Refresh();

                        switch (Console.ReadKey(true).Key)
                        {
                            case ConsoleKey.UpArrow:
                                selectedIndex = selectedIndex == 0
                                    ? orderedBackups.Count - 1
                                    : selectedIndex - 1;
                                break;

                            case ConsoleKey.DownArrow:
                                selectedIndex = selectedIndex == orderedBackups.Count - 1
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

            var selectedBackup = orderedBackups[selectedIndex];

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            UI.Warning($"[bold]{Strings.Backups_OverwriteWarning}[/]");
            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                .Title(Strings.Common_Continue)
                .AddChoices(Enum.GetValues<Confirm>())
                .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            Exception? err = null;
            string? leftover = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Strings.Backups_Restoring, async ctx =>
                {
                    string root = Path.GetFullPath(project.Path).TrimEnd(Path.DirectorySeparatorChar);
                    string stamp = $"{DateTimeOffset.UtcNow:ddMMyyyy~HHmmss}";
                    string staging = $"{root}.staging!{stamp}";
                    string old = $"{root}.old!{stamp}";
                    bool moved = false;

                    try
                    {
                        Directory.CreateDirectory(staging);

                        await using FileStream fs = new FileStream(BackupPath(guid, selectedBackup), FileMode.Open, FileAccess.Read);
                        await using var archive = await ZipArchive.CreateAsync(fs, ZipArchiveMode.Read, false, null);

                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));

                            if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                                throw new InvalidDataException(string.Format(Strings.Backups_EntryEscapes, entry.FullName));

                            ctx.Status(string.Format(Strings.Backups_RestoringFile, entry.FullName));

                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(target);
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                            await using Stream input = await entry.OpenAsync();
                            await using FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write);

                            await input.CopyToAsync(output);
                        }

                        ctx.Status(Strings.Backups_Swapping).Spinner(Spinner.Known.Star).SpinnerStyle(Style.Parse("purple"));

                        if (Directory.Exists(root))
                        {
                            Directory.Move(root, old);
                            moved = true;
                        }

                        try
                        {
                            Directory.Move(staging, root);
                        }
                        catch
                        {
                            try
                            {
                                if (moved)
                                    Directory.Move(old, root);
                            }
                            catch (Exception rollbackEx) when (rollbackEx is IOException or UnauthorizedAccessException)
                            {
                                leftover = old;
                            }

                            throw;
                        }

                        if (!moved)
                            return;

                        try
                        {
                            Directory.Delete(old, true);
                        }
                        catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                        {
                            leftover = old;
                        }
                    }
                    catch (Exception ex)
                    {
                        err = ex;

                        try
                        {
                            if (Directory.Exists(staging))
                                Directory.Delete(staging, true);
                        }
                        catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                        {
                        }
                    }
                });

            if (err != null)
            {
                UI.Error(Markup.Escape(err.Message) +
                         (leftover == null
                             ? string.Empty
                             : "\n\n" + Strings.Backups_OriginalLeftAt + $"\n[grey]{Markup.Escape(leftover)}[/]"),
                    Strings.Backups_RestoreFailed_Title);
                Console.ReadKey();
                return;
            }

            if (leftover == null)
                UI.Success(Strings.Backups_Restored, Strings.Backups_Restore);
            else
                UI.Warning(Strings.Backups_RestoredLeftover + "\n\n" +
                           $"[grey]{Markup.Escape(leftover)}[/]\n\n" +
                           Strings.Backups_DeleteYourself, Strings.Backups_Restore);

            Console.ReadKey();
        }

        private static async Task DeleteBackup(Guid guid)
        {
            if (!BackupsFolderExists)
            {
                UI.Warning(Strings.Backups_NoneAvailable, Strings.Backups_Delete);
                Console.ReadKey();
                return;
            }

            var orderedBackups = GetBackupStamps(guid);

            if (orderedBackups.Count == 0)
            {
                UI.Warning(Strings.Backups_NoneToDelete, Strings.Backups_Delete);
                Console.ReadKey();
                return;
            }

            string projectName;

            if (ConfigurationHandler.GetConfig().Projects.TryGetValue(guid, out var project))
                projectName = project.HelyxName;
            else
            {
                UI.Error(ProjectMissingMessage, Strings.Backups_Delete);
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
            layout["Title"].Update(new Rule($"[blue bold]{Strings.Backups_Header}[/]").LeftJustified());

            int pageSize = Math.Max(3, Console.WindowHeight - headerHeight - 10);
            int selectedIndex = 0;

            int totalPages = (int)Math.Ceiling(orderedBackups.Count / (double)pageSize);

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    bool running = true;

                    while (running)
                    {
                        int currentPage = selectedIndex / pageSize;
                        int firstRow = currentPage * pageSize;
                        int lastRow = Math.Min(firstRow + pageSize, orderedBackups.Count);

                        var table = new Table()
                            .Border(TableBorder.Rounded)
                            .BorderColor(Color.Grey)
                            .Expand();

                        table.AddColumns(" ", Strings.Backups_Col_Project, Strings.Backups_Col_Date);

                        for (int i = firstRow; i < lastRow; i++)
                        {
                            var date = DateTime.ParseExact(orderedBackups[i], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                            .ToLocalTime();

                            table.AddRow(
                                i == selectedIndex ? "[SpringGreen2_1]>[/]" : " ",
                                $"[DarkOrange3]{Markup.Escape(projectName)}[/]",
                                $"[CadetBlue]{date}[/]"
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
                                    $"[grey]{string.Format(Strings.Common_Page, currentPage + 1, totalPages, selectedIndex + 1, orderedBackups.Count)}[/]"))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        ctx.Refresh();

                        switch (Console.ReadKey(true).Key)
                        {
                            case ConsoleKey.UpArrow:
                                selectedIndex = selectedIndex == 0
                                    ? orderedBackups.Count - 1
                                    : selectedIndex - 1;
                                break;

                            case ConsoleKey.DownArrow:
                                selectedIndex = selectedIndex == orderedBackups.Count - 1
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

            var selectedBackup = orderedBackups[selectedIndex];

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(Strings.Common_Continue)
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            Exception? err = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Strings.Backups_Deleting, async ctx =>
                {
                    try
                    {
                        File.Delete(BackupPath(guid, selectedBackup));
                    }
                    catch (Exception ex)
                    {
                        err = ex;
                    }
                });

            if (err != null)
            {
                UI.Error(Markup.Escape(err.Message), Strings.Backups_DeleteFailed_Title);
                Console.ReadKey();
                return;
            }

            UI.Success(Strings.Backups_Deleted, Strings.Backups_Delete);
            Console.ReadKey();
        }

        internal static void DeleteAllBackups(Guid guid)
        {
            foreach (var backup in GetBackupStamps(guid))
            {
                try
                {
                    File.Delete(BackupPath(guid, backup));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        private enum Action
        {
            CreateBackup,
            RestoreBackup,
            DeleteBackup,
            Back
        }

        private enum BackupScope
        {
            SkipIgnored,
            Everything
        }
    }
}
