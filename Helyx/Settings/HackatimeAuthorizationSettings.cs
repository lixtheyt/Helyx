using System;
using System.Collections.Generic;
using System.Text;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace Helyx.Settings
{
    internal static class HackatimeAuthorizationSettings
    {
        internal static void Display()
        {
            while (true)
            {
                var authorized = !string.IsNullOrEmpty(ConfigurationHandler.GetHackatimeToken());

                List<Action> choices = [];

                if (!authorized)
                {
                    choices.Add(Action.AuthorizeWithHackatime);

                    UI.Info($"[{Color.Red3_1}]✗[/] Not connected.", "Hackatime Authorization");
                }
                else
                {
                    choices.Add(Action.UnauthorizeFromHackatime);

                    var name = HackatimeCalls.GetUsername().GetAwaiter().GetResult();
                    var summary = HackatimeCalls.GetSummary().GetAwaiter().GetResult();

                    UI.Info((name == null
                        ? $"[{Color.Red3_1}]{Strings.Common_Unknown}[/]"
                        : Markup.Escape(name)) + (summary == null ? string.Empty : $"\n[{Color.Yellow}]{Markup.Escape(summary)}[/]"), "Hackatime Authorization");
                }

                choices.Add(Action.Back);

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                        .AddChoices(choices)
                        .UseConverter(x => x switch 
                        {
                            Action.AuthorizeWithHackatime => "Authorize with Hackatime",
                            Action.UnauthorizeFromHackatime => "Unauthorize from Hackatime",
                            Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                            _ => x.ToString()
                        }));

                AnsiConsole.Clear();
                
                switch (action)
                {
                    case Action.AuthorizeWithHackatime:
                        HackatimeCalls.AuthorizeHackatime().GetAwaiter().GetResult();
                        break;
                    case Action.UnauthorizeFromHackatime:
                        ConfigurationHandler.ForgetHackatimeToken();
                        HackatimeCalls.ForgetHackatimeUser();

                        UI.Success("Hackatime was successfully unauthorized!", "Hackatime Unauthorization");

                        Console.ReadKey();
                        break;
                    case Action.Back:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                AnsiConsole.Clear();
            }
        }

        private enum Action
        {
            AuthorizeWithHackatime,
            UnauthorizeFromHackatime,
            Back
        }
    }
}
