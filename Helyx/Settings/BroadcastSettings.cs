using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class BroadcastSettings
    {
        internal static void Display()
        {
            UI.Info(Strings.Broadcast_Explain + "\n\n" +
                    (ConfigurationHandler.GetConfig().HideBroadcasts
                        ? $"[{Color.Red}]{Strings.Common_Disabled}[/]"
                        : $"[{Color.Green}]{Strings.Common_Enabled}[/]"),
                Strings.Settings_Broadcasts);

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

            if (ConfigurationHandler.Update(x => x.HideBroadcasts = !enable))
            {
                UI.Success(enable
                    ? $"[{Color.Green}]{Strings.Common_Enabled}[/]"
                    : $"[{Color.Red}]{Strings.Common_Disabled}[/]", Strings.Settings_Broadcasts);

                Console.ReadKey();
            }

            AnsiConsole.Clear();
        }
    }
}
