using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Glasshouse.Ui.Kit;

namespace Glasshouse.Ui
{
    // Appearance > "Apps that switch with it", and its "Add an app" sheet (see FollowApps).
    public partial class MainWindow
    {
        void FollowSection()
        {
            Add(Caption("Apps that switch with it"));
            var rows = new List<UIElement>();
            foreach (var kv in ed.Look.FollowApps)
            {
                var app = FollowApps.Find(kv.Key);
                if (app != null && app.Installed && !app.NoSetting) rows.Add(FollowRow(app, kv.Value));
            }
            var add = new Button { Style = S("Link"), Content = Text("+ Add an app...", 13.5, "Accent"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 12, 14, 12) };
            add.Click += (s, e) => AddAppSheet();
            rows.Add(add);
            Add(Group(rows.ToArray()));
            Add(Note("Each app keeps its own light and dark and switches at the same moment as Windows. A switch means Glasshouse sets it for you; turning it off puts back what it was. A button means only you can change that app's setting, so Glasshouse shows you the one place to click."));
        }

        static string Short(FollowApp app) => app.Name.StartsWith("Google ") ? app.Name.Substring(7) : app.Name.StartsWith("Microsoft ") ? app.Name.Substring(10) : app.Name;

        // What an automatic app's row says. "predict" = the switch was just moved and the change hasn't landed yet.
        static string FollowLine(FollowApp app, bool on, bool predict = false)
        {
            bool waits = app.WaitsForClose && app.Running;
            if (!on) return (predict ? waits : FollowApps.Waiting(app.Id)) ? $"Puts back your own setting the next time you quit {Short(app)} (its menu > Exit)" : $"Off: {Short(app)} keeps its own light and dark";
            bool follows = predict ? !waits || app.Follows : app.Follows;
            if (follows) return app is FollowApps.CodeEditor ? "Switches between your light and your dark theme" : $"Follows Windows (\"{app.FollowName}\")";
            if (waits) return $"Set to \"{app.FollowName}\" the next time you quit {Short(app)} (its menu > Exit)";
            return "Doesn't follow Windows yet: Repair sets it";
        }

        Grid FollowRow(FollowApp app, bool on)
        {
            Grid row = null;
            UIElement ctl;
            if (app.Automatic)
                ctl = Toggle(on, v =>
                {
                    ed.Change("follow", l => l.FollowApps[app.Id] = v);
                    SetSub(row, FollowLine(app, v, predict: true));
                    // once the change has landed, say what really happened
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (s, e) => { t.Stop(); SetSub(row, FollowLine(app, ed.Look.FollowApps.TryGetValue(app.Id, out var now) && now)); };
                    t.Start();
                });
            else ctl = Button("Open " + Short(app), app.Open);
            row = Row(app.Name, ctl, app.Automatic ? FollowLine(app, on) : $"Once in {Short(app)}: {app.Where}", AppImage(app, 26));
            var menu = new ContextMenu { Style = S("Menu") };
            var remove = new MenuItem { Header = "Remove from this list" };
            remove.Click += (s, e) => { ed.Change("follow", l => l.FollowApps.Remove(app.Id)); Show("Appearance"); };
            menu.Items.Add(remove);
            row.ContextMenu = menu;
            row.Background = Brushes.Transparent;   // right-click anywhere on the row
            return row;
        }

        void AddAppSheet()
        {
            var sheet = new StackPanel { Width = 470 };
            sheet.Children.Add(Text("Add an app", 15, "Text", FontWeights.SemiBold));
            var lead = Text("Apps on this computer that have their own light and dark. Automatic: Glasshouse sets it. One step: you click one setting in the app, once.", 12.5, "Sub");
            lead.Margin = new Thickness(0, 4, 0, 14); sheet.Children.Add(lead);

            var picks = new Dictionary<string, CheckBox>();
            Button add = null;
            void Count()
            {
                int n = picks.Count(p => p.Value.IsChecked == true);
                add.Content = n == 1 ? "Add 1 app" : $"Add {n} apps";
                add.IsEnabled = n > 0;
            }
            var found = FollowApps.All.Where(a => !a.NoSetting && a.Installed && !ed.Look.FollowApps.ContainsKey(a.Id)).ToList();
            if (found.Count == 0) { var none = Text("Every app found on this computer is already in the list.", 12.5, "Sub"); none.Margin = new Thickness(4, 0, 4, 18); sheet.Children.Add(none); }
            else sheet.Children.Add(Group(found.Select(a =>
            {
                bool follows = a.Automatic && a.Follows;
                var c = Check(a.Automatic && !follows, _ => Count());
                picks[a.Id] = c;
                var left = new StackPanel { Orientation = Orientation.Horizontal };
                c.Margin = new Thickness(0, 0, 12, 0); left.Children.Add(c); left.Children.Add(AppImage(a, 24));
                var row = Row(a.Name, Text(a.Automatic ? "Automatic" : "One step", 12, "Sub"), follows ? "Already follows Windows" : null, left);
                row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (s, e) => { if (!c.IsMouseOver) c.IsChecked = c.IsChecked != true; };
                return (UIElement)row;
            }).ToArray()));

            var dark = FollowApps.All.Where(a => a.NoSetting && a.Installed).ToList();
            if (dark.Count > 0)
            {
                sheet.Children.Add(Caption("No light and dark to follow"));
                var g = Group(dark.Select(a => (UIElement)Row(a.Name, null, null, AppImage(a, 24))).ToArray());
                g.Opacity = 0.55;
                sheet.Children.Add(g);
            }
            var cancel = Button("Cancel", CloseSheet);
            add = Button("Add", () =>
            {
                var ids = picks.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
                if (ids.Count == 0) return;
                ed.Change("follow", l => { foreach (var id in ids) l.FollowApps[id] = true; });
                CloseSheet();
                Show("Appearance");
            }, true);
            Count();
            sheet.Children.Add(Buttons(cancel, add));
            ShowSheet(sheet);
        }

        // ---------- app pictures: the Store app's logo, or the program's own icon at a size that stays sharp ----------
        static readonly Dictionary<string, ImageSource> AppPictures = new Dictionary<string, ImageSource>();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        static FrameworkElement AppImage(FollowApp app, double size)
        {
            if (!AppPictures.TryGetValue(app.Id, out var src))
            {
                try
                {
                    if (app.Logo != null)
                    {
                        var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(app.Logo); bi.DecodePixelWidth = 64; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
                        bi.Freeze(); src = bi;
                    }
                    else if (app.Exe != null)
                    {
                        var h = new IntPtr[1]; var id = new uint[1];
                        if (PrivateExtractIcons(app.Exe, 0, 64, 64, h, id, 1, 0) == 1 && h[0] != IntPtr.Zero)
                        {
                            src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(h[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            DestroyIcon(h[0]);
                        }
                        else using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(app.Exe))
                                src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src?.Freeze();
                    }
                }
                catch { src = null; }
                AppPictures[app.Id] = src;
            }
            if (src != null) return new Image { Source = src, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(6), Background = Theme.Brush("#8E8E93") };
            var letter = Text(app.Name.Substring(0, 1), 12, "Text", FontWeights.Bold); letter.Foreground = Brushes.White;
            letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
            tile.Child = letter;
            return tile;
        }
    }
}
