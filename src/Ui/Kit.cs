using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Glasshouse.Ui
{
    // Builders for the window's parts, so each page reads like the mockup: a title, captions, grouped rows.
    public static class Kit
    {
        public static Style S(string key) => (Style)Application.Current.Resources[key];

        public static TextBlock Text(string t, double size = 13, string brush = "Text", FontWeight? weight = null)
        {
            var tb = new TextBlock { Text = t, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return tb;
        }

        public static TextBlock Title(string t)
        {
            var tb = Text(t, 22, "Text", FontWeights.SemiBold);
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            tb.Margin = new Thickness(0, 6, 0, 16);
            return tb;
        }

        public static TextBlock Caption(string t)
        {
            var tb = Text(t, 12, "Sub", FontWeights.SemiBold);
            tb.Margin = new Thickness(4, 0, 4, 6);
            return tb;
        }

        public static TextBlock Note(string t)
        {
            var tb = Text(t, 12, "Sub");
            tb.Margin = new Thickness(4, -10, 4, 18);
            tb.LineHeight = 17;
            return tb;
        }

        public static TextBlock Icon(string glyph, double size = 13, string brush = "Text")
        {
            var tb = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return tb;
        }

        // A rounded group of rows with hairlines between them.
        public static Border Group(params UIElement[] rows)
        {
            var sp = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null) continue;
                if (sp.Children.Count > 0)
                {
                    var line = new Border { Height = 1 };
                    line.SetResourceReference(Border.BackgroundProperty, "Line");
                    // a row that's hidden (an editor that opens under its row) takes its line with it
                    if (rows[i] is FrameworkElement fe) line.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = fe });
                    sp.Children.Add(line);
                }
                sp.Children.Add(rows[i]);
            }
            var b = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18), Child = sp };
            b.SetResourceReference(Border.BackgroundProperty, "Group");
            b.SetResourceReference(Border.BorderBrushProperty, "Line");
            return b;
        }

        // label (and a smaller line under it) on the left, a control on the right
        public static Grid Row(string label, UIElement control, string sub = null, UIElement left = null)
        {
            var g = new Grid { MinHeight = 30, Margin = new Thickness(14, 8, 14, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (left != null) { Grid.SetColumn(left, 0); if (left is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 14, 0); g.Children.Add(left); }
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var l = Text(label, 13.5); l.Tag = "label"; sp.Children.Add(l);
            if (!string.IsNullOrEmpty(sub)) { var s = Text(sub, 12, "Sub"); s.Margin = new Thickness(0, 2, 0, 0); s.Tag = "sub"; sp.Children.Add(s); }
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            if (control != null)
            {
                Grid.SetColumn(control, 2);
                if (control is FrameworkElement fc) { fc.VerticalAlignment = VerticalAlignment.Center; fc.Margin = new Thickness(14, 0, 0, 0); }
                g.Children.Add(control);
            }
            return g;
        }

        public static void SetSub(Grid row, string sub)
        {
            var sp = row.Children.OfType<StackPanel>().FirstOrDefault();
            var s = sp?.Children.OfType<TextBlock>().FirstOrDefault(t => (t.Tag as string) == "sub");
            if (s == null && sp != null) { s = Text("", 12, "Sub"); s.Margin = new Thickness(0, 2, 0, 0); s.Tag = "sub"; sp.Children.Add(s); }
            if (s != null) s.Text = sub;
        }

        public static CheckBox Toggle(bool on, Action<bool> changed)
        {
            var c = new CheckBox { Style = S("Toggle"), IsChecked = on };
            c.Checked += (s, e) => changed(true);
            c.Unchecked += (s, e) => changed(false);
            return c;
        }

        public static CheckBox Check(bool on, Action<bool> changed = null)
        {
            var c = new CheckBox { Style = S("Check"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            if (changed != null) { c.Checked += (s, e) => changed(true); c.Unchecked += (s, e) => changed(false); }
            return c;
        }

        // Slider with its value on the right; "changed" fires when the drag ends (or a click), not on every pixel.
        public static FrameworkElement Slider(double min, double max, double value, Func<double, string> show, Action<double> changed, string lo = null, string hi = null, double step = 1)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Width = 320 };
            var s = new Slider { Style = S("MacSlider"), Minimum = min, Maximum = max, Value = value, Width = lo != null ? 190 : 250, VerticalAlignment = VerticalAlignment.Center, SmallChange = step, LargeChange = step * 5, IsSnapToTickEnabled = true, TickFrequency = step };
            var v = Text(show(value), 12.5); v.Width = 52; v.TextAlignment = TextAlignment.Right; v.VerticalAlignment = VerticalAlignment.Center;
            if (lo != null) { var l = Text(lo, 11.5, "Sub"); l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0, 0, 10, 0); sp.Children.Add(l); }
            sp.Children.Add(s);
            if (hi != null) { var h = Text(hi, 11.5, "Sub"); h.VerticalAlignment = VerticalAlignment.Center; h.Margin = new Thickness(10, 0, 0, 0); sp.Children.Add(h); }
            sp.Children.Add(v);
            double last = value;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            timer.Tick += (o, e) => { timer.Stop(); if (s.Value != last) { last = s.Value; changed(s.Value); } };
            s.ValueChanged += (o, e) => { v.Text = show(s.Value); timer.Stop(); timer.Start(); };
            return sp;
        }

        public static Border Segmented(string[] labels, int selected, Action<int> changed)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            string group = Guid.NewGuid().ToString("N");
            for (int i = 0; i < labels.Length; i++)
            {
                int ix = i;
                var r = new RadioButton { Style = S("Seg"), Content = labels[i], GroupName = group, IsChecked = i == selected, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0) };
                r.Checked += (s, e) => changed(ix);
                sp.Children.Add(r);
            }
            var b = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = sp };
            b.SetResourceReference(Border.BackgroundProperty, "Ctl");
            return b;
        }

        public static Button Button(string text, Action click, bool primary = false)
        {
            var b = new Button { Style = S(primary ? "Pri" : "Btn"), Content = text };
            b.Click += (s, e) => click();
            return b;
        }

        public static Border Dot(string state)   // "good" | "bad" | "todo"
        {
            var d = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            if (state == "todo") { d.BorderThickness = new Thickness(1.5); d.SetResourceReference(Border.BorderBrushProperty, "Sub"); }
            else d.SetResourceReference(Border.BackgroundProperty, state == "good" ? "Good" : state == "warn" ? "WarnLine" : "Bad");
            return d;
        }

        public static TextBox Field(string text, double width = 220)
        {
            var t = new TextBox { Style = S("Field"), Text = text, Width = width };
            return t;
        }

        public static Border WarnBox(string bold, string rest)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 18 };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            tb.Inlines.Add(new System.Windows.Documents.Run(bold + " ") { FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new System.Windows.Documents.Run(rest));
            var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 11, 14, 11), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18), Child = tb };
            b.SetResourceReference(Border.BackgroundProperty, "Warn");
            b.SetResourceReference(Border.BorderBrushProperty, "WarnLine");
            return b;
        }

        public static StackPanel Buttons(params Button[] b)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 14) };
            for (int i = 0; i < b.Length; i++) { b[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0); sp.Children.Add(b[i]); }
            return sp;
        }
    }
}
