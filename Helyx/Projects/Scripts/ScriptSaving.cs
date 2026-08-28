using static Helyx.Projects.Scripts.Script.Block;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Helyx.Shared;
using Spectre.Console;
using Color = Spectre.Console.Color;

namespace Helyx.Projects.Scripts
{
    internal static class ScriptSaving
    {
        private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Helyx", "scripts");

        private static bool ScriptsFolderExists => Directory.Exists(Dir);

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { AddTypes }
            }
        };

        private static void AddTypes(JsonTypeInfo info)
        {
            if (info.Type != typeof(IAction))
                return;

            info.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type"
            };

            foreach (var type in typeof(Script).Assembly.GetTypes()
                         .Where(x => info.Type.IsAssignableFrom(x) && !x.IsInterface))
            {
                info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(type, type.Name));
            }
        }

        internal static string ScriptPath(Guid projectGuid, Guid scriptGuid) =>
            Path.Combine(Dir, $"{projectGuid}!{scriptGuid}.json");

        internal static bool CreateDirectory()
        {
            try
            {
                if (!ScriptsFolderExists)
                    Directory.CreateDirectory(Dir);

                return true;
            }
            catch (Exception ex)
            {
                UI.Error(Strings.Scripts_FolderCreateFailed + $"\n\n{ex.Message}", Strings.Other_UserScripts);
                Console.ReadKey();
                return false;
            }
        }

        internal static void SaveScript(Script script, Guid projectGuid)
        {
            if (!CreateDirectory())
                return;

            try
            {
                var path = ScriptPath(projectGuid, script.ScriptGuid);
                var temporary = path + ".tmp";

                File.WriteAllText(temporary, JsonSerializer.Serialize(script, Options));

                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            catch (Exception ex)
            {
                UI.Error(string.Format(Strings.Scripts_SaveFailed, $"'{Markup.Escape(script.ScriptName)}'") + $"\n\n{Markup.Escape(ex.Message)}", Strings.Other_UserScripts);
                Console.ReadKey();
            }
        }

        internal static void DeleteScripts(Guid guid)
        {
            if (!ScriptsFolderExists)
                return;

            List<string> files;

            try
            {
                files = Directory.EnumerateFiles(Dir, $"{guid}!*.json").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        internal static List<Script> GetScripts(Guid guid)
        {
            if (!ScriptsFolderExists)
                return [];

            List<string> files;

            try
            {
                files = Directory.EnumerateFiles(Dir, $"{guid}!*.json").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                UI.Error(Strings.Scripts_FolderReadFailed + $"\n\n{ex.Message}", Strings.Other_UserScripts);
                Console.ReadKey();
                return [];
            }

            var scripts = new List<Script>();
            var problems = new List<string>();

            foreach (var file in files)
            {
                Script? script;

                try
                {
                    script = JsonSerializer.Deserialize<Script>(File.ReadAllText(file), Options);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    script = null;
                }

                if (script == null || string.IsNullOrWhiteSpace(script.ScriptName))
                {
                    problems.Add(string.Format(Strings.Scripts_CouldNotRead, Path.GetFileName(file)));
                    continue;
                }

                script.Blocks ??= [];

                int dropped = script.Blocks.RemoveAll(b => b?.Action == null);

                if (dropped > 0)
                    problems.Add(string.Format(Strings.Scripts_BlocksSkipped, Path.GetFileName(file), dropped));

                scripts.Add(script);
            }

            if (problems.Count > 0)
            {
                UI.Warning(
                    Strings.Scripts_LoadProblems + "\n\n" +
                    string.Join("\n", problems.Select(p => $"[{Color.Gray}]{Markup.Escape(p)}[/]")),
                    Strings.Other_UserScripts);
                Console.ReadKey();
            }

            return scripts.OrderBy(x => x.ScriptName).ToList();
        }
    }
}
