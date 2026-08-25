using Helyx.Data;
using Helyx.Shared;
using Spectre.Console;

namespace Helyx;

internal static class Program
{
    public static string Version =>
        typeof(Program).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";

    private const int MaxConsecutiveFailures = 3;

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {

        }

        NativeMethods.UseOwnConsoleIcon();

        Strings.Culture = ConfigurationHandler.GetConfig().ProgramLanguage.Culture();

        using var single = new Mutex(true, @"Local\Helyx", out var owned);

        if (!owned)
        {
            UI.Error(Strings.Program_AlreadyRunning, Strings.Program_AlreadyRunning_Title);
            Console.ReadKey(true);
            return;
        }

        int consecutiveFailures = 0;

        while (true)
        {
            try
            {
                MainMenu.Display();
                consecutiveFailures = 0;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;

                AnsiConsole.Clear();

                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    UI.Error(
                        $"{Markup.Escape(ex.Message)}\n\n" +
                        $"[grey]{string.Format(Strings.Program_Crash_Closing, consecutiveFailures)}[/]",
                        Strings.Program_Crash_Title);
                    Console.ReadKey(true);
                    Environment.Exit(1);
                }

                UI.Error(
                    $"{Markup.Escape(ex.ToString())}\n\n[grey]{Strings.Program_Crash_Return}[/]",
                    Strings.Program_Crash_Title);
                Console.ReadKey(true);
                AnsiConsole.Clear();
            }
        }
    }
}
