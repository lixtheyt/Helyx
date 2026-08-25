using Helyx.Shared;
using Helyx.Data;
using Spectre.Console;
using System.Diagnostics;
using static Helyx.Data.ConfigurationHandler;

namespace Helyx.Projects
{
    internal static class IDEActions
    {
        internal static readonly Dictionary<IDE, string> ExecutableCommands = new()
        {
            [IDE.VSCode] = "code.cmd",
            [IDE.VS] = "devenv",
            [IDE.Eclipse] = "eclipse",
            [IDE.Vim] = "vim",
            [IDE.Neovim] = "nvim",
            [IDE.Emacs] = "emacs",
            [IDE.SublimeText] = "subl",
            [IDE.PyCharm] = "pycharm",
            [IDE.CLion] = "clion",
            [IDE.IDEA] = "idea",
            [IDE.WebStorm] = "webstorm",
            [IDE.Rider] = "rider",
            [IDE.PhpStorm] = "phpstorm"
        };

        internal static void Display(Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                        .Title(Strings.IDE_Actions_Select)
                        .AddChoices(Enum.GetValues<Action>())
                        .UseConverter(x => x switch
                        {
                            Action.OpenInDefaultIDE => Strings.IDE_OpenInDefault,
                            Action.OpenInIDE => Strings.IDE_OpenIn,
                            Action.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => x.ToString()
                        }));

                switch (choice)
                {
                    case Action.OpenInDefaultIDE:
                        OpenInDefaultIDE(guid);
                        break;
                    case Action.OpenInIDE:
                        OpenInIDE(guid);
                        break;
                    case Action.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void LaunchIDE(IDE ide, Guid guid)
        {
            var project = GetProject(guid);

            if (!Directory.Exists(project.Path))
            {
                UI.Error(Strings.Common_ProjectFolderMissing + $"\n[grey]{Markup.Escape(project.Path)}[/]", Strings.IDE_OpenIn);
                Console.ReadKey();
                return;
            }

            var config = GetConfig();

            string? fileName = null;

            if (config.IDEExecutables.TryGetValue(ide, out var executable) &&
                executable.FoundType == ConfigurationFile.IDEExecutablesClass.TypesOfFound.SetByUser)
            {
                fileName = executable.Path;
            }
            else if (ExecutableCommands.TryGetValue(ide, out var command))
            {
                fileName = command;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                UI.Error(string.Format(Strings.IDE_NoExecutable, ide) + "\n" + Strings.IDE_SetOneHint, Strings.IDE_OpenIn);
                Console.ReadKey();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = $"\"{project.Path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UI.Error(string.Format(Strings.IDE_StartFailed, ide) + $"\n\n{Markup.Escape(ex.Message)}", Strings.IDE_OpenIn);
                Console.ReadKey();
            }
        }

        private static void OpenInDefaultIDE(Guid guid)
        {
            AnsiConsole.Clear();

            var ide = GetConfig().DefaultIDE;

            LaunchIDE(ide, guid);
        }

        private static void OpenInIDE(Guid guid)
        {
            var project = GetProject(guid);

            AnsiConsole.Clear();

            var ranked = IDESignatures.Identify(project.Path);

            IDE? nativeIde = ranked[0].Score > 0 ? ranked[0].Ide : null;

            AnsiConsole.Write(new Rule($"[blue bold]{string.Format(Strings.IDE_OpenTitle, Markup.Escape(project.HelyxName))}[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<IDE?>()
                    .Title(Strings.IDE_Select)
                    .AddChoices(ranked.Select(x => (IDE?)x.Ide).Append(null))
                    .UseConverter(x => x switch
                    {
                        null => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ when x == nativeIde => $"{x}[Orange1] {Strings.IDE_Recommended}[/]",
                        _ => x.ToString()
                    }));

            if (choice == null)
                return;

            var selectedIDE = (IDE)choice;

            LaunchIDE(selectedIDE, guid);

            Console.Clear();
        }

        private enum Action
        {
            OpenInDefaultIDE,
            OpenInIDE,
            Back
        }
    }
}
