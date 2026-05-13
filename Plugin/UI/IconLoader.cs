using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MTGAEnhancementSuite.UI
{
    /// <summary>
    /// Loads PNG icon assets from
    ///   &lt;plugin-dir&gt;/icons/&lt;name&gt;.png
    /// into <see cref="Sprite"/>s, with per-name caching. If a file isn't
    /// present, <see cref="Get"/> returns null and callers should fall back
    /// to a vector / glyph draw.
    ///
    /// Asset spec: monochrome white or grey on transparent background.
    /// PNG, RGBA8, 64×64 or 128×128 (Unity scales freely). Color is
    /// multiplied by the Image's tint, so white assets are the most flexible.
    /// </summary>
    internal static class IconLoader
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static string _iconsDir;

        private static string IconsDir
        {
            get
            {
                if (_iconsDir == null)
                {
                    var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    _iconsDir = Path.Combine(pluginDir ?? ".", "icons");
                }
                return _iconsDir;
            }
        }

        public static Sprite Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out var cached)) return cached;

            try
            {
                var path = Path.Combine(IconsDir, name + ".png");
                if (!File.Exists(path)) { _cache[name] = null; return null; }

                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                if (!tex.LoadImage(bytes))
                {
                    Plugin.Log.LogWarning($"IconLoader: failed to decode {path}");
                    _cache[name] = null;
                    return null;
                }

                var sprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 100f);
                sprite.name = "MTGAES_Icon_" + name;
                _cache[name] = sprite;
                Plugin.Log.LogInfo($"IconLoader: loaded {name}.png ({tex.width}×{tex.height})");
                return sprite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"IconLoader.Get({name}): {ex.Message}");
                _cache[name] = null;
                return null;
            }
        }
    }
}
