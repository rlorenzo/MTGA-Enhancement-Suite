using System;
using System.Collections.Generic;
using System.Linq;
using MTGAEnhancementSuite.State;

namespace MTGAEnhancementSuite.Features
{
    /// <summary>
    /// In-memory owner of the user's deck organization (folders + root order).
    /// All mutations go through here so we get persistence + reconciliation
    /// + per-mutation logging for free.
    ///
    /// Reconciliation: when called with the live list of decks from
    /// MTGA's DecksManager, prunes any folder/root-order entries pointing
    /// at deck ids that no longer exist. This handles the case where a
    /// user deleted a deck through MTGA's own flow (or via another machine)
    /// while a folder still references it.
    ///
    /// The class is intentionally thread-naive — all of MTGA's UI and our
    /// patches run on the Unity main thread.
    /// </summary>
    internal static class DeckOrganizationManager
    {
        private static DeckOrganization Org => ModSettings.Instance.DeckOrganization;

        // ---- Read accessors ----

        public static IReadOnlyList<DeckFolder> Folders => Org.Folders;

        public static IReadOnlyList<Guid> RootOrder => Org.RootOrder;

        public static DeckFolder FindFolderById(Guid id)
        {
            for (int i = 0; i < Org.Folders.Count; i++)
                if (Org.Folders[i].Id == id) return Org.Folders[i];
            return null;
        }

        /// <summary>
        /// Returns the folder containing this deck, or null if it's at root.
        /// O(folders * decks-per-folder); fine for the scales we'll see (a few
        /// dozen of each at most).
        /// </summary>
        public static DeckFolder FindFolderContaining(Guid deckId)
        {
            foreach (var folder in Org.Folders)
                if (folder.DeckIds.Contains(deckId)) return folder;
            return null;
        }

        // ---- Folder lifecycle ----

        public static DeckFolder CreateFolder(string name, IEnumerable<Guid> initialDecks = null)
        {
            var folder = new DeckFolder(string.IsNullOrWhiteSpace(name) ? "New Folder" : name.Trim());
            if (initialDecks != null)
            {
                foreach (var id in initialDecks)
                {
                    // Move the deck out of any folder it was previously in,
                    // and out of root order; the new folder becomes its home.
                    RemoveDeckFromAllFolders(id);
                    Org.RootOrder.Remove(id);
                    if (!folder.DeckIds.Contains(id)) folder.DeckIds.Add(id);
                }
            }
            Org.Folders.Add(folder);
            ModSettings.Instance.Save();
            Plugin.Log.LogInfo($"DeckOrganization: created folder '{folder.Name}' ({folder.Id}) with {folder.DeckIds.Count} decks");
            return folder;
        }

        public static bool RenameFolder(Guid folderId, string newName)
        {
            var folder = FindFolderById(folderId);
            if (folder == null) return false;
            folder.Name = string.IsNullOrWhiteSpace(newName) ? "Untitled" : newName.Trim();
            ModSettings.Instance.Save();
            return true;
        }

        /// <summary>
        /// Deletes a folder. Decks in the folder spill back to root (preserving
        /// their relative order, appended to the end of RootOrder).
        /// </summary>
        public static bool DeleteFolder(Guid folderId)
        {
            var idx = Org.Folders.FindIndex(f => f.Id == folderId);
            if (idx < 0) return false;
            var folder = Org.Folders[idx];
            foreach (var deckId in folder.DeckIds)
                if (!Org.RootOrder.Contains(deckId)) Org.RootOrder.Add(deckId);
            Org.Folders.RemoveAt(idx);
            ModSettings.Instance.Save();
            Plugin.Log.LogInfo($"DeckOrganization: deleted folder '{folder.Name}' ({folder.Id}); {folder.DeckIds.Count} decks spilled to root");
            return true;
        }

        public static bool ReorderFolders(IReadOnlyList<Guid> newOrder)
        {
            if (newOrder == null) return false;
            var dict = Org.Folders.ToDictionary(f => f.Id);
            var rebuilt = new List<DeckFolder>(Org.Folders.Count);
            foreach (var id in newOrder)
                if (dict.TryGetValue(id, out var f) && !rebuilt.Contains(f)) rebuilt.Add(f);
            // Append anything missing from newOrder so we never lose folders.
            foreach (var f in Org.Folders)
                if (!rebuilt.Contains(f)) rebuilt.Add(f);
            Org.Folders.Clear();
            Org.Folders.AddRange(rebuilt);
            ModSettings.Instance.Save();
            return true;
        }

        // ---- Deck membership ----

        public static void MoveDeckToFolder(Guid deckId, Guid folderId, int insertIndex = -1)
        {
            var target = FindFolderById(folderId);
            if (target == null) return;
            RemoveDeckFromAllFolders(deckId);
            Org.RootOrder.Remove(deckId);
            if (insertIndex < 0 || insertIndex > target.DeckIds.Count)
                target.DeckIds.Add(deckId);
            else
                target.DeckIds.Insert(insertIndex, deckId);
            ModSettings.Instance.Save();
        }

        public static void MoveDeckToRoot(Guid deckId, int insertIndex = -1)
        {
            RemoveDeckFromAllFolders(deckId);
            Org.RootOrder.Remove(deckId);
            if (insertIndex < 0 || insertIndex > Org.RootOrder.Count)
                Org.RootOrder.Add(deckId);
            else
                Org.RootOrder.Insert(insertIndex, deckId);
            ModSettings.Instance.Save();
        }

        /// <summary>
        /// Removes a deck reference entirely (folder membership + root order).
        /// Called when MTGA notifies us a deck has been deleted server-side.
        /// </summary>
        public static void ForgetDeck(Guid deckId)
        {
            bool changed = RemoveDeckFromAllFolders(deckId);
            changed |= Org.RootOrder.Remove(deckId);
            if (changed) ModSettings.Instance.Save();
        }

        private static bool RemoveDeckFromAllFolders(Guid deckId)
        {
            bool changed = false;
            foreach (var folder in Org.Folders)
                if (folder.DeckIds.Remove(deckId)) changed = true;
            return changed;
        }

        // ---- Reconciliation ----

        /// <summary>
        /// Drops any folder / root-order entries that point at decks not in
        /// <paramref name="liveDeckIds"/>. Returns the number of stale ids
        /// removed (folder + root combined). Safe to call frequently.
        /// </summary>
        public static int Reconcile(IEnumerable<Guid> liveDeckIds)
        {
            if (liveDeckIds == null) return 0;
            var live = new HashSet<Guid>(liveDeckIds);
            int removed = 0;
            foreach (var folder in Org.Folders)
            {
                int before = folder.DeckIds.Count;
                folder.DeckIds.RemoveAll(id => !live.Contains(id));
                removed += before - folder.DeckIds.Count;
            }
            int rootBefore = Org.RootOrder.Count;
            Org.RootOrder.RemoveAll(id => !live.Contains(id));
            removed += rootBefore - Org.RootOrder.Count;
            if (removed > 0)
            {
                ModSettings.Instance.Save();
                Plugin.Log.LogInfo($"DeckOrganization: reconciled — pruned {removed} stale deck reference(s)");
            }
            return removed;
        }
    }
}
