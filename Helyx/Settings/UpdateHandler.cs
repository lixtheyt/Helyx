using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx.Settings
{
    internal static class UpdateHandler
    {
        private const string UpdatesLink = "https://github.com/LixTheYT/Helyx/releases/latest";

        internal static void Display()
        {
            CheckForUpdates().GetAwaiter().GetResult();
        }

        private static async Task CheckForUpdates()
        {
            bool foundUpdate = false;
            string? latestVersion = null;

            try
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(Strings.Update_Checking, async ctx =>
                    {
                        latestVersion = await GitHubCalls.GetGitHubRepoLatestVersion();

                        if (string.IsNullOrWhiteSpace(latestVersion))
                            throw new Exception(Strings.Update_Error_NoVersion);

                        string trimmedLatestVersion = latestVersion.TrimStart('v', 'V');

                        if (!Version.TryParse(trimmedLatestVersion, out var latest) ||
                            !Version.TryParse(Program.Version, out var current))
                        {
                            throw new Exception(Strings.Update_Error_Unreadable + "\n\n" +
                                string.Format(Strings.Update_Current, Program.Version) + "\n" +
                                string.Format(Strings.Update_Latest, latestVersion));
                        }

                        if (latest > current)
                            foundUpdate = true;
                    }
                );
            }
            catch (Exception ex)
            {
                UI.Error(Strings.Update_Failed + "\n\n" + Markup.Escape(ex.Message), Strings.Update_Failed_Title);
                Console.ReadKey();
                return;
            }

            if (foundUpdate)
            {
                UI.Success(Strings.Update_Available + "\n\n" +
                           $"[link={UpdatesLink}]{Strings.Update_ClickHere}[/]\n" +
                           $"[italic grey]{Strings.Common_CtrlClick}[/]\n\n" +
                           string.Format(Strings.Update_Current, Program.Version) + "\n" +
                           string.Format(Strings.Update_Latest, latestVersion), Strings.Update_Available_Title);
            }
            else
            {
                UI.Info(Strings.Update_None, Strings.Update_None_Title);
            }
            Console.ReadKey();

            AnsiConsole.Clear();
        }
    }
}
