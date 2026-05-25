using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MTGAEnhancementSuite.Features
{
    /// <summary>
    /// One local deck: an Arena-export .txt file on disk. The deck NAME is the
    /// filename stem (Arena export text carries no name line), and the synthetic
    /// deck GUID is a deterministic hash of that stem so it stays stable across
    /// sessions and across the rendering / edit / convert code paths.
    /// </summary>
    /// <summary>
    /// Metadata the Arena export text can't carry, persisted in a sidecar
    /// JSON index so the .txt files stay pure / paste-compatible.
    /// </summary>
    internal sealed class LocalDeckMeta
    {
        public string Format;       // MTGA format name (e.g. "Standard")
        public uint DeckTileId;     // card grpId driving the deck-box art (fallback)
        public uint DeckArtId;      // preferred deck-box art id
        public string FolderId;     // user folder GUID this deck came from (string), or null
        public string FolderName;   // folder name, used as a fallback if the id is gone
    }

    internal sealed class LocalDeck
    {
        public Guid Id;          // deterministic, derived from Name
        public string Name;      // filename stem
        public string FilePath;  // absolute path to the .txt
        public LocalDeckMeta Meta = new LocalDeckMeta();

        // Convenience passthroughs.
        public string Format => Meta?.Format;
    }

    /// <summary>
    /// Disk-backed store for "local decks" — the mechanism that lets users keep
    /// more decks than MTGA's cloud limit. Pure file I/O + an in-memory registry;
    /// no game types here. Game-coupled reconstruction (text → Client_Deck) lives
    /// in <see cref="LocalDeckBridge"/>.
    ///
    /// Files live under %APPDATA%\MTGAEnhancementSuite\localdecks\*.txt so they
    /// survive the plugin auto-updater (which only swaps DLLs) and MTGA patches.
    ///
    /// The <see cref="IsLocal"/> GUID set is the authoritative signal used by the
    /// deck-builder save-intercept to decide "this edit belongs to a local deck,
    /// write it to disk instead of the cloud."
    /// </summary>
    internal static class LocalDeckStore
    {
        private static string _dir;
        private static readonly Dictionary<Guid, LocalDeck> _byId = new Dictionary<Guid, LocalDeck>();
        private static bool _loaded;

        // Arena export text carries no format / deck-box art / folder, so we
        // keep a tiny sidecar index mapping filename-stem (lowercased) -> meta.
        // The .txt files stay pure Arena export (paste-compatible).
        private const string MetaFileName = "_localdecks_meta.json";
        private static Dictionary<string, LocalDeckMeta> _metaByKey = new Dictionary<string, LocalDeckMeta>();

        // Brand-new local decks being authored in the builder before their
        // first save — registered here so the save-intercept recognizes them.
        private static readonly HashSet<Guid> _pendingIds = new HashSet<Guid>();

        private static string MetaKey(string name) => (name ?? "").ToLowerInvariant();

        public static string Directory
        {
            get
            {
                if (_dir == null)
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    _dir = Path.Combine(appData, "MTGAEnhancementSuite", "localdecks");
                }
                return _dir;
            }
        }

        public static IReadOnlyCollection<LocalDeck> All
        {
            get { EnsureLoaded(); return _byId.Values.ToList(); }
        }

        public static int Count { get { EnsureLoaded(); return _byId.Count; } }

        /// <summary>Loads (or reloads) the on-disk deck list into memory.</summary>
        public static void Load()
        {
            _byId.Clear();
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    _metaByKey = new Dictionary<string, LocalDeckMeta>();
                    _loaded = true;
                    return;
                }
                LoadMeta();
                foreach (var path in System.IO.Directory.GetFiles(Directory, "*.txt"))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    _metaByKey.TryGetValue(MetaKey(name), out var meta);
                    var deck = new LocalDeck
                    {
                        Id = GuidForName(name),
                        Name = name,
                        FilePath = path,
                        Meta = meta ?? new LocalDeckMeta(),
                    };
                    // Last-writer-wins on the (astronomically unlikely) hash clash.
                    _byId[deck.Id] = deck;
                }
                Plugin.Log.LogInfo($"LocalDeckStore: loaded {_byId.Count} local deck(s) from {Directory}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckStore.Load failed: {ex.Message}");
            }
            _loaded = true;
        }

        private static void LoadMeta()
        {
            _metaByKey = new Dictionary<string, LocalDeckMeta>();
            try
            {
                var metaPath = Path.Combine(Directory, MetaFileName);
                if (!File.Exists(metaPath)) return;
                var json = File.ReadAllText(metaPath);
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, LocalDeckMeta>>(json);
                if (parsed != null)
                    foreach (var kv in parsed)
                        if (kv.Value != null) _metaByKey[kv.Key.ToLowerInvariant()] = kv.Value;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckStore.LoadMeta: {ex.Message}");
            }
        }

        private static void SaveMeta()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    System.IO.Directory.CreateDirectory(Directory);
                var metaPath = Path.Combine(Directory, MetaFileName);
                File.WriteAllText(metaPath,
                    Newtonsoft.Json.JsonConvert.SerializeObject(_metaByKey, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckStore.SaveMeta: {ex.Message}");
            }
        }

        // ---- pending (new) decks ----

        /// <summary>Registers a not-yet-saved local deck id so the save-intercept recognizes it.</summary>
        public static void RegisterPending(Guid id) { _pendingIds.Add(id); }
        public static void ClearPending(Guid id) { _pendingIds.Remove(id); }

        private static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        public static bool IsLocal(Guid id)
        {
            EnsureLoaded();
            return _byId.ContainsKey(id) || _pendingIds.Contains(id);
        }

        public static LocalDeck Get(Guid id)
        {
            EnsureLoaded();
            return _byId.TryGetValue(id, out var d) ? d : null;
        }

        public static string ReadText(Guid id)
        {
            var deck = Get(id);
            if (deck == null) return null;
            try { return File.ReadAllText(deck.FilePath); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckStore.ReadText({deck.Name}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes a new local deck (or overwrites one with the same resolved
        /// name). Returns the LocalDeck, or null on failure. Use for "Make Local".
        /// </summary>
        public static LocalDeck Save(string desiredName, string exportText, LocalDeckMeta meta = null)
        {
            EnsureLoaded();
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    System.IO.Directory.CreateDirectory(Directory);

                var name = UniqueName(SanitizeName(desiredName));
                var path = Path.Combine(Directory, name + ".txt");
                File.WriteAllText(path, exportText ?? "");

                meta = meta ?? new LocalDeckMeta();
                var deck = new LocalDeck { Id = GuidForName(name), Name = name, FilePath = path, Meta = meta };
                _byId[deck.Id] = deck;
                _metaByKey[MetaKey(name)] = meta;
                SaveMeta();
                Plugin.Log.LogInfo($"LocalDeckStore: saved local deck '{name}' (format={meta.Format ?? "?"})");
                return deck;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckStore.Save('{desiredName}'): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Overwrites an existing local deck's contents (and possibly its name)
        /// in place. Used by the deck-builder save-intercept. Keeps the SAME
        /// GUID the editor was opened with even if the name changed, so the
        /// caller's notion of identity is stable for that session; the on-disk
        /// file is renamed to match the new name.
        /// </summary>
        public static LocalDeck Update(Guid id, string newName, string exportText, LocalDeckMeta meta = null)
        {
            EnsureLoaded();
            var existing = Get(id);
            // Preserve the existing meta if the caller didn't supply one.
            if (meta == null) meta = existing?.Meta ?? new LocalDeckMeta();
            try
            {
                var resolved = SanitizeName(newName);
                if (string.IsNullOrWhiteSpace(resolved)) resolved = existing?.Name ?? "Untitled";

                // If renaming, avoid clobbering a different deck's file.
                string finalName = resolved;
                if (existing == null || !string.Equals(existing.Name, resolved, StringComparison.OrdinalIgnoreCase))
                    finalName = UniqueName(resolved, exceptId: id);

                var newPath = Path.Combine(Directory, finalName + ".txt");

                // Remove the old file if the name changed.
                if (existing != null && !string.Equals(existing.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    try { if (File.Exists(existing.FilePath)) File.Delete(existing.FilePath); } catch { }
                    _byId.Remove(existing.Id);
                    _metaByKey.Remove(MetaKey(existing.Name));
                }

                File.WriteAllText(newPath, exportText ?? "");
                var deck = new LocalDeck { Id = GuidForName(finalName), Name = finalName, FilePath = newPath, Meta = meta };
                _byId[deck.Id] = deck;
                // Also keep the original session GUID resolvable so an open editor
                // (which holds the old id) still maps here until the next reload.
                if (deck.Id != id) _byId[id] = deck;
                _metaByKey[MetaKey(finalName)] = meta;
                SaveMeta();
                _pendingIds.Remove(id); // first save of a new deck makes it real
                Plugin.Log.LogInfo($"LocalDeckStore: updated local deck '{finalName}' (format={meta.Format ?? "?"})");
                return deck;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckStore.Update({id}): {ex.Message}");
                return existing;
            }
        }

        public static bool Delete(Guid id)
        {
            EnsureLoaded();
            var deck = Get(id);
            if (deck == null) return false;
            try
            {
                if (File.Exists(deck.FilePath)) File.Delete(deck.FilePath);
                _byId.Remove(id);
                // Purge any alias entries pointing at the same file.
                foreach (var k in _byId.Where(kv => kv.Value == deck).Select(kv => kv.Key).ToList())
                    _byId.Remove(k);
                _metaByKey.Remove(MetaKey(deck.Name));
                SaveMeta();
                Plugin.Log.LogInfo($"LocalDeckStore: deleted local deck '{deck.Name}'");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckStore.Delete('{deck.Name}'): {ex.Message}");
                return false;
            }
        }

        // ---- helpers ----

        // Deterministic GUID from the (lowercased) deck name via MD5. Stable
        // across sessions; unique because filenames are unique within the dir.
        private static Guid GuidForName(string name)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes("mtgaes-local:" + name.ToLowerInvariant()));
                return new Guid(bytes);
            }
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Untitled";
            var cleaned = new string(name.Select(c =>
                Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray()).Trim();
            // Windows path length guard.
            if (cleaned.Length > 80) cleaned = cleaned.Substring(0, 80).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
        }

        // Appends " (2)", " (3)", … if a file with this stem already exists
        // (ignoring the deck identified by exceptId, for in-place renames).
        private static string UniqueName(string baseName, Guid? exceptId = null)
        {
            bool Taken(string n)
            {
                var id = GuidForName(n);
                if (exceptId.HasValue && id == exceptId.Value) return false;
                return _byId.ContainsKey(id) || File.Exists(Path.Combine(Directory, n + ".txt"));
            }
            if (!Taken(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{baseName} ({i})";
                if (!Taken(candidate)) return candidate;
            }
            return $"{baseName} ({Guid.NewGuid():N})".Substring(0, 80);
        }
    }
}
