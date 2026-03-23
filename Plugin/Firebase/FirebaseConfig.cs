using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace MTGAEnhancementSuite.Firebase
{
    internal class FirebaseConfig
    {
        public string ApiKey { get; set; }
        public string DatabaseUrl { get; set; }
        public string FunctionUrl { get; set; }
        public string ProjectId { get; set; }

        private static FirebaseConfig _instance;

        public static FirebaseConfig Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        private static FirebaseConfig Load()
        {
            try
            {
                // Look for config.json next to the plugin DLL
                var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var configPath = Path.Combine(pluginDir, "config.json");

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<FirebaseConfig>(json);
                    Plugin.Log.LogInfo("Firebase config loaded from " + configPath);
                    return config;
                }

                Plugin.Log.LogWarning("config.json not found at " + configPath);
                return new FirebaseConfig();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to load Firebase config: {ex}");
                return new FirebaseConfig();
            }
        }
    }
}
