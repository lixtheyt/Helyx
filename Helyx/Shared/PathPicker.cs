using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Helyx.Shared
{
    internal static class PathPicker
    {
        private static readonly string[] QtDialogs = ["kdialog", "qarma", "zenity", "yad", "matedialog"];

        private static readonly string[] GtkDialogs = ["zenity", "yad", "matedialog", "qarma", "kdialog"];

        private static readonly Dictionary<string, string[]> DesktopDialogs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["KDE"] = QtDialogs,
            ["plasma"] = QtDialogs,
            ["plasmawayland"] = QtDialogs,
            ["plasmax11"] = QtDialogs,
            ["LXQt"] = QtDialogs,
            ["Razor"] = QtDialogs,
            ["Deepin"] = QtDialogs,
            ["DDE"] = QtDialogs,
            ["UKUI"] = QtDialogs,
            ["Lumina"] = QtDialogs,
            ["Cutefish"] = QtDialogs,
            ["Lingmo"] = QtDialogs,
            ["Trinity"] = QtDialogs,
            ["TDE"] = QtDialogs,
            ["Liri"] = QtDialogs,
            ["theDesk"] = QtDialogs,
            ["GNOME"] = GtkDialogs,
            ["GNOME-Classic"] = GtkDialogs,
            ["GNOME-Flashback"] = GtkDialogs,
            ["ubuntu"] = GtkDialogs,
            ["Unity"] = GtkDialogs,
            ["Unity7"] = GtkDialogs,
            ["X-Cinnamon"] = GtkDialogs,
            ["Cinnamon"] = GtkDialogs,
            ["MATE"] = GtkDialogs,
            ["XFCE"] = GtkDialogs,
            ["xubuntu"] = GtkDialogs,
            ["LXDE"] = GtkDialogs,
            ["Pantheon"] = GtkDialogs,
            ["Budgie"] = GtkDialogs,
            ["COSMIC"] = GtkDialogs,
            ["pop"] = GtkDialogs,
            ["Enlightenment"] = GtkDialogs,
            ["Moksha"] = GtkDialogs,
            ["Phosh"] = GtkDialogs,
            ["ROX"] = GtkDialogs,
            ["sway"] = GtkDialogs,
            ["Hyprland"] = GtkDialogs,
            ["niri"] = GtkDialogs,
            ["river"] = GtkDialogs,
            ["wlroots"] = GtkDialogs,
            ["i3"] = GtkDialogs,
            ["bspwm"] = GtkDialogs,
            ["awesome"] = GtkDialogs,
            ["qtile"] = GtkDialogs,
            ["dwm"] = GtkDialogs,
            ["Openbox"] = GtkDialogs,
            ["Fluxbox"] = GtkDialogs,
            ["IceWM"] = GtkDialogs
        };

        private static readonly Dictionary<string, (string[] Folder, string[] File)> DialogArguments = new()
        {
            ["kdialog"] = (["--getexistingdirectory", "{0}"], ["--getopenfilename", "{0}"]),
            ["qarma"] = (["--file-selection", "--directory", "--filename={0}/"], ["--file-selection", "--filename={0}/"]),
            ["zenity"] = (["--file-selection", "--directory", "--filename={0}/"], ["--file-selection", "--filename={0}/"]),
            ["yad"] = (["--file", "--directory", "--filename={0}/"], ["--file", "--filename={0}/"]),
            ["matedialog"] = (["--file-selection", "--directory", "--filename={0}/"], ["--file-selection", "--filename={0}/"])
        };

        private static readonly Regex KeyPattern = new(@"<[^>\s]+>|[↑↓←→]+", RegexOptions.Compiled);

        internal static string? Pick(string title, bool files)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var attempts = new List<(string Command, string[] Arguments)>();

            if (OperatingSystem.IsMacOS())
            {
                var prompt = title.Replace("\\", "\\\\").Replace("\"", "\\\"");

                attempts.Add(("osascript",
                [
                    "-e",
                    files
                        ? $"POSIX path of (choose file with prompt \"{prompt}\" default location (path to applications folder))"
                        : $"POSIX path of (choose folder with prompt \"{prompt}\")"
                ]));
            }
            else if (OperatingSystem.IsLinux() &&
                     new[] { "WAYLAND_DISPLAY", "DISPLAY" }.Any(x => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(x))))
            {
                var dialogs = new[] { "XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP", "DESKTOP_SESSION" }
                    .SelectMany(x => (Environment.GetEnvironmentVariable(x) ?? string.Empty)
                        .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Select(x => Path.GetFileName(x))
                    .Append(Environment.GetEnvironmentVariable("KDE_FULL_SESSION") == "true" ? "KDE" : string.Empty)
                    .Where(DesktopDialogs.ContainsKey)
                    .Select(x => DesktopDialogs[x])
                    .FirstOrDefault() ?? GtkDialogs;

                attempts.AddRange(dialogs.Select(x => (x,
                    (files ? DialogArguments[x].File : DialogArguments[x].Folder)
                        .Select(y => string.Format(y, home))
                        .Append($"--title={title}")
                        .ToArray())));
            }

            foreach (var attempt in attempts)
            {
                try
                {
                    var info = new ProcessStartInfo
                    {
                        FileName = attempt.Command,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    foreach (var argument in attempt.Arguments)
                        info.ArgumentList.Add(argument);

                    using var process = Process.Start(info);

                    if (process == null)
                        continue;

                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();

                    Task.WaitAll(output, error);

                    process.WaitForExit();

                    var picked = output.Result.Trim('\r', '\n');

                    if (process.ExitCode == 0 && picked.Length > 0)
                        return picked.Length > 1 ? picked.TrimEnd('/') : picked;

                    if (process.ExitCode is 1 or 252 && (!OperatingSystem.IsMacOS() || error.Result.Contains("-128")))
                        return null;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                }
            }

            if (Console.IsInputRedirected)
                return null;

            var places = new[]
                {
                    (Label: Strings.Picker_Home, Path: home),
                    (Label: Strings.Picker_Desktop, Path: Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                    (Label: Strings.Picker_Documents, Path: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                    (Label: Strings.Picker_Downloads, Path: Path.Combine(home, "Downloads"))
                }
                .Where(x => Directory.Exists(x.Path))
                .DistinctBy(x => x.Path)
                .ToList();

            var drivesFrom = places.Count;

            try
            {
                places.AddRange(DriveInfo.GetDrives()
                    .Where(x => x.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network && x.IsReady)
                    .Select(x => (Label: x.Name, Path: x.Name))
                    .Take(12));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            var placesWidth = Math.Clamp(
                places.Select(x => x.Label.Length).Append(Strings.Picker_Places.Length).Append(Strings.Picker_Drives.Length).Max() + 6,
                16,
                30);

            var path = Directory.Exists(home) ? home : Directory.GetCurrentDirectory();
            var history = new List<string> { path };
            var historyIndex = 0;

            var entries = new List<FileSystemInfo>();
            var reload = true;
            var unreadable = false;
            string? focus = null;

            var selectedIndex = 0;
            var placeIndex = 0;
            var inPlaces = false;

            var filter = string.Empty;
            var showHidden = false;
            var sort = Sort.Name;

            var input = Input.None;
            var text = string.Empty;
            var help = false;

            string? message = null;
            var failed = false;
            string? result = null;

            void Open(string target)
            {
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
                history.Add(target);
                historyIndex++;

                path = target;
                selectedIndex = 0;
                filter = string.Empty;
                reload = true;
            }

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Top").Size(3),
                    new Layout("Body")
                        .SplitColumns(
                            new Layout("Places").Size(placesWidth),
                            new Layout("List")),
                    new Layout("Footer").Size(4));

            AnsiConsole.Clear();

            AnsiConsole.Live(layout)
                .Start(ctx =>
                {
                    bool running = true;

                    while (running)
                    {
                        var width = Math.Max(60, Console.WindowWidth);
                        var pageSize = Math.Max(3, Console.WindowHeight - 12);

                        if (reload)
                        {
                            try
                            {
                                entries = new DirectoryInfo(path).EnumerateFileSystemInfos().ToList();
                                unreadable = false;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                            {
                                entries = [];
                                unreadable = true;
                            }

                            reload = false;
                        }

                        var grouped = entries
                            .Where(x => showHidden || !x.Attributes.HasFlag(FileAttributes.Hidden))
                            .Where(x => x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(x => x is not DirectoryInfo);

                        var visible = (sort switch
                            {
                                Sort.Modified => grouped.ThenByDescending(x => x.LastWriteTime),
                                Sort.Size => grouped.ThenByDescending(x => (x as FileInfo)?.Length ?? 0),
                                _ => grouped
                            })
                            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (focus != null)
                        {
                            selectedIndex = visible.FindIndex(x => x.Name == focus);
                            focus = null;
                        }

                        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, visible.Count - 1));

                        int totalPages = Math.Max(1, (int)Math.Ceiling(visible.Count / (double)pageSize));
                        int currentPage = selectedIndex / pageSize;
                        int firstRow = currentPage * pageSize;
                        int lastRow = Math.Min(firstRow + pageSize, visible.Count);

                        var nameLabel = Strings.Picker_Col_Name + (sort == Sort.Name ? " ▲" : string.Empty);
                        var dateLabel = Strings.Picker_Col_Modified + (sort == Sort.Modified ? " ▼" : string.Empty);
                        var sizeLabel = Strings.Picker_Col_Size + (sort == Sort.Size ? " ▼" : string.Empty);

                        var dateWidth = Math.Max(16, Strings.Picker_Col_Modified.Length + 2);
                        var sizeWidth = Math.Max(9, Strings.Picker_Col_Size.Length + 2);

                        var inner = width - placesWidth - 4;
                        var dated = inner - sizeWidth - dateWidth - 6 >= 16;
                        var nameWidth = Math.Max(8, inner - sizeWidth - 4 - (dated ? dateWidth + 2 : 0));

                        var root = Path.GetPathRoot(path) ?? string.Empty;
                        var segments = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).ToList();
                        var total = segments.Count;

                        while (segments.Count > 1 && root.Length + segments.Sum(x => x.Length + 3) > inner - 10)
                            segments.RemoveAt(0);

                        var crumb = $"[{Color.Grey}] {Markup.Escape(root)} {(segments.Count < total ? "… › " : string.Empty)}" +
                                    $"{Markup.Escape(string.Join(" › ", segments.SkipLast(1)))}{(segments.Count > 1 ? " › " : string.Empty)}[/]" +
                                    $"[bold]{Markup.Escape(segments.LastOrDefault() ?? string.Empty)} [/]";

                        var detail = $"[{Color.Grey}]{Markup.Escape(path.Length > inner ? "…" + path[^(inner - 1)..] : path)}[/]";

                        var lines = new List<string>();

                        if (help)
                        {
                            var keys = new[]
                            {
                                (Key: "↑↓", Text: Strings.Picker_Help_Move),
                                (Key: "<pgup/pgdn>", Text: Strings.Picker_Help_Page),
                                (Key: "←→", Text: Strings.Picker_Help_History),
                                (Key: "<tab>", Text: Strings.Picker_Help_Pane),
                                (Key: "<enter>", Text: Strings.Picker_Help_Open),
                                (Key: "<backspace>", Text: Strings.Picker_Help_Parent),
                                (Key: "</>", Text: Strings.Picker_Help_Filter),
                                (Key: "<p>", Text: Strings.Picker_Help_Path),
                                (Key: "<n>", Text: Strings.Picker_Help_NewFolder),
                                (Key: "<s>", Text: Strings.Picker_Help_Sort),
                                (Key: "<.>", Text: Strings.Picker_Help_Hidden),
                                (Key: "<f2>", Text: Strings.Picker_Help_Select),
                                (Key: "<esc>", Text: Strings.Picker_Help_Back)
                            };

                            var keyWidth = keys.Max(x => x.Key.Length) + 2;

                            lines.AddRange(keys
                                .Take(pageSize + 1)
                                .Select(x => $"[{Color.White}]{x.Key.PadRight(keyWidth)}[/][{Color.Grey}]{x.Text}[/]"));
                        }
                        else
                        {
                            lines.Add($"[{Color.Grey}]  {nameLabel.PadRight(nameWidth)}" +
                                      $"{(dated ? "  " + dateLabel.PadLeft(dateWidth) : string.Empty)}  {sizeLabel.PadLeft(sizeWidth)}[/]");

                            if (visible.Count == 0)
                                lines.Add($"  [{Color.Grey}]{(unreadable ? Strings.Picker_Unreadable : filter.Length > 0 ? Strings.Picker_NoMatch : Strings.Picker_Empty)}[/]");

                            for (int i = firstRow; i < lastRow; i++)
                            {
                                var entry = visible[i];
                                var folder = entry is DirectoryInfo;
                                var current = i == selectedIndex && !inPlaces;

                                var name = entry.Name + (folder ? Path.DirectorySeparatorChar.ToString() : string.Empty);
                                var shown = name.Length > nameWidth ? name[..(nameWidth - 1)] + "…" : name.PadRight(nameWidth);
                                var color = folder ? Color.LightSkyBlue1 : files ? Color.White : Color.Grey;
                                var date = entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm");

                                var size = entry is FileInfo file
                                    ? file.Length switch
                                    {
                                        < 1024 => $"{file.Length} B",
                                        < 1024 * 1024 => $"{file.Length / 1024.0:0.#} KB",
                                        < 1024 * 1024 * 1024 => $"{file.Length / (1024.0 * 1024):0.#} MB",
                                        _ => $"{file.Length / (1024.0 * 1024 * 1024):0.#} GB"
                                    }
                                    : Path.Exists(Path.Combine(entry.FullName, ".git")) ? "git" : string.Empty;

                                lines.Add($"{(current ? $"[{Color.SpringGreen1}]>[/]" : " ")} " +
                                          $"[{(current ? "bold " : string.Empty)}{color}]{Markup.Escape(shown)}[/]" +
                                          $"{(dated ? $"  [{Color.CadetBlue}]{date.PadLeft(dateWidth)}[/]" : string.Empty)}" +
                                          $"  [{(folder ? Color.Green : Color.Grey)}]{size.PadLeft(sizeWidth)}[/]");

                                if (i == selectedIndex)
                                    detail = $"[{color}]{Markup.Escape(shown.TrimEnd())}[/]  [{Color.Grey}]" +
                                             $"{(size == "git" ? Strings.Picker_Type_Repo : folder ? Strings.Picker_Type_Folder : Strings.Picker_Type_File)}" +
                                             $"{(folder ? string.Empty : $"   ·   {size}")}   ·   {date}[/]";
                            }
                        }

                        while (lines.Count < pageSize + 1)
                            lines.Add(" ");

                        var placeLines = new List<string>();

                        for (int i = 0; i < places.Count; i++)
                        {
                            if (i == drivesFrom)
                                placeLines.Add($"[{Color.Grey}]{Strings.Picker_Drives}[/]");

                            var label = places[i].Label.Length > placesWidth - 6
                                ? places[i].Label[..(placesWidth - 7)] + "…"
                                : places[i].Label;

                            placeLines.Add($"{(inPlaces && i == placeIndex ? $"[{Color.SpringGreen1}]>[/]" : " ")} " +
                                           $"[{(places[i].Path == path ? Color.LightSkyBlue1 : Color.Grey)}]{Markup.Escape(label)}[/]");
                        }

                        var asked = input switch
                        {
                            Input.Filter => Strings.Picker_Ask_Filter,
                            Input.Path => Strings.Common_Path,
                            _ => Strings.Picker_Ask_NewFolder
                        };

                        var state = string.Join("   ·   ", new[]
                        {
                            string.Format(Strings.Picker_Sort, sort switch
                            {
                                Sort.Modified => Strings.Picker_Col_Modified,
                                Sort.Size => Strings.Picker_Col_Size,
                                _ => Strings.Picker_Col_Name
                            }),
                            showHidden ? Strings.Picker_HiddenShown : string.Empty,
                            filter.Length > 0 ? $"{Strings.Picker_Ask_Filter}: {filter}" : string.Empty
                        }.Where(x => x.Length > 0));

                        layout["Top"].Update(new Panel(
                            new Grid()
                                .AddColumn()
                                .AddColumn(new GridColumn().RightAligned())
                                .Expand()
                                .AddRow(
                                    input != Input.None
                                        ? $"[{Color.Blue}]{asked}:[/] {Markup.Escape(text.Length > width - 30 ? "…" + text[^(width - 31)..] : text)}[{Color.Grey}]_[/]"
                                        : message != null
                                            ? $"[{(failed ? Color.Red : Color.Green)}]{message}[/]"
                                            : $"[{(historyIndex > 0 ? Color.White : Color.Grey35)}]←[/] " +
                                              $"[{(historyIndex < history.Count - 1 ? Color.White : Color.Grey35)}]→[/]  [bold]{Markup.Escape(title)}[/]",
                                    input != Input.None ? string.Empty : $"[{Color.Grey}]{Markup.Escape(state)}[/]"))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        layout["Places"].Update(new Panel(new Markup(placeLines.Count == 0 ? " " : string.Join("\n", placeLines)))
                            .Header($"[bold {Color.Blue}] {Strings.Picker_Places} [/]")
                            .RoundedBorder()
                            .BorderColor(inPlaces ? Color.Blue : Color.Grey)
                            .Expand()
                            .Padding(1, 0));

                        layout["List"].Update(new Panel(new Rows(
                                new Markup(string.Join("\n", lines)),
                                new Rule().RuleStyle(new Style(Color.Grey)),
                                new Markup(detail)))
                            .Header(help ? $"[bold {Color.Blue}] {Strings.Picker_Help_Title} [/]" : crumb)
                            .RoundedBorder()
                            .BorderColor(inPlaces ? Color.Grey : Color.Blue)
                            .Expand()
                            .Padding(1, 0));

                        layout["Footer"].Update(new Panel(new Rows(
                                new Grid()
                                    .AddColumn()
                                    .AddColumn(new GridColumn().RightAligned())
                                    .Expand()
                                    .AddRow(
                                        $"[{Color.Grey}]{KeyPattern.Replace(help ? Strings.Picker_Footer_Help : input != Input.None ? Strings.Picker_Footer_Input : Strings.Picker_Footer_List, x => $"[{Color.White}]{x.Value}[/]")}[/]",
                                        help
                                            ? string.Empty
                                            : $"[{Color.Grey}]{string.Format(Strings.Common_Page, currentPage + 1, totalPages, visible.Count == 0 ? 0 : selectedIndex + 1, visible.Count)}[/]"),
                                new Markup(help || input != Input.None
                                    ? " "
                                    : $"[{Color.Grey}]{KeyPattern.Replace(Strings.Picker_Footer_Actions, x => $"[{Color.White}]{x.Value}[/]")}[/]")))
                            .RoundedBorder()
                            .Expand()
                            .Padding(1, 0));

                        ctx.Refresh();

                        var key = Console.ReadKey(true);

                        message = null;

                        if (input != Input.None)
                        {
                            switch (key.Key)
                            {
                                case ConsoleKey.Escape:
                                    if (input == Input.Filter)
                                        filter = string.Empty;

                                    input = Input.None;
                                    break;

                                case ConsoleKey.Backspace when text.Length > 0:
                                    text = text[..^1];
                                    break;

                                case ConsoleKey.Enter when input == Input.Filter:
                                    filter = text;
                                    input = Input.None;
                                    break;

                                case ConsoleKey.Enter when input == Input.Folder:
                                    var created = text.Trim();

                                    if (created.Length == 0 ||
                                        created.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                                        Path.Exists(Path.Combine(path, created)))
                                    {
                                        message = Strings.Picker_NameInvalid;
                                        failed = true;
                                    }
                                    else
                                    {
                                        try
                                        {
                                            Directory.CreateDirectory(Path.Combine(path, created));

                                            message = Strings.Picker_FolderCreated;
                                            failed = false;
                                            focus = created;
                                            filter = string.Empty;
                                            reload = true;
                                        }
                                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                        {
                                            message = Markup.Escape(ex.Message);
                                            failed = true;
                                        }
                                    }

                                    input = Input.None;
                                    break;

                                case ConsoleKey.Enter:
                                    var typed = text.Trim();

                                    if (typed == "~" || typed.StartsWith("~/") || typed.StartsWith("~\\"))
                                        typed = home + typed[1..];

                                    var target = string.Empty;

                                    try
                                    {
                                        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(typed, path));
                                    }
                                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
                                    {
                                    }

                                    if (Directory.Exists(target))
                                        Open(target);
                                    else if (files && File.Exists(target))
                                    {
                                        result = target;
                                        running = false;
                                    }
                                    else if (files && typed.Length > 0 &&
                                             typed.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ' ']) < 0 &&
                                             Shell.IsOnPath(typed))
                                    {
                                        result = typed;
                                        running = false;
                                    }
                                    else
                                    {
                                        message = Strings.Picker_PathMissing;
                                        failed = true;
                                    }

                                    input = Input.None;
                                    break;

                                default:
                                    if (!char.IsControl(key.KeyChar))
                                        text += key.KeyChar;

                                    break;
                            }

                            if (input == Input.Filter)
                            {
                                filter = text;
                                selectedIndex = 0;
                            }
                        }
                        else if (help)
                        {
                            if (key.KeyChar == '?' || key.Key == ConsoleKey.Escape)
                                help = false;
                        }
                        else
                        {
                            switch (key)
                            {
                                case { KeyChar: '?' }:
                                    help = true;
                                    break;

                                case { KeyChar: '/' }:
                                    input = Input.Filter;
                                    text = filter;
                                    inPlaces = false;
                                    break;

                                case { KeyChar: '.' }:
                                    showHidden = !showHidden;
                                    break;

                                case { Key: ConsoleKey.P }:
                                    input = Input.Path;
                                    text = path;
                                    break;

                                case { Key: ConsoleKey.N }:
                                    input = Input.Folder;
                                    text = string.Empty;
                                    inPlaces = false;
                                    break;

                                case { Key: ConsoleKey.S }:
                                    sort = (Sort)(((int)sort + 1) % 3);
                                    break;

                                case { Key: ConsoleKey.Tab } when places.Count > 0:
                                    inPlaces = !inPlaces;
                                    break;

                                case { Key: ConsoleKey.UpArrow } when inPlaces:
                                    placeIndex = placeIndex == 0
                                        ? places.Count - 1
                                        : placeIndex - 1;
                                    break;

                                case { Key: ConsoleKey.DownArrow } when inPlaces:
                                    placeIndex = placeIndex == places.Count - 1
                                        ? 0
                                        : placeIndex + 1;
                                    break;

                                case { Key: ConsoleKey.UpArrow } when visible.Count > 0:
                                    selectedIndex = selectedIndex == 0
                                        ? visible.Count - 1
                                        : selectedIndex - 1;
                                    break;

                                case { Key: ConsoleKey.DownArrow } when visible.Count > 0:
                                    selectedIndex = selectedIndex == visible.Count - 1
                                        ? 0
                                        : selectedIndex + 1;
                                    break;

                                case { Key: ConsoleKey.PageUp }:
                                    selectedIndex = Math.Max(0, selectedIndex - pageSize);
                                    break;

                                case { Key: ConsoleKey.PageDown }:
                                    selectedIndex += pageSize;
                                    break;

                                case { Key: ConsoleKey.LeftArrow } when historyIndex > 0:
                                    historyIndex--;
                                    path = history[historyIndex];
                                    selectedIndex = 0;
                                    filter = string.Empty;
                                    reload = true;
                                    break;

                                case { Key: ConsoleKey.RightArrow } when historyIndex < history.Count - 1:
                                    historyIndex++;
                                    path = history[historyIndex];
                                    selectedIndex = 0;
                                    filter = string.Empty;
                                    reload = true;
                                    break;

                                case { Key: ConsoleKey.Enter } when inPlaces:
                                    Open(places[placeIndex].Path);
                                    inPlaces = false;
                                    break;

                                case { Key: ConsoleKey.Enter } when visible.Count > 0 && visible[selectedIndex] is DirectoryInfo:
                                    Open(visible[selectedIndex].FullName);
                                    break;

                                case { Key: ConsoleKey.Enter } when visible.Count > 0 && files:
                                    result = visible[selectedIndex].FullName;
                                    running = false;
                                    break;

                                case { Key: ConsoleKey.Enter } when visible.Count > 0:
                                    message = Strings.Picker_OnlyFolders;
                                    failed = true;
                                    break;

                                case { Key: ConsoleKey.Backspace } when Path.GetDirectoryName(path) is { } parent:
                                    focus = Path.GetFileName(path);
                                    Open(parent);
                                    break;

                                case { Key: ConsoleKey.F2 } when !files:
                                    result = path;
                                    running = false;
                                    break;

                                case { Key: ConsoleKey.F2 } when visible.Count > 0 &&
                                                                 (visible[selectedIndex] is FileInfo || visible[selectedIndex].Name.EndsWith(".app")):
                                    result = visible[selectedIndex].FullName;
                                    running = false;
                                    break;

                                case { Key: ConsoleKey.Escape } when filter.Length > 0:
                                    filter = string.Empty;
                                    break;

                                case { Key: ConsoleKey.Escape }:
                                    running = false;
                                    break;
                            }
                        }

                        UI.FlushInput();
                    }
                });

            AnsiConsole.Clear();

            return result;
        }

        private enum Input
        {
            None,
            Filter,
            Path,
            Folder
        }

        private enum Sort
        {
            Name,
            Modified,
            Size
        }
    }
}
