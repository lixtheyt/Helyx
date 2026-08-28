using Color = Spectre.Console.Color;
using Spectre.Console;
using System.Reflection;

namespace Helyx
{
    internal static class CreditsMenu
    {
        internal static void DisplayCredits()
        {
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream("Helyx.Figlet_Fonts.3d.flf");

            AnsiConsole.Markup($"[bold {Color.LightSlateBlue}]{Strings.Credits_Title}[/]\n\n\n");
            AnsiConsole.Write(new FigletText(FigletFont.Load(stream!), "Lix             (^_^)"));
            AnsiConsole.Markup($"\n\n[link=https://github.com/LixTheYT]https://github.com/LixTheYT[/]\n[italic {Color.Grey}]{Strings.Common_CtrlClick}[/]");
            Console.ReadKey();
            AnsiConsole.Clear();
        }
    }
}
