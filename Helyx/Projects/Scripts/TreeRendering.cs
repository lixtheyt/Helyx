using static Helyx.Projects.Scripts.Script.Block;
using Spectre.Console;

namespace Helyx.Projects.Scripts
{
    internal static class TreeRendering
    {
        internal static string Describe(IAction action) =>
            action.MarkupName + action switch
            {
                CreateFileAction createFile => $" (\"{Markup.Escape(createFile.Path)}\")",
                CreateFolderAction createFolder => $" (\"{Markup.Escape(createFolder.Path)}\")",
                DeleteFileAction deleteFile => $" (\"{Markup.Escape(deleteFile.Path)}\")",
                DeleteFolderAction deleteFolder => $" (\"{Markup.Escape(deleteFolder.Path)}\")",
                ExecuteAction execute => $" (\"{Markup.Escape(execute.Command)}\")",
                WaitAction wait => $" ({wait.Duration}s)",
                LogAction log => $" (\"{Markup.Escape(log.Message)}\")",
                OpenAction open => $" (\"{Markup.Escape(open.Path)}\")",
                _ => ""
            };
    }
}
