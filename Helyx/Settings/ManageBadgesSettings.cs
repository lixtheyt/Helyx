using Helyx.Data;
using Helyx.Projects;
using Helyx.Shared;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace Helyx.Settings
{
    internal static class ManageBadgesSettings
    {
        internal static void Display()
        {
            while (true)
            {
                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                        .Title($"[{Color.Blue}]{Strings.Settings_ManageBadges}[/]")
                        .AddChoices(Enum.GetValues<Action>())
                        .UseConverter(a => a switch
                        {
                            Action.AddBadge => Strings.Badges_Add,
                            Action.EditBadge => Strings.Badges_Edit,
                            Action.DeleteBadge => Strings.Badges_Delete,
                            Action.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                            _ => a.ToString()
                        }));
                switch (action)
                {
                    case Action.AddBadge:
                        AddBadge();
                        break;
                    case Action.EditBadge:
                        EditBadge();
                        break;
                    case Action.DeleteBadge:
                        DeleteBadge();
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

        private static void AddBadge()
        {
            var name = AnsiConsole.Prompt(
                    new TextPrompt<string>(Strings.Badges_EnterName)
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

            if (Tags.AllBadges().NameExists(name))
            {
                UI.Error(string.Format(Strings.Badges_NameExists, $"'{Markup.Escape(name)}'"), Strings.Common_NameTaken);
                Console.ReadKey();
                return;
            }

            var color = Tags.PickColor();

            if (color == null)
                return;

            if (!ConfigurationHandler.Update(x => x.Badges.Add(Guid.NewGuid(), new TagDefinition
            {
                Name = name,
                Hex = ((Color)color).ToHex()
            })))
                return;

            UI.Success(string.Format(Strings.Badges_Added, $"'[{color}][[{name}]][/]'"));
            Console.ReadKey();
        }

        private static void EditBadge()
        {
            var config = ConfigurationHandler.GetConfig();

            if (config.Badges.Count == 0)
            {
                UI.Info(Strings.Badges_NoneToEdit, Strings.Badges_None_Title);
                Console.ReadKey();
                return;
            }

            var choice1 = AnsiConsole.Prompt(
                new SelectionPrompt<Guid?>()
                .Title(Strings.Badges_SelectToEdit)
                .AddChoices(config.Badges.Keys.Cast<Guid?>().Append(null))
                .UseConverter(x => x switch
                {
                    null => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                    _ => Tags.Markup(config.Badges[(Guid)x], $"[[{Markup.Escape(config.Badges[(Guid)x].Name)}]]")
                })
            );

            if (choice1 == null)
                return;

            var badgeGuid = (Guid)choice1;
            var edited = false;

            while (true)
            {
                if (!ConfigurationHandler.GetConfig().Badges.TryGetValue(badgeGuid, out var badge))
                    return;

                var choice2 = AnsiConsole.Prompt(
                    new SelectionPrompt<EditTagAction>()
                    .Title(Strings.Common_SelectEditAction)
                    .AddChoices(Enum.GetValues<EditTagAction>())
                    .UseConverter(x => x switch
                    {
                        EditTagAction.EditName => Strings.Common_EditName,
                        EditTagAction.EditColor => Strings.Common_EditColor,
                        EditTagAction.Back => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    })
                );

                switch (choice2)
                {
                    case EditTagAction.EditName:
                        var newName = AnsiConsole.Ask(Strings.Common_EnterNewName, badge.Name)
                            .Trim();

                        AnsiConsole.Clear();

                        if (!Tags.IsValidName(newName, out var reason))
                        {
                            UI.Error(reason, Strings.Common_InvalidName);
                            Console.ReadKey();
                            AnsiConsole.Clear();
                            continue;
                        }

                        if (ConfigurationHandler.GetConfig().Badges.NameExists(newName, badgeGuid))
                        {
                            UI.Error(string.Format(Strings.Badges_NameExists, $"'{Markup.Escape(newName)}'"), Strings.Common_NameTaken);
                            Console.ReadKey();
                            AnsiConsole.Clear();
                            continue;
                        }

                        if (!ConfigurationHandler.Update(x =>
                            {
                                if (x.Badges.TryGetValue(badgeGuid, out var target))
                                    target.Name = newName;
                            }))
                            return;

                        break;
                    case EditTagAction.EditColor:
                        var picked = Tags.PickColor(Color.FromHex(Tags.SafeHex(badge.Hex)));

                        if (picked == null)
                            continue;

                        if (!ConfigurationHandler.Update(x =>
                            {
                                if (x.Badges.TryGetValue(badgeGuid, out var target))
                                    target.Hex = ((Color)picked).ToHex();
                            }))
                            return;

                        break;
                    case EditTagAction.Back:
                        if (!edited)
                            return;

                        foreach (var projectGuid in GitHubActions.ConfirmSync(
                                     ConfigurationHandler.GetConfig().Projects.Values.Where(x => x.Badges.Contains(badgeGuid)),
                                     GitHubSync.SyncBadgesWithGitHubRepo,
                                     Strings.Common_ChangeInHelyxOnly))
                            GitHubActions.SyncBadges(projectGuid, null, false, [badgeGuid]);

                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                edited = true;

                AnsiConsole.Clear();

                if (!ConfigurationHandler.GetConfig().Badges.TryGetValue(badgeGuid, out var saved))
                    return;

                UI.Success(string.Format(Strings.Badges_Edited, $"'{Tags.Markup(saved, $"[[{Markup.Escape(saved.Name)}]]")}'"));
                Console.ReadKey();

                AnsiConsole.Clear();
            }
        }
        
        private static void DeleteBadge()
        {
            var config = ConfigurationHandler.GetConfig();

            if (config.Badges.Count == 0)
            {
                UI.Info(Strings.Badges_NoneToDelete, Strings.Badges_None_Title);
                Console.ReadKey();
                return;
            }

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Guid?>()
                    .Title(Strings.Badges_SelectToDelete)
                    .AddChoices(config.Badges.Keys.Cast<Guid?>().Append(null))
                    .UseConverter(x => x switch
                    {
                        null => $"[{Color.Red3_1}]{Strings.Common_Back}[/]",
                        _ => Tags.Markup(config.Badges[(Guid)x], $"[[{Markup.Escape(config.Badges[(Guid)x].Name)}]]")
                    })
                );

            if (choice == null)
                return;

            var badgeGuid = (Guid)choice;
            var badge = config.Badges[badgeGuid];
            var label = Tags.Markup(badge, $"[[{Markup.Escape(badge.Name)}]]");

            var assignedTo = config.Projects.Values.Count(x => x.Badges.Contains(badgeGuid));
            
            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                    .Title(string.Format(Strings.Badges_DeleteConfirm, $"'{label}'") +
                           (assignedTo > 0
                               ? $"\n[{Color.Grey}]{string.Format(Strings.Badges_RemovedFrom, assignedTo)}[/]"
                               : string.Empty))
                    .AddChoices(Enum.GetValues<Confirm>())
                    .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            foreach (var projectGuid in GitHubActions.ConfirmSync(
                         config.Projects.Values.Where(x => x.Badges.Contains(badgeGuid)),
                         GitHubSync.SyncBadgesWithGitHubRepo,
                         Strings.Common_DeleteInHelyxOnly))
                GitHubActions.SyncBadges(projectGuid, [badgeGuid], false, [badgeGuid]);

            if (!ConfigurationHandler.Update(x =>
                {
                    x.Badges.Remove(badgeGuid);

                    foreach (var project in x.Projects.Values)
                        project.Badges.Remove(badgeGuid);
                }))
                return;

            UI.Success(string.Format(Strings.Badges_Deleted, $"'{label}'"));
            Console.ReadKey();
        }

        private enum Action
        {
            AddBadge,
            EditBadge,
            DeleteBadge,
            Back
        }
    }
}
