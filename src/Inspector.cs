using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Glasshouse.Inspector
{
    // The Windows UI Inspector (first built 22 Oct 2026 as a separate tool), now inside Glasshouse.
    // Takes a real screenshot of a piece of Windows' own interface (Start, search, File Explorer, its right-click
    // menu, the Dock) with every part UI Automation can see outlined, so a person can point at a part and say how
    // it should look. The window shows it; the same page is served on http://localhost:18911 for the Claude
    // preview pane (POST /capture {"surface":"start"}, GET /selection).
    public static class Capture
    {
        public static readonly string[][] Surfaces =
        {
            new[] { "start", "Start menu" }, new[] { "search", "Start search" }, new[] { "explorer", "File Explorer" },
            new[] { "explorer-menu", "Right-click menu" }, new[] { "dock", "Dock and preview" }, new[] { "screen", "Window in front" }
        };
        public static string DataDir => Path.Combine(Log.Dir, "inspector");
        const int Pad = 24;
        static readonly object Gate = new object();

        public static Dictionary<string, object> Take(string surface, string query = "settings", string hover = "", string hoverAt = "")
        {
            if (!Surfaces.Any(s => s[0] == surface)) throw new ArgumentException("unknown view " + surface);
            lock (Gate)
            {
                Directory.CreateDirectory(DataDir);
                var raw = Path.Combine(DataDir, "raw");
                Directory.CreateDirectory(raw);
                var script = Path.Combine(DataDir, "capture.ps1");
                File.WriteAllText(script, Program.Resource("inspector.capture.ps1"), new UTF8Encoding(true));
                var (code, output) = Shell.Run("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Surface {surface} -Out \"{raw}\" -Query \"{query.Replace("\"", "")}\" -Hover \"{hover.Replace("\"", "")}\" -HoverAt \"{hoverAt}\"", 120);
                if (code != 0) throw new InvalidOperationException(output.Length > 600 ? output.Substring(output.Length - 600) : output);
                var rj = Json.ParseObject(File.ReadAllText(Path.Combine(raw, "raw.json")).TrimStart('﻿'));
                var parts = rj.Arr("parts")?.Cast<Dictionary<string, object>>().ToList()
                            ?? (rj.Obj("parts") is Dictionary<string, object> one ? new List<Dictionary<string, object>> { one } : new List<Dictionary<string, object>>());
                long vx = rj.Long("vx"), vy = rj.Long("vy"), vw = rj.Long("vw"), vh = rj.Long("vh");
                var inner = parts.Where(p => p.Long("depth") > 0).ToList();
                if (inner.Count == 0) inner = parts;
                if (surface == "dock") { var b = parts.Where(p => p.Str("type") == "Button").ToList(); if (b.Count > 0) inner = b; }
                if (rj.Obj("extra") is Dictionary<string, object> ex) inner.Add(ex);
                long left = Math.Max(inner.Min(p => p.Long("x")) - Pad, vx), top = Math.Max(inner.Min(p => p.Long("y")) - Pad, vy);
                long right = Math.Min(inner.Max(p => p.Long("x") + p.Long("w")) + Pad, vx + vw), bottom = Math.Min(inner.Max(p => p.Long("y") + p.Long("h")) + Pad, vy + vh);
                var name = $"shot-{surface}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.png";
                using (var full = new Bitmap(Path.Combine(raw, "full.png")))
                using (var crop = full.Clone(new Rectangle((int)(left - vx), (int)(top - vy), (int)(right - left), (int)(bottom - top)), full.PixelFormat))
                    crop.Save(Path.Combine(DataDir, name), ImageFormat.Png);
                foreach (var old in Directory.GetFiles(DataDir, $"shot-{surface}-*.png")) if (Path.GetFileName(old) != name) try { File.Delete(old); } catch { }
                var outParts = new List<object>();
                foreach (var p in parts)
                {
                    long x = Math.Max(p.Long("x"), left), y = Math.Max(p.Long("y"), top);
                    long x2 = Math.Min(p.Long("x") + p.Long("w"), right), y2 = Math.Min(p.Long("y") + p.Long("h"), bottom);
                    if (x2 - x < 2 || y2 - y < 2 || (p.Long("depth") == 0 && parts.Count > 1)) continue;   // off-picture parts and the window itself
                    var q = new Dictionary<string, object>(p) { ["parent"] = p.Long("parent"), ["x"] = x - left, ["y"] = y - top, ["w"] = x2 - x, ["h"] = y2 - y };
                    outParts.Add(q);
                }
                var result = new Dictionary<string, object>
                {
                    ["surface"] = surface, ["title"] = Surfaces.First(s => s[0] == surface)[1], ["query"] = query, ["time"] = rj.Str("time"),
                    ["image"] = name, ["width"] = right - left, ["height"] = bottom - top, ["left"] = left, ["top"] = top, ["parts"] = outParts
                };
                File.WriteAllText(Path.Combine(DataDir, $"cap-{surface}.json"), Json.Write(result), new UTF8Encoding(false));
                return result;
            }
        }

        public static List<object> All()
        {
            var caps = new List<object>();
            foreach (var s in Surfaces)
            {
                var f = Path.Combine(DataDir, $"cap-{s[0]}.json");
                if (!File.Exists(f)) continue;
                var c = Json.ParseObject(File.ReadAllText(f));
                c["stamp"] = new DateTimeOffset(File.GetLastWriteTimeUtc(f)).ToUnixTimeSeconds();
                caps.Add(c);
            }
            return caps;
        }

        public static void Select(Dictionary<string, object> body)
        {
            Directory.CreateDirectory(DataDir);
            body["picked_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            File.WriteAllText(Path.Combine(DataDir, "selection.json"), Json.Write(body), new UTF8Encoding(false));
            File.AppendAllText(Path.Combine(DataDir, "selections.log"), Json.Write(body).Replace("\n", " ") + "\n");
        }

        // A styler target for the part, as a starting point (UI Automation names controls, the stylers target the
        // XAML elements inside them, so it often needs a "> Grid > Border#..." on the end).
        public static string StylerHint(Dictionary<string, object> part)
        {
            var cls = part.Str("cls", ""); var aid = part.Str("aid", "");
            if (cls.Length == 0) return "";
            return aid.Length > 0 && !aid.Contains(" ") ? $"{cls}#{aid}" : cls;
        }
    }

    // Small HTTP server on 127.0.0.1:18911 (localhost only) serving the inspector page for the preview pane.
    public static class Server
    {
        public const int Port = 18911;
        static TcpListener listener;

        public static bool Start()
        {
            if (listener != null) return true;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                new Thread(Loop) { IsBackground = true, Name = "inspector-http" }.Start();
                Log.Info($"Inspector on http://localhost:{Port}");
                return true;
            }
            catch (SocketException) { listener = null; Log.Warn($"port {Port} is busy (the older Python inspector may be running)"); return false; }
        }

        public static void RunForever() { if (Start()) Thread.Sleep(Timeout.Infinite); }

        static void Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(c));
            }
        }

        static void Handle(TcpClient client)
        {
            using (client)
            using (var s = client.GetStream())
            {
                try
                {
                    s.ReadTimeout = 10000;
                    var head = new StringBuilder();
                    var buf = new List<byte>();
                    int b;
                    while ((b = s.ReadByte()) >= 0) { buf.Add((byte)b); if (buf.Count >= 4 && buf[buf.Count - 4] == 13 && buf[buf.Count - 3] == 10 && buf[buf.Count - 2] == 13 && buf[buf.Count - 1] == 10) break; }
                    var lines = Encoding.ASCII.GetString(buf.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    string method = first[0], path = first[1].Split('?')[0];
                    int len = 0;
                    foreach (var l in lines) if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out len);
                    var body = new byte[len];
                    for (int got = 0; got < len;) { int n = s.Read(body, got, len - got); if (n <= 0) break; got += n; }
                    var (code, type, data) = Route(method, path, Encoding.UTF8.GetString(body));
                    var hdr = $"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nCache-Control: no-store\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n";
                    var hb = Encoding.ASCII.GetBytes(hdr);
                    s.Write(hb, 0, hb.Length); s.Write(data, 0, data.Length);
                }
                catch { }
            }
        }

        static (int, string, byte[]) Route(string method, string path, string body)
        {
            const string J = "application/json";
            byte[] U(string t) => Encoding.UTF8.GetBytes(t);
            if (method == "GET")
            {
                if (path == "/" || path == "/index.html") return (200, "text/html; charset=utf-8", U(Program.Resource("inspector.index.html")));
                if (path == "/captures") return (200, J, U(Json.Write(Capture.All())));
                if (path == "/selection")
                {
                    var f = Path.Combine(Capture.DataDir, "selection.json");
                    return (200, J, File.Exists(f) ? File.ReadAllBytes(f) : U("{}"));
                }
                if (path.StartsWith("/shots/"))
                {
                    var f = Path.Combine(Capture.DataDir, Path.GetFileName(path));
                    if (f.EndsWith(".png") && File.Exists(f)) return (200, "image/png", File.ReadAllBytes(f));
                }
            }
            else if (method == "POST")
            {
                Dictionary<string, object> d;
                try { d = body.Trim().Length == 0 ? new Dictionary<string, object>() : Json.ParseObject(body); } catch { return (400, J, U("{\"error\":\"bad JSON\"}")); }
                if (path == "/capture")
                {
                    try { return (200, J, U(Json.Write(Capture.Take(d.Str("surface", "start"), d.Str("query", "settings"), d.Str("hover", ""), d.Str("hover_at", ""))))); }
                    catch (Exception e) { return (500, J, U(Json.Write(new Dictionary<string, object> { ["error"] = e.Message }))); }
                }
                if (path == "/select") { Capture.Select(d); return (200, J, U("{\"ok\":true}")); }
            }
            return (404, J, U("{\"error\":\"not found\"}"));
        }
    }
}
