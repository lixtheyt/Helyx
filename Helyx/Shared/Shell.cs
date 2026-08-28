using System.ComponentModel;
using System.Diagnostics;

namespace Helyx.Shared
{
    internal class Shell
    {
        internal static bool IsOnPath(string command)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                    return false;

                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();

                Task.WaitAll(output, error);

                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return false;
            }
        }
    }
}
