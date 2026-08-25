using Helyx.Data;
using Helyx.Projects;
using Helyx.Shared;
using LibGit2Sharp;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace Helyx.Settings
{
    internal static class ManageCustomStatusesSettings
    {
        internal static void Display()
        {
            while (true)
            {
                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                        .Title($"[blue]{Strings.Settings_ManageCustomStatuses}[/]")
                        .AddChoices(Enum.GetValues<Action>())
                        .UseConverter(a => a switch
                        {
                            Action.AddCustomStatus => Strings.Statuses_Add,
                            Action.EditCustomStatus => Strings.Statuses_Edit,
                            Action.DeleteCustomStatus => Strings.Statuses_Delete,
                            Action.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => a.ToString()
                        }));
                switch (action)
                {
                    case Action.AddCustomStatus:
                        AddCustomStatus();
                        break;
                    case Action.EditCustomStatus:
                        EditCustomStatus();
                        break;
                    case Action.DeleteCustomStatus:
                        DeleteCustomStatus();
                        break;
                    case Action.Back:
                        AnsiConsole.Clear();
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                AnsiConsole.Clear();
            }
        }

        private static void AddCustomStatus()
        {
            var name = AnsiConsole.Prompt(
                    new TextPrompt<string>(Strings.Statuses_EnterName)
                        .AllowEmpty())
                .Trim();

            AnsiConsole.Clear();

            if (string.IsNullOrEmpty(name))
                return;

            if (!Tags.IsValidName(name, out var reason))
            {
                UI.Error(reason, Strings.Common_InvalidName);
                Console.ReadKey();
                return;
            }

            if (Tags.AllStatuses().NameExists(name))
            {
                UI.Error(string.Format(Strings.Statuses_NameExists, $"'{Markup.Escape(name)}'"), Strings.Common_NameTaken);
                Console.ReadKey();
                return;
            }

            var color = Tags.PickColor();

            if (color == null)
                return;

            if (!ConfigurationHandler.Update(x => x.CustomStatuses.Add(Guid.NewGuid(), new TagDefinition
            {
                Name = name,
                Hex = ((Color)color).ToHex()
            })))
                return;

            UI.Success(string.Format(Strings.Statuses_Added, $"'[{color}]{name}[/]'"));
            Console.ReadKey();
        }

        private static void EditCustomStatus()
        {
            var config = ConfigurationHandler.GetConfig();

            if (config.CustomStatuses.Count == 0)
            {
                UI.Info(Strings.Statuses_NoneToEdit, Strings.Statuses_None_Title);
                Console.ReadKey();
                return;
            }

            var choice1 = AnsiConsole.Prompt(
                new SelectionPrompt<Guid?>()
                .Title(Strings.Statuses_SelectToEdit)
                .AddChoices(config.CustomStatuses.Keys.Cast<Guid?>().Append(null))
                .UseConverter(x => x switch
                {
                    null => $"[Red3_1]{Strings.Common_Back}[/]",
                    _ => Tags.Markup(config.CustomStatuses[(Guid)x], Markup.Escape(config.CustomStatuses[(Guid)x].Name))
                })
            );

            if (choice1 == null)
                return;

            var statusGuid = (Guid)choice1;
            var edited = false;

            while (true)
            {
                if (!ConfigurationHandler.GetConfig().CustomStatuses.TryGetValue(statusGuid, out var status))
                    return;

                var choice2 = AnsiConsole.Prompt(
                    new SelectionPrompt<EditTagAction>()
                    .Title(Strings.Common_SelectEditAction)
                    .AddChoices(Enum.GetValues<EditTagAction>())
                    .UseConverter(x => x switch
                    {
                        EditTagAction.EditName => Strings.Common_EditName,
                        EditTagAction.EditColor => Strings.Common_EditColor,
                        EditTagAction.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    })
                );

                switch (choice2)
                {
                    case EditTagAction.EditName:
                        var newName = AnsiConsole.Ask(Strings.Common_EnterNewName, status.Name)
                            .Trim();

                        AnsiConsole.Clear();

                        if (!Tags.IsValidName(newName, out var reason))
                        {
                            UI.Error(reason, Strings.Common_InvalidName);
                            Console.ReadKey();
                            AnsiConsole.Clear();
                            continue;
                        }

                        if (Tags.AllStatuses().NameExists(newName, statusGuid))
                        {
                            UI.Error(string.Format(Strings.Statuses_NameExists, $"'{Markup.Escape(newName)}'"), Strings.Common_NameTaken);
                            Console.ReadKey();
                            AnsiConsole.Clear();
                            continue;
                        }

                        if (!ConfigurationHandler.Update(x =>
                            {
                                if (x.CustomStatuses.TryGetValue(statusGuid, out var target))
                                    target.Name = newName;
                            }))
                            return;

                        break;
                    case EditTagAction.EditColor:
                        var picked = Tags.PickColor(Color.FromHex(Tags.SafeHex(status.Hex)));

                        if (picked == null)
                            continue;

                        if (!ConfigurationHandler.Update(x =>
                            {
                                if (x.CustomStatuses.TryGetValue(statusGuid, out var target))
                                    target.Hex = ((Color)picked).ToHex();
                            }))
                            return;

                        break;
                    case EditTagAction.Back:
                        if (!edited)
                            return;

                        foreach (var projectGuid in GitHubActions.ConfirmSync(
                                     ConfigurationHandler.GetConfig().Projects.Values.Where(x => x.Status == statusGuid),
                                     GitHubSync.SyncStatusWithGitHubRepo,
                                     Strings.Common_ChangeInHelyxOnly))
                            GitHubActions.SyncStatus(projectGuid, statusGuid, false);

                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                edited = true;

                AnsiConsole.Clear();

                if (!ConfigurationHandler.GetConfig().CustomStatuses.TryGetValue(statusGuid, out var saved))
                    return;

                UI.Success(string.Format(Strings.Statuses_Edited, $"'{Tags.Markup(saved, Markup.Escape(saved.Name))}'"));
                Console.ReadKey();

                AnsiConsole.Clear();
            }
        }

        private static void DeleteCustomStatus()
        {
            var config = ConfigurationHandler.GetConfig();

            if (config.CustomStatuses.Count == 0)
            {
                UI.Info(Strings.Statuses_NoneToDelete, Strings.Statuses_None_Title);
                Console.ReadKey();
                return;
            }

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Guid?>()
                .Title(Strings.Statuses_SelectToDelete)
                .AddChoices(config.CustomStatuses.Keys.Cast<Guid?>().Append(null))
                .UseConverter(x => x switch
                {
                    null => $"[Red3_1]{Strings.Common_Back}[/]",
                    _ => Tags.Markup(config.CustomStatuses[(Guid)x], Markup.Escape(config.CustomStatuses[(Guid)x].Name))
                })
            );

            if (choice == null)
                return;

            var statusGuid = (Guid)choice;
            var status = config.CustomStatuses[statusGuid];
            var label = Tags.Markup(status, Markup.Escape(status.Name));

            var usedBy = config.Projects.Values.Where(x => x.Status == statusGuid).ToList();

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                .Title(string.Format(Strings.Statuses_DeleteConfirm, $"'{label}'") +
                       (usedBy.Count > 0
                           ? $"\n[grey]{string.Format(Strings.Statuses_UsedBy, usedBy.Count)}[/]"
                           : string.Empty))
                .AddChoices(Enum.GetValues<Confirm>())
                .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            foreach (var projectGuid in GitHubActions.ConfirmSync(
                         usedBy,
                         GitHubSync.SyncStatusWithGitHubRepo,
                         Strings.Common_DeleteInHelyxOnly))
                GitHubActions.SyncStatus(projectGuid, BuiltInStatusIds.Active, false);

            if (!ConfigurationHandler.Update(x =>
                {
                    x.CustomStatuses.Remove(statusGuid);

                    foreach (var project in x.Projects.Values.Where(y => y.Status == statusGuid))
                        project.Status = BuiltInStatusIds.Active;
                }))
                return;

            var active = Tags.BuiltInStatuses[BuiltInStatusIds.Active];

            UI.Success(string.Format(Strings.Statuses_Deleted, $"'{label}'") +
                       (usedBy.Count > 0
                           ? "\n" + string.Format(Strings.Statuses_ResetToActive, $"'{Tags.Markup(active, active.Name)}'")
                           : string.Empty));
            Console.ReadKey();
        }

        private enum Action
        {
            AddCustomStatus,
            EditCustomStatus,
            DeleteCustomStatus,
            Back
        }
    }
}
