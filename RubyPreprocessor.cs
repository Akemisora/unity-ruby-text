using System;
using UnityEngine;

namespace Furari.Text {
    public class RubyPreprocessor : TextPreprocessor {

        [Header("Ruby Settings")]
        [SerializeField] private float rubyScale = 0.5f;
        [SerializeField] private float verticalOffset = 1f;

        [SerializeField] private bool enableRuby = true;

        //---------------------------------------------------------

        public override ReadOnlySpan<char> PreprocessText(ref ValueStringBuilder sb, ReadOnlySpan<char> input) {
            if (enableRuby) {
                RubyUtility.Transform(ref sb, input, rubyScale, verticalOffset, textComponent.font, textComponent.fontStyle);
            } else {
                RubyUtility.TransformNoRuby(ref sb, input);
            }
            return sb.AsSpan();
        }

    }

}