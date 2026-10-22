using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Glasshouse.Ui
{
    // --uitest <folder>: opens the REAL installer and Glasshouse windows off-screen (they never take the focus or show
    // on screen), presses their buttons the way Windows' accessibility tools do, and saves a picture of each step.
    // Changes nothing: it stops before Install and doesn't move any setting.
    public static class UiTest
    {
        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Log.Error("UI error: " + e.Exception); e.Handled = true; };
            Theme.Init(app);
            int errors = 0;
            void Idle() => app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            void Off(Window w) { w.ShowActivated = false; w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -32000; w.Top = -32000; w.ShowInTaskbar = false; }
            T Find<T>(DependencyObject root, Func<T, bool> ok) where T : DependencyObject
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var c = VisualTreeHelper.GetChild(root, i);
                    if (c is T t && ok(t)) return t;
                    var r = Find(c, ok);
                    if (r != null) return r;
                }
                return null;
            }
            void Press(Window w, string text)
            {
                var b = Find<Button>(w, x => ((x.Content as string) == text || (x.Tag as string) == text) && x.IsVisible);
                if (b == null) { Log.Error($"no visible button '{text}'"); errors++; return; }
                ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
                Idle();
            }
            void Pic(Window w, string name) { Idle(); MainWindowRender(w, Path.Combine(dir, name + ".png")); }

            // 1. the installer
            var look = Program.BuiltInLook();
            var wiz = new Wizard(look); Off(wiz); wiz.Show(); Idle();
            Log.Info("installer opened: " + wiz.Title);
            Pic(wiz, "wiz-1-welcome");
            Press(wiz, "Continue"); Pic(wiz, "wiz-2-colour");
            Press(wiz, "colour:#F05A22"); Log.Info("picked orange: buttons now " + ((SolidColorBrush)app.Resources["Accent"]).Color);
            Pic(wiz, "wiz-2b-orange");
            Press(wiz, "colour:#2283F0"); Log.Info("picked blue: buttons now " + ((SolidColorBrush)app.Resources["Accent"]).Color);
            Press(wiz, "Continue"); Pic(wiz, "wiz-3-light-dark");
            Log.Info("time boxes hidden at first: " + (Find<TextBox>(wiz, t => t.IsVisible) == null));
            Press(wiz, "when:times"); Log.Info("Times I choose shows the time boxes: " + (Find<TextBox>(wiz, t => t.IsVisible && t.Text.Contains(":")) != null));
            Pic(wiz, "wiz-3b-set-times");
            Press(wiz, "apps:light"); Log.Info("Always light hides when-to-switch: " + (Find<Button>(wiz, x => (x.Tag as string) == "when:place" && x.IsVisible) == null));
            Press(wiz, "apps:switch"); Press(wiz, "when:here");
            Log.Info("no named place in the shared look: " + (Find<Button>(wiz, x => (x.Tag as string) == "when:place") == null));
            Press(wiz, "Continue"); Pic(wiz, "wiz-4-what");
            Press(wiz, "part:fileConverter"); Pic(wiz, "wiz-4b-unticked"); Press(wiz, "part:fileConverter");
            var install = Find<Button>(wiz, x => (x.Tag as string) == "Install" && x.IsVisible);
            Log.Info("Install button there (not pressed): " + (install != null));
            Press(wiz, "Go Back"); Pic(wiz, "wiz-back");
            wiz.Close();

            // 2. the Glasshouse window
            var mw = new MainWindow(null); Off(mw); mw.Show(); Idle();
            System.Threading.Thread.Sleep(500); Idle();
            Log.Info("window opened: " + mw.Title);
            foreach (var page in new[] { "Appearance", "Dock", "Window previews", "Pointers and apps", "Look files", "Repair" })
            {
                var nav = Find<RadioButton>(mw, r => (r.Tag as string) == page);
                if (nav == null) { Log.Error("no sidebar item " + page); errors++; continue; }
                nav.IsChecked = true; Idle();
                if (page == "Repair") { var until = DateTime.Now.AddSeconds(8); while (DateTime.Now < until) { Idle(); System.Threading.Thread.Sleep(100); } }
                Pic(mw, "win-" + page.ToLowerInvariant().Replace(' ', '-'));
            }
            mw.Close();
            Log.Info(errors == 0 ? "uitest: every step worked" : $"uitest: {errors} problem(s)");
            return errors == 0 ? 0 : 1;
        }

        static void MainWindowRender(Window w, string file)
        {
            var c = (FrameworkElement)w.Content;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(c.ActualWidth * 1.5), (int)(c.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(Theme.Res("Win"), null, new Rect(0, 0, c.ActualWidth, c.ActualHeight));
            bmp.Render(bg); bmp.Render(c);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var f = File.Create(file)) enc.Save(f);
        }
    }
}
