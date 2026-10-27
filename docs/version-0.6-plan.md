# Version 0.6: planned changes

Status: built on 27 Oct 2026 (see `docs/release-notes-0.6.0.md`).

## 1. Tracker: "In the game now" missing

- **Likely cause:** the comparison is only shown when the selected Local file differs from the game's copy. Your
  tracker files in `assets/tracker/19 Oct 2026` are the same files as in `qw/textures/tracker` (the first scan
  copied them), so every one is marked "in game" and nothing is compared.
- **Plan:** always show the "In the game now" section. When the selected file *is* the one in the game, it says
  "This is the file the game uses now" instead of repeating the image. Applies to every asset type.
- To confirm: does it also happen when you select a tracker file that is **not** marked "in game"?

## 2. Check for updates and install

- **Possible, yes.** The app asks GitHub for the latest release
  (`api.github.com/repos/mushis/Asset-manager/releases/latest`), compares versions, and offers to install it.
- **Install:** download the zip/tar.gz for this system into a temp folder, unpack it, close the app, swap the
  executable, `lang/` and the icon, and start the new version. `config/` and `cache/` are never touched.
- **How the swap works (as built):** a running executable can't be overwritten on Windows, but it can be renamed.
  The app renames itself to `QuakeAssetManager.exe.old`, copies the new one in its place, starts it and closes; the
  new version deletes the `.old` file when it starts. No helper process is needed. Same on Linux.
- **Where:** Settings > General: "Check for updates" button, the current version, and a "Check for updates when the
  app starts" checkbox (off by default).
- **Safety:** the old executable is kept as `QuakeAssetManager.old` until the new one starts; if anything fails,
  nothing is replaced. Downloads only come from github.com release assets of mushis/Asset-manager.

## 3. MD3 preview

- **Possible.** MD3 is a simple format (surfaces, triangles, vertices, skin names). The preview would draw the
  model's first frame as a shaded 3D view, with the skin texture next to it (the `.skin` file or the image with the
  model's name), and let you turn it with the mouse.
- Size: a small software renderer in Core (no GPU library needed), about the work of the image decoders.
- `.mdl` models could get the same 3D preview later (today only their skin is shown).

## 4. Import summary: hide empty rows

- Asset types with 0 files added are left out of the summary. Only rows with files, plus "Already in Local
  assets", "Not managed by the app" and "Ignored" when they're not 0.

## 5. Import button in the Local header

- An icon button next to "New folder" opens a small menu: **Import files…** (several files: archives, images,
  models) or **Import folders…** (several folders). Then the same flow as a drop: questions, summary, Revert.

## 6. Duplicates by content

- **Find duplicates in the Local assets folders:** already compares the SHA-256 of the contents (same name and size
  first, then the hash). Change: group by hash only, so copies with **different names** are found too.
- **Find duplicates in the active Quake folder:** groups by path; it already marks identical contents (SHA-256).

## 7. Duplicates marked in the Local list

- When files are added to Local assets (import, drop, download, scan, archives), files whose SHA-256 matches another
  file in Local assets are marked **duplicate** in the list, like invalid ones (own colour and label).
- **On import (decided: ask):** when files being imported have the same content as files already in Local assets,
  the summary asks: **Skip them** (default) or **Add them anyway** (they're added and marked duplicate).
- How "same" is decided: the SHA-256 hash of the file's contents. Name, folder and date don't matter: two files
  are duplicates only when their hashes match (the chance of two different files sharing a SHA-256 is nil).
  Hashes are cached by path, size and date, so only new or changed files are read again.
- **Preview of a duplicate:** a list of the other copies' paths; clicking one opens the system file explorer at
  that file (selected).
