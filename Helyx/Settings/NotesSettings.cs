using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class NotesSettings
    {
        internal static void Display()
        {
            while (true)
            {
                AnsiConsole.Clear();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                    .Title(Strings.Common_SelectAction)
                    .AddChoices(Enum.GetValues<Action>())
                    .UseConverter(x => x switch
                    {
                        Action.EncryptNotes => Strings.Notes_Encrypt,
                        Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    })
                );

                switch (choice)
                {
                    case Action.EncryptNotes:
                        EncryptNotes();
                        break;
                    case Action.Back:
                        return;
                }
            }
        }

        private static void EncryptNotes()
        {
            UI.Info(ConfigurationHandler.GetConfig().NotesEncryption
                ? $"[{Color.Green}]{Strings.Common_Enabled}[/]"
                : $"[{Color.Red}]{Strings.Common_Disabled}[/]"
                , Strings.Notes_State_Title);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<bool?>()
                .AddChoices(true, false, null)
                .UseConverter(x => x switch
                {
                    true => Strings.Common_Enable,
                    false => Strings.Common_Disable,
                    null => $"[{Color.Red3_1}]{Strings.Common_Back}[/]"
                })
            );

            AnsiConsole.Clear();

            if (choice == null)
                return;

            var enable = (bool)choice;
            var converted = 0;

            List<string> unreadable = [];
            List<string> failed = [];

            var saved = ConfigurationHandler.Update(x =>
            {
                x.NotesEncryption = enable;

                foreach (var project in x.Projects.Values)
                {
                    if (string.IsNullOrEmpty(project.Notes))
                        continue;

                    if (!ConfigurationHandler.TryReadNotes(project.Notes, out var plain))
                    {
                        unreadable.Add(project.HelyxName);
                        continue;
                    }

                    var wasEncrypted = plain != project.Notes;

                    if (wasEncrypted == enable)
                        continue;

                    var stored = enable ? ConfigurationHandler.ProtectNotes(plain) : plain;

                    if (stored == null)
                    {
                        failed.Add(project.HelyxName);
                        continue;
                    }

                    project.Notes = stored;
                    converted++;
                }
            });

            if (!saved)
                return;

            var summary = (enable ? $"[{Color.Green}]{Strings.Notes_Result_Enabled}[/]" : $"[{Color.Red}]{Strings.Notes_Result_Disabled}[/]") + "\n" +
                          string.Format(enable ? Strings.Notes_Converted_Encrypted : Strings.Notes_Converted_Decrypted, converted);

            if (unreadable.Count == 0 && failed.Count == 0)
                UI.Success(summary, Strings.Notes_Title);
            else
                UI.Warning(summary +
                           (unreadable.Count == 0
                               ? string.Empty
                               : "\n\n" + Strings.Notes_Unreadable + "\n" +
                                 string.Join("\n", unreadable.Select(y => $"[{Color.Grey}]{Markup.Escape(y)}[/]"))) +
                           (failed.Count == 0
                               ? string.Empty
                               : "\n\n" + Strings.Notes_Failed + "\n" +
                                 string.Join("\n", failed.Select(y => $"[{Color.Grey}]{Markup.Escape(y)}[/]"))),
                    Strings.Notes_Title);

            Console.ReadKey();
        }

        private enum Action
        {
            EncryptNotes,
            Back
        }
    }
}
