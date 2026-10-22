using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Glasshouse.Ui.Kit;

namespace Glasshouse.Ui
{
    public partial class MainWindow
    {
        static readonly (string hex, string name)[] Swatches =
        {
            ("#F05A22", "Lava Orange"), ("#2283F0", "Blue"), ("#A550A7", "Purple"), ("#F74F9E", "Pink"),
            ("#FF5257", "Red"), ("#FFC600", "Yellow"), ("#62BA46", "Green"), ("#8C8C8C", "Graphite")
        };

        // ---------- Appearance ----------
        void PageAppearance()
        {
            var look = ed.Look;
            Add(Title("Appearance"), Caption("Accent colour"));
            string AccentSub(string hex) => $"This computer: {Swatches.FirstOrDefault(s => s.hex == hex).name ?? "your own colour"} {hex}. Shows on the top search result, switches and highlights.";
            var sw = new StackPanel { Orientation = Orientation.Horizontal };
            var colourRow = Row("Colour", sw, AccentSub(look.Accent));
            var customRow = new Grid { Visibility = Visibility.Collapsed };
            var list = Swatches.Select(s => s.hex).ToList();
            if (!list.Contains(look.Accent)) list.Insert(0, look.Accent);
            foreach (var hex in list)
            {
                var rb = new RadioButton { Style = S("Swatch"), Background = Theme.Brush(hex), GroupName = "accent", IsChecked = hex == look.Accent, ToolTip = hex, Margin = new Thickness(0, 0, 2, 0) };
                rb.Checked += (s, e) => SetAccent(hex, colourRow, AccentSub);
                sw.Children.Add(rb);
            }
            var hexBox = Field(look.Accent, 110);
            var customBtn = Button("Custom...", () => customRow.Visibility = customRow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
            customBtn.Margin = new Thickness(8, 0, 0, 0);
            sw.Children.Add(customBtn);
            var useHex = Button("Use", () =>
            {
                try { var hex = Glasshouse.Colors.Normalise(hexBox.Text); foreach (var r in sw.Children.OfType<RadioButton>()) r.IsChecked = false; SetAccent(hex, colourRow, AccentSub); }
                catch { hexBox.Text = ed.Look.Accent; }
            });
            var cp = new StackPanel { Orientation = Orientation.Horizontal }; cp.Children.Add(hexBox); useHex.Margin = new Thickness(8, 0, 0, 0); cp.Children.Add(useHex);
            customRow.Children.Add(Row("Your own colour", cp, "A hex colour, like #2283F0"));
            Add(Group(colourRow, customRow));

            Add(Caption("Light and dark"));
            if (!AutoDarkMode.Installed) Add(Note("Auto Dark Mode isn't installed. Set it up from Repair."));
            var ld = look.LightDark ?? new LightDark();
            int appsIx = ld.Apps == "light" ? 1 : ld.Apps == "dark" ? 2 : 0;
            var appsRow = Row("Apps", Segmented(new[] { ld.CustomTimes ? "Switch at set times" : "Switch at sunset", "Light", "Dark" }, appsIx,
                i => { ed.Change("lightdark", l => l.LightDark.Apps = i == 1 ? "light" : i == 2 ? "dark" : "switch"); }), SunLine(ld));
            var winRow = Row("Windows itself (taskbar, Start, File Explorer)", Segmented(new[] { "Switch", "Always dark" }, ld.Windows == "dark" ? 1 : 0,
                i => ed.Change("lightdark", l => l.LightDark.Windows = i == 1 ? "dark" : "switch")));
            var placeEditor = PlaceEditor(ld, () => Show("Appearance"));
            placeEditor.Visibility = Visibility.Collapsed;
            var placeBtn = new Button { Style = S("Link") };
            var pb = new StackPanel { Orientation = Orientation.Horizontal };
            pb.Children.Add(Text(ld.CustomTimes ? $"Set times {ld.LightAt} and {ld.DarkAt}" : ld.UseWindowsLocation ? "Where Windows says this computer is" : (string.IsNullOrEmpty(ld.Place) ? $"{ld.Lat:0.###}, {ld.Lon:0.###}" : ld.Place), 13, "Sub"));
            var chev = Icon("", 11, "Sub"); chev.Margin = new Thickness(10, 0, 0, 0); pb.Children.Add(chev);
            placeBtn.Content = pb;
            placeBtn.Click += (s, e) => placeEditor.Visibility = placeEditor.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            Add(Group(appsRow, winRow, Row("Where you are", placeBtn, "Sets sunrise and sunset. Or pick your own times."), placeEditor));

            Add(Caption("Wallpaper"));
            var img = Wallpaper.CurrentImage(look.Wallpaper);
            var thumb = new Border { Width = 86, Height = 54, CornerRadius = new CornerRadius(6) };
            if (img != null) try
                {
                    var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(img); bi.DecodePixelWidth = 172; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
                    thumb.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                }
                catch { }
            else thumb.SetResourceReference(Border.BackgroundProperty, "Ctl");
            var change = Button("Change...", () => { });
            change.Click += async (s, e) => await WallpaperMenu(change);
            Add(Group(Row(string.IsNullOrEmpty(look.Wallpaper) ? "No moving wallpaper" : look.Wallpaper.Replace('_', ' '), change,
                string.IsNullOrEmpty(look.Wallpaper) ? "Your own wallpaper stays" : "Changes through the day with the sun (WinDynamicDesktop)", thumb)));
        }

        void SetAccent(string hex, Grid row, Func<string, string> sub)
        {
            ed.Change("accent", l => l.Accent = hex);
            Theme.SetAccent(Application.Current, hex);
            SetSub(row, sub(hex));
        }

        static string SunLine(LightDark ld)
        {
            if (ld.CustomTimes) return $"Light from {ld.LightAt}, dark from {ld.DarkAt}";
            if (ld.UseWindowsLocation) return "Light from sunrise, dark from sunset, wherever this computer is";
            var t = Sun.Times(ld.Lat, ld.Lon, DateTime.Today);
            return t == null ? "" : $"Light from sunrise {Sun.Clock(t.Value.rise)}, dark from sunset {Sun.Clock(t.Value.set)}";
        }

        FrameworkElement PlaceEditor(LightDark ld, Action done)
        {
            var place = Field(ld.Place, 200); var lat = Field(ld.Lat.ToString(CultureInfo.InvariantCulture), 90); var lon = Field(ld.Lon.ToString(CultureInfo.InvariantCulture), 90);
            var light = Field(ld.LightAt, 70); var dark = Field(ld.DarkAt, 70);
            var times = Toggle(ld.CustomTimes, _ => { });
            var winLoc = Toggle(ld.UseWindowsLocation, _ => { });
            var sp = new StackPanel { Margin = new Thickness(14, 4, 14, 12) };
            StackPanel Line(params UIElement[] els) { var l = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) }; foreach (var e in els) { if (e is FrameworkElement f) f.Margin = new Thickness(0, 0, 8, 0); l.Children.Add(e); } return l; }
            TextBlock L(string t) { var x = Text(t, 12.5, "Sub"); x.VerticalAlignment = VerticalAlignment.Center; return x; }
            sp.Children.Add(Line(L("Use where Windows says this computer is"), winLoc));
            sp.Children.Add(Line(L("Or a place:"), place, L("Latitude"), lat, L("Longitude"), lon));
            sp.Children.Add(Line(L("Use set times instead of the sun"), times, L("Light at"), light, L("Dark at"), dark));
            sp.Children.Add(Line(Button("Save", () =>
            {
                double.TryParse(lat.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var la);
                double.TryParse(lon.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo);
                bool okTime(string t) => TimeSpan.TryParseExact(t, @"hh\:mm", CultureInfo.InvariantCulture, out _);
                ed.Change("lightdark", l =>
                {
                    l.LightDark.Place = place.Text.Trim(); l.LightDark.Lat = la; l.LightDark.Lon = lo;
                    l.LightDark.CustomTimes = times.IsChecked == true;
                    l.LightDark.UseWindowsLocation = winLoc.IsChecked == true && !l.LightDark.CustomTimes;
                    if (okTime(light.Text)) l.LightDark.LightAt = light.Text; if (okTime(dark.Text)) l.LightDark.DarkAt = dark.Text;
                });
                ed.Change("wallpaper", l => { });   // the wallpaper app follows the same place
                done();
            }, true)));
            return sp;
        }

        async Task WallpaperMenu(Button anchor)
        {
            var menu = new ContextMenu { Style = S("Menu"), PlacementTarget = anchor };
            List<string> themes;
            try { themes = await Task.Run(() => Net.GetText("https://raw.githubusercontent.com/t1m0thyj/WinDynamicDesktop/main/src/resources/default_themes.yaml").Replace("\r", "").Split('\n').Where(l => l.Length > 1 && !l.StartsWith("-") && !l.StartsWith("#") && l.TrimEnd().EndsWith(":")).Select(l => l.TrimEnd().TrimEnd(':')).ToList()); }
            catch { themes = new List<string> { "Catalina", "Big_Sur", "Mojave_Desert" }; }
            foreach (var t in themes.Concat(new[] { "" }))
            {
                var mi = new MenuItem { Header = t.Length == 0 ? "None (keep your own wallpaper)" : t.Replace('_', ' ') + (t == ed.Look.Wallpaper ? "   (now)" : "") };
                mi.Click += (s, e) => { ed.Change("wallpaper", l => l.Wallpaper = t); status.Text = t.Length > 0 ? "Downloading " + t.Replace('_', ' ') + "..." : "Saved"; Dispatcher.BeginInvoke(new Action(() => Show("Appearance")), System.Windows.Threading.DispatcherPriority.Background); };
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        // ---------- Dock ----------
        void PageDock()
        {
            Add(Title("Dock"));
            if (ed.M(Editor.Dock) == null) { Add(Note("The Dock mod (MacOS Style Dock) isn't in this look.")); return; }
            Add(Group(
                Row("Size", Slider(0, 100, ed.Int(Editor.Dock, "dock.size", 55), v => $"{v}", v => ed.SetInt(Editor.Dock, "dock.size", (long)v))),
                Row("Magnification", Slider(0, 100, ed.Int(Editor.Dock, "magnification.amount", 60), Pct, v => ed.SetInt(Editor.Dock, "magnification.amount", (long)v), "Off", "Max")),
                Row("Magnification spread", Slider(0, 100, ed.Int(Editor.Dock, "magnification.range", 55), v => $"{v}", v => ed.SetInt(Editor.Dock, "magnification.range", (long)v))),
                Row("Automatically hide and show the Dock", Toggle(ed.Look.Tidy.AutoHideDock, on => ed.Change("autohide", l =>
                {
                    l.Tidy.AutoHideDock = on;
                    l.Mod(Editor.Dock).Settings["magnification.overWindows"] = on ? 0L : 1L;   // the mod owns auto-hide when it magnifies over windows
                })), ed.Look.Tidy.AutoHideDock ? "Windows get the full screen height; the Dock slides up at the bottom edge" : "The Dock keeps its own strip at the bottom of the screen")));
            Add(Caption("Glass"));
            var col = ed.Str(Editor.Dock, "dock.color", "auto");
            Add(Group(
                Row("Colour", Segmented(new[] { "System", "Dark", "Light" }, col == "dark" ? 1 : col == "light" ? 2 : 0, i => ed.SetStr(Editor.Dock, "dock.color", i == 1 ? "dark" : i == 2 ? "light" : "auto"))),
                Row("Blur", Slider(0, 100, ed.Int(Editor.Dock, "dock.blur", 35), v => $"{v}", v => ed.SetInt(Editor.Dock, "dock.blur", (long)v))),
                Row("Tint", Slider(0, 100, ed.Int(Editor.Dock, "dock.tint", 20), Pct, v => ed.SetInt(Editor.Dock, "dock.tint", (long)v))),
                Row("Show Recycle Bin", Toggle(ed.Int(Editor.Dock, "items.recycleBin", 1) == 1, on => ed.SetInt(Editor.Dock, "items.recycleBin", on ? 1 : 0))),
                Row("Divider after File Explorer", Toggle(ed.Int(Editor.Dock, "items.appsDivider", 1) == 1, on => ed.SetInt(Editor.Dock, "items.appsDivider", on ? 1 : 0)))));
        }

        // ---------- Menu bar ----------
        void PageMenuBar()
        {
            Add(Title("Menu bar"));
            if (ed.M(Editor.Island) == null) { Add(Note("The menu bar mod (Mac Island) isn't in this look.")); return; }
            var ics = Field(ed.Str(Editor.Island, "calendarIcs[0]", ""), 300);
            var save = Button("Save", () => ed.SetStr(Editor.Island, "calendarIcs[0]", ics.Text.Trim()));
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(ics); save.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(save);
            Add(Group(
                Row("Menu bar at the top", Toggle(ed.Int(Editor.Island, "showIsland", 1) == 1, on => ed.SetInt(Editor.Island, "showIsland", on ? 1 : 0)), "The clock, control centre, notifications and tray icons, in a pill at the top"),
                Row("Calendar", row, "An .ics link, like Google Calendar's secret iCal address, to see events in the bar's calendar")));
            Add(Note("Its style, colours and what it shows are set in the bar itself: right-click it, then click the gear that appears under it."));
        }

        // ---------- Window previews ----------
        void PagePreviews()
        {
            Add(Title("Window previews"));
            Add(Group(
                Row("Size", Slider(70, 150, Math.Round(ed.PreviewScale * 100), Pct, v => ed.SetPreviewScale(v / 100)), "Pictures of each window when you hover an app in the Dock"),
                Row("Darkness behind them", Slider(0, 100, Math.Round(ed.PreviewTint * 100), Pct, v => ed.SetPreviewTint(v / 100)), "Keeps the white window names readable over light windows")));
            Add(Note("Some preview changes show after the taskbar restarts (the link appears at the bottom left)."));
        }

        // ---------- Glass and hover ----------
        void PageGlass()
        {
            Add(Title("Glass and hover"), Caption("Start, search and File Explorer"));
            double strength = ed.GlassStrength, blur = ed.GlassBlur;
            Add(Group(
                Row("Glass darkness", Slider(0, 60, Math.Round(strength * 100), Pct, v => { strength = v / 100; ed.SetGlass(strength, blur); })),
                Row("Blur", Slider(0, 40, blur, v => $"{v}", v => { blur = v; ed.SetGlass(strength, blur); })),
                Row("Hover", Slider(0, 25, Math.Round(ed.HoverLift * 100), Pct, v => ed.SetHoverLift(v / 100)), "How bright things get under the pointer, the same everywhere")));
            Add(Group(Row("Keep them dark all day", Toggle(ed.DarkShell, on => ed.SetDarkShell(on)), "Start, search and File Explorer stay dark while your apps switch to light by day")));
            var glass = ed.M(Windhawk.GlassModId);
            if (glass != null)
                Add(Group(Row("Window glass", Text(glass.Disabled ? "Off" : glass.Include.StartsWith("%SystemRoot%") ? "Windows' own programs" : "On", 13, "Sub"),
                    "See-through backgrounds for File Explorer, Task Manager, Control Panel and the rest of Windows. Apps you install draw their own design and are left alone, so they can't glitch or crash.")));
        }

        // ---------- Pointers and apps ----------
        void PagePointers()
        {
            var t = ed.Look.Tidy;
            Add(Title("Pointers and apps"));
            Add(Group(Row("macOS-style pointers", Toggle(ed.Look.MacPointers, on => ed.Change("pointers", l => l.MacPointers = on)), "macOS-style mouse pointers (apple_cursor)")));
            Add(Caption("Desktop"));
            Add(Group(
                Row("Hide desktop icons", Toggle(t.HideDesktopIcons, on => ed.Change("tidy", l => l.Tidy.HideDesktopIcons = on))),
                Row("Hide Task view, Copilot and search buttons", Toggle(t.HideTaskbarExtras, on => ed.Change("tidy", l => l.Tidy.HideTaskbarExtras = on))),
                Row("No recent files or suggestions in Start", Toggle(t.StartNoRecent, on => ed.Change("tidy", l => l.Tidy.StartNoRecent = on))),
                Row("Accent colour on title bars", Toggle(t.AccentTitleBars, on => ed.Change("tidy", l => l.Tidy.AccentTitleBars = on))),
                Row("Transparency effects", Toggle(t.Transparency, on => ed.Change("tidy", l => l.Tidy.Transparency = on)), "The glass needs this on"),
                Row("Windows sounds off", Toggle(t.QuietSounds, on => ed.Change("tidy", l => l.Tidy.QuietSounds = on)), "Turning it on again: Settings > Sound > More sound settings"),
                Row("Task Manager always dark", Toggle(t.DarkTaskManager, on => ed.Change("tidy", l => l.Tidy.DarkTaskManager = on)), "In light mode the glass makes its list unreadable")));
            Add(Caption("Small fixes for this computer"));
            Add(Group(PersonalItem.Known.Select(p => (UIElement)Row(p.Label, Toggle(ed.Look.Personal.Contains(p.Id), on =>
            {
                string area = p.Id == "phone-panel-hidden" ? "phone" : "mod:" + PersonalItem.Rules[p.Id].mod;
                ed.Change(area, l => { if (on) l.Personal.Add(p.Id); else l.Personal.Remove(p.Id); });
            }), p.Detail)).ToArray()));
            Add(Caption("Apps"));
            Add(Group(
                AppRow("Raycast", "Search everything with Alt+Space", AppPaths.RaycastInstalled, Shell.Running("Raycast"), () => Shell.AsUser("explorer.exe", @"shell:AppsFolder\Raycast.Raycast_qypenmj9wpt2a!Raycast", null), "9PFXXSHC64H3", "msstore"),
                AppRow("PowerToys", "Peek with Ctrl+Space, window zones and more", PowerToys.Installed, Shell.Running("PowerToys"), () => Shell.AsUser(AppPaths.PowerToysExe, "", null), "Microsoft.PowerToys", "winget"),
                AppRow("File Converter", "Right-click any file, then Convert", AppPaths.FileConverterInstalled, true, null, "AdrienAllard.FileConverter", "winget")));
        }

        Grid AppRow(string name, string what, bool installed, bool running, Action open, string id, string source)
        {
            UIElement ctl;
            if (!installed)
                ctl = Button("Install", async () =>
                {
                    status.Text = "Installing " + name + "...";
                    bool ok = await Task.Run(() => Winget.Install(id, source, source == "winget" && id == "Microsoft.PowerToys" ? "user" : null));
                    status.Text = ok ? name + " installed" : "Couldn't install " + name;
                    Show("Pointers and apps");
                });
            else if (!running && open != null) ctl = Button("Open", open);
            else ctl = Text(running ? "Running" : "Installed", 12.5, "Sub");
            return Row(name, ctl, what, Dot(installed ? "good" : "todo"));
        }
    }
}
