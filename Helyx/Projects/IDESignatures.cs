using Helyx.Data;

namespace Helyx.Projects
{
    internal static class IDESignatures
    {
        internal interface IDESearch
        {
            List<IDESearchListClass> Keywords { get; }
            IDE Ide { get; }
        }

        private static readonly List<IDESearch> All =
        [
            new VSCode(),
            new VS(),
            new Rider(),
            new IDEA(),
            new PyCharm(),
            new CLion(),
            new WebStorm(),
            new PhpStorm(),
            new Eclipse(),
            new SublimeText(),
            new Vim(),
            new Neovim(),
            new Emacs()
        ];

        private class VSCode : IDESearch
        {
            public IDE Ide => IDE.VSCode;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".vscode", IsDirectory = true, Points = 40 },
                new() { Keyword = ".code-workspace", IsExtension = true, Points = 50 },
                new() { Keyword = "launch.json", IsFile = true, Points = 15 },
                new() { Keyword = "tasks.json", IsFile = true, Points = 10 },
                new() { Keyword = "settings.json", IsFile = true, Points = 10 },
                new() { Keyword = "package.json", IsFile = true, Points = 3 },
                new() { Keyword = "tsconfig.json", IsFile = true, Points = 3 }
            ];
        }

        private class VS : IDESearch
        {
            public IDE Ide => IDE.VS;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".sln", IsExtension = true, Points = 50 },
                new() { Keyword = ".vs", IsDirectory = true, Points = 40 },
                new() { Keyword = ".suo", IsExtension = true, Points = 40 },
                new() { Keyword = ".vcxproj", IsExtension = true, Points = 40 },
                new() { Keyword = ".csproj", IsExtension = true, Points = 20 },
                new() { Keyword = "bin", IsDirectory = true, Points = 2 },
                new() { Keyword = "obj", IsDirectory = true, Points = 2 }
            ];
        }

        private class Rider : IDESearch
        {
            public IDE Ide => IDE.Rider;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".idea", IsDirectory = true, Points = 40 },
                new() { Keyword = ".DotSettings.user", IsExtension = true, Points = 80 },
                new() { Keyword = ".sln", IsExtension = true, Points = 30 },
                new() { Keyword = ".csproj", IsExtension = true, Points = 10 }
            ];
        }

        private class IDEA : IDESearch
        {
            public IDE Ide => IDE.IDEA;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".idea", IsDirectory = true, Points = 40 },
                new() { Keyword = "pom.xml", IsFile = true, Points = 25 },
                new() { Keyword = "build.gradle", IsFile = true, Points = 25 },
                new() { Keyword = "build.gradle.kts", IsFile = true, Points = 25 }
            ];
        }

        private class PyCharm : IDESearch
        {
            public IDE Ide => IDE.PyCharm;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".idea", IsDirectory = true, Points = 40 },
                new() { Keyword = "requirements.txt", IsFile = true, Points = 15 },
                new() { Keyword = "pyproject.toml", IsFile = true, Points = 15 },
                new() { Keyword = ".venv", IsDirectory = true, Points = 20 }
            ];
        }

        private class CLion : IDESearch
        {
            public IDE Ide => IDE.CLion;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".idea", IsDirectory = true, Points = 30 },
                new() { Keyword = "CMakeLists.txt", IsFile = true, Points = 50 },
                new() { Keyword = "cmake-build-", IsPrefix = true, IsDirectory = true, Points = 30 }
            ];
        }

        private class WebStorm : IDESearch
        {
            public IDE Ide => IDE.WebStorm;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".idea", IsDirectory = true, Points = 30 },
                new() { Keyword = "package.json", IsFile = true, Points = 30 },
                new() { Keyword = "vite.config.", IsFile = true, IsPrefix = true, Points = 20 },
                new() { Keyword = "webpack.config.", IsFile = true, IsPrefix = true, Points = 20 }
            ];
        }

        private class PhpStorm : IDESearch
        {
            public IDE Ide => IDE.PhpStorm;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new () { Keyword = ".idea", IsDirectory = true, Points = 30 },
                new () { Keyword = "composer.json", IsFile = true, Points = 40 }
            ];
        }

        private class Eclipse : IDESearch
        {
            public IDE Ide => IDE.Eclipse;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".project", IsFile = true, Points = 50 },
                new() { Keyword = ".classpath", IsFile = true, Points = 50 },
                new() { Keyword = ".settings", IsDirectory = true, Points = 30 }
            ];
        }

        private class SublimeText : IDESearch
        {
            public IDE Ide => IDE.SublimeText;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".sublime-project", IsExtension = true, Points = 60 },
                new() { Keyword = ".sublime-workspace", IsExtension = true, Points = 60 }
            ];
        }

        private class Vim : IDESearch
        {
            public IDE Ide => IDE.Vim;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".vimrc", IsFile = true, Points = 20 },
                new() { Keyword = ".swp", IsExtension = true, Points = 15 }
            ];
        }

        private class Neovim : IDESearch
        {
            public IDE Ide => IDE.Neovim;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = "init.lua", IsFile = true, Points = 20 },
                new() { Keyword = "nvim", IsDirectory = true, Points = 20 }
            ];
        }

        private class Emacs : IDESearch
        {
            public IDE Ide => IDE.Emacs;
            public List<IDESearchListClass> Keywords { get; } =
            [
                new() { Keyword = ".projectile", IsFile = true, Points = 20 },
                new() { Keyword = "init.el", IsFile = true, Points = 20 }
            ];
        }

        internal class IDESearchListClass
        {
            public required string Keyword;
            public bool IsFile;
            public bool IsExtension;
            public bool IsPrefix;
            public bool IsDirectory;
            public int Points;
        }

        internal static (IDE Ide, int Score)[] Identify(string path)
        {
            Dictionary<IDE, int> ideScores = new();

            if (!Directory.Exists(path))
                return Enum.GetValues<IDE>().Select(x => (x, 0)).ToArray();

            foreach (var ideClass in IDESignatures.All)
            {
                int score = 0;

                foreach (var entry in ideClass.Keywords)
                {
                    bool matched = false;

                    try
                    {
                        if (entry.IsDirectory)
                        {
                            if (entry.IsPrefix)
                                matched = Directory.EnumerateDirectories(path)
                                    .Any(d => Path.GetFileName(d).StartsWith(entry.Keyword));
                            else
                                matched = Directory.Exists(Path.Combine(path, entry.Keyword));
                        }
                        else if (entry.IsExtension)
                        {
                            matched = Directory.EnumerateFiles(path, $"*{entry.Keyword}", SearchOption.TopDirectoryOnly).Any();
                        }
                        else if (entry.IsFile)
                        {
                            if (entry.IsPrefix)
                                matched = Directory.EnumerateFiles(path, $"{entry.Keyword}*", SearchOption.TopDirectoryOnly).Any();
                            else
                                matched = File.Exists(Path.Combine(path, entry.Keyword));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        matched = false;
                    }

                    if (matched)
                        score += entry.Points;
                }

                if (!ideScores.TryAdd(ideClass.Ide, score))
                    ideScores[ideClass.Ide] += score;
            }

            return ideScores
                .OrderByDescending(x => x.Value)
                .Select(x => (x.Key, x.Value))
                .ToArray();
        }
    }
}
