using System.Globalization;
using Helyx.Data;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Helyx.Shared
{
    internal static class UI
    {
        internal static void Error(string message, string? title = null) => WritePanel(message, title ?? Strings.Common_Error, UIKind.Error);
        internal static void Success(string message, string? title = null) => WritePanel(message, title ?? Strings.Common_Success, UIKind.Success);
        internal static void Warning(string message, string? title = null) => WritePanel(message, title ?? Strings.Common_Warning, UIKind.Warning);
        internal static void Info(string message, string title) => WritePanel(message, title, UIKind.Info);

        internal static Color GetColor(string language) => language.ToLowerInvariant() switch
        {
            "c#" => Color.FromHex("#178600"),
            "c++" => Color.FromHex("#f34b7d"),
            "c" => Color.FromHex("#555555"),

            "python" => Color.FromHex("#3572A5"),
            "java" => Color.FromHex("#b07219"),
            "javascript" => Color.FromHex("#f1e05a"),
            "typescript" => Color.FromHex("#3178c6"),

            "go" => Color.FromHex("#00ADD8"),
            "rust" => Color.FromHex("#dea584"),
            "ruby" => Color.FromHex("#701516"),
            "php" => Color.FromHex("#4F5D95"),
            "swift" => Color.FromHex("#F05138"),
            "kotlin" => Color.FromHex("#A97BFF"),

            "html" => Color.FromHex("#e34c26"),
            "css" => Color.FromHex("#563d7c"),
            "scss" => Color.FromHex("#c6538c"),

            "shell" => Color.FromHex("#89e051"),
            "powershell" => Color.FromHex("#012456"),

            "sql" => Color.FromHex("#e38c00"),

            "dart" => Color.FromHex("#00B4AB"),
            "lua" => Color.FromHex("#000080"),
            "r" => Color.FromHex("#198CE7"),

            "scala" => Color.FromHex("#c22d40"),
            "haskell" => Color.FromHex("#5e5086"),
            "elixir" => Color.FromHex("#6e4a7e"),
            "erlang" => Color.FromHex("#B83998"),

            "objective-c" => Color.FromHex("#438eff"),
            "assembly" => Color.FromHex("#6E4C13"),

            "dockerfile" => Color.FromHex("#384d54"),
            "makefile" => Color.FromHex("#427819"),

            "yaml" => Color.FromHex("#cb171e"),
            "json" => Color.FromHex("#292929"),
            "xml" => Color.FromHex("#0060ac"),
            "toml" => Color.FromHex("#9c4221"),

            "markdown" => Color.FromHex("#083fa1"),

            "vue" => Color.FromHex("#41b883"),
            "svelte" => Color.FromHex("#ff3e00"),

            "jupyter notebook" => Color.FromHex("#DA5B0B"),

            _ => Color.Grey,
        };

        internal static string ConfirmName(Confirm value) =>
            value == Confirm.Yes ? Strings.Common_Yes : Strings.Common_No;

        internal static void Box(IRenderable content, string title, UIKind kind = UIKind.Info)
        {
            FlushInput();
            AnsiConsole.Write(StyledPanel(content, title, kind));
            AnsiConsole.WriteLine();
        }

        internal static void FlushInput()
        {
            if (Console.IsInputRedirected)
                return;

            while (Console.KeyAvailable)
                Console.ReadKey(true);
        }

        internal static void EditText(StringBuilder text)
        {
            var crlf = text.ToString().Contains("\r\n");

            text.Replace("\r\n", "\n");

            var cursor = text.Length;
            var desiredColumn = -1;
            var scrollRow = 0;

            Console.CursorVisible = false;

            try
            {
                while (true)
                {
                    var width = Math.Max(20, Console.WindowWidth);
                    var height = Math.Max(3, Console.WindowHeight);
                    var visible = height - 1;

                    var lines = text.ToString().Split('\n');

                    var numberWidth = lines.Length.ToString().Length;
                    var gutter = numberWidth + 3;
                    var body = Math.Max(1, width - gutter);

                    var starts = new int[lines.Length];

                    for (var i = 1; i < lines.Length; i++)
                        starts[i] = starts[i - 1] + lines[i - 1].Length + 1;

                    var rows = new List<(int Line, int Start, int Length)>();

                    for (var i = 0; i < lines.Length; i++)
                        for (var start = 0; ; start += body)
                        {
                            rows.Add((i, start, Math.Min(body, lines[i].Length - start)));

                            if (start + body > lines[i].Length)
                                break;
                        }

                    var line = text.ToString(0, cursor).Count(x => x == '\n');
                    var column = cursor - starts[line];

                    var cursorRow = rows.FindIndex(x => x.Line == line && column >= x.Start && column < x.Start + x.Length);

                    if (cursorRow < 0)
                        cursorRow = rows.FindLastIndex(x => x.Line == line);

                    var cursorColumn = column - rows[cursorRow].Start;

                    if (cursorRow < scrollRow)
                        scrollRow = cursorRow;

                    if (cursorRow >= scrollRow + visible)
                        scrollRow = cursorRow - visible + 1;

                    scrollRow = Math.Max(0, Math.Min(scrollRow, rows.Count - visible));

                    var screen = new StringBuilder();

                    for (var i = 0; i < visible; i++)
                    {
                        var row = scrollRow + i;

                        if (row >= rows.Count)
                        {
                            screen.Append(' ', width);
                            continue;
                        }

                        var head = rows[row].Start == 0
                            ? (rows[row].Line + 1).ToString().PadLeft(numberWidth)
                            : new string(' ', numberWidth);

                        screen.Append(($"{head} │ " + lines[rows[row].Line]
                            .Substring(rows[row].Start, rows[row].Length)
                            .Replace('\t', ' ')).PadRight(width)[..width]);
                    }

                    Console.SetCursorPosition(0, 0);
                    Console.Write(screen.ToString());

                    var status = $" Ln {line + 1}/{lines.Length}, Col {column + 1}   <esc> save & exit   ↑↓/←→ move   <pgup/pgdn> page   <f1> redraw ";

                    Console.SetCursorPosition(0, height - 1);
                    Console.Write(status.Length >= width ? status[..(width - 1)] : status.PadRight(width - 1));

                    Console.SetCursorPosition(Math.Min(width - 1, gutter + cursorColumn), cursorRow - scrollRow);
                    Console.CursorVisible = true;

                    var key = Console.ReadKey(true);

                    Console.CursorVisible = false;

                    if (key.Key is not (ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown))
                        desiredColumn = -1;

                    switch (key.Key)
                    {
                        case ConsoleKey.Escape:
                            return;

                        case ConsoleKey.F1:
                            Console.Clear();
                            break;

                        case ConsoleKey.LeftArrow:
                            cursor = Math.Max(0, cursor - 1);
                            break;

                        case ConsoleKey.RightArrow:
                            cursor = Math.Min(text.Length, cursor + 1);
                            break;

                        case ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown:
                            {
                                var target = key.Key switch
                                {
                                    ConsoleKey.UpArrow => cursorRow - 1,
                                    ConsoleKey.DownArrow => cursorRow + 1,
                                    ConsoleKey.PageUp => cursorRow - visible,
                                    _ => cursorRow + visible
                                };

                                target = Math.Clamp(target, 0, rows.Count - 1);

                                if (desiredColumn < 0)
                                    desiredColumn = cursorColumn;

                                var last = target == rows.Count - 1 || rows[target + 1].Line != rows[target].Line;

                                cursor = starts[rows[target].Line] + rows[target].Start +
                                         Math.Min(desiredColumn, Math.Max(0, rows[target].Length - (last ? 0 : 1)));
                                break;
                            }

                        case ConsoleKey.Home:
                            cursor = starts[line];
                            break;

                        case ConsoleKey.End:
                            cursor = starts[line] + lines[line].Length;
                            break;

                        case ConsoleKey.Enter:
                            text.Insert(cursor, '\n');
                            cursor++;
                            break;

                        case ConsoleKey.Backspace when cursor > 0:
                            text.Remove(cursor - 1, 1);
                            cursor--;
                            break;

                        case ConsoleKey.Delete when cursor < text.Length:
                            text.Remove(cursor, 1);
                            break;

                        default:
                            if (char.IsControl(key.KeyChar))
                                break;

                            text.Insert(cursor, key.KeyChar);
                            cursor++;
                            break;
                    }
                }
            }
            finally
            {
                if (crlf)
                    text.Replace("\n", "\r\n");

                Console.CursorVisible = true;
                Console.Clear();
            }
        }

        private static readonly MarkdownPipeline MarkdownPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        private static readonly Regex MentionPattern =
            new(@"(?<![\w@/-])@([A-Za-z\d](?:[A-Za-z\d]|-(?=[A-Za-z\d])){0,38})", RegexOptions.Compiled);

        private static readonly Regex ReferencePattern =
            new(@"(?<![\w#])#(\d+)\b", RegexOptions.Compiled);

        internal static string MarkdownToMarkup(string? markdown, Guid guid)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return $"[italic grey]{Strings.GH_NoDescription}[/]";

            var username = GitHubCalls.GetCachedUsername().GetAwaiter().GetResult();
            var repoName = ConfigurationHandler.GetProject(guid).GitHubName;

            var repoUrl = string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(repoName)
                ? null
                : $"https://github.com/{username}/{repoName}";

            return RenderBlock(Markdown.Parse(markdown.Replace("\r\n", "\n"), MarkdownPipeline), repoUrl).Trim('\n');
        }

        private static string RenderBlock(Block block, string? repoUrl)
        {
            switch (block)
            {
                case MarkdownDocument or ListItemBlock:
                    return string.Join("", ((ContainerBlock)block).Select(x => RenderBlock(x, repoUrl)));

                case HeadingBlock heading:
                    return Wrap(RenderInline(heading.Inline, repoUrl), "bold underline") + "\n\n";

                case ParagraphBlock paragraph:
                    return RenderInline(paragraph.Inline, repoUrl) + "\n\n";

                case QuoteBlock quote:
                    return Prefix(string.Join("", quote.Select(x => RenderBlock(x, repoUrl))).TrimEnd('\n'), "[grey]│[/] ") + "\n\n";

                case ListBlock list:
                    return string.Join("", list.Select((x, i) =>
                        Prefix(RenderBlock(x, repoUrl).TrimEnd('\n'), list.IsOrdered ? $"  {i + 1}. " : "  • ", "     ") + "\n")) + "\n";

                case CodeBlock code:
                    return Prefix(Markup.Escape(code.Lines.ToString()), "[grey50]▏[/] ") + "\n\n";

                case ThematicBreakBlock:
                    return "[grey]────────────[/]\n\n";

                default:
                    return "";
            }
        }

        internal static string Link(string? url, string text) =>
            string.IsNullOrEmpty(url) || url.Any(x => x is '[' or ']' || char.IsWhiteSpace(x))
                ? text
                : Wrap(text, $"link={url}");

        private static string Wrap(string text, string style) =>
            string.Join("\n", text.Split('\n').Select(x => $"[{style}]{x}[/]"));

        private static string Prefix(string text, string first, string? rest = null) =>
            string.Join("\n", text.Split('\n').Select((x, i) => (i == 0 ? first : rest ?? first) + x));

        private static string RenderInline(ContainerInline? container, string? repoUrl)
        {
            if (container == null)
                return "";

            var builder = new StringBuilder();

            foreach (var inline in container)
                builder.Append(inline switch
                {
                    LiteralInline literal => Linkify(Markup.Escape(literal.Content.ToString()), repoUrl),
                    CodeInline code => Wrap(Markup.Escape(code.Content ?? ""), "grey93 on grey19"),
                    EmphasisInline emphasis =>
                        Wrap(RenderInline(emphasis, repoUrl), (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
                        {
                            ('~', _) => "strikethrough",
                            (_, 2) => "bold",
                            _ => "italic"
                        }),
                    LinkInline { IsImage: true } image => $"[grey](image: {Markup.Escape(image.Url ?? "")})[/]",
                    LinkInline link => Link(link.Url, RenderInline(link, repoUrl)),
                    TaskList task => task.Checked ? "[green]☑[/]" : "☐",
                    AutolinkInline autolink => Link(autolink.Url, Markup.Escape(autolink.Url ?? "")),
                    LineBreakInline => "\n",
                    _ => ""
                });

            return builder.ToString();
        }

        private static string Linkify(string text, string? repoUrl)
        {
            text = MentionPattern.Replace(text,
                x => Link($"https://github.com/{x.Groups[1].Value}", $"[SteelBlue1]@{x.Groups[1].Value}[/]"));

            return repoUrl == null
                ? text
                : ReferencePattern.Replace(text,
                    x => Link($"{repoUrl}/issues/{x.Groups[1].Value}", $"[SteelBlue1]#{x.Groups[1].Value}[/]"));
        }

        internal static IRenderable SafeMarkup(string message, Style? style = null)
        {
            try
            {
                return new Markup(message, style);
            }
            catch (InvalidOperationException)
            {
                return new Text(message);
            }
        }

        private static void WritePanel(string message, string title, UIKind kind)
        {
            FlushInput();
            AnsiConsole.Write(StyledPanel(SafeMarkup(message, new Style(Color.White)), title, kind));
            AnsiConsole.WriteLine();
        }

        internal static Panel StyledPanel(IRenderable content, string title, UIKind kind)
        {
            var (headerColor, borderColor) = Palette(kind);

            var panel = new Panel(content)
                .RoundedBorder()
                .BorderColor(borderColor)
                .Padding(1, 1)
                .Expand();

            if (!string.IsNullOrWhiteSpace(title))
                panel.Header($"[{headerColor} bold] {Markup.Escape(title)} [/]");

            return panel;
        }

        private static (string headerColor, Color borderColor) Palette(UIKind kind) => kind switch
        {
            UIKind.Error => ("red", Color.Red),
            UIKind.Success => ("green", Color.Green),
            UIKind.Warning => ("orange1", Color.Orange1),
            _ => ("blue", Color.Grey),
        };

        internal static CultureInfo Culture(this Language language) => new(language switch
        {
            Language.French => "fr",
            Language.German => "de",
            Language.Italian => "it",
            Language.Portuguese => "pt",
            Language.Russian => "ru",
            Language.Slovak => "sk",
            Language.Spanish => "es",
            _ => "en"
        });
    }
}
