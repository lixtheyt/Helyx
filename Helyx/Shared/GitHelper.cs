using LibGit2Sharp;
using Color = Spectre.Console.Color;

namespace Helyx.Shared
{
    internal static class GitHelper
    {
        internal static Repository? OpenRepo(string path, string title = "Git")
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                UI.Error(Strings.Common_ProjectFolderMissing + $"\n[{Color.Grey}]{Spectre.Console.Markup.Escape(path ?? string.Empty)}[/]", title);
                Console.ReadKey();
                return null;
            }

            if (!Repository.IsValid(path))
            {
                UI.Error(Strings.Common_NotAGitRepo + "\n" + Strings.Common_InitGitHint, title);
                Console.ReadKey();
                return null;
            }

            try
            {
                return new Repository(path);
            }
            catch (Exception ex)
            {
                UI.Error(Strings.Git_RepoOpenFailed + $"\n\n{Spectre.Console.Markup.Escape(ex.Message)}", title);
                Console.ReadKey();
                return null;
            }
        }

        internal static (Color color, string label) StatusColorLabel(FileStatus state) => state switch
        {
            FileStatus.Unaltered => (Color.Green, Strings.Git_Status_Clean),

            FileStatus.NewInIndex => (Color.Blue, Strings.Git_Status_StagedNew),
            FileStatus.ModifiedInIndex => (Color.Blue, Strings.Git_Status_StagedModified),
            FileStatus.RenamedInIndex => (Color.Blue, Strings.Git_Status_StagedRename),
            FileStatus.TypeChangeInIndex => (Color.Blue, Strings.Git_Status_StagedTypeChange),
            FileStatus.DeletedFromIndex => (Color.DarkBlue, Strings.Git_Status_StagedDelete),

            FileStatus.NewInWorkdir => (Color.Yellow, Strings.Git_Status_New),
            FileStatus.ModifiedInWorkdir => (Color.Yellow, Strings.Git_Status_Modified),
            FileStatus.RenamedInWorkdir => (Color.Yellow, Strings.Git_Status_Renamed),
            FileStatus.TypeChangeInWorkdir => (Color.Yellow, Strings.Git_Status_TypeChanged),
            FileStatus.DeletedFromWorkdir => (Color.Orange1, Strings.Git_Status_Deleted),

            FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir => (Color.Yellow, Strings.Git_Status_StagedAndModified),
            FileStatus.ModifiedInIndex | FileStatus.RenamedInIndex => (Color.Yellow, Strings.Git_Status_StagedAndRenamed),

            FileStatus.Ignored => (Color.Grey, Strings.Git_Status_Ignored),
            FileStatus.Nonexistent => (Color.Grey19, Strings.Git_Status_Missing),
            FileStatus.Unreadable => (Color.Red, Strings.Git_Status_Unreadable),
            FileStatus.Conflicted => (Color.DarkRed, Strings.Git_Status_Conflict),

            _ when IsStagedInIndex(state) && PendingLabel(state) is { } pending
                => (Color.Yellow, string.Format(Strings.Git_Status_StagedPlus, pending)),

            _ => (Color.Red3, $"{Strings.Git_Status_Unknown} --> {state}")
        };

        private static string? PendingLabel(FileStatus state) => state switch
        {
            var s when s.HasFlag(FileStatus.ModifiedInWorkdir) => Strings.Git_Status_Modified,
            var s when s.HasFlag(FileStatus.DeletedFromWorkdir) => Strings.Git_Status_Deleted,
            var s when s.HasFlag(FileStatus.RenamedInWorkdir) => Strings.Git_Status_Renamed,
            var s when s.HasFlag(FileStatus.TypeChangeInWorkdir) => Strings.Git_Status_TypeChanged,
            var s when s.HasFlag(FileStatus.NewInWorkdir) => Strings.Git_Status_New,
            _ => null
        };

        internal enum FileGroup
        {
            Conflicted,
            Modified,
            New,
            Deleted,
            Renamed,
            Other
        }

        internal static FileGroup Group(FileStatus state) => state switch
        {
            var s when s.HasFlag(FileStatus.Conflicted) => FileGroup.Conflicted,
            var s when s.HasFlag(FileStatus.ModifiedInWorkdir) || s.HasFlag(FileStatus.ModifiedInIndex) => FileGroup.Modified,
            var s when s.HasFlag(FileStatus.NewInWorkdir) || s.HasFlag(FileStatus.NewInIndex) => FileGroup.New,
            var s when s.HasFlag(FileStatus.DeletedFromWorkdir) || s.HasFlag(FileStatus.DeletedFromIndex) => FileGroup.Deleted,
            var s when s.HasFlag(FileStatus.RenamedInWorkdir) || s.HasFlag(FileStatus.RenamedInIndex) => FileGroup.Renamed,
            _ => FileGroup.Other
        };

        internal static string GroupName(FileGroup group) => group switch
        {
            FileGroup.Conflicted => Strings.Git_Group_Conflicted,
            FileGroup.Modified => Strings.Git_Group_Modified,
            FileGroup.New => Strings.Git_Group_New,
            FileGroup.Deleted => Strings.Git_Group_Deleted,
            FileGroup.Renamed => Strings.Git_Group_Renamed,
            _ => Strings.Git_Group_Other
        };

        internal static bool IsStagedInIndex(FileStatus state) =>
            state.HasFlag(FileStatus.NewInIndex) ||
            state.HasFlag(FileStatus.ModifiedInIndex) ||
            state.HasFlag(FileStatus.RenamedInIndex) ||
            state.HasFlag(FileStatus.TypeChangeInIndex) ||
            state.HasFlag(FileStatus.DeletedFromIndex);

        internal static readonly StatusOptions FastStatus = new()
        {
            IncludeIgnored = false,
            RecurseIgnoredDirs = false
        };
    }
}
