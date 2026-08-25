using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class LanguageSettings
    {
        internal static void Display()
        {
            UI.Info(ConfigurationHandler.GetConfig().ProgramLanguage.ToString(), Strings.Language_Current);

            var languageChoices = Enum.GetValues<Language>()
                .Cast<Language?>()
                .Append(null)
                .ToArray();

            var selectedChoice = AnsiConsole.Prompt(
                new SelectionPrompt<Language?>()
                    .Title(Strings.Language_Select)
                    .AddChoices(languageChoices)
                    .UseConverter(choice => choice?.ToString() ?? $"[Red3_1]{Strings.Common_Back}[/]"));

            AnsiConsole.Clear();

            if (selectedChoice == null)
                return;

            if (!ConfigurationHandler.Update(x => x.ProgramLanguage = (Language)selectedChoice))
                return;

            Strings.Culture = ((Language)selectedChoice).Culture();

            AnsiConsole.MarkupLine($"[green]{Strings.Language_Saved}[/]" + Environment.NewLine);
            Console.ReadKey();
            AnsiConsole.Clear();
        }
    }
}
