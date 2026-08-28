using Helyx.Data;
using Helyx.Projects;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class SettingsMenu
    {
        internal static void DisplaySettings()
        {
            while (true)
            {
                AnsiConsole.Write(new Rule($"[blue bold]{Strings.Common_Settings}[/]").LeftJustified());
                Console.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                        .PageSize(10)
                        .AddChoices(Enum.GetValues<Action>())
                        .UseConverter(action => action switch
                        {
                            Action.Language => Strings.Settings_Language,
                            Action.IDESettings => Strings.Settings_IDE,
                            Action.ManageCustomStatuses => Strings.Settings_ManageCustomStatuses,
                            Action.ManageBadges => Strings.Settings_ManageBadges,
                            Action.ManageConfigurationFile => Strings.Settings_ManageConfigurationFile,
                            Action.GitHubAuthorization => Strings.Settings_GitHubAuthorization,
                            Action.PreferredIdentity => Strings.Settings_PreferredIdentity,
                            Action.NotesSettings => Strings.Settings_Notes,
                            Action.Updates => Strings.Settings_Updates,
                            Action.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => action.ToString()
                        })
                );

                AnsiConsole.Clear();

                switch (choice)
                {
                    case Action.Language:
                        LanguageSettings.Display();
                        break;
                    case Action.IDESettings:
                        IDESettings.Display();
                        break;
                    case Action.ManageCustomStatuses:
                        ManageCustomStatusesSettings.Display();
                        break;
                    case Action.ManageBadges:
                        ManageBadgesSettings.Display();
                        break;
                    case Action.ManageConfigurationFile:
                        ConfigurationFileSettings.Display();
                        break;
                    case Action.GitHubAuthorization:
                        GitHubAuthorizationSettings.Display();
                        break;
                    case Action.PreferredIdentity:
                        PreferredIdentitySettings.Display();
                        break;
                    case Action.NotesSettings:
                        NotesSettings.Display();
                        break;
                    case Action.Updates:
                        UpdateHandler.Display();
                        break;
                    case Action.Back:
                        AnsiConsole.Clear();
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        internal static ConfigurationFile.IDEExecutablesClass.TypesOfFound IsFound(IDE ide)
        {
            var config = ConfigurationHandler.GetConfig();

            if (config.IDEExecutables.TryGetValue(ide, out var executable))
                return executable.FoundType;

            if (!IDEActions.ExecutableCommands.TryGetValue(ide, out var command))
                return ConfigurationFile.IDEExecutablesClass.TypesOfFound.NotFound;

            var where = Shell.IsOnPath(command);

            return where
                ? ConfigurationFile.IDEExecutablesClass.TypesOfFound.Found
                : ConfigurationFile.IDEExecutablesClass.TypesOfFound.NotFound;
        }

        private enum Action
        {
            Language,
            IDESettings,
            ManageCustomStatuses,
            ManageBadges,
            ManageConfigurationFile,
            GitHubAuthorization,
            PreferredIdentity,
            NotesSettings,
            Updates,
            Back
        }
    }
}
