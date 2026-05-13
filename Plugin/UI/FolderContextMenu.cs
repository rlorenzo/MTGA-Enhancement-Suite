using System;
using MTGAEnhancementSuite.Features;
using MTGAEnhancementSuite.Patches;
using MTGAEnhancementSuite.State;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MTGAEnhancementSuite.UI
{
    /// <summary>
    /// Small floating menu shown when the user right-clicks a custom folder's
    /// header. Two rows: Rename and Delete. Built on its own ScreenSpaceOverlay
    /// canvas with a transparent backdrop that captures outside-clicks to
    /// dismiss.
    /// </summary>
    internal static class FolderContextMenu
    {
        public static void Show(Guid folderId, Vector2 screenPos)
        {
            var folder = DeckOrganizationManager.FindFolderById(folderId);
            if (folder == null) return;

            var modal = new GameObject("MTGAES_FolderContextMenu");
            UnityEngine.Object.DontDestroyOnLoad(modal);

            var canvas = modal.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;
            modal.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            modal.AddComponent<GraphicRaycaster>();

            // Outside-click dismiss backdrop (transparent, full-screen).
            var bg = NewChild(modal.transform, "Backdrop");
            StretchFull(bg);
            bg.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            var bgBtn = bg.AddComponent<Button>();
            bgBtn.transition = Selectable.Transition.None;
            bgBtn.onClick.AddListener(new UnityAction(() => UnityEngine.Object.Destroy(modal)));

            // Menu panel
            const float menuW = 180f, menuH = 88f;
            var menu = NewChild(modal.transform, "Menu");
            var rt = menu.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 1f); // top-left of menu at click point
            rt.sizeDelta = new Vector2(menuW, menuH);

            // Clamp to screen so menu never opens off-edge.
            float clampedX = Mathf.Min(screenPos.x, Screen.width - menuW - 4f);
            float clampedY = Mathf.Max(screenPos.y, menuH + 4f);
            rt.anchoredPosition = new Vector2(clampedX, clampedY);

            menu.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.16f, 0.98f);

            // Rename row
            MakeRow(menu.transform, "Rename", new Vector2(0f, 0.5f), new Vector2(1f, 1f),
                () =>
                {
                    UnityEngine.Object.Destroy(modal);
                    RenameFolderModal.Show(folderId);
                });

            // Delete row
            MakeRow(menu.transform, "Delete", new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                () =>
                {
                    UnityEngine.Object.Destroy(modal);
                    RenameFolderModal.ConfirmDelete(folderId, folder.Name);
                });
        }

        private static void MakeRow(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax, UnityAction onClick)
        {
            var row = NewChild(parent, $"Row_{label}");
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(2, 2);
            rt.offsetMax = new Vector2(-2, -2);
            row.AddComponent<Image>().color = new Color(0.16f, 0.18f, 0.24f, 1f);
            var btn = row.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(onClick);

            var t = NewChild(row.transform, "Text");
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            var tmp = t.AddComponent<TextMeshProUGUI>();
            var font = TmpFontHelper.Get();
            if (font != null) tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 17;
            tmp.color = label == "Delete"
                ? new Color(0.95f, 0.55f, 0.50f)
                : Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin = new Vector4(12, 0, 12, 0);
            tmp.raycastTarget = false;
            tmp.extraPadding = false;
        }

        private static GameObject NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static void StretchFull(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
