using System;
using UnityEngine;
using UnityEngine.EventSystems;
using Wizards.Mtga.Decks;

namespace MTGAEnhancementSuite.UI
{
    /// <summary>
    /// Runtime-attached pointer handler on user-folder <see cref="DeckFolderView"/>
    /// instances. Catches right-clicks on the folder header and opens the
    /// rename/delete context menu. Left-clicks pass through to MTGA's
    /// own Toggle (expand / collapse).
    ///
    /// Attached only to folders we created — built-in folders (My Decks,
    /// Starter Decks, Example Decks) don't get this component, so they
    /// stay read-only.
    /// </summary>
    internal class FolderContextMenuHandler : MonoBehaviour, IPointerClickHandler
    {
        public Guid FolderId { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Right) return;
            try
            {
                FolderContextMenu.Show(FolderId, eventData.position);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"FolderContextMenuHandler: {ex.Message}");
            }
        }
    }
}
