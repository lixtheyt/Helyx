using Color = Spectre.Console.Color;
using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class IDESettings
    {
        internal static void Display()
        {
            while (true)
            {
                var ides = Enum.GetValues<IDE>();

                var foundCache = ides.ToDictionary(ide => ide, SettingsMenu.IsFound);

                var ideChoices = ides
                    .Cast<IDE?>()
                    .Append(null)
                    .ToArray();

                UI.Info(ConfigurationHandler.GetConfig().DefaultIDE.ToString(), Strings.IDE_Default);

                var ideChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<IDE?>()
                        .Title($"[{Color.Blue}]{Strings.Settings_IDE}[/]")
                        .AddChoices(ideChoices)
                        .UseConverter(choice => choice == null
                            ? $"[{Color.Red3_1}]{Strings.Common_Back}[/]"
                            : choice + $" [[{foundCache[choice.Value] switch
                            {
                                ConfigurationFile.IDEExecutablesClass.TypesOfFound.Found => "[{Color.Green}]✓[/]",
                                ConfigurationFile.IDEExecutablesClass.TypesOfFound.NotFound => "[{Color.Red}]X[/]",
                                ConfigurationFile.IDEExecutablesClass.TypesOfFound.SetByUser => "[{Color.Cyan}]✓[/]",
                                _ => $"[{Color.Red}]{Strings.Common_Unknown}[/]"
                            }}]]"));

                if (ideChoice == null)
                {
                    AnsiConsole.Clear();
                    break;
                }

                var selectedIDE = ideChoice.Value;

                var statusGrid = new Grid()
                    .AddColumn(new GridColumn().NoWrap().PadRight(2))
                    .AddColumn();

                statusGrid.AddRow($"[bold]{Strings.Common_Found}[/]", foundCache[selectedIDE] switch
                {
                    ConfigurationFile.IDEExecutablesClass.TypesOfFound.Found => $"[{Color.Green}]{Strings.Common_Found}[/]",
                    ConfigurationFile.IDEExecutablesClass.TypesOfFound.NotFound => $"[{Color.Red}]{Strings.IDE_State_NotFound}[/]",
                    ConfigurationFile.IDEExecutablesClass.TypesOfFound.SetByUser => $"[{Color.Cyan}]{Strings.IDE_State_SetByUser}[/]",
                    _ => $"[{Color.Red}]{Strings.Common_Unknown}[/]"
                });

                if (foundCache[selectedIDE] == ConfigurationFile.IDEExecutablesClass.TypesOfFound.SetByUser
                    && ConfigurationHandler.GetConfig().IDEExecutables.TryGetValue(selectedIDE, out var executable))
                    statusGrid.AddRow($"[bold]{Strings.Common_Path}[/]", $"[{Color.Yellow1}]{Markup.Escape(executable.Path)}[/]");

                UI.Box(statusGrid, $"{selectedIDE}");

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                        .AddChoices(Enum.GetValues<Action>())
                        .UseConverter(a => a switch
                        {
                            Action.SetAsDefaultIDE => Strings.IDE_SetDefault,
                            Action.ChangeIDEPath => Strings.IDE_ChangePath,
                            Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                            _ => a.ToString()
                        })
                );

                switch (action)
                {
                    case Action.SetAsDefaultIDE:
                        if (!ConfigurationHandler.Update(x => x.DefaultIDE = selectedIDE))
                            return;

                        AnsiConsole.Clear();
                        AnsiConsole.MarkupLine($"[{Color.Green}]{string.Format(Strings.IDE_Default_Set, selectedIDE)}[/]\n");
                        break;
                    case Action.ChangeIDEPath:
                        var newExecutable = PickIDEExecutable();

                        if (!string.IsNullOrWhiteSpace(newExecutable))
                        {
                            if (!ConfigurationHandler.Update(x => x.IDEExecutables[selectedIDE] = new ConfigurationFile.IDEExecutablesClass
                            {
                                FoundType = ConfigurationFile.IDEExecutablesClass.TypesOfFound.SetByUser,
                                Path = newExecutable
                            }))
                                return;

                            AnsiConsole.Clear();
                            AnsiConsole.MarkupLine($"[{Color.Green}]{string.Format(Strings.IDE_Path_Changed, Markup.Escape(newExecutable))}[/]\n");
                        }
                        else
                        {
                            AnsiConsole.Clear();
                            AnsiConsole.MarkupLine($"[{Color.Red}]{Strings.IDE_Path_Invalid}[/]\n");
                        }

                        break;
                    case Action.Back:
                        AnsiConsole.Clear();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static string? PickIDEExecutable()
        {
            using var dialog = new OpenFileDialog
            {
                Title = Strings.IDE_Picker_Title,
                Filter = Strings.IDE_Picker_Filter,
                CheckFileExists = true,
                CheckPathExists = true
            };

            return dialog.ShowDialog() == DialogResult.OK
                ? dialog.FileName
                : null;
        }

        private enum Action
        {
            SetAsDefaultIDE,
            ChangeIDEPath,
            Back
        }
    }
}
