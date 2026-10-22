using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Glasshouse.Ui
{
    // The installer anyone can run: double-click, choose, done, and change anything later in the settings window.
    // Light,
    // and anything clickable has a visible edge and a hover state; information never sits in a pressable-looking box.
    // Choices are big cards with a radio dot or tick box that turn the chosen colour when picked.
    public class Wizard : Window
    {
        // the installer is always light, whatever Windows is set to
        static readonly (string key, string hex)[] Light =
        {
            ("Text", "#1D1D1F"), ("Sub", "#5A5A60"), ("Line", "#D2D2D7"), ("Ctl", "#FFFFFF"), ("Group", "#FFFFFF"), ("Win", "#FFFFFF"),
            ("Good", "#34A853"), ("Bad", "#D93025"), ("Warn", "#FFF3D6"), ("WarnLine", "#F0C46A"), ("CheckLine", "#8E8E93"), ("Sel", "#E8E8ED"), ("Hover", "#F2F2F5"),
        };
        static readonly Brush Rail = Theme.Brush("#F2F2F5"), LineB = Theme.Brush("#D2D2D7"), CardLine = Theme.Brush("#B8B8BE"), CardLineHover = Theme.Brush("#6E6E73"),
            CardHover = Theme.Brush("#F7F7F9"), White = Brushes.White, TextB = Theme.Brush("#1D1D1F"), SubB = Theme.Brush("#5A5A60"), Faint = Theme.Brush("#ECECEF"),
            GoodB = Theme.Brush("#34A853"), GoodText = Theme.Brush("#2E7D32"), UpdText = Theme.Brush("#A86A00"), DisabledB = Theme.Brush("#E5E5EA"), DisabledText = Theme.Brush("#A1A1A6");

        readonly Look look;
        readonly StackPanel stepsPanel = new StackPanel();
        readonly Border host = new Border { Padding = new Thickness(36, 28, 36, 8) };
        readonly TextBlock where = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly Button back, next;
        readonly string[] names = { "Welcome", "Your colour", "Light and dark", "What to install", "Installing", "Done" };
        readonly List<Action> refresh = new List<Action>();   // repaint the cards on the current page after a click
        readonly Dictionary<Button, Action> paints = new Dictionary<Button, Action>();
        int at;

        // choices
        string accent;
        readonly LightDark ld;
        readonly Dictionary<string, bool> parts = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> personal = new Dictionary<string, bool>();
        readonly Tidy tidy;
        bool installed, failed; string error;
        readonly Dictionary<string, (Border icon, TextBlock state, Grid row)> rows = new Dictionary<string, (Border, TextBlock, Grid)>();
        readonly Border barFill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        readonly TextBlock nowLine = new TextBlock { FontSize = 13, Margin = new Thickness(0, 0, 0, 12), TextTrimming = TextTrimming.CharacterEllipsis };
        int doneCount;

        public Wizard(Look look)
        {
            this.look = look;
            ld = LightDark.FromJson(look.LightDark.ToJson());
            tidy = Tidy.FromJson(look.Tidy.ToJson());
            bool here = string.Equals(look.SavedFrom, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            foreach (var p in PersonalItem.Known.Where(p => look.Personal.Contains(p.Id))) personal[p.Id] = here;   // only extras the look has
            var mine = Glasshouse.Accent.Current();
            accent = mine != null && !string.Equals(mine, "#0078D4", StringComparison.OrdinalIgnoreCase) ? mine : look.Accent;
            foreach (var p in Setup.Parts(look)) parts[p.Id] = p.Wanted;

            foreach (var (k, h) in Light) Resources[k] = Theme.Brush(h);
            SetAccent(accent);
            Title = "Install Glasshouse";
            ResizeMode = ResizeMode.CanMinimize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = White;
            SetResourceReference(FontFamilyProperty, "Font");
            Icon = AppIcon();
            SourceInitialized += (s, e) => Theme.StyleTitleBar(this, dark: false);
            where.Foreground = SubB;
            nowLine.Foreground = SubB;
            barFill.SetResourceReference(Border.BackgroundProperty, "Accent");

            var root = new Grid { Width = 920, Height = 606 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition());

            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 30) };
            brand.Children.Add(new Image { Source = AppIcon(), Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0) });
            brand.Children.Add(new TextBlock { Text = "Glasshouse", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = TextB, VerticalAlignment = VerticalAlignment.Center });
            var railPanel = new StackPanel { Margin = new Thickness(22, 26, 22, 26) };
            railPanel.Children.Add(brand); railPanel.Children.Add(stepsPanel);
            root.Children.Add(new Border { Background = Rail, BorderBrush = LineB, BorderThickness = new Thickness(0, 0, 1, 0), Child = railPanel });

            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition());
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
            main.Children.Add(host);
            back = WButton("Go Back", false, () => { if (at == 5) OpenGlasshouse(); else Go(at - 1); });
            next = WButton("Continue", true, Next, after: "");
            var footer = new DockPanel { Margin = new Thickness(36, 0, 26, 0), LastChildFill = true };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            back.Margin = new Thickness(0, 0, 10, 0); btns.Children.Add(back); btns.Children.Add(next);
            DockPanel.SetDock(btns, Dock.Right);
            footer.Children.Add(btns); footer.Children.Add(where);
            var fb = new Border { BorderBrush = LineB, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
            Grid.SetRow(fb, 1); main.Children.Add(fb);
            Grid.SetColumn(main, 1); root.Children.Add(main);
            Content = root;
            Go(0);
        }

        public static BitmapImage AppIcon()
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.UriSource = new Uri("pack://application:,,,/Glasshouse;component/assets/glasshouse-256.png"); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception e) { Log.Warn("icon: " + e.Message); return null; }
        }

        void SetAccent(string hex)
        {
            accent = hex;
            Theme.SetAccent(Application.Current, hex);
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var soft = new SolidColorBrush(Color.FromArgb(0x1F, c.R, c.G, c.B)); soft.Freeze();
            Application.Current.Resources["AccentSoft"] = soft;
        }

        void Next()
        {
            if (at == 3) { Go(4); Install(); return; }
            if (at == 5) { Close(); return; }
            Go(at + 1);
        }

        // ---------- building blocks ----------

        static TextBlock T(string text, double size, Brush fg, FontWeight? w = null) =>
            new TextBlock { Text = text, FontSize = size, Foreground = fg, FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

        static TextBlock Glyph(string g, double size, Brush fg) =>
            new TextBlock { Text = g, FontSize = size, Foreground = fg, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        TextBlock H1(string text)
        {
            var t = T(text, 26, TextB, FontWeights.SemiBold); t.Margin = new Thickness(0, 0, 0, 8);
            t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            return t;
        }
        static TextBlock Lead(string text, double bottom = 18) { var t = T(text, 15, Theme.Brush("#3A3A3F")); t.LineHeight = 22; t.Margin = new Thickness(0, 0, 0, bottom); return t; }
        static TextBlock Section(string text, double top = 0) { var t = T(text, 13, SubB, FontWeights.SemiBold); t.Margin = new Thickness(0, top, 0, 8); return t; }

        // An outlined or filled button that clearly looks like one.
        Button WButton(string text, bool primary, Action click, string after = null, string before = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = text, FontSize = 14.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            TextBlock g1 = before != null ? Glyph(before, 12, Brushes.White) : null, g2 = after != null ? Glyph(after, 12, Brushes.White) : null;
            if (g1 != null) { g1.Margin = new Thickness(0, 0, 8, 0); sp.Children.Add(g1); }
            sp.Children.Add(label);
            if (g2 != null) { g2.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(g2); }
            var b = new Border { Height = 38, Padding = new Thickness(20, 0, 20, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), Child = sp };
            var btn = new Button { Style = Kit.S("Bare"), Content = b, Tag = text };
            void Paint()
            {
                var words = sp.Children.OfType<TextBlock>().ToList();
                if (!btn.IsEnabled) { b.Background = DisabledB; b.BorderBrush = DisabledB; foreach (var w in words) w.Foreground = DisabledText; btn.Cursor = null; return; }
                btn.Cursor = System.Windows.Input.Cursors.Hand;
                if (primary)
                {
                    b.SetResourceReference(Border.BackgroundProperty, "Accent"); b.SetResourceReference(Border.BorderBrushProperty, "Accent");
                    b.Opacity = btn.IsMouseOver ? 0.88 : 1;
                    foreach (var w in words) w.Foreground = Brushes.White;
                }
                else
                {
                    b.Background = btn.IsMouseOver ? Rail : White; b.BorderBrush = btn.IsMouseOver ? CardLineHover : Theme.Brush("#9A9AA0");
                    foreach (var w in words) w.Foreground = TextB;
                }
            }
            btn.MouseEnter += (s, e) => Paint(); btn.MouseLeave += (s, e) => Paint();
            btn.IsEnabledChanged += (s, e) => Paint();
            btn.Click += (s, e) => click();
            btn.Loaded += (s, e) => Paint();
            paints[btn] = Paint;
            Paint();
            return btn;
        }

        // A card: a whole clickable area with a visible edge; it turns the chosen colour when "on".
        Button Card(UIElement inner, Func<bool> on, Action click, double minHeight, Thickness pad, string tag, double radius = 11)
        {
            var b = new Border { MinHeight = minHeight, CornerRadius = new CornerRadius(radius), Child = inner };
            var btn = new Button { Style = Kit.S("Bare"), Content = b, Tag = tag, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            void Paint()
            {
                bool isOn = on();
                if (isOn)
                {
                    b.BorderThickness = new Thickness(2.5); b.Padding = new Thickness(Math.Max(0, pad.Left - 1), Math.Max(0, pad.Top - 1), Math.Max(0, pad.Right - 1), Math.Max(0, pad.Bottom - 1));
                    b.SetResourceReference(Border.BorderBrushProperty, "Accent"); b.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                }
                else
                {
                    b.BorderThickness = new Thickness(1.5); b.Padding = pad;
                    b.BorderBrush = btn.IsMouseOver ? CardLineHover : CardLine; b.Background = btn.IsMouseOver ? CardHover : White;
                }
            }
            btn.MouseEnter += (s, e) => Paint(); btn.MouseLeave += (s, e) => Paint();
            btn.Click += (s, e) => { click(); foreach (var r in refresh) r(); };
            refresh.Add(Paint);
            Paint();
            return btn;
        }

        static Grid RadioDot(Func<bool> on)
        {
            var g = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            var ring = new Ellipse { StrokeThickness = 1.5 };
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White };
            g.Children.Add(ring); g.Children.Add(dot);
            g.Loaded += (s, e) => Repaint();
            void Repaint()
            {
                if (on()) { ring.SetResourceReference(Shape.StrokeProperty, "Accent"); ring.SetResourceReference(Shape.FillProperty, "Accent"); dot.Visibility = Visibility.Visible; }
                else { ring.Stroke = Theme.Brush("#8E8E93"); ring.Fill = Brushes.White; dot.Visibility = Visibility.Collapsed; }
            }
            g.Tag = (Action)Repaint;
            Repaint();
            return g;
        }

        static Border TickBox(Func<bool> on, double size = 20)
        {
            var tick = Glyph("", size * 0.55, Brushes.White);
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5), Child = tick, VerticalAlignment = VerticalAlignment.Center };
            void Repaint()
            {
                if (on()) { b.SetResourceReference(Border.BackgroundProperty, "Accent"); b.SetResourceReference(Border.BorderBrushProperty, "Accent"); tick.Visibility = Visibility.Visible; }
                else { b.Background = Brushes.White; b.BorderBrush = Theme.Brush("#8E8E93"); tick.Visibility = Visibility.Hidden; }
            }
            b.Tag = (Action)Repaint;
            Repaint();
            return b;
        }

        // registers a small part's own repaint with the page's refresh list
        T2 Live<T2>(T2 el) where T2 : FrameworkElement { if (el.Tag is Action a) refresh.Add(a); return el; }

        static UniformGrid Cols(int cols, double gapX, double gapY, IEnumerable<UIElement> items)
        {
            var g = new UniformGrid { Columns = cols, Margin = new Thickness(-gapX / 2, -gapY / 2, -gapX / 2, -gapY / 2) };
            foreach (var i in items) { if (i is FrameworkElement f) f.Margin = new Thickness(gapX / 2, gapY / 2, gapX / 2, gapY / 2); g.Children.Add(i); }
            return g;
        }

        // ---------- pages ----------

        public void Go(int i)
        {
            at = Math.Max(0, Math.Min(names.Length - 1, i));
            refresh.Clear();
            stepsPanel.Children.Clear();
            for (int k = 0; k < names.Length; k++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 38 };
                var n = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
                if (k < at) { n.Background = GoodB; n.BorderBrush = GoodB; n.Child = Glyph("", 11, Brushes.White); }
                else if (k == at) { n.SetResourceReference(Border.BackgroundProperty, "Accent"); n.SetResourceReference(Border.BorderBrushProperty, "Accent"); n.Child = new TextBlock { Text = (k + 1).ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; }
                else { n.BorderBrush = CardLine; n.Child = new TextBlock { Text = (k + 1).ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = SubB, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; }
                row.Children.Add(n);
                row.Children.Add(new TextBlock { Text = names[k], FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = k <= at ? TextB : SubB, FontWeight = k == at ? FontWeights.SemiBold : FontWeights.Normal });
                stepsPanel.Children.Add(row);
            }
            back.Visibility = (at > 0 && at < 4) || at == 5 ? Visibility.Visible : Visibility.Collapsed;
            SetWords(back, at == 5 ? "Open Glasshouse" : "Go Back");
            if (paints.TryGetValue(back, out var pb)) pb();
            var nextLabel = at == 3 ? "Install" : at == 5 ? "Finish" : "Continue";
            ReplaceNext(nextLabel, at == 3 ? "" : null, at == 3 || at == 5 ? null : "");
            next.IsEnabled = at != 4;
            var body = new StackPanel();
            switch (at)
            {
                case 0: Welcome(body); where.Text = "Step 1 of 6"; break;
                case 1: Colour(body); where.Text = "Step 2 of 6"; break;
                case 2: LightAndDark(body); where.Text = "Step 3 of 6"; break;
                case 3: WhatToInstall(body); where.Text = "Step 4 of 6 · Windows asks for permission once when you click Install"; break;
                case 4: Installing(body); where.Text = "Step 5 of 6 · The taskbar restarts once near the end, so the screen may flicker"; break;
                case 5: Done(body); where.Text = "Step 6 of 6"; break;
            }
            host.Child = body;
        }

        void ReplaceNext(string text, string before, string after)
        {
            // the main button's words change per page (Continue, Install, Finish)
            var b = (Border)next.Content; var sp = (StackPanel)b.Child;
            sp.Children.Clear();
            if (before != null) { var g = Glyph(before, 12, Brushes.White); g.Margin = new Thickness(0, 0, 8, 0); sp.Children.Add(g); }
            sp.Children.Add(new TextBlock { Text = text, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            if (after != null) { var g = Glyph(after, 12, Brushes.White); g.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(g); }
            next.Tag = text;
            if (paints.TryGetValue(next, out var paint)) paint();
        }

        // 1. Welcome: what you get (information only, nothing to press but Continue)
        void Welcome(StackPanel b)
        {
            b.Children.Add(H1("A macOS-style desktop for Windows"));
            b.Children.Add(Lead("This sets up a macOS-style desktop on this computer. You'll choose your colour and a few options next, and you can change anything later.", 18));
            b.Children.Add(Hero());
            var facts = new (string g, string c, string t, string s)[]
            {
                ("", "#48484A", "A Dock that magnifies", "Hides at the bottom until you need it"),
                ("", "#0A84FF", "A menu bar at the top", "Clock, controls and notifications"),
                ("", "#64A8D2", "Frosted glass", "Start, search and File Explorer"),
                ("", "#5E5CE6", "Spotlight search", "Press Alt+Space to find anything"),
                ("", "#30B0C7", "Light by day, dark at night", "With a wallpaper that follows the sun"),
                ("", "#8E8E93", "macOS-style pointers", "The arrow, hand and text cursors"),
            };
            b.Children.Add(Cols(2, 28, 14, facts.Select(f =>
            {
                var g = new System.Windows.Controls.Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
                var ic = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = Theme.Brush(f.c), Child = Glyph(f.g, 16, Brushes.White), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                var sp = new StackPanel(); sp.Children.Add(T(f.t, 14.5, TextB, FontWeights.SemiBold)); sp.Children.Add(T(f.s, 13, SubB));
                Grid.SetColumn(sp, 1); g.Children.Add(ic); g.Children.Add(sp);
                return (UIElement)g;
            })));
            var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            var gi = Glyph("", 15, SubB); gi.VerticalAlignment = VerticalAlignment.Top; gi.Margin = new Thickness(0, 1, 10, 0);
            var nt = T("It's all free. Each piece downloads from its own official website while you install. Windows will ask for permission once, and it takes about five minutes.", 13, SubB); nt.MaxWidth = 590; nt.LineHeight = 19;
            note.Children.Add(gi); note.Children.Add(nt);
            b.Children.Add(note);
            if (Sac.State == Sac.Mode.On || Sac.State == Sac.Mode.Evaluation)
                b.Children.Add(Warning(Sac.State == Sac.Mode.On ? "Smart App Control is on. It blocks the Dock, menu bar and glass. To have them, turn it off in Windows Security > App and browser control. The rest works either way."
                                                                 : "Smart App Control is checking this computer. If Windows switches it on, it blocks the Dock, menu bar and glass."));
        }

        Border Warning(string text)
        {
            var t = T(text, 13, TextB); t.LineHeight = 19;
            return new Border { Background = Theme.Brush("#FFF3D6"), BorderBrush = Theme.Brush("#F0C46A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 12, 0, 0), Child = t };
        }

        // a drawing of what you get (no Apple pictures)
        UIElement Hero()
        {
            var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#6CA6E8"), 0));
            grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#3D6FB8"), 0.55));
            grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#2B4E8A"), 1));
            var g = new System.Windows.Controls.Grid { Height = 132, Margin = new Thickness(0, 0, 0, 20), ClipToBounds = true };
            g.Children.Add(new Border { Background = grad, CornerRadius = new CornerRadius(12) });
            var pill = new DockPanel { Width = 230, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
            var pillB = new Border { CornerRadius = new CornerRadius(11), Background = Theme.Brush("#8C14161C"), Child = pill, Padding = new Thickness(12, 0, 12, 0), Width = 230, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
            var left = T("●  " + DateTime.Now.ToString("ddd d MMM", CultureInfo.CreateSpecificCulture("en-NZ")), 11, Brushes.White); left.VerticalAlignment = VerticalAlignment.Center;
            var right = T(DateTime.Now.ToString("h:mm tt").ToLowerInvariant(), 11, Brushes.White); right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right); pill.Children.Add(right); pill.Children.Add(left);
            pill.Width = double.NaN; pill.Height = double.NaN; pill.Margin = new Thickness(0);
            g.Children.Add(pillB);
            g.Children.Add(new Border { Width = 210, Height = 66, CornerRadius = new CornerRadius(8), Background = Theme.Brush("#59FFFFFF"), BorderBrush = Theme.Brush("#80FFFFFF"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(60, 40, 0, 0) });
            g.Children.Add(new Border { Width = 170, Height = 56, CornerRadius = new CornerRadius(8), Background = Theme.Brush("#591E1E24"), BorderBrush = Theme.Brush("#4DFFFFFF"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 46, 70, 0) });
            var dock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            foreach (var (c, big) in new[] { ("#2283F0", false), ("#F5B400", false), ("#F05A22", true), ("#62BA46", false), ("#A550A7", false), ("#8C8C8C", false) })
                dock.Children.Add(new Border { Width = big ? 44 : 34, Height = big ? 44 : 34, CornerRadius = new CornerRadius(big ? 11 : 9), Background = Theme.Brush(c), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Bottom });
            g.Children.Add(new Border { Child = dock, Height = 50, Padding = new Thickness(6, 0, 6, 7), CornerRadius = new CornerRadius(16), Background = Theme.Brush("#47FFFFFF"), BorderBrush = Theme.Brush("#66FFFFFF"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) });
            return g;
        }

        static readonly (string hex, string name)[] Swatches =
        {
            ("#F05A22", "Orange"), ("#2283F0", "Blue"), ("#A550A7", "Purple"), ("#F74F9E", "Pink"), ("#FF5257", "Red"),
            ("#E8A800", "Yellow"), ("#62BA46", "Green"), ("#8C8C8C", "Graphite"), ("#30B0C7", "Teal")
        };

        // 2. Your colour
        void Colour(StackPanel b)
        {
            b.Children.Add(H1("Choose your colour"));
            b.Children.Add(Lead("Click a colour. It's used for switches, highlights and the top search result. This computer's current colour is already picked.", 18));
            bool own = !Swatches.Any(s => string.Equals(s.hex, accent, StringComparison.OrdinalIgnoreCase));
            var cards = new List<UIElement>();
            foreach (var (hex, name) in Swatches)
            {
                var h = hex;
                cards.Add(SwatchCard(new Ellipse { Width = 34, Height = 34, Fill = Theme.Brush(hex == "#E8A800" ? "#FFC600" : hex) }, name,
                    () => !own && string.Equals(accent, h, StringComparison.OrdinalIgnoreCase), () => { own = false; SetAccent(h); }, "colour:" + hex));
            }
            var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            foreach (var (c, o) in new[] { ("#F05A22", 0.0), ("#FFC600", 0.2), ("#62BA46", 0.4), ("#30B0C7", 0.55), ("#2283F0", 0.7), ("#A550A7", 0.85), ("#F74F9E", 1.0) })
                rainbow.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), o));
            cards.Add(SwatchCard(new Ellipse { Width = 34, Height = 34, Fill = rainbow }, "Your own...", () => own, () =>
            {
                var c = (Color)ColorConverter.ConvertFromString(accent);
                using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) })
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { own = true; SetAccent($"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}"); }
            }, "colour:own"));
            b.Children.Add(Cols(5, 12, 12, cards));
            b.Children.Add(Section("What it will look like", 22));
            b.Children.Add(Sample());
        }

        Button SwatchCard(UIElement swatch, string name, Func<bool> on, Action pick, string tag)
        {
            var inner = new System.Windows.Controls.Grid();
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (swatch is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.Margin = new Thickness(0, 0, 0, 8); }
            sp.Children.Add(swatch);
            var label = new TextBlock { Text = name, FontSize = 13.5, Foreground = TextB, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(label);
            var badge = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 5, 0), Child = Glyph("", 10, Brushes.White) };
            badge.SetResourceReference(Border.BackgroundProperty, "Accent");
            inner.Children.Add(sp); inner.Children.Add(badge);
            refresh.Add(() => { badge.Visibility = on() ? Visibility.Visible : Visibility.Hidden; label.FontWeight = on() ? FontWeights.SemiBold : FontWeights.Normal; });
            badge.Visibility = on() ? Visibility.Visible : Visibility.Hidden;
            label.FontWeight = on() ? FontWeights.SemiBold : FontWeights.Normal;
            return Card(inner, on, pick, 92, new Thickness(0), tag, 12);
        }

        // where the colour shows: a picture, not controls
        UIElement Sample()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            StackPanel Item(UIElement el, string label)
            {
                var s = new StackPanel { Margin = new Thickness(0, 0, 26, 0), VerticalAlignment = VerticalAlignment.Center };
                s.Children.Add(el);
                var l = T(label, 12, Theme.Brush("#BDBDC2")); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 6, 0, 0);
                s.Children.Add(l); return s;
            }
            var row = new System.Windows.Controls.Grid { Width = 250, Height = 36 };
            row.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Theme.Brush("#12FFFFFF") });
            var bar = new Border { Width = 3, Margin = new Thickness(0, 9, 0, 9), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
            bar.SetResourceReference(Border.BackgroundProperty, "Accent");
            row.Children.Add(bar);
            var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(T("Settings", 13, Theme.Brush("#EDEDED"), FontWeights.SemiBold)); var sys = T("   System", 13, Theme.Brush("#9A9AA0")); words.Children.Add(sys);
            row.Children.Add(words);
            sp.Children.Add(Item(row, "Top search result"));
            var sw = new System.Windows.Controls.Grid { Width = 40, Height = 22 };
            var track = new Border { CornerRadius = new CornerRadius(11) }; track.SetResourceReference(Border.BackgroundProperty, "Accent");
            sw.Children.Add(track); sw.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0) });
            sp.Children.Add(Item(sw, "Switches"));
            var fake = new Border { Height = 30, Padding = new Thickness(14, 0, 14, 0), CornerRadius = new CornerRadius(6), Child = new TextBlock { Text = "Install", Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center } };
            fake.SetResourceReference(Border.BackgroundProperty, "Accent");
            sp.Children.Add(Item(fake, "Buttons"));
            return new Border { CornerRadius = new CornerRadius(12), Background = Theme.Brush("#2A2A2E"), Padding = new Thickness(18, 16, 18, 16), Child = sp, IsHitTestVisible = false };
        }

        // 3. Light and dark
        void LightAndDark(StackPanel b)
        {
            b.Children.Add(H1("Light and dark"));
            b.Children.Add(Lead("Like macOS set to Auto: light by day, dark at night. Click one option in each row.", 14));
            string place = string.IsNullOrEmpty(look.LightDark.Place) ? $"{look.LightDark.Lat:0.##}, {look.LightDark.Lon:0.##}" : look.LightDark.Place.Split(',')[0];
            var sun = Sun.Times(look.LightDark.Lat, look.LightDark.Lon, DateTime.Today);
            string sunLine = sun == null ? "" : $"Light {Sun.Clock(sun.Value.rise)}, dark {Sun.Clock(sun.Value.set)}";

            var whenBox = new StackPanel();
            var times = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            Action showHide = () =>
            {
                whenBox.Visibility = ld.Apps == "switch" ? Visibility.Visible : Visibility.Collapsed;
                times.Visibility = ld.CustomTimes ? Visibility.Visible : Visibility.Collapsed;
            };

            b.Children.Add(Section("Your apps"));
            b.Children.Add(Cols(3, 10, 10, new UIElement[]
            {
                Radio("Switch with the sun", "Like macOS: light by day, dark at night", () => ld.Apps == "switch", () => ld.Apps = "switch", "apps:switch"),
                Radio("Always light", "Never changes", () => ld.Apps == "light", () => ld.Apps = "light", "apps:light"),
                Radio("Always dark", "Never changes", () => ld.Apps == "dark", () => ld.Apps = "dark", "apps:dark"),
            }));

            whenBox.Children.Add(Section("When to switch", 16));
            // a look file can carry its own place (a town, for sunrise and sunset); the shared default uses wherever the computer is
            bool hasPlace = !look.LightDark.UseWindowsLocation && (look.LightDark.Lat != 0 || look.LightDark.Lon != 0);
            var whenCards = new List<UIElement>();
            if (hasPlace)
                whenCards.Add(Radio("Sun in " + place, sunLine, () => !ld.CustomTimes && !ld.UseWindowsLocation,
                      () => { ld.CustomTimes = false; ld.UseWindowsLocation = false; ld.Lat = look.LightDark.Lat; ld.Lon = look.LightDark.Lon; ld.Place = look.LightDark.Place; }, "when:place"));
            whenCards.Add(Radio("Sun where I am", "Uses Windows' location setting", () => !ld.CustomTimes && ld.UseWindowsLocation, () => { ld.CustomTimes = false; ld.UseWindowsLocation = true; }, "when:here"));
            whenCards.Add(Radio("Times I choose", "Same two times every day", () => ld.CustomTimes, () => { ld.CustomTimes = true; ld.UseWindowsLocation = false; }, "when:times"));
            whenBox.Children.Add(Cols(whenCards.Count, 10, 10, whenCards));
            TextBox TimeBox(string v, Action<string> set)
            {
                var tb = new TextBox { Text = v, Width = 90, Height = 34, FontSize = 14, Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center,
                                       BorderThickness = new Thickness(1.5), BorderBrush = Theme.Brush("#8E8E93"), Foreground = TextB, Background = Brushes.White, Margin = new Thickness(0, 0, 18, 0) };
                tb.TextChanged += (s, e) => { if (TimeSpan.TryParseExact(tb.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var ts)) set(ts.ToString(@"hh\:mm")); };
                return tb;
            }
            TextBlock L(string t) { var x = T(t, 13.5, TextB, FontWeights.SemiBold); x.VerticalAlignment = VerticalAlignment.Center; x.Margin = new Thickness(0, 0, 10, 0); return x; }
            times.Children.Add(L("Light at")); times.Children.Add(TimeBox(ld.LightAt, v => ld.LightAt = v));
            times.Children.Add(L("Dark at")); times.Children.Add(TimeBox(ld.DarkAt, v => ld.DarkAt = v));
            var hint = T("24-hour time, like 07:00 and 20:00", 12, SubB); hint.VerticalAlignment = VerticalAlignment.Center; times.Children.Add(hint);
            whenBox.Children.Add(times);
            b.Children.Add(whenBox);

            b.Children.Add(Section("Taskbar, Start and File Explorer", 16));
            b.Children.Add(Cols(2, 10, 10, new UIElement[]
            {
                Radio("Always dark", "Best with the glass: stays dark while your apps switch", () => ld.Windows == "dark", () => ld.Windows = "dark", "shell:dark"),
                Radio("Switch with your apps", "Light by day too", () => ld.Windows != "dark", () => ld.Windows = "switch", "shell:switch"),
            }));
            refresh.Add(showHide);
            showHide();
        }

        Button Radio(string title, string sub, Func<bool> on, Action pick, string tag)
        {
            var g = new System.Windows.Controls.Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
            var dot = Live(RadioDot(on)); dot.Margin = new Thickness(0, 0, 11, 0);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(T(title, 13.5, TextB, FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(sub)) { var s = T(sub, 12, SubB); s.Margin = new Thickness(0, 1, 0, 0); sp.Children.Add(s); }
            Grid.SetColumn(sp, 1); g.Children.Add(dot); g.Children.Add(sp);
            return Card(g, on, pick, 58, new Thickness(12, 8, 12, 8), tag);
        }

        // 4. What to install
        void WhatToInstall(StackPanel b)
        {
            b.Children.Add(H1("What to install"));
            b.Children.Add(Lead("Everything is ticked. Click a card to leave that part out.", 12));
            var plan = Windhawk.IsInstalled ? Machine.Plan(look, new Machine.Options()) : null;
            var cards = new List<UIElement>();
            foreach (var p in Setup.Parts(look))
            {
                var id = p.Id;
                string status; Brush statusColour;
                if (id == "windhawk" && Windhawk.IsInstalled && plan != null)
                {
                    int adds = plan.Count(d => d.New || d.Reinstall), changes = plan.Count(d => d.Any && !d.New && !d.Reinstall);
                    status = adds > 0 ? $"Adds {adds} mod{(adds == 1 ? "" : "s")}" : changes > 0 ? "Updates settings" : "Already here";
                    statusColour = adds > 0 || changes > 0 ? UpdText : GoodText;
                }
                else if (p.Installed()) { status = "Already here"; statusColour = GoodText; }
                else { status = "Downloads"; statusColour = SubB; }
                var g = new System.Windows.Controls.Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
                var box = Live(TickBox(() => parts[id])); box.Margin = new Thickness(0, 0, 11, 0);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = p.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = TextB, TextTrimming = TextTrimming.CharacterEllipsis });
                var small = new TextBlock { FontSize = 11.5, Foreground = SubB, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
                small.Inlines.Add(new Run(status) { Foreground = statusColour, FontWeight = FontWeights.SemiBold });
                var shortWhat = new Dictionary<string, string> { ["windhawk"] = "Windhawk", ["wallpaper"] = "WinDynamicDesktop", ["powertoys"] = "PowerToys" };
                small.Inlines.Add(new Run($" · {(shortWhat.TryGetValue(id, out var sw) ? sw : p.What)} · {Host(p.Source)}"));
                sp.Children.Add(small);
                Grid.SetColumn(sp, 1); g.Children.Add(box); g.Children.Add(sp);
                cards.Add(Card(g, () => parts[id], () => parts[id] = !parts[id], 50, new Thickness(12, 6, 12, 6), "part:" + id, 10));
            }
            b.Children.Add(Cols(2, 10, 8, cards));
            b.Children.Add(Section("Also", 14));
            var minis = new List<UIElement>
            {
                Mini("Hide desktop icons", () => tidy.HideDesktopIcons, () => tidy.HideDesktopIcons = !tidy.HideDesktopIcons, "also:icons"),
                Mini("Hide extra taskbar buttons", () => tidy.HideTaskbarExtras, () => tidy.HideTaskbarExtras = !tidy.HideTaskbarExtras, "also:taskbar"),
                Mini("No suggestions in Start", () => tidy.StartNoRecent, () => tidy.StartNoRecent = !tidy.StartNoRecent, "also:start"),
                Mini("Turn off Windows sounds", () => tidy.QuietSounds, () => tidy.QuietSounds = !tidy.QuietSounds, "also:sounds"),
            };
            var labels = new Dictionary<string, string> { ["parsec-helper"] = "Hide Parsec's helper button", ["start-pins-hidden"] = "Hide pinned apps in Start", ["phone-panel-hidden"] = "Hide the phone panel in Start" };
            foreach (var p in PersonalItem.Known.Where(p => personal.ContainsKey(p.Id))) { var id = p.Id; minis.Add(Mini(labels[id], () => personal[id], () => personal[id] = !personal[id], "also:" + id)); }
            b.Children.Add(Cols(2, 10, 4, minis));
        }

        static string Host(string source)
        {
            if (source.StartsWith("winget")) return "winget";
            if (source.Contains("Store")) return "Microsoft Store";
            if (source.Contains("github")) return "github.com";
            if (source.Contains("windhawk")) return "windhawk.net";
            return source;
        }

        // a lighter tick-box line: no border until the pointer is on it
        Button Mini(string text, Func<bool> on, Action toggle, string tag)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var box = Live(TickBox(on, 18)); box.Margin = new Thickness(0, 0, 9, 0);
            sp.Children.Add(box); sp.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = TextB, VerticalAlignment = VerticalAlignment.Center });
            var bd = new Border { Height = 34, Padding = new Thickness(9, 0, 9, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = sp, Background = White, BorderBrush = Brushes.Transparent };
            var btn = new Button { Style = Kit.S("Bare"), Content = bd, Tag = tag, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            btn.MouseEnter += (s, e) => { bd.Background = Rail; bd.BorderBrush = LineB; };
            btn.MouseLeave += (s, e) => { bd.Background = White; bd.BorderBrush = Brushes.Transparent; };
            btn.Click += (s, e) => { toggle(); foreach (var r in refresh) r(); };
            return btn;
        }

        // 5. Installing: progress, nothing to click
        void Installing(StackPanel b)
        {
            rows.Clear(); doneCount = 0;
            b.Children.Add(H1("Installing"));
            var track = new System.Windows.Controls.Grid { Height = 8, Margin = new Thickness(0, 4, 0, 6) };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = DisabledB });
            barFill.Width = 0; track.Children.Add(barFill);
            b.Children.Add(track);
            nowLine.Text = "Starting...";
            b.Children.Add(nowLine);
            var list = new List<(string id, string name)> { ("admin", "Permission from Windows") };
            list.AddRange(Setup.Parts(look).Select(p => (p.Id, p.Name)));
            list.Add(("look", "Putting the look in place"));
            var sp = new StackPanel();
            sp.Children.Add(new Border { Height = 1, Background = Faint });
            foreach (var (id, name) in list)
            {
                var g = new System.Windows.Controls.Grid { Height = 35 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                var nameT = T(name, 14, TextB); nameT.VerticalAlignment = VerticalAlignment.Center;
                var st = T(parts.TryGetValue(id, out var want) && !want ? "Left out" : "Waiting", 12.5, SubB); st.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(nameT, 1); Grid.SetColumn(st, 2);
                g.Children.Add(icon); g.Children.Add(nameT); g.Children.Add(st);
                rows[id] = (icon, st, g);
                SetIcon(icon, parts.TryGetValue(id, out var w2) && !w2 ? "skipped" : "waiting");
                sp.Children.Add(g);
                sp.Children.Add(new Border { Height = 1, Background = Faint });
            }
            b.Children.Add(sp);
        }

        void SetIcon(Border icon, string state)
        {
            icon.Child = null; icon.Background = null; icon.BorderThickness = new Thickness(0); icon.BorderBrush = null; icon.RenderTransform = null;
            switch (state)
            {
                case "done": icon.Background = GoodB; icon.Child = Glyph("", 10, Brushes.White); break;
                case "failed": icon.Background = Theme.Brush("#D93025"); icon.Child = new TextBlock { Text = "!", FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; break;
                case "skipped": icon.BorderThickness = new Thickness(1.5); icon.BorderBrush = Theme.Brush("#D2D2D7"); break;
                case "waiting": icon.BorderThickness = new Thickness(1.5); icon.BorderBrush = CardLine; break;
                case "doing":
                    var ring = new Ellipse { StrokeThickness = 2.5, Stroke = DisabledB };
                    var arc = new Ellipse { StrokeThickness = 2.5, StrokeDashArray = new DoubleCollection { 4, 100 }, RenderTransformOrigin = new Point(0.5, 0.5) };
                    arc.SetResourceReference(Shape.StrokeProperty, "Accent");
                    var rot = new RotateTransform(); arc.RenderTransform = rot;
                    rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
                    var g = new System.Windows.Controls.Grid(); g.Children.Add(ring); g.Children.Add(arc);
                    icon.Child = g; break;
            }
        }

        void Mark(string id, string state, string detail)
        {
            if (!rows.TryGetValue(id, out var r)) return;
            SetIcon(r.icon, state);
            ((TextBlock)r.row.Children[1]).FontWeight = state == "doing" ? FontWeights.SemiBold : FontWeights.Normal;
            r.state.Foreground = SubB; r.state.FontWeight = FontWeights.Normal;
            switch (state)
            {
                case "doing": r.state.Text = string.IsNullOrEmpty(detail) ? "Working..." : detail; r.state.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); r.state.FontWeight = FontWeights.SemiBold; break;
                case "done": r.state.Text = string.IsNullOrEmpty(detail) ? "Done" : detail; doneCount++; break;
                case "failed": r.state.Text = "Didn't work"; r.state.Foreground = Theme.Brush("#D93025"); failed = true; doneCount++; break;
                case "skipped": r.state.Text = "Left out"; doneCount++; break;
            }
            barFill.Width = 628.0 * Math.Min(1, doneCount / (double)rows.Count);
            nowLine.Text = $"{doneCount} of {rows.Count} done" + (state == "doing" && !string.IsNullOrEmpty(detail) ? " · " + detail : "");
        }

        async void Install()
        {
            var final = Look.FromJsonText(look.ToJsonText());
            final.LightDark = ld; final.Tidy = tidy; final.Accent = accent;
            var o = new Machine.Options { Accent = accent, PersonalOn = new HashSet<string>(personal.Where(k => k.Value).Select(k => k.Key)) };
            var skip = new HashSet<string>(parts.Where(k => !k.Value).Select(k => k.Key));
            Action<string> line = l => Dispatcher.BeginInvoke(new Action(() => { if (!string.IsNullOrWhiteSpace(l) && !l.StartsWith("  [admin]")) nowLine.Text = $"{doneCount} of {rows.Count} done · {l.Trim()}"; }));
            Log.Line += line;
            await Task.Run(() =>
            {
                try { Setup.Run(final, o, skip, (id, state, detail) => Dispatcher.BeginInvoke(new Action(() => Mark(id, state, detail)))); }
                catch (Exception e) { error = e.Message; }
            });
            Log.Line -= line;
            installed = true;
            if (error != null) failed = true;
            await Task.Delay(900);
            Go(5);
        }

        // 6. Done
        void Done(StackPanel b)
        {
            var big = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16),
                                   Background = failed ? Theme.Brush("#F29900") : GoodB, Child = failed ? (UIElement)T("!", 28, Brushes.White, FontWeights.Bold) : Glyph("", 26, Brushes.White) };
            if (big.Child is TextBlock tb) { tb.HorizontalAlignment = HorizontalAlignment.Center; tb.VerticalAlignment = VerticalAlignment.Center; }
            b.Children.Add(big);
            b.Children.Add(H1(failed ? "Glasshouse is set up, with a problem" : "Your Glasshouse desktop is ready"));
            if (failed)
            {
                b.Children.Add(Lead("Most of it is in place. Open Glasshouse and go to Repair: it shows what's missing and puts it right.", 10));
                if (error != null) b.Children.Add(Warning("What stopped: " + error));
                b.Children.Add(new Border { Height = 14 });
            }
            else b.Children.Add(Lead("A few things to know:", 12));
            var tips = new StackPanel();
            void Tip(string glyph, string colour, params Inline[] text)
            {
                var g = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 14) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
                var ic = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Theme.Brush(colour), Child = Glyph(glyph, 14, Brushes.White), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                var t = new TextBlock { FontSize = 14.5, Foreground = TextB, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
                foreach (var i in text) t.Inlines.Add(i);
                Grid.SetColumn(t, 1); g.Children.Add(ic); g.Children.Add(t);
                tips.Children.Add(g);
            }
            Inline Key(string k) => new InlineUIContainer(new Border { BorderBrush = CardLine, BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(5), Background = Theme.Brush("#F7F7F9"), Padding = new Thickness(6, 0, 6, 0), Child = T(k, 12, TextB, FontWeights.SemiBold) }) { BaselineAlignment = BaselineAlignment.Center };
            Tip("", "#48484A", new Run("The Dock hides at the bottom of the screen. Move your pointer to the bottom edge to show it."));
            if (parts.TryGetValue("raycast", out var rc) && rc)
                Tip("", "#5E5CE6", new Run("Press "), Key("Alt"), new Run(" + "), Key("Space"), new Run(" for Spotlight search. The first time, Raycast asks which shortcut to use: press Alt+Space."));
            Tip("", "#FF9F0A", new Run("To change your colour, the Dock, the glass or the light and dark times later, open "), new Bold(new Run("Glasshouse")), new Run(" from the Start menu."));
            b.Children.Add(tips);
        }

        void OpenGlasshouse()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.File.Exists(Elevation.InstalledExe) ? Elevation.InstalledExe : Program.SelfPath) { UseShellExecute = true }); } catch { }
            Close();
        }

        static void SetWords(Button btn, string text)
        {
            var sp = (StackPanel)((Border)btn.Content).Child;
            foreach (var t in sp.Children.OfType<TextBlock>()) if (!(t.FontFamily?.Source ?? "").Contains("Icons")) t.Text = text;
            btn.Tag = text;
        }
    }
}
