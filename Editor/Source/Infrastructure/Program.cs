using System.Windows;
namespace Gp200.Replica;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), e.ExceptionObject?.ToString()); } catch {}
        };
        try
        {
            if(args.Contains("--self-test"))
            {
                return QaHarness.RunAllAsync().GetAwaiter().GetResult();
            }

            var unknown=args.FirstOrDefault(a=>a.StartsWith("--",StringComparison.Ordinal)&&a is not ("--english" or "--spanish" or "--self-test"));
            if(unknown!=null){Console.Error.WriteLine("Unsupported command-line option.");return 2;}
            UiText.Language = args.Contains("--spanish") ? "es" : args.Contains("--english") ? "en" : AppPreferences.Load().Language;
            var app = new Application();
            DarkUiTheme.ApplyGlobalAuraCodeTheme();
            app.DispatcherUnhandledException += (s, e) =>
            {
                try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), e.Exception?.ToString()); } catch {}
            };
            app.Run(new EditorWindow(args.Where(p => p.EndsWith(".prst", StringComparison.OrdinalIgnoreCase)).ToArray(), !AppPreferences.ProfileSelectionRequired));
            return 0;
        }
        catch (Exception e)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), e.ToString()); } catch {}
            UiDialogs.Show(e.Message, "GP200 Replica");
            return 1;
        }
    }
}
