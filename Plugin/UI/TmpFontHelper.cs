using TMPro;
using UnityEngine;

namespace MTGAEnhancementSuite.UI
{
    /// <summary>
    /// At runtime, finds a usable <see cref="TMP_FontAsset"/> by sampling any
    /// already-loaded TMP_Text in the scene. TMP needs an explicit font asset
    /// when you AddComponent it at runtime — without one, glyphs silently
    /// don't render. Calling Resources.Load on TMP's defaults is unreliable
    /// because MTGA may not ship them.
    /// </summary>
    internal static class TmpFontHelper
    {
        private static TMP_FontAsset _cached;

        public static TMP_FontAsset Get()
        {
            if (_cached != null) return _cached;

            // Prefer a normal TMP_Text we can see; falls back to scanning all
            // loaded assets if nothing's currently in the hierarchy.
            var sample = Object.FindObjectOfType<TMP_Text>();
            if (sample != null && sample.font != null) { _cached = sample.font; return _cached; }

            var any = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (any != null && any.Length > 0) { _cached = any[0]; return _cached; }

            return null;
        }
    }
}
