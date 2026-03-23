using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace MTGAEnhancementSuite.UrlScheme
{
    internal static class UrlSchemeRegistrar
    {
        private const string Scheme = "mtgaes";

        /// <summary>
        /// Registers the mtgaes:// URL protocol handler pointing at the current MTGA.exe.
        /// Uses HKCU so no admin rights needed.
        /// </summary>
        public static void Register()
        {
            var platform = Environment.OSVersion.Platform;

            if (platform == PlatformID.Win32NT || platform == PlatformID.Win32Windows)
            {
                RegisterWindows();
            }
            else if (platform == PlatformID.Unix || platform == PlatformID.MacOSX)
            {
                // macOS/Linux: URL scheme registration will be handled by the installer.
                // The plugin cannot register schemes at runtime on these platforms
                // without root access or a properly signed .app bundle.
                Plugin.Log.LogInfo("URL scheme registration skipped (non-Windows platform)");
            }
            else
            {
                Plugin.Log.LogWarning($"URL scheme registration not supported on platform: {platform}");
            }
        }

        private static void RegisterWindows()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule.FileName;

                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}"))
                {
                    key.SetValue("", $"URL:MTGA Enhancement Suite");
                    key.SetValue("URL Protocol", "");

                    using (var commandKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                Plugin.Log.LogInfo($"URL scheme '{Scheme}://' registered → {exePath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to register URL scheme: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks command line args for a mtgaes:// URL.
        /// Returns the URL if found, null otherwise.
        /// </summary>
        public static string GetUrlFromArgs()
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg.StartsWith("mtgaes://", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo($"Found URL in command line args: {arg}");
                    return arg;
                }
            }
            return null;
        }

        /// <summary>
        /// Parses a mtgaes://join/{challengeId}/{format} URL.
        /// Returns true if valid, with challengeId and format populated.
        /// </summary>
        public static bool ParseJoinUrl(string url, out string challengeId, out string format)
        {
            challengeId = null;
            format = null;

            if (string.IsNullOrEmpty(url))
                return false;

            try
            {
                // mtgaes://join/{challengeId}/{format}
                var uri = new Uri(url);
                if (uri.Scheme != Scheme)
                    return false;

                var path = uri.Host + uri.AbsolutePath; // "join/{id}/{format}"
                var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3 && parts[0].Equals("join", StringComparison.OrdinalIgnoreCase))
                {
                    challengeId = parts[1];
                    format = parts[2].ToLowerInvariant();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to parse URL '{url}': {ex.Message}");
            }

            return false;
        }
    }
}
