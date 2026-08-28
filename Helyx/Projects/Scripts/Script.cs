using Spectre.Console;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Helyx.Projects.Scripts
{
    internal sealed class Script(string scriptName)
    {
        public Guid ScriptGuid { get; set; } = Guid.NewGuid();

        public string ScriptName { get; set; } = scriptName;

        public List<Block> Blocks { get; set; } = [];

        [JsonIgnore]
        public Channel<string> Logs { get; set; } = Channel.CreateUnbounded<string>();

        public sealed class BlockResult
        {
            public object? Value { get; set; }
            public bool End { get; set; }
        }

        internal sealed class Block
        {
            public Guid BlockGuid { get; set; } = Guid.NewGuid();

            public IAction Action { get; set; } = null!;

            public interface IAction
            {
                string Name { get; }
                string MarkupName { get; }

                void Configure(Script script);

                Task<BlockResult> Execute(Script script);
            }

            #region Action guards
            private static string RequirePath(string? path, string what)
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException(string.Format(Strings.Script_NothingConfigured, what));

                try
                {
                    return System.IO.Path.GetFullPath(path);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    throw new InvalidOperationException(string.Format(Strings.Script_InvalidPath, $"'{path}'", what), ex);
                }
            }

            private static string RequireDeletableFolder(string? path)
            {
                var full = RequirePath(path, Strings.Script_Folder).TrimEnd(System.IO.Path.DirectorySeparatorChar);

                var segments = full.Split(
                    System.IO.Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length < 3)
                    throw new InvalidOperationException(
                        string.Format(Strings.Script_TooCloseToRoot, $"'{full}'"));

                return full;
            }
            #endregion

            #region Actions
            public sealed class WaitAction : IAction
            {
                public string Name => Strings.Script_Wait;
                public string MarkupName => $"[Orange1]{Strings.Script_Wait}[/]";

                private const double MaxDurationSeconds = 86_400;

                public double Duration { get; set; }

                public void Configure(Script script)
                {
                    Duration = AnsiConsole.Prompt(
                        new TextPrompt<double>(Strings.Script_EnterDuration)
                            .Validate(x => x is >= 0 and <= MaxDurationSeconds
                                ? ValidationResult.Success()
                                : ValidationResult.Error($"[red]{string.Format(Strings.Script_DurationRange, MaxDurationSeconds)}[/]"))
                    );
                }

                public async Task<BlockResult> Execute(Script script)
                {
                    if (Duration is < 0 or > MaxDurationSeconds)
                        throw new InvalidOperationException(
                            string.Format(Strings.Script_DurationRangeError, MaxDurationSeconds));

                    await Task.Delay(TimeSpan.FromSeconds(Duration));

                    return new BlockResult();
                }
            }

            public sealed class LogAction : IAction
            {
                public string Name => Strings.Script_Log;
                public string MarkupName => $"[yellow]{Strings.Script_Log}[/]";

                public string Message { get; set; } = "";

                public void Configure(Script script)
                {
                    Message = AnsiConsole.Ask<string>(Strings.Script_EnterLogMessage);
                }

                public Task<BlockResult> Execute(Script script)
                {
                    script.Logs.Writer.TryWrite($"[grey]<{DateTime.Now.ToString("G", CultureInfo.CurrentCulture)}>:[/] {Markup.Escape(Message)}");

                    return Task.FromResult(
                        new BlockResult()
                    );
                }
            }

            public sealed class ExecuteAction : IAction
            {
                public string Name => Strings.Script_Execute;
                public string MarkupName => $"[deepskyblue1]{Strings.Script_Execute}[/]";

                public string Command { get; set; } = "";

                public void Configure(Script script)
                {
                    Command = AnsiConsole.Ask<string>(Strings.Script_EnterCommand);
                }

                public async Task<BlockResult> Execute(Script script)
                {
                    if (string.IsNullOrWhiteSpace(Command))
                        throw new InvalidOperationException(Strings.Script_NoCommand);

                    async Task Pump(StreamReader reader, bool error)
                    {
                        while (await reader.ReadLineAsync() is { } line)
                            script.Logs.Writer.TryWrite($"[deepskyblue1]│[/] " + (error
                                ? $"[Red3_1]{Markup.Escape(line)}[/]"
                                : $"[grey]{Markup.Escape(line)}[/]"));
                    }

                    script.Logs.Writer.TryWrite($"[deepskyblue1]│ $ {Markup.Escape(Command)}[/]");

                    try
                    {
                        using var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-NoProfile -NonInteractive -Command {Command}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }) ?? throw new InvalidOperationException(Strings.Script_NoCommand);

                        var draining = Task.WhenAll(
                            Pump(process.StandardOutput, false),
                            Pump(process.StandardError, true));

                        await process.WaitForExitAsync();
                        await draining;

                        script.Logs.Writer.TryWrite(process.ExitCode == 0
                            ? $"[deepskyblue1]╰─[/] [Green3_1]{Strings.Script_CommandDone}[/]"
                            : $"[deepskyblue1]╰─[/] [Red3_1]{string.Format(Strings.Script_ExitCode, process.ExitCode)}[/]");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format(Strings.Script_CommandFailed, ex.Message), ex);
                    }

                    return new BlockResult();
                }
            }

            public sealed class OpenAction : IAction
            {
                public string Name => Strings.Script_Open;
                public string MarkupName => $"[mediumpurple]{Strings.Script_Open}[/]";

                public string Path { get; set; } = "";

                public void Configure(Script script)
                {
                    Path = AnsiConsole.Ask<string>(Strings.Script_EnterPathOpen);
                }

                public Task<BlockResult> Execute(Script script)
                {
                    if (string.IsNullOrWhiteSpace(Path))
                        throw new InvalidOperationException(Strings.Script_NoPath);

                    try
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = Path,
                                UseShellExecute = true
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format(Strings.Script_OpenFailed, $"'{Path}'", ex.Message), ex);
                    }

                    return Task.FromResult(
                        new BlockResult()
                    );
                }
            }

            public sealed class CreateFileAction : IAction
            {
                public string Name => Strings.Script_CreateFile;
                public string MarkupName => $"[green]{Strings.Script_CreateFile}[/]";

                public string Path { get; set; } = "";

                public void Configure(Script script)
                {
                    Path = AnsiConsole.Ask<string>(Strings.Script_EnterPathNewFile);
                }

                public Task<BlockResult> Execute(Script script)
                {
                    var target = RequirePath(Path, Strings.Script_FilePath);

                    try
                    {
                        var parent = System.IO.Path.GetDirectoryName(target);

                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);

                        File.Create(target).Dispose();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format(Strings.Script_CreateFailed, $"'{target}'", ex.Message), ex);
                    }

                    return Task.FromResult(
                        new BlockResult()
                    );
                }
            }

            public sealed class CreateFolderAction : IAction
            {
                public string Name => Strings.Script_CreateFolder;
                public string MarkupName => $"[green]{Strings.Script_CreateFolder}[/]";

                public string Path { get; set; } = "";

                public void Configure(Script script)
                {
                    Path = AnsiConsole.Ask<string>(Strings.Script_EnterPathNewFolder);
                }

                public Task<BlockResult> Execute(Script script)
                {
                    var target = RequirePath(Path, Strings.Script_FolderPath);

                    try
                    {
                        Directory.CreateDirectory(target);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format(Strings.Script_CreateFailed, $"'{target}'", ex.Message), ex);
                    }

                    return Task.FromResult(
                        new BlockResult()
                    );
                }
            }

            public sealed class DeleteFileAction : IAction
            {
                public string Name => Strings.Script_DeleteFile;
                public string MarkupName => $"[red]{Strings.Script_DeleteFile}[/]";

                public string Path { get; set; } = "";

                public void Configure(Script script)
                {
                    Path = AnsiConsole.Ask<string>(Strings.Script_EnterPathDeleteFile);
                }

                public Task<BlockResult> Execute(Script script)
                {
                    var target = RequirePath(Path, Strings.Script_FilePath);

                    try
                    {
                        if (File.Exists(target))
                            File.Delete(target);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format(Strings.Script_DeleteFailed, $"'{target}'", ex.Message), ex);
                    }

                    return Task.FromResult(
                        new BlockResult()
                    );
                }
            }

            public sealed class DeleteFolderAction : IAction
            {
                public string Name => Strings.Script_DeleteFolder;
                public string MarkupName => $"[red]{Strings.Script_DeleteFolder}[/]";

                public string Path { get; set; } = "";

                public void Configure(Script script)
                {
                    Path = AnsiConsole.Ask<string>(Strings.Script_EnterPathDeleteFolder);
                }

                public Task<BlockResult> Execute(Script script)
                {
                    var target = RequireDeletableFolder(Path);

                    try
                    {
                        if (Directory.Exists(target))
                            Directory.Delete(target, true);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format(Strings.Script_DeleteFailed, $"'{target}'", ex.Message), ex);
                    }

                    return Task.FromResult(
                        new BlockResult()
                    );
                }
            }

            #endregion
        }

        public async Task Run()
        {
            try
            {
                for (int i = 0; i < Blocks.Count; i++)
                {
                    var block = Blocks[i];

                    BlockResult result;

                    try
                    {
                        result = await block.Action.Execute(this);
                    }
                    catch (Exception ex)
                    {
                        Logs.Writer.TryWrite(
                            $"[red]{string.Format(Strings.Script_BlockFailed, i + 1, Markup.Escape(block.Action?.Name ?? Strings.Common_Unknown))}[/] " +
                            Markup.Escape(ex.Message));

                        Logs.Writer.TryWrite($"[bold red]{Strings.Script_Stopped}[/]");
                        break;
                    }

                    if (result.End)
                        break;
                }
            }
            finally
            {
                Logs.Writer.Complete();
            }
        }
    }
}
