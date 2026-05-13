using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MTGAEnhancementSuite.UI
{
    /// <summary>
    /// Small hover-label widget attached to an MTGA <c>CustomButton</c>
    /// (or <c>CustomTouchButton</c>) clone. Subscribes to the button's
    /// OnMouseover / OnMouseoff UnityEvents and shows a dark pill containing
    /// the configured text above the button while the cursor is over it.
    ///
    /// Lives as a child GameObject of the button so it inherits positioning
    /// — but on its own Canvas with overrideSorting so it renders above
    /// sibling UI elements (no mask clipping).
    /// </summary>
    internal class ButtonTooltip : MonoBehaviour
    {
        private GameObject _label;
        private TextMeshProUGUI _labelTmp;

        public string Text { get; set; } = "";

        /// <summary>
        /// Attach (or update) a tooltip on the given button. Idempotent.
        /// </summary>
        public static ButtonTooltip Attach(GameObject button, string text)
        {
            if (button == null) return null;
            var existing = button.GetComponent<ButtonTooltip>();
            if (existing != null)
            {
                existing.Text = text;
                if (existing._labelTmp != null) existing._labelTmp.text = text;
                return existing;
            }
            var tt = button.AddComponent<ButtonTooltip>();
            tt.Text = text;
            return tt;
        }

        private void Awake()
        {
            BuildLabel();
            HookHoverEvents();
        }

        private void BuildLabel()
        {
            _label = new GameObject("MTGAES_Tooltip");
            _label.transform.SetParent(transform, false);

            // Use our own Canvas with a high sorting order so the tooltip
            // never gets buried under sibling UI or clipped by masks.
            var canvas = _label.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 150;
            _label.AddComponent<GraphicRaycaster>();

            var rt = _label.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f); // top-center of button
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);     // anchor bottom of tooltip
            rt.anchoredPosition = new Vector2(0f, 12f); // 12px above button top
            rt.sizeDelta = new Vector2(180f, 32f);

            var bg = _label.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.10f, 0.95f);
            bg.raycastTarget = false;

            // Text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(_label.transform, false);
            var textRt = textGO.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10, 0);
            textRt.offsetMax = new Vector2(-10, 0);
            _labelTmp = textGO.AddComponent<TextMeshProUGUI>();
            var font = TmpFontHelper.Get();
            if (font != null) _labelTmp.font = font;
            _labelTmp.text = Text;
            _labelTmp.fontSize = 16;
            _labelTmp.color = new Color(0.92f, 0.93f, 0.96f);
            _labelTmp.alignment = TextAlignmentOptions.Center;
            _labelTmp.raycastTarget = false;
            _labelTmp.extraPadding = false;
            // Auto-size width to fit content so short labels don't look
            // stranded inside a big pill.
            _labelTmp.enableAutoSizing = false;

            _label.SetActive(false);
        }

        private void HookHoverEvents()
        {
            // CustomButton (or CustomTouchButton) is on the same GameObject;
            // find it via reflection so we don't pin a hard reference to a
            // type that may change name across MTGA releases.
            foreach (var comp in GetComponents<Component>())
            {
                if (comp == null) continue;
                var t = comp.GetType();
                var n = t.Name;
                if (n.IndexOf("CustomButton", StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("CustomTouchButton", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // The casing of these events varies — CustomButton uses
                // OnMouseover/OnMouseoff, CustomTouchButton uses OnMouseOver/OnMouseOff.
                AttachIfFound(comp, t, "OnMouseover", "OnMouseOver", Show);
                AttachIfFound(comp, t, "OnMouseoff", "OnMouseOff", Hide);
                return;
            }
            // Plain Unity Button fallback — no built-in hover event, so we
            // skip. Rare path; our buttons are CustomButton clones.
        }

        private static void AttachIfFound(Component comp, Type t,
            string nameA, string nameB, UnityAction handler)
        {
            var prop = AccessTools.Property(t, nameA) ?? AccessTools.Property(t, nameB);
            var ev = prop?.GetValue(comp) as UnityEvent;
            ev?.AddListener(handler);
        }

        private void Show()
        {
            // Sync text on every show — Attach is called AFTER AddComponent
            // triggers our Awake, which means BuildLabel ran with whatever
            // Text was (often empty) at that point. Setting it lazily here
            // keeps the label in sync regardless of the construction order.
            if (_labelTmp != null) _labelTmp.text = Text ?? "";
            if (_label != null) _label.SetActive(true);
        }

        private void Hide()
        {
            if (_label != null) _label.SetActive(false);
        }
    }
}
