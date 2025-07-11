using System;
using TMPro;

namespace Furari.Text {
    public static class RubyUtility {

        public static void Transform(ref ValueStringBuilder sb, ReadOnlySpan<char> sourceText, float rubyScale, float verticalOffset, TMP_FontAsset fontAsset, FontStyles style) {

            ReadOnlySpan<char> baseText = "";
            ReadOnlySpan<char> rubyText = "";

            foreach (var (segment, type) in new RubyTagParser(sourceText)) {

                switch (type) {
                    case RubyType.None:
                        if (!baseText.IsEmpty || !rubyText.IsEmpty) {
                            AppendFormat(ref sb, baseText, rubyText, rubyScale, verticalOffset, fontAsset, style);
                            baseText = "";
                            rubyText = "";
                        }
                        sb.Append(segment);
                        break;

                    case RubyType.Base:
                        if (!baseText.IsEmpty || !rubyText.IsEmpty) {
                            AppendFormat(ref sb, baseText, rubyText, rubyScale, verticalOffset, fontAsset, style);
                        }
                        rubyText = "";
                        baseText = segment;
                        break;

                    case RubyType.Top:
                        rubyText = segment;
                        break;

                    case RubyType.Parenthesis:
                        break;  // Ignore.
                }
            }

            if (!baseText.IsEmpty || !rubyText.IsEmpty) {
                AppendFormat(ref sb, baseText, rubyText, rubyScale, verticalOffset, fontAsset, style);
            }
        }

        public static void TransformNoRuby(ref ValueStringBuilder sb, ReadOnlySpan<char> sourceText) {

            foreach (var (segment, _) in new RubyTagParser(sourceText)) {
                sb.Append(segment);
            }
        }

        public static void AppendFormat(ref ValueStringBuilder sb, ReadOnlySpan<char> baseText, ReadOnlySpan<char> rubyText, float rubyScale, float verticalOffset, TMP_FontAsset fontAsset, FontStyles style) {

            if (baseText.IsEmpty && rubyText.IsEmpty) { return; }

            if (rubyText.IsEmpty) { 
                sb.Append(baseText); 
                return; 
            }

            if (baseText.IsEmpty) {
                sb.Append("<voffset=");
                sb.AppendFormat(verticalOffset, "#.##");
                sb.Append("em><size=");
                sb.AppendFormat(rubyScale, "#.##");
                sb.Append("em>");
                sb.Append(rubyText);
                sb.Append("</size></voffset>");
                return;
            }

            var (baseInitOffset, rubyInitOffset, baseLateOffset) = CalculateOffsets(baseText, rubyText, rubyScale, fontAsset, style);

            sb.Append("<nobr>");
            AppendSpace(ref sb, baseInitOffset);
            sb.Append(baseText);
            AppendSpace(ref sb, rubyInitOffset);
            sb.Append("<voffset=");
            sb.AppendFormat(verticalOffset, "#.##");
            sb.Append("em><size=");
            sb.AppendFormat(rubyScale, "#.##");
            sb.Append("em>");
            sb.Append(rubyText);
            sb.Append("</size></voffset>");
            AppendSpace(ref sb, baseLateOffset);
            sb.Append("</nobr>");

            static void AppendSpace(ref ValueStringBuilder sb, float space) {
                if (space == 0) { return; }
                sb.Append("<space=");
                sb.AppendFormat(space, "#.##");
                sb.Append("em>");
            }
        }

        public static (float baseInit, float rubyInit, float baseLate) CalculateOffsets(ReadOnlySpan<char> baseText, ReadOnlySpan<char> rubyText, float rubyScale, TMP_FontAsset fontAsset, FontStyles style) {

            float baseWidth = TMP_Ext.TextWidthApprox(baseText, fontAsset, style);
            float rubyWidth = TMP_Ext.TextWidthApprox(rubyText, fontAsset, style) * rubyScale;

            float baseInit;
            float rubyInit;
            float baseLate;

            if (baseWidth > rubyWidth) {
                baseInit = 0;
                rubyInit = -(baseWidth + rubyWidth) / 2;
                baseLate = -(rubyWidth + rubyInit);
            } else {
                baseInit = (rubyWidth - baseWidth) / 2;
                rubyInit = -(baseWidth + baseInit);
                baseLate = 0;
            }

            return (baseInit, rubyInit, baseLate);
        }
    }

    public ref struct RubyTagParser {

        private ReadOnlySpan<char> text;
        private ReadOnlySpan<char> result;
        private RubyType currentType;
        private bool insideRuby;

        public RubyTagParser(ReadOnlySpan<char> text) {
            this.text = text;
            this.result = default;
            this.currentType = RubyType.None;
            this.insideRuby = false;
        }

        public TaggedReadOnlySpan<char, RubyType> Current => new(result, currentType);

        public bool MoveNext() {

            while (!text.IsEmpty) {

                if (text[0] != '<') {
                    int tagOpen = text.IndexOf('<');
                    result = tagOpen < 0 ? text : text.Slice(0, tagOpen);
                    text = tagOpen < 0 ? ReadOnlySpan<char>.Empty : text.Slice(tagOpen);
                    return true;
                }

                int tagClose = text.IndexOf('>');

                if (tagClose < 0) { // Malformed tag; yield everything left.
                    result = text;
                    text = ReadOnlySpan<char>.Empty;
                    return true;
                }

                var tag = text.Slice(1, tagClose - 1);
                RubyTagHash hash = (RubyTagHash)TMP_Ext.Hash(tag);

                switch (hash) {
                    case RubyTagHash.RUBY:
                        insideRuby = true;
                        currentType = RubyType.Base;
                        break;
                    case RubyTagHash.SLASH_RUBY:
                        insideRuby = false;
                        currentType = RubyType.None;
                        break;
                    case RubyTagHash.RUBY_TOP:
                        currentType = RubyType.Top;
                        break;
                    case RubyTagHash.SLASH_RUBY_TOP:
                        currentType = insideRuby ? RubyType.Base : RubyType.None;
                        break;
                    case RubyTagHash.RUBY_PARENTHESIS:
                        currentType = RubyType.Parenthesis;
                        break;
                    case RubyTagHash.SLASH_RUBY_PARENTHESIS:
                        currentType = insideRuby ? RubyType.Base : RubyType.None;
                        break;
                    default:
                        result = text.Slice(0, tagClose + 1);
                        text = text.Slice(tagClose + 1);
                        return true;
                }

                text = text.Slice(tagClose + 1);
            }

            return false;
        }
        public RubyTagParser GetEnumerator() => this;
    }

    public enum RubyTagHash {
        RUBY = 3006684,                     // <ruby>
        SLASH_RUBY = 58451059,              // </ruby>
        RUBY_TOP = 2758,                    // <rt>
        SLASH_RUBY_TOP = 53673,             // </rt>
        RUBY_PARENTHESIS = 2754,            // <rp>
        SLASH_RUBY_PARENTHESIS = 53677      // </rp>

    }

    public enum RubyType {
        None,
        Base,
        Top,
        Parenthesis
    }
}