using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SrcCad.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        WindowsRegressionJournal.Write(new {Completed=false,Passed=0,Failed=0,Step="Application startup"});
        System.Windows.Forms.Application.ThreadException += (_,args) =>
        {
            WriteCrash(args.Exception);
            if(WindowsRegressionJournal.Active){WindowsRegressionJournal.Fatal(args.Exception);Current.Shutdown(1);}
            else System.Windows.MessageBox.Show(args.Exception.Message,"SrcCad");
        };
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrash(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        if(WindowsRegressionJournal.Active)
        {
            WindowsRegressionJournal.Fatal(e.Exception);e.Handled=true;Current.Shutdown(1);return;
        }
        System.Windows.MessageBox.Show(
            "SrcCad beklenmeyen bir hata ile karşılaştı. logs\\crash.log oluşturuldu.\n\n" + e.Exception.Message,
            "SrcCad", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n\n");
        }
        catch { }
    }
}
