using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace MTGAEnhancementSuite.State
{
    /// <summary>
    /// Persistent mod settings stored in settings.json alongside the plugin DLL.
    /// </summary>
    internal class ModSettings
    {
        private static ModSettings _instance;
        private static string _settingsPath;

        [JsonProperty("disableCompanions")]
        public bool DisableCompanions { get; set; } = false;

        [JsonProperty("disableCardVFX")]
        public bool DisableCardVFX { get; set; } = false;

        /// <summary>
        /// User-defined deck folders + root-level ordering. Stored on-disk
        /// in this settings file only — never in deck names, never in
        /// MTGA's deck state, never on the wire. A user running MTGA on a
        /// different machine without the mod sees the normal deck list.
        /// </summary>
        [JsonProperty("deckOrganization")]
        public DeckOrganization DeckOrganization { get; set; } = new DeckOrganization();

        public static ModSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        private static string GetSettingsPath()
        {
            if (_settingsPath == null)
            {
                var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _settingsPath = Path.Combine(pluginDir, "settings.json");
            }
            return _settingsPath;
        }

        private static ModSettings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonConvert.DeserializeObject<ModSettings>(json);
                    // Older settings files won't have a deckOrganization field;
                    // the property initializer + Json.NET default handling
                    // leaves it as an empty DeckOrganization, but be defensive.
                    bool needsRewrite = false;
                    if (settings.DeckOrganization == null)
                    {
                        settings.DeckOrganization = new DeckOrganization();
                        needsRewrite = true;
                    }
                    // If the on-disk JSON doesn't already include the
                    // deckOrganization key, save once so the field becomes
                    // editable by hand. Cheap and self-healing.
                    if (!json.Contains("\"deckOrganization\"")) needsRewrite = true;

                    var folderCount = settings.DeckOrganization.Folders?.Count ?? 0;
                    var rootOrderCount = settings.DeckOrganization.RootOrder?.Count ?? 0;
                    Plugin.Log.LogInfo($"Settings loaded: companions={!settings.DisableCompanions}, cardVFX={!settings.DisableCardVFX}, folders={folderCount}, rootOrder={rootOrderCount}");
                    if (needsRewrite) { _instance = settings; settings.Save(); }
                    return settings;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not load settings: {ex.Message}");
            }
            return new ModSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(GetSettingsPath(), json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not save settings: {ex.Message}");
            }
        }
    }
}
