using System;
using System.Linq;
using System.Windows;

namespace Glasshouse.Ui
{
    public static class App
    {
        public static int Run(string lookFile)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error(e.Exception.ToString());
                MessageBox.Show(e.Exception.Message, "Glasshouse");
                e.Handled = true;
            };
            Theme.Init(app);
            if (!Setup.Done || System.Environment.GetCommandLineArgs().Any(a => a == "--wizard"))
            {
                Look look;
                try { look = lookFile != null ? Look.Load(lookFile) : Program.BuiltInLook(); }
                catch (Exception e) { MessageBox.Show("That look file can't be opened: " + e.Message, "Glasshouse"); look = Program.BuiltInLook(); }
                return app.Run(new Wizard(look));
            }
            return app.Run(new MainWindow(lookFile));
        }
    }
}
