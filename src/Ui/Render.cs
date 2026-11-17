using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glasshouse.Ui
{
    // --render <folder> [--light|--dark]: draws every page to a PNG without showing a window (for checking the
    // window's look while someone is using the computer).
    public partial class MainWindow
    {
        public static int RenderAll(string dir, bool? dark, bool sampleApps = false)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Init(app);
            if (dark.HasValue) Theme.Force(app, dark.Value);
            var w = new MainWindow(null, render: true);
            Directory.CreateDirectory(dir);
            w.ed = w.NewEditor(Editor.LoadCurrent());
            w.ed.Offline = true;   // a picture only: nothing it touches is applied
            // --sample-apps: an example "Apps that switch with it" list, for checking the page on a computer with none
            if (sampleApps && w.ed.Look.FollowApps.Count == 0)
                foreach (var id in new[] { "brave", "chrome", "office", "vscode", "discord", "whatsapp" }) w.ed.Look.FollowApps[id] = true;
            Theme.SetAccent(app, w.ed.Look.Accent);
            foreach (var p in Pages.Where(p => p.name != "-").Select(p => p.name))
            {
                w.Show(p);
                SaveRender(w, Path.Combine(dir, p.ToLowerInvariant().Replace(' ', '-') + ".png"));
            }
            // the whole Appearance page (it scrolls in the window) and its "Add an app" sheet
            double h = w.Height;
            w.Height = 1240; w.Show("Appearance"); SaveRender(w, Path.Combine(dir, "appearance-full.png"));
            w.Height = 1100; w.Show("Appearance"); w.AddAppSheet(); SaveRender(w, Path.Combine(dir, "appearance-add-app.png")); w.CloseSheet(); w.Height = h;
            var wiz = new Wizard(Program.BuiltInLook());
            for (int i = 0; i < 6; i++) { wiz.Go(i); SaveRender(wiz, Path.Combine(dir, $"wizard-{i + 1}.png")); }
            return 0;
        }

        static void SaveRender(Window w, string file)
        {
            var c = (FrameworkElement)w.Content;
            // windows that size themselves to their content (the installer) have no Width/Height of their own
            double Width = !double.IsNaN(w.Width) ? w.Width : c.Width, Height = !double.IsNaN(w.Height) ? w.Height : c.Height + 34;
            c.Measure(new Size(Width, Height - 34));
            c.Arrange(new Rect(0, 0, Width, Height - 34));
            c.UpdateLayout();
            w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bmp = new RenderTargetBitmap((int)(Width * 2), (int)((Height - 34) * 2), 192, 192, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(Theme.Res("Win"), null, new Rect(0, 0, Width, Height));
            bmp.Render(bg);
            bmp.Render(c);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(file)) enc.Save(f);
        }
    }
}
