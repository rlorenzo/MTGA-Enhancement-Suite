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
    /// Rename + delete flows for a user folder. Both are simple
    /// ScreenSpaceOverlay modals styled to match
    /// <see cref="MoveToFolderModal"/> and <see cref="ConfirmDeleteModal"/>.
    /// </summary>
    internal static class RenameFolderModal
    {
        public static void Show(Guid folderId)
        {
            var folder = DeckOrganizationManager.FindFolderById(folderId);
            if (folder == null) return;

            var modal = new GameObject("MTGAES_RenameFolderModal");
            UnityEngine.Object.DontDestroyOnLoad(modal);
            var canvas = modal.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 210;
            var scaler = modal.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            modal.AddComponent<GraphicRaycaster>();

            var bg = NewChild(modal.transform, "Backdrop");
            StretchFull(bg);
            bg.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            var bgBtn = bg.AddComponent<Button>();
            bgBtn.transition = Selectable.Transition.None;
            bgBtn.onClick.AddListener(new UnityAction(() => UnityEngine.Object.Destroy(modal)));

            var dialog = NewChild(modal.transform, "Dialog");
            var dRt = dialog.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0.32f, 0.40f);
            dRt.anchorMax = new Vector2(0.68f, 0.60f);
            dRt.sizeDelta = Vector2.zero;
            dialog.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.18f, 0.98f);

            var title = NewChild(dialog.transform, "Title");
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0.05f, 0.72f);
            tRt.anchorMax = new Vector2(0.95f, 0.95f);
            tRt.sizeDelta = Vector2.zero;
            var titleTmp = title.AddComponent<TextMeshProUGUI>();
            var font = TmpFontHelper.Get();
            if (font != null) titleTmp.font = font;
            titleTmp.text = "Rename folder";
            titleTmp.fontSize = 22;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.Left;
            titleTmp.extraPadding = false;

            // Text input pre-filled with current name
            var inputObj = NewChild(dialog.transform, "Input");
            var iRt = inputObj.GetComponent<RectTransform>();
            iRt.anchorMin = new Vector2(0.05f, 0.40f);
            iRt.anchorMax = new Vector2(0.95f, 0.68f);
            iRt.sizeDelta = Vector2.zero;
            inputObj.AddComponent<Image>().color = new Color(0.18f, 0.18f, 0.28f, 1f);
            var inputField = inputObj.AddComponent<TMP_InputField>();

            var textArea = NewChild(inputObj.transform, "TextArea");
            StretchFull(textArea);
            textArea.AddComponent<RectMask2D>();
            var textGO = NewChild(textArea.transform, "Text");
            StretchFull(textGO);
            var textTmp = textGO.AddComponent<TextMeshProUGUI>();
            if (font != null) textTmp.font = font;
            textTmp.fontSize = 18;
            textTmp.color = Color.white;
            textTmp.alignment = TextAlignmentOptions.Left;
            textTmp.margin = new Vector4(10, 0, 6, 0);
            textTmp.extraPadding = false;
            inputField.textComponent = textTmp;
            inputField.textViewport = textArea.GetComponent<RectTransform>();
            inputField.text = folder.Name ?? "";

            MakeBtn(dialog.transform, "Cancel", "Cancel",
                new Vector2(0.08f, 0.08f), new Vector2(0.46f, 0.30f),
                new Color(0.3f, 0.3f, 0.4f, 1f),
                () => UnityEngine.Object.Destroy(modal));

            MakeBtn(dialog.transform, "Save", "Save",
                new Vector2(0.54f, 0.08f), new Vector2(0.92f, 0.30f),
                new Color(0.20f, 0.55f, 0.30f, 1f),
                () =>
                {
                    var name = (inputField.text ?? "").Trim();
                    if (string.IsNullOrEmpty(name)) name = "Untitled";
                    DeckOrganizationManager.RenameFolder(folderId, name);
                    Plugin.Log.LogInfo($"Renamed folder {folderId} → '{name}'");
                    UnityEngine.Object.Destroy(modal);
                    DeckViewSelectorPatch.RebuildUserFolders();
                    DeckManagerControllerPatch.RefreshDeckGrid();
                });

            // Custom blinking caret (TMP_InputField's built-in caret doesn't
            // render reliably on runtime-built input fields).
            BlinkingCaret.Attach(inputField);

            inputField.ActivateInputField();
            inputField.MoveTextEnd(false);
        }

        public static void ConfirmDelete(Guid folderId, string folderName)
        {
            var modal = new GameObject("MTGAES_DeleteFolderModal");
            UnityEngine.Object.DontDestroyOnLoad(modal);
            var canvas = modal.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 210;
            var scaler = modal.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            modal.AddComponent<GraphicRaycaster>();

            var bg = NewChild(modal.transform, "Backdrop");
            StretchFull(bg);
            bg.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            var bgBtn = bg.AddComponent<Button>();
            bgBtn.transition = Selectable.Transition.None;
            bgBtn.onClick.AddListener(new UnityAction(() => UnityEngine.Object.Destroy(modal)));

            var dialog = NewChild(modal.transform, "Dialog");
            var dRt = dialog.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0.32f, 0.40f);
            dRt.anchorMax = new Vector2(0.68f, 0.60f);
            dRt.sizeDelta = Vector2.zero;
            dialog.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.18f, 0.98f);

            var title = NewChild(dialog.transform, "Title");
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0.05f, 0.65f);
            tRt.anchorMax = new Vector2(0.95f, 0.93f);
            tRt.sizeDelta = Vector2.zero;
            var titleTmp = title.AddComponent<TextMeshProUGUI>();
            var font = TmpFontHelper.Get();
            if (font != null) titleTmp.font = font;
            titleTmp.text = $"Delete folder \"{folderName}\"?";
            titleTmp.fontSize = 24;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.extraPadding = false;

            var body = NewChild(dialog.transform, "Body");
            var bRt = body.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.05f, 0.35f);
            bRt.anchorMax = new Vector2(0.95f, 0.62f);
            bRt.sizeDelta = Vector2.zero;
            var bodyTmp = body.AddComponent<TextMeshProUGUI>();
            if (font != null) bodyTmp.font = font;
            bodyTmp.text = "Decks inside this folder move back to My Decks. No decks are deleted.";
            bodyTmp.fontSize = 16;
            bodyTmp.color = new Color(0.85f, 0.85f, 0.90f);
            bodyTmp.alignment = TextAlignmentOptions.Center;
            bodyTmp.extraPadding = false;

            MakeBtn(dialog.transform, "Cancel", "Cancel",
                new Vector2(0.08f, 0.08f), new Vector2(0.46f, 0.28f),
                new Color(0.3f, 0.3f, 0.4f, 1f),
                () => UnityEngine.Object.Destroy(modal));

            MakeBtn(dialog.transform, "Delete", "Delete",
                new Vector2(0.54f, 0.08f), new Vector2(0.92f, 0.28f),
                new Color(0.75f, 0.20f, 0.20f, 1f),
                () =>
                {
                    DeckOrganizationManager.DeleteFolder(folderId);
                    Plugin.Log.LogInfo($"Deleted folder '{folderName}' ({folderId})");
                    UnityEngine.Object.Destroy(modal);
                    DeckViewSelectorPatch.RebuildUserFolders();
                    DeckManagerControllerPatch.RefreshDeckGrid();
                });
        }

        // ---- helpers ----
        private static void MakeBtn(Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color bgColor, UnityAction onClick)
        {
            var btn = NewChild(parent, name);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.sizeDelta = Vector2.zero;
            btn.AddComponent<Image>().color = bgColor;
            var b = btn.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);

            var t = NewChild(btn.transform, "Text");
            StretchFull(t);
            var tmp = t.AddComponent<TextMeshProUGUI>();
            var font = TmpFontHelper.Get();
            if (font != null) tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 18;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
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
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
