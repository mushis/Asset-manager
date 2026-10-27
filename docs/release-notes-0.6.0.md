# Quake Asset Manager 0.6.0

Windows and Linux, x64, self-contained (no .NET install needed).

## Install

- **Windows:** unzip `QuakeAssets-0.6.0-win-x64.zip` and run `QuakeAssetManager.exe`.
- **Linux:** extract `QuakeAssets-0.6.0-linux-x64.tar.gz` and run `QuakeAssets/QuakeAssetManager`.

To update 0.5, unzip 0.6 over your 0.5 folder (or into a new one and copy `config/` across). From 0.6 on, the app
can update itself: Settings > General > **Check for updates**.

## What's new in 0.6

**Updates:** Settings > General has **Check for updates** and an option to check when the app starts. When a new
version is on GitHub, **Install and restart** downloads it, replaces the app and starts the new version. Your
settings (`config/`) and Local assets are never touched.

**MD3 models in the preview:** `.md3` files show as a 3D model, with their skin when it's next to them. Drag the
picture to turn the model. Works in Local assets, the game folders and the Archives tab.

**Duplicates:**
- A file in Local assets with the same contents (SHA-256) as another one is marked **duplicate**. Its preview
  lists the other copies; click one to show it in your file explorer.
- When an import contains files already in Local assets, the summary lets you skip them (default) or add them
  anyway, marked as duplicates.
- **Find duplicates in the Local assets folders** compares contents only, so copies saved under different names
  are found too.

**Import button:** next to New folder in the Local list, **Import files…** or **Import folders…** works like drag
and drop.

**Other changes:**
- The preview's "In the game now" section is always shown; for the file the game uses it says so.
- The import summary lists only the asset types that received files.

All UI texts are available in English, French, Spanish, Portuguese, Swedish, Finnish and Russian.
