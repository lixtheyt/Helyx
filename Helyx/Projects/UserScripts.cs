using Helyx.Projects.Scripts;
using Helyx.Data;
using static Helyx.Projects.Scripts.Script.Block;
using Helyx.Shared;
using Spectre.Console;
using Spectre.Console.Rendering;
using Panel = Spectre.Console.Panel;
using TreeNode = Spectre.Console.TreeNode;

namespace Helyx.Projects
{
    internal static class UserScripts
    {
        internal static void Display(Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                ProjectsMenu.PrintHeader(guid);

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<Action>()
                    .AddChoices(Enum.GetValues<Action>())
                    .UseConverter(x => x switch
                    {
                        Action.RunScript => Strings.Scripts_Run,
                        Action.EditScript => Strings.Scripts_EditScript,
                        Action.CreateScript => Strings.Scripts_Create,
                        Action.DeleteScript => Strings.Scripts_DeleteScript,
                        Action.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => x.ToString()
                    }));

                switch (choice)
                {
                    case Action.RunScript:
                        RunScript(guid).GetAwaiter().GetResult();
                        break;

                    case Action.EditScript:
                        EditScript(guid);
                        break;

                    case Action.CreateScript:
                        CreateScript(guid);
                        break;

                    case Action.DeleteScript:
                        DeleteScript(guid);
                        break;

                    case Action.Back:
                        return;
                }
            }
        }

        private static async Task RunScript(Guid guid)
        {
            if (!ScriptSaving.CreateDirectory())
                return;

            var scripts = ScriptSaving.GetScripts(guid);

            if (scripts.Count == 0)
            {
                UI.Info(Strings.Scripts_None, Strings.Scripts_Run);
                Console.ReadKey();
                return;
            }

            var script = AnsiConsole.Prompt(
                new SelectionPrompt<Script?>()
                    .Title(Strings.Scripts_SelectToRun)
                    .AddChoices(scripts.Cast<Script?>().Append(null))
                    .UseConverter(x => x == null ? $"[Red3_1]{Strings.Common_Back}[/]" : Markup.Escape(x.ScriptName)));

            if (script == null)
                return;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.DotsCircle)
                .StartAsync(Strings.Scripts_Running, async ctx =>
                {
                    AnsiConsole.Write(
                        new Rule($"[bold Green3_1]{Strings.Scripts_Begun}[/]")
                        );

                    Task task = script.Run();

                    await foreach (var log in script.Logs.Reader.ReadAllAsync())
                    {
                        AnsiConsole.Write(UI.SafeMarkup(log));
                        AnsiConsole.WriteLine();
                    }

                    try
                    {
                        await task;
                    }
                    catch (Exception ex)
                    {
                        UI.Error(Markup.Escape(ex.Message), Strings.Scripts_Failed_Title);
                    }
                });

            AnsiConsole.Write(
                new Rule($"[bold Red3_1]{Strings.Scripts_Ended}[/]")
                );

            UI.FlushInput();

            Console.ReadKey();
        }

        private static void EditScript(Guid guid)
        {
            var scripts = ScriptSaving.GetScripts(guid);

            if (scripts.Count == 0)
            {
                UI.Info(Strings.Scripts_None, Strings.Scripts_EditScript);
                Console.ReadKey();
                return;
            }

            var script = AnsiConsole.Prompt(
                new SelectionPrompt<Script?>()
                .Title(Strings.Scripts_SelectToEdit)
                .AddChoices(scripts.Cast<Script?>().Append(null))
                .UseConverter(x => x == null ? $"[Red3_1]{Strings.Common_Back}[/]" : Markup.Escape(x.ScriptName)));

            if (script == null)
                return;

            ScriptPanel(script, guid);
        }

        private static void CreateScript(Guid guid)
        {
            if (!ScriptSaving.CreateDirectory())
                return;

            var scriptName = AnsiConsole.Prompt(
                new TextPrompt<string>(Strings.Scripts_EnterName)
                .AllowEmpty());

            if (string.IsNullOrEmpty(scriptName))
                return;

            var script = new Script(scriptName);

            ScriptPanel(script, guid);
        }

        private static void DeleteScript(Guid guid)
        {
            var scripts = ScriptSaving.GetScripts(guid);

            if (scripts.Count == 0)
            {
                UI.Info(Strings.Scripts_None, Strings.Scripts_DeleteScript);
                Console.ReadKey();
                return;
            }

            var script = AnsiConsole.Prompt(
                new SelectionPrompt<Script?>()
                .Title(Strings.Scripts_SelectToDelete)
                .AddChoices(scripts.Cast<Script?>().Append(null))
                .UseConverter(x => x == null ? $"[Red3_1]{Strings.Common_Back}[/]" : Markup.Escape(x.ScriptName)));

            if (script == null)
                return;

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                .Title(string.Format(Strings.Scripts_DeleteConfirm, $"'{Markup.Escape(script.ScriptName)}'"))
                .AddChoices(Enum.GetValues<Confirm>())
                .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            var filePath = ScriptSaving.ScriptPath(guid, script.ScriptGuid);

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    UI.Success(string.Format(Strings.Scripts_Deleted, $"'{Markup.Escape(script.ScriptName)}'"), Strings.Scripts_DeleteScript);
                }
                else
                    UI.Warning(string.Format(Strings.Scripts_AlreadyGone, $"'{Markup.Escape(script.ScriptName)}'"), Strings.Scripts_DeleteScript);
            }
            catch (Exception ex)
            {
                UI.Error(string.Format(Strings.Scripts_DeleteFailed, $"'{Markup.Escape(script.ScriptName)}'") + $"\n\n{Markup.Escape(ex.Message)}", Strings.Scripts_DeleteScript);
            }

            Console.ReadKey();
        }

        internal static void DeleteAllScripts(Guid guid)
        {
            ScriptSaving.DeleteScripts(guid);
        }

        private static void ScriptPanel(Script script, Guid guid)
        {
            while (true)
            {
                AnsiConsole.Clear();
                PrintScriptHeader(script);
                Console.WriteLine();

                DrawTree(script);
                Console.WriteLine();
                Console.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<InsideScriptAction>()
                        .Title(Strings.Scripts_SelectOperation)
                        .AddChoices(Enum.GetValues<InsideScriptAction>())
                        .UseConverter(x => x switch
                        {
                            InsideScriptAction.AddBlock => Strings.Scripts_AddBlock,
                            InsideScriptAction.EditBlock => Strings.Scripts_EditBlock,
                            InsideScriptAction.RemoveBlock => Strings.Scripts_RemoveBlock,
                            InsideScriptAction.Back => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => x.ToString()
                        }));

                switch (choice)
                {
                    case InsideScriptAction.AddBlock:
                        AddBlock(script);
                        ScriptSaving.SaveScript(script, guid);
                        break;
                    case InsideScriptAction.EditBlock:
                        EditBlock(script);
                        ScriptSaving.SaveScript(script, guid);
                        break;
                    case InsideScriptAction.RemoveBlock:
                        RemoveBlock(script);
                        ScriptSaving.SaveScript(script, guid);
                        break;
                    case InsideScriptAction.Back:
                        ScriptSaving.SaveScript(script, guid);
                        return;
                }

            }
        }

        private static void AddBlock(Script script)
        {
            var choice = PromptForAction();

            if (choice == null)
                return;

            choice.Configure(script);

            script.Blocks.Add(new Script.Block { Action = choice });
        }

        private static void EditBlock(Script script)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Script.Block?>()
                .Title(Strings.Scripts_SelectBlockEdit)
                .AddChoices(script.Blocks.Cast<Script.Block?>().Append(null))
                .UseConverter(x => x switch
                    {
                        null => $"[Red3_1]{Strings.Common_Back}[/]",
                        _ => $"{x.Action.Name} [DarkSlateGray1][[{script.Blocks.IndexOf(x) + 1}]][/]",
                    }
                ));

            choice?.Action.Configure(script);
        }

        private static void RemoveBlock(Script script)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Script.Block?>()
                    .Title(Strings.Scripts_SelectBlockRemove)
                    .AddChoices(script.Blocks.Cast<Script.Block?>().Append(null))
                    .UseConverter(x => x switch
                        {
                            null => $"[Red3_1]{Strings.Common_Back}[/]",
                            _ => $"{x.Action.Name} [DarkSlateGray1][[{script.Blocks.IndexOf(x) + 1}]][/]",
                        }
                    ));

            if (choice == null)
                return;

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<Confirm>()
                .Title(Strings.Scripts_ConfirmContinue)
                .AddChoices(Enum.GetValues<Confirm>())
                .UseConverter(UI.ConfirmName));

            if (confirm == Confirm.No)
                return;

            script.Blocks.Remove(choice);
        }

        private static void PrintScriptHeader(Script script)
        {
            var detailsGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                .AddColumn();

            detailsGrid.AddRow($"[bold]{Strings.Scripts_Guid}[/]", $"[Grey]{script.ScriptGuid}[/]");
            detailsGrid.AddRow($"[bold]{Strings.Scripts_BlockCount}[/]", $"[yellow]{script.Blocks.Count}[/]");

            UI.Box(detailsGrid, script.ScriptName);
        }

        private static IAction? PromptForAction()
        {
            var actions = typeof(Script.Block)
                .GetNestedTypes()
                .Where(x => x.IsAssignableTo(typeof(IAction)) && x is { IsInterface: false, IsAbstract: false })
                .Select(x => (IAction)Activator.CreateInstance(x)!)
                .OrderBy(x => x.Name);

            return AnsiConsole.Prompt(
                new SelectionPrompt<IAction?>()
                    .Title(Strings.Common_SelectAction)!
                    .AddChoices(actions.Cast<IAction?>().Append(null))
                    .UseConverter(x => x?.Name ?? $"[Red3_1]{Strings.Common_Back}[/]"));
        }

        private static void DrawTree(Script script)
        {
            var tree = new Tree(new Panel($"[bold Green3_1]{Strings.Scripts_Start}[/]"));
            TreeNode node = null!;

            for (int i = 0; i < script.Blocks.Count; i++)
            {
                var action = script.Blocks[i].Action;

                IRenderable renderable = new Markup(TreeRendering.Describe(action) + $" [DarkSlateGray1][[{i + 1}]][/]");

                node = i == 0
                    ? tree.AddNode(renderable)
                    : node.AddNode(renderable);
            }

            if (tree.Nodes.Count == 0)
                tree.AddNode(new Panel($"[bold Red3_1]{Strings.Scripts_End}[/]"));
            else
            {
                node = tree.Nodes.Last();

                while (node.Nodes.Count > 0)
                    node = node.Nodes.Last();

                node.AddNode(new Panel($"[bold Red3_1]{Strings.Scripts_End}[/]"));
            }

            AnsiConsole.Write(tree);
        }

        private enum InsideScriptAction
        {
            AddBlock,
            EditBlock,
            RemoveBlock,
            Back
        }

        private enum Action
        {
            RunScript,
            EditScript,
            CreateScript,
            DeleteScript,
            Back
        }
    }
}
