using Helyx.Shared;
using Helyx.Projects.Scripts;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using static Helyx.Data.ConfigurationHandler;

namespace Helyx.Projects
{
    internal static class OtherActions
    {
        public static void Display(Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                    .Title(Strings.Other_Title)
                    .AddChoices(Enum.GetValues<Action>())
                    .UseConverter(x => x switch
                    {
                        Action.Notes => Strings.Other_Notes,
                        Action.OpenIn => Strings.Other_OpenIn,
                        Action.UserScripts => Strings.Other_UserScripts,
                        Action.BackupProject => Strings.Other_BackupProject,
                        Action.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    }));

                switch (choice)
                {
                    case Action.OpenIn:
                        OpenIn(guid);
                        break;
                    case Action.UserScripts:
                        UserScripts.Display(guid);
                        break;
                    case Action.BackupProject:
                        Backups.Display(guid);
                        break;
                    case Action.Notes:
                        Notes(guid);
                        break;
                    case Action.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void OpenIn(Guid guid)
        {
            var project = GetProject(guid);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<OpenInAction>()
                .Title(Strings.Other_OpenIn)
                .AddChoices(Enum.GetValues<OpenInAction>())
                .UseConverter(x => x switch
                {
                    OpenInAction.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                    _ => x.ToString()
                })
            );

            string fileName;
            string? workingDir = null;

            switch (choice)
            {
                case OpenInAction.Explorer:
                    fileName = project.Path;
                    break;
                case OpenInAction.Cmd:
                    fileName = "cmd";
                    workingDir = project.Path;
                    break;
                case OpenInAction.PowerShell:
                    fileName = "powershell";
                    workingDir = project.Path;
                    break;
                case OpenInAction.Back:
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            try
            {
                if (workingDir == null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = fileName,
                        WorkingDirectory = workingDir,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                UI.Error(Markup.Escape(ex.Message));
                Console.ReadKey();
            }
        }

        private static void Notes(Guid guid)
        {
            AnsiConsole.Clear();
            ProjectsMenu.PrintHeader(guid);

            var project = GetProject(guid);

            var plain = string.Empty;

            if (!string.IsNullOrEmpty(project.Notes) && !TryReadNotes(project.Notes, out plain))
            {
                UI.Error(
                    Strings.Notes_ForeignAccount + "\n\n" +
                    $"[grey]{Strings.Notes_EditorStaysClosed}[/]",
                    Strings.Other_Notes);
                Console.ReadKey();
                return;
            }

            StringBuilder text = new StringBuilder(plain);

            UI.EditText(text);

            var notes = text.ToString();

            if (GetConfig().NotesEncryption && notes.Length > 0)
            {
                var protectedNotes = ProtectNotes(notes);

                if (protectedNotes == null)
                {
                    UI.Warning(
                        Strings.Notes_ProtectFailed,
                        Strings.Other_Notes);
                    Console.ReadKey();
                }
                else
                    notes = protectedNotes;
            }

            UpdateProject(guid, x => x.Notes = notes);
        }

        private enum OpenInAction
        {
            Explorer,
            Cmd,
            PowerShell,
            Back
        }

        private enum Action
        {
            OpenIn,
            UserScripts,
            BackupProject,
            Notes,
            Back
        }
    }
}
