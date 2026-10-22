using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static Glasshouse.Ui.Kit;

namespace Glasshouse.Ui
{
    public partial class MainWindow
    {
        // ---------- Look files ----------
        void PageLooks()
        {
            Add(Title("Look files"));
            Add(Group(
                Row("Save this look", Button("Save...", SaveLook), "One small file with every setting. Send it to a friend or keep it for a new computer."),
                Row("Open a look", Button("Open...", () =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Filter = "Glasshouse files (*.glasshouse)|*.glasshouse", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
                    if (d.ShowDialog(this) == true) OpenLookSheet(d.FileName);
                }), "You choose what to bring in before anything changes")));
            Add(Note("A look file carries everything on this computer except its \"small fixes\" (Pointers and apps), which stay off when the look is opened somewhere else unless they're ticked. The safety exclusions for Tailscale and Claude's service always go with it."));
        }

        void SaveLook()
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Glasshouse files (*.glasshouse)|*.glasshouse", FileName = Environment.MachineName + " look.glasshouse",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (d.ShowDialog(this) != true) return;
            var look = Look.FromJsonText(ed.Look.ToJsonText());
            look.Name = System.IO.Path.GetFileNameWithoutExtension(d.FileName);
            look.SavedFrom = Environment.MachineName;
            look.SavedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            look.Save(d.FileName);
            status.Text = "Saved " + System.IO.Path.GetFileName(d.FileName);
        }

        // The "Open a look" sheet: what to bring in, personal bits unticked unless it came from this computer.
        void OpenLookSheet(string path)
        {
            Look look;
            try { look = Look.Load(path); }
            catch (Exception e) { MessageBox.Show(this, "That isn't a Glasshouse file: " + e.Message, "Glasshouse"); return; }
            bool here = string.Equals(look.SavedFrom, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            var sheet = new StackPanel { Width = 540 };
            sheet.Children.Add(Text($"Open \"{System.IO.Path.GetFileName(path)}\"", 15, "Text", FontWeights.SemiBold));
            var sub = Text($"Saved on {look.SavedFrom}{(DateTime.TryParse(look.SavedAt, out var at) ? ", " + at.ToString("d MMM yyyy") : "")}. Choose what to bring onto this computer.", 12.5, "Sub");
            sub.Margin = new Thickness(0, 4, 0, 14); sheet.Children.Add(sub);
            var cMods = Check(true); var cRest = Check(true); var cTidy = Check(true);
            string mine = Glasshouse.Accent.Current() ?? "#2283F0";
            bool sameAccent = string.Equals(mine, look.Accent, StringComparison.OrdinalIgnoreCase);
            var cAccent = Check(!sameAccent);
            var dotMine = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Theme.Brush(mine) };
            sheet.Children.Add(Group(
                Row("Dock, menu bar, previews, glass and hover", null, $"{look.Mods.Count} Windhawk mods with the same settings", cMods),
                Row("Light and dark, wallpaper, pointers", null, null, cRest),
                Row("Desktop tidy-up", null, "Desktop icons, taskbar buttons, Start suggestions, sounds", cTidy),
                sameAccent ? null : Row("Keep this computer's accent colour", dotMine, $"{mine} stays. Untick to use the file's {look.Accent}.", cAccent)));
            var personalChecks = new Dictionary<string, CheckBox>();
            if (look.Personal.Count > 0)
            {
                sheet.Children.Add(Caption(here ? "Personal to this computer" : $"Personal to {look.SavedFrom} (off unless you tick them)"));
                sheet.Children.Add(Group(PersonalItem.Known.Where(p => look.Personal.Contains(p.Id)).Select(p =>
                {
                    var c = Check(here); personalChecks[p.Id] = c;
                    return (UIElement)Row(p.Label, null, p.Detail, c);
                }).ToArray()));
            }
            var note = Text("Always kept: window glass skips Tailscale and Claude's background service, so they never crash.", 12, "Sub");
            note.Margin = new Thickness(0, -6, 0, 14); sheet.Children.Add(note);
            var progress = Text("", 12, "Sub"); progress.Margin = new Thickness(0, 0, 0, 10);
            sheet.Children.Add(progress);
            Button apply = null, cancel = null;
            cancel = Button("Cancel", CloseSheet);
            apply = Button("Apply", async () =>
            {
                apply.IsEnabled = cancel.IsEnabled = false;
                var o = new Machine.Options
                {
                    Mods = cMods.IsChecked == true, LightDark = cRest.IsChecked == true, Wallpaper = cRest.IsChecked == true, Pointers = cRest.IsChecked == true,
                    Tidy = cTidy.IsChecked == true, Accent = cAccent.IsChecked == true || sameAccent ? mine : look.Accent,
                    PersonalOn = new HashSet<string>(personalChecks.Where(k => k.Value.IsChecked == true).Select(k => k.Key))
                };
                Action<string> line = l => Dispatcher.BeginInvoke(new Action(() => progress.Text = l.Trim()));
                Log.Line += line;
                string err = null;
                await Task.Run(() => { try { Machine.Apply(look, o); } catch (Exception e) { err = e.Message; } });
                Log.Line -= line;
                if (err != null) { progress.Text = "Couldn't finish: " + err; apply.IsEnabled = cancel.IsEnabled = true; return; }
                ed = NewEditor(Editor.LoadCurrent());
                Theme.SetAccent(Application.Current, ed.Look.Accent);
                CloseSheet();
                status.Text = "Look applied";
                Show(current);
            }, true);
            sheet.Children.Add(Buttons(cancel, apply));
            ShowSheet(sheet);
        }

        void ShowSheet(UIElement content)
        {
            overlay.Children.Clear();
            var b = new Border
            {
                Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(24, 22, 24, 8),
                CornerRadius = new CornerRadius(0, 0, 12, 12), BorderThickness = new Thickness(1, 0, 1, 1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                MaxHeight = ActualHeight - 60, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.45 }
            };
            b.SetResourceReference(Border.BackgroundProperty, "Win");
            b.SetResourceReference(Border.BorderBrushProperty, "Line");
            overlay.Children.Add(b);
            overlay.Visibility = Visibility.Visible;
        }

        void CloseSheet() { overlay.Visibility = Visibility.Collapsed; overlay.Children.Clear(); }

        // ---------- Repair ----------
        void PageRepair()
        {
            Add(Title("Repair"));
            var list = new StackPanel();
            list.Children.Add(Text("Checking...", 13, "Sub"));
            var group = Group(list);
            var log = Text("", 12, "Sub"); log.Margin = new Thickness(4, 0, 4, 0);
            Button repair = null;
            repair = Button("Repair everything", async () =>
            {
                repair.IsEnabled = false;
                var lines = new List<string>();
                Action<string> line = l => Dispatcher.BeginInvoke(new Action(() => { lines.Add(l.Trim()); log.Text = string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 12))); }));
                Log.Line += line;
                string err = null;
                var look = ed.Look;
                await Task.Run(() => { try { Repair.Run(look, new Machine.Options { PersonalOn = look.Personal }); } catch (Exception e) { err = e.Message; } });
                Log.Line -= line;
                status.Text = err == null ? "Repaired" : "Repair: " + err;
                ed = NewEditor(Editor.LoadCurrent());
                if (current == "Repair") Show("Repair");
            }, true);
            var again = Button("Run the installer again...", () => { new Wizard(Program.BuiltInLook()) { Owner = this }.ShowDialog(); ed = NewEditor(Editor.LoadCurrent()); Show("Repair"); });
            Add(group, Buttons(again, repair), Note("Use after a Windows or Windhawk update. Puts back anything that changed and restarts the taskbar once. Window glass is only ever changed with Tailscale and Claude's service stopped first."), log);
            var look0 = ed.Look;
            Task.Run(() => Machine.Status(look0)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                list.Children.Clear();
                if (t.Exception != null) { list.Children.Add(Text(t.Exception.InnerException?.Message ?? "", 13, "Sub")); return; }
                bool first = true;
                foreach (var c in t.Result)
                {
                    if (!first) { var line = new Border { Height = 1 }; line.SetResourceReference(Border.BackgroundProperty, "Line"); list.Children.Add(line); }
                    first = false;
                    list.Children.Add(Row(c.Name, null, c.Detail, Dot(c.Ok ? "good" : "bad")));
                }
            })));
        }

        // ---------- Inspector ----------
        void PageInspector()
        {
            Add(Title("Inspector"));
            Add(Note("Takes a real picture of a part of Windows with every piece outlined. Click a piece to see what it's called, to restyle it. Each picture opens that part of Windows for a few seconds, so don't type meanwhile. Also at http://localhost:18911 for Claude's preview pane."));
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var pic = new Grid();
            var details = new StackPanel();
            var info = Text("", 12, "Sub");
            foreach (var s in Inspector.Capture.Surfaces)
            {
                var surface = s[0];
                var b = Button(s[1], async () =>
                {
                    info.Text = "Taking the picture...";
                    try { await Task.Run(() => Inspector.Capture.Take(surface)); info.Text = ""; ShowCapture(surface, pic, details); }
                    catch (Exception e) { info.Text = "Couldn't: " + e.Message; }
                });
                b.Margin = new Thickness(0, 0, 8, 8);
                bar.Children.Add(b);
            }
            Add(bar, info, pic, details);
            var last = Inspector.Capture.All().Cast<Dictionary<string, object>>().OrderByDescending(c => c.Long("stamp")).FirstOrDefault();
            if (last != null) ShowCapture(last.Str("surface"), pic, details);
        }

        void ShowCapture(string surface, Grid host, StackPanel details)
        {
            host.Children.Clear(); details.Children.Clear();
            var f = System.IO.Path.Combine(Inspector.Capture.DataDir, $"cap-{surface}.json");
            if (!File.Exists(f)) return;
            var cap = Json.ParseObject(File.ReadAllText(f));
            var imgPath = System.IO.Path.Combine(Inspector.Capture.DataDir, cap.Str("image"));
            if (!File.Exists(imgPath)) return;
            var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(imgPath); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
            double w = cap.Long("width"), h = cap.Long("height");
            var canvas = new Canvas { Width = w, Height = h, Background = new ImageBrush(bi) };
            Rectangle picked = null;
            foreach (var p in (cap.Arr("parts") ?? new List<object>()).Cast<Dictionary<string, object>>().OrderByDescending(p => p.Long("w") * p.Long("h")))
            {
                var r = new Rectangle { Width = Math.Max(2, p.Long("w")), Height = Math.Max(2, p.Long("h")), Stroke = new SolidColorBrush(Color.FromArgb(110, 191, 90, 242)), StrokeThickness = 1, Fill = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = $"{p.Str("type")} {p.Str("cls")} {p.Str("name")}".Trim() };
                Canvas.SetLeft(r, p.Long("x")); Canvas.SetTop(r, p.Long("y"));
                r.MouseEnter += (s, e) => { if (r != picked) r.Fill = new SolidColorBrush(Color.FromArgb(40, 191, 90, 242)); };
                r.MouseLeave += (s, e) => { if (r != picked) r.Fill = Brushes.Transparent; };
                r.MouseLeftButtonDown += (s, e) =>
                {
                    if (picked != null) { picked.Fill = Brushes.Transparent; picked.StrokeThickness = 1; }
                    picked = r; r.StrokeThickness = 2.5; r.Fill = new SolidColorBrush(Color.FromArgb(50, 191, 90, 242));
                    var sel = new Dictionary<string, object>(p) { ["surface"] = surface };
                    Inspector.Capture.Select(sel);
                    details.Children.Clear();
                    details.Children.Add(Group(
                        Row("Type", Text(p.Str("type"), 13, "Sub")), Row("Windows class", Text(p.Str("cls"), 13, "Sub")),
                        Row("Name", Text(p.Str("name"), 13, "Sub")), Row("AutomationId", Text(p.Str("aid"), 13, "Sub")),
                        Row("Styler target to start from", Text(Inspector.Capture.StylerHint(p), 13, "Sub"))));
                    e.Handled = true;
                };
                canvas.Children.Add(r);
            }
            var vb = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 520, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
            var frame = new Border { Child = vb, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 14) };
            host.Children.Add(frame);
            details.Children.Add(Caption($"{cap.Str("title")}, {cap.Str("time")}: click a piece"));
        }

        // ---------- Set up (first run on a computer) ----------
        void ShowSetup(string lookPath)
        {
            Look look;
            try { look = lookPath != null ? Look.Load(lookPath) : Program.BuiltInLook(); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Glasshouse"); return; }
            foreach (var rb in nav.Children.OfType<RadioButton>()) { rb.IsEnabled = false; rb.IsChecked = false; }
            page.Children.Clear();
            Add(Title("Set up Glasshouse"));
            if (Sac.State == Sac.Mode.On) Add(WarnBox("Smart App Control is on.", "It blocks Windhawk's mods, so the Dock, menu bar and glass won't load. To use them, turn it off in Windows Security, App and browser control. Everything else still works with it on."));
            var parts = Setup.Parts(look).Where(p => p.Wanted).ToList();
            Add(Group(parts.Select(p => (UIElement)Row(p.Name, Text(p.Installed() ? "Installed" : "Will install", 12.5, "Sub"), p.Source, Dot(p.Installed() ? "good" : "todo"))).ToArray()));
            Add(Note($"Look: \"{look.Name}\". Everything downloads from its own official source. Windows asks for admin once, and says \"Unknown publisher\" because Glasshouse isn't signed."));
            var log = Text("", 12, "Sub");
            Button go = null, choose = null;
            choose = Button("Choose a look file...", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = "Glasshouse files (*.glasshouse)|*.glasshouse" };
                if (d.ShowDialog(this) == true) ShowSetup(d.FileName);
            });
            go = Button("Set up", async () =>
            {
                go.IsEnabled = choose.IsEnabled = false;
                var lines = new List<string>();
                Action<string> line = l => Dispatcher.BeginInvoke(new Action(() => { lines.Add(l.Trim()); log.Text = string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 14))); }));
                Log.Line += line;
                string err = null;
                await Task.Run(() => { try { Setup.Run(look, new Machine.Options()); } catch (Exception e) { err = e.Message; } });
                Log.Line -= line;
                if (err != null) { log.Text += "\n\nCouldn't finish: " + err; go.IsEnabled = choose.IsEnabled = true; return; }
                foreach (var rb in nav.Children.OfType<RadioButton>()) rb.IsEnabled = true;
                ed = NewEditor(Editor.LoadCurrent());
                Theme.SetAccent(Application.Current, ed.Look.Accent);
                Show("Appearance");
                status.Text = "Set up finished";
            }, true);
            Add(Buttons(choose, go), log);
        }
    }
}
