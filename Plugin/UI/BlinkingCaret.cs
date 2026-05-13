using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MTGAEnhancementSuite.UI
{
    /// <summary>
    /// Custom blinking text cursor for runtime-built <see cref="TMP_InputField"/>s.
    /// TMP_InputField's built-in caret doesn't render reliably when the
    /// field is constructed via AddComponent at runtime (the caret is created
    /// lazily and needs ducks-in-a-row state we can't always guarantee).
    /// This is a simple replacement: a 2px white Image parented to the
    /// textComponent, repositioned at the end of the rendered text each
    /// time the field changes, blinking via a coroutine.
    ///
    /// Attach with <see cref="Attach"/> after constructing the input field.
    /// </summary>
    internal class BlinkingCaret : MonoBehaviour
    {
        private TMP_InputField _input;
        private TMP_Text _text;
        private Image _caret;
        private Coroutine _blinkRoutine;
        private int _lastCaretPos = -1;
        private string _lastText = null;

        public static BlinkingCaret Attach(TMP_InputField input)
        {
            if (input == null) return null;
            var existing = input.GetComponent<BlinkingCaret>();
            if (existing != null) return existing;
            return input.gameObject.AddComponent<BlinkingCaret>();
        }

        private void Awake()
        {
            _input = GetComponent<TMP_InputField>();
            if (_input == null) { enabled = false; return; }
            _text = _input.textComponent;
            // Suppress TMP's own (broken-here) caret. We're drawing our own.
            _input.caretWidth = 0;

            BuildCaret();
            _input.onValueChanged.AddListener(_ => Schedule(UpdateCaretPos));
            // Reposition once after layout settles.
            Schedule(UpdateCaretPos);
        }

        private void OnEnable()
        {
            if (_caret != null) _blinkRoutine = StartCoroutine(BlinkLoop());
        }

        private void Update()
        {
            // TMP_InputField doesn't expose a "caret moved" event, so poll
            // caretPosition here. Cheap — just an int compare. Reposition
            // both on caret-move (arrow keys, click-to-position) and on text
            // change (typing). onValueChanged catches typing too, but
            // polling text length as a tiebreaker keeps everything in sync
            // even when input is mutated externally.
            if (_input == null) return;
            int pos = _input.caretPosition;
            string txt = _input.text ?? "";
            if (pos != _lastCaretPos || !ReferenceEquals(txt, _lastText) && txt != _lastText)
            {
                _lastCaretPos = pos;
                _lastText = txt;
                UpdateCaretPos();
                // Reset blink to "visible" state so feedback is immediate
                // when the user moves the caret (Windows behavior).
                if (_caret != null) _caret.enabled = true;
            }
        }

        private void OnDisable()
        {
            if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
            _blinkRoutine = null;
        }

        private void BuildCaret()
        {
            // Parent the caret directly to the InputField's GameObject (a
            // sibling of textArea), NOT inside textComponent. Two reasons:
            //   - textArea has a RectMask2D that clips its children; the
            //     caret could end up clipped if its rect extends outside.
            //   - TMP_Text rebuilds its mesh aggressively and can behave
            //     unpredictably with child UI Graphics.
            // Sibling-of-textArea also means we render AFTER textArea's
            // children (text + placeholder), guaranteeing the caret is on
            // top of the text glyphs.
            _caret = new GameObject("MTGAES_BlinkingCaret")
                .AddComponent<Image>();
            _caret.transform.SetParent(_input.transform, false);
            _caret.color = Color.white;
            _caret.raycastTarget = false;

            var rt = _caret.rectTransform;
            // Centered anchor: anchoredPosition.x maps 1:1 to "offset from
            // the input's horizontal center". Since textComponent is
            // full-stretched within the InputField (same width, same center),
            // a character's local-x position in textComponent coordinates
            // works as-is here without extra translation.
            //
            // Fixed-pixel size — anchor band-based sizing made the caret
            // too tall (taking nearly the full input field).
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1.5f, 22f);
            rt.anchoredPosition = Vector2.zero;

            Plugin.Log.LogInfo($"BlinkingCaret built on '{_input.name}'");
        }

        private void Schedule(System.Action a)
        {
            // Defer one frame so TMP_Text finishes its layout pass before we
            // read preferredWidth.
            StartCoroutine(DeferOne(a));
        }

        private IEnumerator DeferOne(System.Action a)
        {
            yield return null;
            try { a(); } catch { /* swallowed; cursor is cosmetic */ }
        }

        /// <summary>
        /// Positions the caret at <see cref="TMP_InputField.caretPosition"/>
        /// (i.e. between characters [pos-1] and [pos]). Honors arrow-key
        /// and click-to-position movement, not just end-of-text.
        ///
        /// TMP's <c>characterInfo[i].topRight.x</c> is in textComponent's
        /// LOCAL space relative to its pivot (centered). Our caret is
        /// parented to the InputField with a centered anchor, and the
        /// textComponent shares the InputField's center (full-stretched
        /// within textArea, which is full-stretched within the InputField).
        /// So the local X transfers directly — no translation needed.
        /// </summary>
        private void UpdateCaretPos()
        {
            if (_caret == null || _text == null || _input == null) return;

            _text.ForceMeshUpdate();
            int charCount = _text.textInfo?.characterCount ?? 0;

            float x;
            if (charCount == 0 || string.IsNullOrEmpty(_input.text))
            {
                // Empty: park at the text component's left margin (left
                // edge of rect + margin.x in local coords).
                x = _text.rectTransform.rect.xMin + _text.margin.x;
            }
            else
            {
                // Caret sits at the LEADING edge of the character at
                // caretPosition. Pos 0 = before first char. Pos N = before
                // char N. Pos == charCount = after the last char.
                int caretPos = Mathf.Clamp(_input.caretPosition, 0, charCount);
                if (caretPos == 0)
                {
                    // Just before the first character — use its topLeft.x.
                    x = _text.textInfo.characterInfo[0].topLeft.x;
                }
                else
                {
                    // After character (caretPos - 1) — use its topRight.x.
                    x = _text.textInfo.characterInfo[caretPos - 1].topRight.x;
                }
            }
            _caret.rectTransform.anchoredPosition = new Vector2(x, 0f);
        }

        private IEnumerator BlinkLoop()
        {
            // Standard ~530ms blink half-cycle (Windows default).
            var on = new WaitForSecondsRealtime(0.53f);
            var off = new WaitForSecondsRealtime(0.53f);
            while (true)
            {
                if (_caret != null) _caret.enabled = true;
                yield return on;
                if (_caret != null) _caret.enabled = false;
                yield return off;
            }
        }
    }
}
