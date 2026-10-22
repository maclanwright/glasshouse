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

namespace Glasshouse.Ui
{
    public partial class MainWindow : Window
    {
        Editor ed;
        readonly Grid root = new Grid();
        readonly StackPanel nav = new StackPanel();
        readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly StackPanel page = new StackPanel { Margin = new Thickness(34, 4, 34, 24) };
        readonly TextBlock status = Kit.Text("", 11.5, "Sub");
        readonly Button restartLink;
        readonly Grid overlay = new Grid { Visibility = Visibility.Collapsed };
        string current = "Appearance";

        // Sidebar pages: name, Segoe Fluent icon, icon square colour, words that find it in the search box
        static readonly (string name, string icon, string color, string words)[] Pages =
        {
            ("Appearance", "", "#5E5CE6", "accent colour color light dark sunset sunrise wallpaper catalina theme location"),
            ("Dock", "", "#48484A", "dock size magnification hide auto-hide blur tint recycle bin divider taskbar"),
            ("Menu bar", "", "#0A84FF", "menu bar island pill clock calendar top"),
            ("Window previews", "", "#30B0C7", "previews thumbnails hover window size tint"),
            ("Glass and hover", "", "#64A8D2", "glass blur hover start search file explorer dark transparency"),
            ("Pointers and apps", "", "#8E8E93", "pointers cursor mouse raycast powertoys file converter desktop icons sounds"),
            ("-", "", "", ""),
            ("Look files", "", "#FF9F0A", "save open share friend file export import personal"),
            ("Repair", "", "#8E8E93", "repair fix update status check"),
            ("Inspector", "", "#BF5AF2", "inspector point parts screenshot outline"),
        };

        public MainWindow(string openLook, bool render = false)
        {
            Title = "Glasshouse";
            Width = 960; Height = 680; MinWidth = 840; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "Win");
            SetResourceReference(FontFamilyProperty, "Font");
            Icon = Wizard.AppIcon();
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            SourceInitialized += (s, e) => Theme.StyleTitleBar(this);
            Theme.Changed += () => Theme.StyleTitleBar(this);

            restartLink = new Button { Style = Kit.S("Link"), Content = "Restart the taskbar", Visibility = Visibility.Collapsed, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            restartLink.Click += async (s, e) => { restartLink.Visibility = Visibility.Collapsed; status.Text = "Restarting the taskbar..."; await Task.Run(() => { Shell.RestartExplorer(); if (ed.Look.Tidy.AutoHideDock) Shell.AutoHide = true; }); status.Text = "Saved"; };

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var side = BuildSidebar();
            root.Children.Add(side);
            var main = new Border { Child = scroll };
            main.SetResourceReference(Border.BackgroundProperty, "Bg");
            Grid.SetColumn(main, 1);
            root.Children.Add(main);
            scroll.Content = page;
            Grid.SetColumnSpan(overlay, 2);
            overlay.SetResourceReference(Grid.BackgroundProperty, "Shade");
            root.Children.Add(overlay);
            Content = root;

            if (render) return;
            Loaded += (s, e) =>
            {
                Inspector.Server.Start();
                if (!Setup.Done && !Windhawk.IsInstalled) ShowSetup(null);
                else
                {
                    ed = NewEditor(Editor.LoadCurrent());
                    Show(current);
                    if (openLook != null) OpenLookSheet(openLook);
                }
            };
        }

        Editor NewEditor(Look look)
        {
            var e = new Editor(look);
            e.Status += t => Dispatcher.Invoke(() => status.Text = t);
            e.TaskbarRestartNeeded += () => Dispatcher.Invoke(() => restartLink.Visibility = Visibility.Visible);
            return e;
        }

        Border BuildSidebar()
        {
            var sp = new DockPanel { Margin = new Thickness(10, 6, 10, 10), LastChildFill = true };
            var search = new TextBox { Style = Kit.S("Field") };
            var hint = Kit.Text("Search", 13, "Sub"); hint.IsHitTestVisible = false; hint.Margin = new Thickness(32, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center;
            var glass = Kit.Icon("", 13, "Sub"); glass.HorizontalAlignment = HorizontalAlignment.Left; glass.Margin = new Thickness(10, 0, 0, 0); glass.IsHitTestVisible = false;
            search.Padding = new Thickness(22, 0, 0, 0);
            var sg = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            sg.Children.Add(search); sg.Children.Add(glass); sg.Children.Add(hint);
            search.TextChanged += (s, e) =>
            {
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                var q = search.Text.Trim().ToLowerInvariant();
                foreach (var item in nav.Children.OfType<RadioButton>())
                {
                    var p = Pages.First(x => x.name == (string)item.Tag);
                    item.Visibility = q.Length == 0 || p.name.ToLowerInvariant().Contains(q) || p.words.Contains(q) ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) nav.Children.OfType<RadioButton>().FirstOrDefault(r => r.Visibility == Visibility.Visible)?.SetValue(RadioButton.IsCheckedProperty, true); };
            DockPanel.SetDock(sg, Dock.Top);
            sp.Children.Add(sg);

            foreach (var p in Pages)
            {
                if (p.name == "-") { nav.Children.Add(new Border { Height = 10 }); continue; }
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var ico = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Theme.Brush(p.color), Margin = new Thickness(0, 0, 10, 0) };
                var g = Kit.Icon(p.icon, 13); g.Foreground = Brushes.White; ico.Child = g;
                row.Children.Add(ico);
                row.Children.Add(new TextBlock { Text = p.name, VerticalAlignment = VerticalAlignment.Center });
                var rb = new RadioButton { Style = Kit.S("Nav"), Content = row, GroupName = "nav", Tag = p.name, IsChecked = p.name == current, Margin = new Thickness(0, 0, 0, 2) };
                rb.Checked += (s, e) => { if (ed != null && current != (string)rb.Tag) Show((string)rb.Tag); };
                nav.Children.Add(rb);
            }
            var foot = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            foot.Children.Add(status); foot.Children.Add(restartLink);
            DockPanel.SetDock(foot, Dock.Bottom);
            sp.Children.Add(foot);
            sp.Children.Add(nav);
            var b = new Border { Child = sp, BorderThickness = new Thickness(0, 0, 1, 0) };
            b.SetResourceReference(Border.BackgroundProperty, "Side");
            b.SetResourceReference(Border.BorderBrushProperty, "Line");
            return b;
        }

        void Show(string name)
        {
            current = name;
            page.Children.Clear();
            scroll.ScrollToTop();
            foreach (var rb in nav.Children.OfType<RadioButton>()) if ((string)rb.Tag == name && rb.IsChecked != true) rb.IsChecked = true;
            switch (name)
            {
                case "Appearance": PageAppearance(); break;
                case "Dock": PageDock(); break;
                case "Menu bar": PageMenuBar(); break;
                case "Window previews": PagePreviews(); break;
                case "Glass and hover": PageGlass(); break;
                case "Pointers and apps": PagePointers(); break;
                case "Look files": PageLooks(); break;
                case "Repair": PageRepair(); break;
                case "Inspector": PageInspector(); break;
            }
        }

        void Add(params UIElement[] els) { foreach (var e in els) if (e != null) page.Children.Add(e); }
        static string Pct(double v) => $"{Math.Round(v)}%";
    }
}
