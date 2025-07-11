A component that allows the usage of `<ruby>` tag for furigana.
## Example
```
これは<ruby>漢字<rt>かんじ</rt></ruby>です
```

![Example Image](/example.png)

## How It Works
This leverages `ITextPreprocessor` supplied to a `TMP_Text` to transform the input string into a desired string. The string is decorated with rich text tags to format the placement and size of the base text and the ruby text. The length of base text and ruby text are calculated to determine offsets needed.

Given:
- `string baseText`
- `string rubyText`
- `float rubyScale`
- `float verticalOffset` (in em unit)
- `TMP_FontAsset fontAsset` (for character size lookup)
- `FontStyles style` (bold text has a slightly different value adjustment)
It calculates:
- `float baseInitOffset` (offset from the end of last normal text to the start of base text)
- `float rubyInitOffset` (offset from the end of base text to the start of ruby text)
- `float baseLateOffset` (offset from the end of ruby text to the next normal text)
It will result with:
```
result = $"<nobr><space={baseInitOffset:#.##}em>{baseText}<space={rubyInitOffset:#.##}em><voffset={verticalOffset:#.##}em><size={rubyScale:#.##}em>{rubyText}</size></voffset><space={baseLateOffset:#.##}em></nobr>";
```
## Limitations
- This currently doesn't cover the case when the text wraps.
- This currently only support center alignment.
- Usage of other rich text tags that change spacing inside ruby tag might mess up the offset calculations.
- It allocates on heap one `string` because the `ITextPreprocessor` returns a `string`.
- Haven't checked on RTL writing systems.
## Known Issues
- If the last part of string is surrounded by `<ruby>` tag and not followed by a non-whitespace text, alignment other than left doesn't work.