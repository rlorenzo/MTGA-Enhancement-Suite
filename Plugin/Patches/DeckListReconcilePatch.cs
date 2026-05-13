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
