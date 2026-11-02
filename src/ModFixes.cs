using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Glasshouse
{
    // Fixed builds of other people's mods, for bugs their own builds still have. Each one is the author's code with a
    // small change (the patch is in fixes\), built with Windhawk's own compiler and published on this repo's
    // "mod-fixes" release. Installed in place of the mods.windhawk.net build of the same version and checked against
    // the hashes here; the mod keeps the author's version number, so Windhawk still offers the author's updates.
    public static class ModFixes
    {
        public class Fix
        {
            public string Id, Version, Name, Why;
            public string Sha64, Sha32, ShaSource;
            public string File(string suffix) => $"{Id}_{Version}_{Name}{suffix}";
        }

        const string ReleaseUrl = "https://github.com/maclanwright/glasshouse/releases/download/mod-fixes/";

        public static readonly Fix[] All =
        {
            new Fix
            {
                Id = "mac-island-dock", Version = "2.0.1", Name = "fix3",
                Why = "Bluetooth list: a device clicked stayed on \"Connecting...\" for good (and blocked other clicks), "
                    + "and Disconnect left headphones connected",
                Sha64 = "13ef527297149710031061679b3956dae168a629493f8e7dac699578bc2700c6", Sha32 = "63ddc231bf6836c687e8c186cc46b45f0ac5eb1d3850560a6d1ea77e662a4817",
                ShaSource = "5dcc59bd904c8f7f885e063b1fed9d2cc319ebf94267bb678a977aedfed08f69",
            },
        };

        // Only built for 64-bit Windows (an ARM machine gets the author's build).
        public static Fix For(string id, string version) =>
            RuntimeInformation.OSArchitecture == Architecture.X64 ? All.FirstOrDefault(f => f.Id == id && f.Version == version) : null;

        // The mod is meant to have a fix but the installed build isn't it.
        public static bool Missing(ModSpec want, string libraryFileName)
        {
            var fix = For(want.Id, want.Version);
            if (fix == null) return false;
            var have = Path.Combine(Windhawk.EngineModsDir, "64", libraryFileName ?? "");
            return !System.IO.File.Exists(have) || Sha256(have) != fix.Sha64;
        }

        // Downloads and places the fixed build like Windhawk.DownloadAndPlace does the author's; returns its file name,
        // or null when it can't be downloaded (the author's build is installed instead). A wrong hash stops the install.
        public static string Place(Fix fix, string modsDir, string sourceDir)
        {
            string lib = $"{fix.Id}_{fix.Version}_{fix.Name}_{new Random().Next(100000, 999999)}.dll";
            byte[] b64, b32, src;
            try
            {
                b64 = Net.Get(ReleaseUrl + fix.File("_64.dll"));
                b32 = Net.Get(ReleaseUrl + fix.File("_32.dll"));
                src = Net.Get(ReleaseUrl + fix.File(".wh.cpp"));
            }
            catch (Exception e) when (e is System.Net.WebException)
            {
                Log.Info($"    couldn't download the fixed {fix.Id} ({e.Message}): installing the author's build");
                return null;
            }
            Check(b64, fix.Sha64, fix.File("_64.dll"));
            Check(b32, fix.Sha32, fix.File("_32.dll"));
            Check(src, fix.ShaSource, fix.File(".wh.cpp"));
            foreach (var (folder, bytes) in new[] { ("64", b64), ("32", b32) })
            {
                var dir = Path.Combine(modsDir, folder);
                Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(Path.Combine(dir, lib), bytes);
            }
            Directory.CreateDirectory(sourceDir);
            System.IO.File.WriteAllBytes(Path.Combine(sourceDir, fix.Id + ".wh.cpp"), src);
            Log.Info($"    downloaded the fixed {fix.Id} {fix.Version} ({fix.Name}: {fix.Why})");
            return lib;
        }

        static void Check(byte[] bytes, string sha, string what)
        {
            if (Sha256(bytes) != sha) throw new InvalidDataException($"{what} isn't the published fix (hash differs)");
        }

        static string Sha256(byte[] bytes) { using (var h = SHA256.Create()) return Hex(h.ComputeHash(bytes)); }
        static string Sha256(string file) { using (var h = SHA256.Create()) using (var s = System.IO.File.OpenRead(file)) return Hex(h.ComputeHash(s)); }
        static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
