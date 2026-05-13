using System;
using MTGAEnhancementSuite.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Wizards.Mtga.Decks;

namespace MTGAEnhancementSuite.UI
{
    /// <summary>
    /// Runtime-attached overlay on each <see cref="DeckView"/> tile. Renders
    /// a checkmark in the corner when the deck is selected in multi-select
    /// mode; renders nothing when multi-select is off.
    ///
    /// Subscribes to <see cref="DeckMultiSelectState.OnChanged"/> for
    /// reactive updates rather than polling.
    /// </summary>
    internal class DeckTileSelectionOverlay : MonoBehaviour
    {
        private DeckView _deckView;
        private GameObject _badge;
        private Image _badgeBg;
        private GameObject _check;

        private const float BadgeSize = 56f;
        private const float BadgeMargin = 12f;

        private void Awake()
        {
            _deckView = GetComponent<DeckView>();
            BuildBadge();
            DeckMultiSelectState.OnChanged += OnSelectionChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            DeckMultiSelectState.OnChanged -= OnSelectionChanged;
        }

        /// <summary>
        /// Re-reads state from <see cref="DeckMultiSelectState"/> and updates
        /// the badge's visibility / checked state. Idempotent.
        /// </summary>
        public void Refresh()
        {
            if (_deckView == null || _badge == null) return;

            if (!DeckMultiSelectState.IsActive)
            {
                _badge.SetActive(false);
                return;
            }

            _badge.SetActive(true);
            Guid id;
            try { id = _deckView.GetDeckId(); }
            catch { _badge.SetActive(false); return; }

            bool selected = id != Guid.Empty && DeckMultiSelectState.IsSelected(id);
            // Unselected: transparent fill (just the thin ring shows).
            // Selected: filled blue.
            _badgeBg.color = selected
                ? new Color(0.18f, 0.55f, 0.95f, 0.95f)
                : new Color(0, 0, 0, 0);
            _badgeBg.raycastTarget = false;
            if (_check != null) _check.SetActive(selected);
        }

        private void OnSelectionChanged() => Refresh();

        private void BuildBadge()
        {
            if (_badge != null) return;

            // Top-right corner of the tile's RectTransform.
            _badge = new GameObject("MTGAES_SelectionBadge");
            _badge.transform.SetParent(transform, false);

            var rt = _badge.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-BadgeMargin, -BadgeMargin);
            rt.sizeDelta = new Vector2(BadgeSize, BadgeSize);

            _badgeBg = _badge.AddComponent<Image>();
            _badgeBg.color = new Color(0, 0, 0, 0);
            _badgeBg.raycastTarget = false;

            // Hollow white border made from four thin edge rectangles.
            // (An Image set to "slightly larger than the badge" doesn't make
            // a border — it just covers the badge. A real hollow outline
            // needs four edges or a 9-sliced sprite, and we don't have a
            // sprite asset.)
            const float Border = 2f;
            var ringColor = new Color(1f, 1f, 1f, 0.95f);
            BuildEdge(_badge.transform, "EdgeTop",    ringColor, anchorMinY: 1, anchorMaxY: 1, height: Border, offsetY: 0);
            BuildEdge(_badge.transform, "EdgeBottom", ringColor, anchorMinY: 0, anchorMaxY: 0, height: Border, offsetY: 0);
            BuildSide(_badge.transform, "EdgeLeft",   ringColor, anchorMinX: 0, anchorMaxX: 0, width: Border);
            BuildSide(_badge.transform, "EdgeRight", ringColor, anchorMinX: 1, anchorMaxX: 1, width: Border);

            // Checkmark — prefer a real PNG asset from icons/check.png;
            // fall back to a TMP ✓ glyph if not present.
            _check = new GameObject("Check");
            _check.transform.SetParent(_badge.transform, false);
            var checkRt = _check.AddComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(6, 6);
            checkRt.offsetMax = new Vector2(-6, -6);

            var sprite = IconLoader.Get("check");
            if (sprite != null)
            {
                var img = _check.AddComponent<Image>();
                img.sprite = sprite;
                img.color = Color.white;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                var tmp = _check.AddComponent<TextMeshProUGUI>();
                var font = TmpFontHelper.Get();
                if (font != null) tmp.font = font;
                tmp.text = "✓";
                tmp.fontSize = 56;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
                tmp.extraPadding = false;
            }

            _badge.SetActive(false); // shown by Refresh()
        }

        /// <summary>Top or bottom edge — stretches full width.</summary>
        private static void BuildEdge(Transform parent, string name, Color color,
            float anchorMinY, float anchorMaxY, float height, float offsetY)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, anchorMinY);
            rt.anchorMax = new Vector2(1f, anchorMaxY);
            rt.pivot = new Vector2(0.5f, anchorMinY);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, offsetY);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        /// <summary>Left or right edge — stretches full height.</summary>
        private static void BuildSide(Transform parent, string name, Color color,
            float anchorMinX, float anchorMaxX, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorMinX, 0f);
            rt.anchorMax = new Vector2(anchorMaxX, 1f);
            rt.pivot = new Vector2(anchorMinX, 0.5f);
            rt.sizeDelta = new Vector2(width, 0f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }
    }
}
