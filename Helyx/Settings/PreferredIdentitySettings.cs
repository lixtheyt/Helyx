using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class PreferredIdentitySettings
    {
        internal static void Display()
        {
            if (!GitHubCalls.IsAuthorizedWithGitHub())
            {
                UI.Error(Strings.Identity_NeedsAuth);
                Console.ReadKey();
                AnsiConsole.Clear();
                return;
            }

            UI.Info(ConfigurationHandler.GetConfig().PreferredIdentity.ToString(), Strings.Identity_Current);

            var choices = Enum.GetValues<PreferredIdentity>()
                .Cast<PreferredIdentity?>()
                .Append(null);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<PreferredIdentity?>()
                .Title(Strings.Identity_Select)
                .AddChoices(choices)
                .UseConverter(x => x?.ToString() ?? $"[{Color.Red3_1}]{Strings.Common_Back}[/]"));

            AnsiConsole.Clear();

            if (choice == null)
                return;

            ConfigurationHandler.Update(x => x.PreferredIdentity = (PreferredIdentity)choice);
        }
    }
}
