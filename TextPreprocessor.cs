using System;
using UnityEngine;
using TMPro;

namespace Furari.Text {
    public abstract class TextPreprocessor : MonoBehaviour, ITextPreprocessor {

        [SerializeField] protected TMP_Text textComponent;

        protected /*virtual*/ int StringBuilderSize => 512;

#if UNITY_EDITOR
        [Header("Preview")]
        [TextArea, SerializeField] protected string processedText;
#endif

        private void Awake() => Attach();
        private void OnValidate() => Attach();
        private void OnDestroy() => Detach();

        private void Attach() {
            if (textComponent) { 
                textComponent.textPreprocessor = this;
                textComponent.ForceMeshUpdate();
            }
        }

        private void Detach() {
            if (textComponent && ReferenceEquals(textComponent.textPreprocessor, this)) {
                textComponent.textPreprocessor = null;
            }
        }

        public virtual string PreprocessText(string text) {
            var sb = new ValueStringBuilder(StringBuilderSize);
            var result = PreprocessText(ref sb, text).ToString();
            sb.Dispose();
#if UNITY_EDITOR
            processedText = result;
#endif
            return result;
        }

        public abstract ReadOnlySpan<char> PreprocessText(ref ValueStringBuilder sb, ReadOnlySpan<char> input);

        /* Note:
         * Because of ITextPreprocessor interface, it will always allocate at least one string.
         * If that's a concern, please use the PreprocessText(ref ValueStringBuilder, ReadOnlySpan<char>)
         * then extract the char array from the ValueStringBuilder and set it to the TMP_Text.
         */
    }
}