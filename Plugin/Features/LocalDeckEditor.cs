using System;
using System.Reflection;
using HarmonyLib;
using MTGAEnhancementSuite.UI;
using Wizards.Mtga.Decks;
using Wizards.Mtga.FrontDoorModels;

namespace MTGAEnhancementSuite.Features
{
    /// <summary>
    /// Opens MTGA's deck builder on a local deck (a synthetic Client_Deck
    /// reconstructed from the local .txt) for true in-place editing. The
    /// companion <see cref="Patches.DeckBuilderSavePatch"/> intercepts the
    /// builder's save and redirects it back to disk instead of creating a
    /// cloud deck.
    /// </summary>
    internal static class LocalDeckEditor
    {
        private static MethodInfo _editGoToBuilder;

        /// <summary>
        /// Opens the deck builder on a fresh, empty local deck (the "+" button
        /// on the Local Decks folder). A pending local id is registered so the
        /// save-intercept writes it to disk on save instead of the cloud.
        /// </summary>
        public static void CreateNew()
        {
            try
            {
                var pendingId = Guid.NewGuid();
                LocalDeckStore.RegisterPending(pendingId);

                var deck = new Client_Deck();
                deck.Summary.DeckId = pendingId;
                deck.Summary.Name = "New Local Deck";
                deck.Summary.IsNetDeck = false;
                deck.Summary.NetDeckFolderId = null;
                try { deck.Summary.Format = WrapperController.Instance?.FormatManager?.GetDefaultFormat()?.FormatName; }
                catch { /* builder will fall back to its own default */ }

                var info = DeckServiceWrapperHelpers.ToAzureModel(deck);
                info.id = pendingId;
                info.isLoaded = true;
                info.name = "New Local Deck";

                if (!LaunchBuilder(info))
                {
                    LocalDeckStore.ClearPending(pendingId);
                    return;
                }
                Plugin.Log.LogInfo("LocalDeckEditor: created new local deck (pending)");
                PerPlayerLog.Info($"LocalDeckEditor: new local deck (pending {pendingId})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"LocalDeckEditor.CreateNew failed: {ex}");
                Toast.Error("Couldn't open the deck builder");
            }
        }

        public static void OpenForEdit(Guid localId)
        {
            try
            {
                var local = LocalDeckStore.Get(localId);
                if (local == null) { Toast.Error("Local deck not found"); return; }

                var deck = LocalDeckBridge.BuildClientDeck(local, out _);
                if (deck == null) { Toast.Error($"Couldn't open '{local.Name}'"); return; }

                // Keep identity == localId so the save-intercept matches it.
                deck.Summary.DeckId = localId;

                var info = DeckServiceWrapperHelpers.ToAzureModel(deck);
                info.id = localId;
                // isLoaded=true makes WrapperDeckBuilder.Activate skip the
                // server GetFullDeck(id) it would otherwise do for a deck it
                // thinks needs loading (which would fail for a non-server id).
                info.isLoaded = true;
                // Make sure the builder opens in the deck's saved format.
                if (!string.IsNullOrEmpty(local.Format)) info.format = local.Format;

                if (!LaunchBuilder(info)) return;
                Plugin.Log.LogInfo($"LocalDeckEditor: opened '{local.Name}' for editing");
                PerPlayerLog.Info($"LocalDeckEditor: editing local '{local.Name}' ({localId})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"LocalDeckEditor.OpenForEdit failed: {ex}");
                Toast.Error("Couldn't open the deck builder");
            }
        }

        // Invokes DeckManagerController.Edit_GoToDeckBuilder(info, DeckBuilding,
        // isFirstEdit:false). isFirstEdit:false routes the builder's save through
        // UpdateDeck (which our save-intercept suppresses) — safer than
        // isFirstEdit:true, which would CreateDeck a junk cloud deck if the
        // intercept ever failed. Returns false (with a toast) on failure.
        private static bool LaunchBuilder(DeckInfo info)
        {
            var ctrl = LocalDeckBridge.Controller;
            if (ctrl == null) { Toast.Error("Deck manager not available"); return false; }

            if (_editGoToBuilder == null)
            {
                _editGoToBuilder = AccessTools.Method(typeof(DeckManagerController),
                    GameRefs.DeckManager_EditGoToBuilder,
                    new[] { typeof(DeckInfo), typeof(DeckBuilderMode), typeof(bool) });
            }
            if (_editGoToBuilder == null)
            {
                Plugin.Log.LogWarning("LocalDeckEditor: Edit_GoToDeckBuilder not found");
                Toast.Error("Couldn't open the deck builder");
                return false;
            }

            _editGoToBuilder.Invoke(ctrl, new object[] { info, DeckBuilderMode.DeckBuilding, false });
            return true;
        }
    }
}
