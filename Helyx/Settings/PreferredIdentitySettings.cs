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

            var config = ConfigurationHandler.GetConfig();

            UI.Info(config.PreferredIdentity == PreferredIdentity.GitHub
                ? $"GitHub\n[{Color.Grey}]{(config.CommitEmail == CommitEmail.Primary ? Strings.Identity_Email_Primary : Strings.Identity_Email_Private)}[/]"
                : config.PreferredIdentity.ToString(), Strings.Identity_Current);

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

            if (choice == PreferredIdentity.Git)
            {
                ConfigurationHandler.Update(x => x.PreferredIdentity = PreferredIdentity.Git);
                return;
            }

            var email = AnsiConsole.Prompt(
                new SelectionPrompt<CommitEmail?>()
                .Title(Strings.Identity_Email_Select)
                .AddChoices(CommitEmail.Private, CommitEmail.Primary, null)
                .UseConverter(x => x switch
                {
                    CommitEmail.Private => Strings.Identity_Email_Private,
                    CommitEmail.Primary => Strings.Identity_Email_Primary,
                    _ => $"[{Color.Red3_1}]{Strings.Common_Back}[/]"
                }));

            AnsiConsole.Clear();

            if (email == null)
                return;

            if (!ConfigurationHandler.Update(x =>
                {
                    x.PreferredIdentity = PreferredIdentity.GitHub;
                    x.CommitEmail = (CommitEmail)email;
                }))
                return;

            if (email == CommitEmail.Primary)
            {
                UI.Warning(Strings.Identity_Email_PrimaryWarning, Strings.Settings_PreferredIdentity);
                Console.ReadKey();
                AnsiConsole.Clear();
            }
        }
    }
}
