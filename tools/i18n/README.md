# Translations

The app's texts are translated with `Loc.T("English text")` in C# and `{local:T 'English text'}` in XAML.
The English text is the key. Each language is `src/QuakeAssetManager/lang/<code>.json`, mapping English text to the translation.

## Adding or changing a UI text

1. Use `Loc.T(...)` or `Loc.F(...)` (with `{0}` placeholders) in the code.
2. Run `python extract_keys.py` to rebuild `keys.json`, the ordered list of every text.
3. Add the translation to each `tr/<code>.txt` as an `index|translation` line, using the index from `keys.json`.
4. Run `python build_lang.py`. It writes the `lang/*.json` files and reports any of these problems:
   - missing lines
   - a `{0}` placeholder that's missing or extra
   - `**bold**` or `` `code` `` markers that don't match the English text

   Note that the indexes shift when texts are added in the middle of the list.

A text with no translation shows in English. Russian has two extra plural keys: `{0} files#few` and `{0} files#one`.
