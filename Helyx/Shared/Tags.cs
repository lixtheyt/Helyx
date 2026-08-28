using Helyx.Data;
using Spectre.Console;
using System.Reflection;
using System.Text.RegularExpressions;
using Color = Spectre.Console.Color;

namespace Helyx.Shared
{
    internal static class Tags
    {
        private const int MaxNameLength = 40;

        private const string FallbackHex = "808080";

        internal static Dictionary<Guid, TagDefinition> BuiltInStatuses => new()
        {
            [BuiltInStatusIds.Active] = new TagDefinition { Name = Strings.Status_Active, Hex = "008000" },
            [BuiltInStatusIds.Inactive] = new TagDefinition { Name = Strings.Status_Inactive, Hex = "FF0000" },
            [BuiltInStatusIds.Paused] = new TagDefinition { Name = Strings.Status_Paused, Hex = "FFA500" },
            [BuiltInStatusIds.Archived] = new TagDefinition { Name = Strings.Status_Archived, Hex = "808080" }
        };

        private static readonly Dictionary<Guid, string> ShieldNames = new()
        {
            [BuiltInStatusIds.Active] = "Active",
            [BuiltInStatusIds.Inactive] = "Inactive",
            [BuiltInStatusIds.Paused] = "Paused",
            [BuiltInStatusIds.Archived] = "Archived"
        };

        internal static string ShieldName(Guid id, TagDefinition definition) =>
            ShieldNames.TryGetValue(id, out var name) ? name : definition.Name;

        internal static Dictionary<Guid, TagDefinition> AllStatuses()
        {
            var all = new Dictionary<Guid, TagDefinition>(BuiltInStatuses);

            foreach (var (guid, definition) in ConfigurationHandler.GetConfig().CustomStatuses)
                all[guid] = definition;

            return all;
        }

        internal static Dictionary<Guid, TagDefinition> AllBadges() =>
            ConfigurationHandler.GetConfig().Badges;

        internal static bool NameExists(this Dictionary<Guid, TagDefinition> tags, string name, Guid? ignore = null) =>
            tags.Any(x => x.Key != ignore && string.Equals(x.Value.Name, name, StringComparison.OrdinalIgnoreCase));

        internal static string SafeHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return FallbackHex;

            var value = hex.TrimStart('#');

            if (value.Length != 6 || !value.All(Uri.IsHexDigit))
                return FallbackHex;

            return value;
        }

        internal static string Markup(TagDefinition tag, string text) =>
            $"[#{SafeHex(tag.Hex)}]{text}[/]";

        internal static bool IsValidName(string name, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                reason = Strings.Tags_NameEmpty;
                return false;
            }

            if (name.Length > MaxNameLength)
            {
                reason = string.Format(Strings.Tags_NameTooLong, MaxNameLength);
                return false;
            }

            if (name.Any(c => c is '[' or ']'))
            {
                reason = Strings.Tags_NameBrackets;
                return false;
            }

            if (name.Any(char.IsControl))
            {
                reason = Strings.Tags_NameControl;
                return false;
            }

            return true;
        }

        internal static Color? PickColor(Color? defaultValue = null)
        {
            var allColors = typeof(Color)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(Color))
                .Where(p => p.Name != "Default")
                .Select(p => new
                {
                    p.Name,
                    Color = (Color)p.GetValue(null)!
                })
                .DistinctBy(c => c.Color)
                .OrderBy(x => x.Name)
                .ToList();

            var names = allColors.ToDictionary(x => x.Color, x => Regex.Replace(x.Name, @"(?<!^)(?=[A-Z]|\d)", " "));

            return AnsiConsole.Prompt(
                new SelectionPrompt<Color?>()
                    .Title(Strings.Tags_SelectColor)
                    .PageSize(20)
                    .AddChoices(allColors.Select(c => (Color?)c.Color).Append(null))
                    .DefaultValue(defaultValue)
                    .UseConverter(x => x == null
                        ? $"[{Color.Red3_1}]{Strings.Common_Back}[/]"
                        : $"[#{((Color)x).ToHex()}]{(names.TryGetValue((Color)x, out var name) ? name : ((Color)x).ToHex())}[/]"));
        }
    }

    internal enum EditTagAction
    {
        EditName,
        EditColor,
        Back
    }
}
