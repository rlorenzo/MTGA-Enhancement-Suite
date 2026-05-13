using System;
using System.Collections.Generic;

namespace MTGAEnhancementSuite.State
{
    /// <summary>
    /// Session-scoped state for the deck-manager multi-select mode. Owned by
    /// the plugin (never persisted). Subscribers (deck-tile overlays, the
    /// bottom-strip button swap, etc.) listen on <see cref="OnChanged"/>.
    /// </summary>
    internal static class DeckMultiSelectState
    {
        public static bool IsActive { get; private set; }
        public static HashSet<Guid> SelectedIds { get; } = new HashSet<Guid>();

        /// <summary>Fires whenever IsActive flips or the selection set changes.</summary>
        public static event Action OnChanged;

        public static int SelectionCount => SelectedIds.Count;

        public static bool IsSelected(Guid deckId) => SelectedIds.Contains(deckId);

        public static void EnterMode()
        {
            if (IsActive) return;
            IsActive = true;
            SelectedIds.Clear();
            Plugin.Log.LogInfo("DeckMultiSelect: entering mode");
            Notify();
        }

        public static void ExitMode()
        {
            if (!IsActive) return;
            IsActive = false;
            SelectedIds.Clear();
            Plugin.Log.LogInfo("DeckMultiSelect: exiting mode");
            Notify();
        }

        public static void ToggleMode()
        {
            if (IsActive) ExitMode();
            else EnterMode();
        }

        public static void ToggleDeck(Guid deckId)
        {
            if (!IsActive) return;
            if (SelectedIds.Contains(deckId)) SelectedIds.Remove(deckId);
            else SelectedIds.Add(deckId);
            Notify();
        }

        public static void ClearSelection()
        {
            if (!IsActive || SelectedIds.Count == 0) return;
            SelectedIds.Clear();
            Notify();
        }

        private static void Notify()
        {
            try { OnChanged?.Invoke(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"DeckMultiSelect.OnChanged handler threw: {ex.Message}"); }
        }
    }
}
