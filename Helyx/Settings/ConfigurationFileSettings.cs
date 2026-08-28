using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;
using System.Diagnostics;

namespace Helyx.Settings
{
    internal static class ConfigurationFileSettings
    {
        internal static void Display()
        {
            while (true)
            {
                AnsiConsole.Clear();

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                        .Title($"[{Color.Blue}]{Strings.Settings_ManageConfigurationFile}[/]")
                        .AddChoices(Enum.GetValues<Action>())
                        .UseConverter(a => a switch
                        {
                            Action.ResetConfigurationFile => Strings.ConfigFile_Reset,
                            Action.OpenConfigurationFileDirectory => Strings.ConfigFile_OpenDirectory,
                            Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                            _ => a.ToString()
                        }));

                switch (action)
                {
                    case Action.ResetConfigurationFile:
                        ResetConfigFile();
                        break;
                    case Action.OpenConfigurationFileDirectory:
                        OpenConfigFileDir();
                        break;
                    case Action.Back:
                        AnsiConsole.Clear();
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void ResetConfigFile()
        {
            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title($"[{Color.Red}]{Strings.ConfigFile_Reset_Confirm}[/]")
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(c => c switch
                    {
                        Confirm.Yes => Strings.Common_Yes,
                        Confirm.No => Strings.Common_No,
                        _ => c.ToString()
                    }));

            if (confirm == Confirm.No)
                return;

            try
            {
                ConfigurationHandler.CreateConfig();
            }
            catch (Exception ex)
            {
                UI.Error(string.Format(Strings.ConfigFile_Reset_Failed, ex.Message));
                Console.ReadKey();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UI.Warning(string.Format(Strings.ConfigFile_Restart_Failed, ex.Message));
                Console.ReadKey();
            }

            Environment.Exit(0);
        }

        private static void OpenConfigFileDir()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(ConfigurationHandler.GetConfigPath()),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UI.Error(string.Format(Strings.ConfigFile_Open_Failed, ex.Message));
                Console.ReadKey();
            }
        }

        private enum Action
        {
            ResetConfigurationFile,
            OpenConfigurationFileDirectory,
            Back
        }
    }
}
