using static Helyx.Data.ConfigurationHandler;
using Spectre.Console;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Color = Spectre.Console.Color;

namespace Helyx.Data
{
    internal static class ConfigurationMigrator
    {
        internal static void CheckAndMergeConfig()
        {
            string configPath = GetConfigPath();

            if (!File.Exists(configPath))
            {
                var newConfig = new ConfigurationFile();
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                File.WriteAllText(configPath, JsonSerializer.Serialize(newConfig, Options));
                CreateSecrets();
                AnsiConsole.MarkupLine($"[{Color.Green}]{Strings.Migrator_Created}[/]");
                return;
            }

            void KeepBroken()
            {
                try
                {
                    var kept = configPath + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}";

                    File.Move(configPath, kept);

                    AnsiConsole.MarkupLine($"[{Color.Grey}]{string.Format(Strings.Config_KeptAs, Markup.Escape(Path.GetFileName(kept)))}[/]");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            JsonNode? existingNode;
            try
            {
                existingNode = JsonNode.Parse(File.ReadAllText(configPath));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[{Color.Red}]{Strings.Migrator_Corrupted}[/]");
                KeepBroken();
                CreateConfig();
                return;
            }

            if (existingNode is not JsonObject existingObj)
            {
                AnsiConsole.MarkupLine($"[{Color.Red}]{Strings.Migrator_Invalid}[/]");
                KeepBroken();
                CreateConfig();
                return;
            }
            bool changed = false;

            JsonObject? FindObjectKey(JsonObject parent, string name) =>
                parent.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;

            JsonArray? FindArrayKey(JsonObject parent, string name) =>
                parent.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value as JsonArray;

            string? FindActualKey(JsonObject parent, string name) =>
                parent.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Key;

            if (FindArrayKey(existingObj, "projects") is JsonArray oldProjects)
            {
                var keyed = new JsonObject();

                foreach (var item in oldProjects)
                    if (item is JsonObject projectObj)
                        keyed[Guid.NewGuid().ToString()] = projectObj.DeepClone();

                var actualKey = FindActualKey(existingObj, "projects") ?? "Projects";
                existingObj[actualKey] = keyed;
                changed = true;
                AnsiConsole.MarkupLine($"[{Color.Yellow}]{Strings.Migrator_ProjectsToGuid}[/]");
            }

            string? ReadString(JsonObject parent, string? key) =>
                key != null && parent[key] is JsonValue value && value.TryGetValue<string>(out var text)
                    ? text
                    : null;

            Dictionary<string, Guid> ReKeyTagsById(string property, HashSet<Guid> knownIds)
            {
                var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                if (FindObjectKey(existingObj, property) is not JsonObject tags)
                    return ids;

                var rekeyed = new JsonObject();

                foreach (var tagKvp in tags.ToList())
                {
                    var id = Guid.TryParse(tagKvp.Key, out var existingId) ? existingId : Guid.NewGuid();
                    var definition = tagKvp.Value?.DeepClone() as JsonObject ?? new JsonObject();

                    var nameKey = FindActualKey(definition, "name") ?? "Name";
                    var name = ReadString(definition, nameKey);

                    if (string.IsNullOrWhiteSpace(name))
                        name = tagKvp.Key;

                    definition[nameKey] = name;
                    ids[name] = id;
                    knownIds.Add(id);
                    rekeyed[id.ToString()] = definition;

                    if (!Guid.TryParse(tagKvp.Key, out _))
                        changed = true;
                }

                existingObj[FindActualKey(existingObj, property) ?? property] = rekeyed;

                return ids;
            }

            var knownStatusIds = new HashSet<Guid>
            {
                BuiltInStatusIds.Active,
                BuiltInStatusIds.Inactive,
                BuiltInStatusIds.Paused,
                BuiltInStatusIds.Archived
            };

            var knownBadgeIds = new HashSet<Guid>();

            var statusesPresent = FindObjectKey(existingObj, "CustomStatuses") is not null;
            var badgesPresent = FindObjectKey(existingObj, "Badges") is not null;

            var statusIds = ReKeyTagsById("CustomStatuses", knownStatusIds);
            var badgeIds = ReKeyTagsById("Badges", knownBadgeIds);

            statusIds["Active"] = BuiltInStatusIds.Active;
            statusIds["Inactive"] = BuiltInStatusIds.Inactive;
            statusIds["Paused"] = BuiltInStatusIds.Paused;
            statusIds["Archived"] = BuiltInStatusIds.Archived;

            if (FindObjectKey(existingObj, "projects") is JsonObject projectsObj)
            {
                bool statusMigrated = false;
                bool tagIdsMigrated = false;

                foreach (var projectKvp in projectsObj.ToList())
                {
                    if (projectKvp.Value is not JsonObject projectObj)
                        continue;

                    var badgesActualKey = FindActualKey(projectObj, "badges");

                    if (badgesActualKey != null && projectObj[badgesActualKey] is JsonArray badgeArray)
                    {
                        var upgraded = new JsonArray();
                        bool rewritten = false;

                        foreach (var badgeNode in badgeArray)
                        {
                            if (badgeNode is not JsonValue badgeVal || !badgeVal.TryGetValue<string>(out var badgeName))
                            {
                                rewritten = true;
                                continue;
                            }

                            if (Guid.TryParse(badgeName, out var keptId))
                            {
                                if (!badgesPresent || knownBadgeIds.Contains(keptId))
                                    upgraded.Add(keptId.ToString());
                                else
                                    rewritten = true;
                            }
                            else if (badgeIds.TryGetValue(badgeName, out var mappedId))
                            {
                                upgraded.Add(mappedId.ToString());
                                rewritten = true;
                            }
                            else
                                rewritten = true;
                        }

                        if (rewritten)
                        {
                            projectObj[badgesActualKey] = upgraded;
                            tagIdsMigrated = true;
                        }
                    }

                    var statusActualKey = FindActualKey(projectObj, "status");

                    if (statusActualKey == null)
                        continue;

                    var statusNode = projectObj[statusActualKey];

                    switch (statusNode)
                    {
                        case JsonObject statusObj:
                            {
                                string statusName = "Active";

                                var textKey = FindActualKey(statusObj, "text");
                                if (textKey != null && statusObj[textKey] is JsonValue textVal && textVal.TryGetValue<string>(out var t))
                                    statusName = t;

                                projectObj[statusActualKey] = statusName;
                                statusMigrated = true;
                                break;
                            }
                        case null:
                            projectObj[statusActualKey] = "Active";
                            statusMigrated = true;
                            break;
                        case JsonValue val when val.GetValueKind() != JsonValueKind.String:
                            projectObj[statusActualKey] = "Active";
                            statusMigrated = true;
                            break;
                    }

                    var status = ReadString(projectObj, statusActualKey);

                    if (string.IsNullOrWhiteSpace(status))
                        continue;

                    if (Guid.TryParse(status, out var keptStatusId))
                    {
                        if (!statusesPresent || knownStatusIds.Contains(keptStatusId))
                            continue;

                        projectObj[statusActualKey] = BuiltInStatusIds.Active.ToString();
                        tagIdsMigrated = true;
                        continue;
                    }

                    projectObj[statusActualKey] = statusIds.TryGetValue(status, out var statusId)
                        ? statusId.ToString()
                        : BuiltInStatusIds.Active.ToString();

                    tagIdsMigrated = true;
                }

                if (statusMigrated)
                {
                    changed = true;
                    AnsiConsole.MarkupLine($"[{Color.Yellow}]{Strings.Migrator_StatusToString}[/]");
                }

                if (tagIdsMigrated)
                {
                    changed = true;
                    AnsiConsole.MarkupLine($"[{Color.Yellow}]{Strings.Migrator_TagsToIds}[/]");
                }
            }

            bool IsSimpleType(Type t)
            {
                t = Nullable.GetUnderlyingType(t) ?? t;
                return t.IsPrimitive || t.IsEnum || t == typeof(string) ||
                       t == typeof(decimal) || t == typeof(DateTime) ||
                       t == typeof(DateTimeOffset) || t == typeof(Guid) ||
                       t == typeof(TimeSpan);
            }

            bool TryGetListElementType(Type t, out Type elementType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                {
                    elementType = t.GetGenericArguments()[0];
                    return true;
                }
                elementType = null!;
                return false;
            }

            bool TryGetDictionaryValueType(Type t, out Type valueType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    valueType = t.GetGenericArguments()[1];
                    return true;
                }
                valueType = null!;
                return false;
            }

            IEnumerable<(string Name, Type Type)> GetMembers(Type t)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    yield return (f.Name, f.FieldType);
                }

                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead || !p.CanWrite) continue;
                    if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    if (p.GetIndexParameters().Length > 0) continue;
                    yield return (p.Name, p.PropertyType);
                }
            }

            object? CreateDefault(Type t)
            {
                try
                {
                    return t.IsValueType ? Activator.CreateInstance(t) :
                           t.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(t) : null;
                }
                catch
                {
                    return null;
                }
            }

            JsonValueKind ExpectedKind(Type t)
            {
                if (TryGetListElementType(t, out _)) return JsonValueKind.Array;
                if (!IsSimpleType(t) && t.IsClass) return JsonValueKind.Object;
                return JsonValueKind.Undefined;
            }

            var visiting = new HashSet<Type>();

            void MergeAll(Type type, JsonObject existing)
            {
                if (!visiting.Add(type))
                    return;

                try
                {
                    MergeMembers(type, existing);
                }
                finally
                {
                    visiting.Remove(type);
                }
            }

            void MergeMembers(Type type, JsonObject existing)
            {
                foreach (var (name, memberType) in GetMembers(type))
                {
                    var actualKey = existing.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Key;

                    if (actualKey == null)
                    {
                        var defaultValue = CreateDefault(memberType);
                        existing[name] = JsonSerializer.SerializeToNode(defaultValue, memberType, Options);
                        changed = true;
                        continue;
                    }

                    var node = existing[actualKey];
                    bool isExplicitNull = node == null;
                    var nodeKind = node?.GetValueKind();
                    var expectedKind = ExpectedKind(memberType);

                    bool simpleTypeMismatch = !isExplicitNull &&
                                               IsSimpleType(memberType) &&
                                               nodeKind is JsonValueKind.Object or JsonValueKind.Array;

                    bool structuralMismatch = !isExplicitNull &&
                                               expectedKind != JsonValueKind.Undefined &&
                                               nodeKind != expectedKind;

                    if (structuralMismatch || simpleTypeMismatch)
                    {
                        var defaultValue = CreateDefault(memberType);
                        existing[actualKey] = JsonSerializer.SerializeToNode(defaultValue, memberType, Options);
                        changed = true;
                        AnsiConsole.MarkupLine($"[{Color.Yellow}]{string.Format(Strings.Migrator_PropertyReset, name)}[/]");
                        continue;
                    }

                    if (isExplicitNull)
                        continue;

                    if (TryGetDictionaryValueType(memberType, out var valueType) &&
                        !IsSimpleType(valueType) && node is JsonObject dictObj)
                    {
                        foreach (var kvp in dictObj)
                        {
                            if (kvp.Value is JsonObject valObj)
                                MergeAll(valueType, valObj);
                        }
                    }
                    else if (TryGetListElementType(memberType, out var elementType) &&
                             !IsSimpleType(elementType) && node is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            if (item is JsonObject itemObj)
                                MergeAll(elementType, itemObj);
                        }
                    }
                    else if (!IsSimpleType(memberType) && memberType.IsClass && node is JsonObject nestedObj)
                    {
                        MergeAll(memberType, nestedObj);
                    }
                }
            }
            MergeAll(typeof(ConfigurationFile), existingObj);

            ConfigurationFile config;
            try
            {
                config = existingObj.Deserialize<ConfigurationFile>(Options) ?? new ConfigurationFile();
            }
            catch (JsonException)
            {
                AnsiConsole.MarkupLine($"[{Color.Red}]{Strings.Migrator_UnreadableAfter}[/]");
                KeepBroken();
                config = new ConfigurationFile();
                changed = true;
            }

            config.Projects ??= [];
            config.CustomStatuses ??= [];
            config.Badges ??= [];
            config.IDEExecutables ??= [];

            foreach (var project in config.Projects.Values)
            {
                var current = project.GitHubSyncSettings ?? [];

                var rebuilt = Enum.GetValues<GitHubSync>()
                    .ToDictionary(x => x, x => current.TryGetValue(x, out var enabled) && enabled);

                bool alreadyCorrect =
                    current.Count == rebuilt.Count &&
                    rebuilt.All(kv => current.TryGetValue(kv.Key, out var value) && value == kv.Value);

                if (alreadyCorrect)
                    continue;

                project.GitHubSyncSettings = rebuilt;
                changed = true;
            }

            if (!File.Exists(GetSecretsPath()))
            {
                CreateSecrets();
            }

            if (!changed)
                return;

            existingObj = JsonSerializer.SerializeToNode(config, Options)!.AsObject();

            var temporary = configPath + ".tmp";

            try
            {
                File.WriteAllText(temporary, existingObj.ToJsonString(Options));

                if (File.Exists(configPath))
                    File.Replace(temporary, configPath, null);
                else
                    File.Move(temporary, configPath);

                AnsiConsole.MarkupLine($"[{Color.Yellow}]{Strings.Migrator_Updated}[/]");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                }

                AnsiConsole.MarkupLine($"[{Color.Red}]{string.Format(Strings.Migrator_SaveFailed, Markup.Escape(ex.Message))}[/]");
            }
        }
    }
}
