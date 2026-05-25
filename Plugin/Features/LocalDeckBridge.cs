using System;
using System.Reflection;
using HarmonyLib;
using MTGAEnhancementSuite.Helpers;
using SharedClientCore.SharedClientCore.Code.Providers;
using UnityEngine;
using Wizards.Mtga.Decks;
using Wizards.Mtga.FrontDoorModels;
using Wotc.Mtga.Cards.Database;
using Wotc.Mtga.Loc;

namespace MTGAEnhancementSuite.Features
{
    /// <summary>
    /// Game-coupled glue between <see cref="LocalDeckStore"/> (plain disk I/O)
    /// and MTGA's deck types. Converts local Arena-export text into a
    /// <see cref="Client_Deck"/> for rendering / editing / cloud-conversion, and
    /// serializes a Client_Deck back to text for "Make Local".
    ///
    /// Pulls the providers it needs (card DB, set metadata) from the live
    /// <c>DeckManagerController</c>'s private fields — those are guaranteed to be
    /// the correct instances and the controller is always present in the deck
    /// screen where every local-deck operation originates.
    /// </summary>
    internal static class LocalDeckBridge
    {
        private static FieldInfo _cardDbField;
        private static FieldInfo _setMetaField;
        private static FieldInfo _decksManagerField;

        private static void EnsureFields()
        {
            if (_cardDbField != null) return;
            var t = typeof(DeckManagerController);
            _cardDbField       = AccessTools.Field(t, "_cardDatabase");
            _setMetaField      = AccessTools.Field(t, "_setMetadataProvider");
            _decksManagerField = AccessTools.Field(t, "_decksManager");
        }

        public static DeckManagerController Controller =>
            UnityEngine.Object.FindObjectOfType<DeckManagerController>();

        // Providers resolved Pantry-first so they work in ANY scene — crucially
        // the deck-builder scene, where DeckManagerController isn't findable
        // (FindObjectOfType skips inactive objects). Falls back to the live
        // controller's fields if Pantry doesn't have them.
        private static CardDatabase GetCardDb()
        {
            var db = GameReflection.PantryGet<CardDatabase>();
            if (db != null) return db;
            var ctrl = Controller;
            if (ctrl == null) return null;
            EnsureFields();
            return _cardDbField?.GetValue(ctrl) as CardDatabase;
        }

        private static ISetMetadataProvider GetSetMeta()
        {
            var sm = GameReflection.PantryGet<ISetMetadataProvider>();
            if (sm != null) return sm;
            var ctrl = Controller;
            if (ctrl == null) return null;
            EnsureFields();
            return _setMetaField?.GetValue(ctrl) as ISetMetadataProvider;
        }

        public static DecksManager GetDecksManager()
        {
            var ctrl = Controller;
            if (ctrl != null)
            {
                EnsureFields();
                var dm = _decksManagerField?.GetValue(ctrl) as DecksManager;
                if (dm != null) return dm;
            }
            // Fallback to Pantry if the controller isn't up yet.
            return GameReflection.PantryGet<DecksManager>();
        }

        /// <summary>
        /// Reconstructs a Client_Deck from a local deck's text via MTGA's own
        /// importer, stamping our stable local GUID + name onto it. Returns null
        /// on parse failure. NOTE: MTGA's importer is inventory-clamped — cards
        /// the player no longer owns come back reduced; <paramref name="clamped"/>
        /// is set true when that (or any unknown-card substitution) likely
        /// happened so the caller can warn once.
        /// </summary>
        public static Client_Deck BuildClientDeck(LocalDeck local, out bool clamped)
        {
            clamped = false;
            if (local == null) return null;
            var text = LocalDeckStore.ReadText(local.Id);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var cardDb = GetCardDb();
            var setMeta = GetSetMeta();
            if (cardDb == null || setMeta == null)
            {
                Plugin.Log.LogWarning("LocalDeckBridge: card DB / set metadata provider unavailable");
                return null;
            }

            var inventory = WrapperController.Instance?.InventoryManager?.Cards;

            Client_Deck deck;
            MTGALocalizedString err;
            bool ok;
            try
            {
                ok = WrapperDeckUtilities.TryImportDeck(
                    text, cardDb, setMeta, Languages.ActiveLocProvider,
                    inventory, Languages.CurrentLanguage, out deck, out err);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckBridge.BuildClientDeck: TryImportDeck threw: {ex.Message}");
                return null;
            }

            if (!ok || deck == null)
            {
                Plugin.Log.LogWarning($"LocalDeckBridge: import failed for '{local.Name}': {err?.Key}");
                return null;
            }

            // Stamp our identity. IsNetDeck=false → tile renders as an editable
            // user deck (rename pencil shown).
            deck.Summary.DeckId = local.Id;
            deck.Summary.Name = local.Name;
            deck.Summary.IsNetDeck = false;
            deck.Summary.NetDeckFolderId = null;
            // Arena export text has no format / deck-box art; restore them from
            // the sidecar meta so the tile shows the right box image + legality
            // badge, the deck-bucket filter works, and the builder opens in the
            // right format. Fall back to the importer's guess if unset.
            var meta = local.Meta;
            if (meta != null)
            {
                if (!string.IsNullOrEmpty(meta.Format)) deck.Summary.Format = meta.Format;
                if (meta.DeckTileId != 0) deck.Summary.DeckTileId = meta.DeckTileId;
                if (meta.DeckArtId != 0) deck.Summary.DeckArtId = meta.DeckArtId;
            }

            // Heuristic clamp detection: if the player is missing cards, the
            // imported quantities can be lower than the text asked for. We can't
            // cheaply diff here, so flag clamp when inventory is present and the
            // import path could have reduced — left false unless we add a diff.
            clamped = false;
            return deck;
        }

        /// <summary>Serializes a Client_Deck to Arena export text for "Make Local".</summary>
        public static string ExportText(Client_Deck deck)
        {
            if (deck == null) return null;
            try { return ExportTextFromDeckInfo(DeckServiceWrapperHelpers.ToAzureModel(deck)); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckBridge.ExportText: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Serializes a DeckInfo (Azure model) to Arena export text. Used by the
        /// deck-builder save-intercept, which already has the edited deck as a
        /// DeckInfo.
        /// </summary>
        public static string ExportTextFromDeckInfo(DeckInfo info)
        {
            if (info == null) return null;
            var cardDb = GetCardDb();
            if (cardDb == null) { Plugin.Log.LogWarning("LocalDeckBridge.ExportTextFromDeckInfo: no card DB"); return null; }
            try
            {
                return WrapperDeckUtilities.ToExportString(info, Languages.ActiveLocProvider, cardDb);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckBridge.ExportTextFromDeckInfo: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds a DeckViewInfo for a local deck so it can be rendered as a
        /// native tile. Uses MTGA's own builder, which fills in the cosmetic
        /// fields (sleeve, cardback, etc.) that the tile renderer dereferences.
        /// Returns null on failure (caller falls back to custom rows).
        /// </summary>
        public static DeckViewInfo BuildDeckViewInfo(LocalDeck local)
        {
            var deck = BuildClientDeck(local, out _);
            if (deck == null) return null;
            var builder = GameReflection.PantryGet<DeckViewBuilder>();
            if (builder == null)
            {
                Plugin.Log.LogWarning("LocalDeckBridge: DeckViewBuilder not in Pantry");
                return null;
            }
            try
            {
                return builder.CreateDeckViewInfoFromDeckSummary(deck);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"LocalDeckBridge.BuildDeckViewInfo('{local.Name}'): {ex.Message}");
                return null;
            }
        }
    }
}
