using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MTGAEnhancementSuite.Features;
using Wizards.Mtga.Decks;

namespace MTGAEnhancementSuite.Patches
{
    /// <summary>
    /// Phase 1 of the deck-organization feature: fires whenever the deck
    /// grid is populated, reconciles the saved folder/root-order data against
    /// the live deck list, and logs a one-line summary so we can verify the
    /// pipeline end-to-end before adding any UI.
    ///
    /// The actual MTGA populate method is
    ///   DeckViewSelector.SetDecks(IReadOnlyList&lt;DeckViewInfo&gt;, bool)
    /// (NOT MetaDeckSelector, which is a different widget). MTGA already has
    /// a server-side folder concept (`NetDeckFolder`); our client-side
    /// folders are layered on top without touching its data.
    ///
    /// No visible side effects yet — this is purely the storage-layer hook.
    /// </summary>
    [HarmonyPatch(typeof(DeckViewSelector), "SetDecks")]
    internal static class DeckListReconcilePatch
    {
        // The largest live deck count we've seen this session. Reconcile only
        // prunes when the current list is at least this big — see the guard
        // below for why this matters.
        private static int _maxLiveSeen;

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                // Pull the live deck list from MTGA's DecksManager (same path
                // DeckValidationPatch uses).
                var pantryType = AccessTools.TypeByName("Pantry");
                if (pantryType == null) return;
                var get = pantryType.GetMethod("Get").MakeGenericMethod(typeof(DecksManager));
                var decksManager = get.Invoke(null, null) as DecksManager;
                if (decksManager == null) return;

                var live = decksManager.GetAllCachedDecks();
                if (live == null) return;

                var liveIds = live
                    .Where(d => d != null && d.Summary != null)
                    .Select(d => d.Summary.DeckId)
                    .ToList();

                // DANGER: Reconcile permanently removes (and saves) any folder
                // assignment whose deck id isn't in liveIds. SetDecks fires
                // mid-load and during our own deck-list reloads (Make Local /
                // Make Cloud / edit), and in those windows GetAllCachedDecks()
                // can be empty or only partially populated. Reconciling against
                // that transient list would dump every deck out of its folder.
                //
                // Guard: only prune when the live list is at least as large as
                // the fullest we've seen this session. An empty or shrunken list
                // is treated as "not fully loaded" and skipped. A genuine user
                // deletion just leaves a harmless stale folder reference (our own
                // delete flows call ForgetDeck; a dangling id simply renders
                // nothing) — far better than nuking valid assignments.
                if (liveIds.Count == 0)
                {
                    PerPlayerLog.Info("DeckListReconcile: skipped (live list empty — likely mid-load)");
                    return;
                }
                if (_maxLiveSeen > 0 && liveIds.Count < _maxLiveSeen)
                {
                    PerPlayerLog.Info(
                        $"DeckListReconcile: skipped pruning (live={liveIds.Count} < max {_maxLiveSeen}; likely partial load)");
                    return;
                }
                _maxLiveSeen = liveIds.Count;

                int pruned = DeckOrganizationManager.Reconcile(liveIds);
                PerPlayerLog.Info(
                    $"DeckListReconcile: live={liveIds.Count}, " +
                    $"folders={DeckOrganizationManager.Folders.Count}, " +
                    $"rootOrder={DeckOrganizationManager.RootOrder.Count}, " +
                    $"pruned={pruned}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"DeckListReconcilePatch: {ex.Message}");
            }
        }
    }
}
