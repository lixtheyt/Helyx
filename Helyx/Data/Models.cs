using System.Text.Json.Serialization;

namespace Helyx.Data
{
    internal static class BuiltInStatusIds
    {
        internal static readonly Guid Active = new("00000000-0000-0000-0000-000000000001");
        internal static readonly Guid Inactive = new("00000000-0000-0000-0000-000000000002");
        internal static readonly Guid Paused = new("00000000-0000-0000-0000-000000000003");
        internal static readonly Guid Archived = new("00000000-0000-0000-0000-000000000004");
    }

    internal class ProjectClass
    {
        public ProjectClass(string helyxName = null!, string path = null!, List<string> usedLanguages = null!)
        {
            HelyxName = helyxName;
            Path = path;
            UsedLanguages = usedLanguages ?? [];
        }
        [JsonIgnore]
        public Guid Guid { get; set; } = Guid.Empty;
        public string HelyxName { get; set; }
        public string GitHubName { get; set; } = string.Empty;
        public string Path { get; set; }
        public string RootCommit { get; set; } = string.Empty;
        public Guid Status { get; set; } = BuiltInStatusIds.Active;
        public List<Guid> Badges { get; set; } = new();
        public List<string> UsedLanguages { get; set; }
        public Dictionary<GitHubSync, bool> GitHubSyncSettings { get; set; } =
            Enum.GetValues<GitHubSync>()
                .ToDictionary(x => x, _ => false);
        public string Notes { get; set; } = string.Empty;
        public GitUndoState? UndoState { get; set; }
    }

    internal class TagDefinition
    {
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "808080";
    }

    internal class GitUndoState
    {
        public string? CommitSha { get; init; }
        public string? Branch { get; init; }
    }

    internal class ConfigurationFile
    {
        public Dictionary<Guid, ProjectClass> Projects { get; set; } = new();
        public bool NotesEncryption { get; set; }

        public Language ProgramLanguage { get; set; }

        public IDE DefaultIDE { get; set; }

        public PreferredIdentity PreferredIdentity { get; set; } = PreferredIdentity.Git;

        public Dictionary<IDE, IDEExecutablesClass> IDEExecutables { get; set; } = new();

        public Dictionary<Guid, TagDefinition> CustomStatuses { get; set; } = new();
        public Dictionary<Guid, TagDefinition> Badges { get; set; } = new();

        public class IDEExecutablesClass
        {
            public required TypesOfFound FoundType { get; init; }

            public required string Path { get; init; }

            public enum TypesOfFound
            {
                Found,
                SetByUser,
                NotFound
            }
        }
    }

    internal class SecretsFile
    {
        public string GitHubAccessToken { get; set; } = string.Empty;
    }

    internal enum Confirm { Yes, No }

    internal enum GitHubSync
    {
        SyncStatusWithGitHubRepo,
        SyncBadgesWithGitHubRepo,
        FetchGitHubRepoTopics,
        FetchGitHubRepoStats,
        OverwriteUsedLanguagesByGitHub,
        FetchGitHubActions
    }

    internal enum IDE
    {
        VSCode,
        VS,
        Eclipse,
        Vim,
        Neovim,
        Emacs,
        SublimeText,
        PyCharm,
        CLion,
        IDEA,
        WebStorm,
        Rider,
        PhpStorm
    }

    internal enum PreferredIdentity
    {
        Git,
        GitHub
    }

    public enum Language
    {
        English,
        French,
        German,
        Italian,
        Portuguese,
        Russian,
        Slovak,
        Spanish
    }

    internal enum UIKind
    {
        Info,
        Success,
        Warning,
        Error
    }
}
