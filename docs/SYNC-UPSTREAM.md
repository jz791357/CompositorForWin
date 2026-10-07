# Syncing with upstream Compositor

Run this flow each time the macOS original publishes a release. Cost should stay
proportional to the size of the upstream diff — that is what the 1:1 file mapping
(`PORTING-MAP.md`) buys.

## 0. Prepare

```
git checkout main && git pull
git clone https://github.com/robbietilton/Compositor.git upstream/Compositor   # if missing
cd upstream/Compositor && git fetch --tags && git checkout <new-tag>
```

Record the old tag you tracked (PORTING-MAP.md header) as `<old-tag>`.

## 1. See what changed

```
git -C upstream/Compositor diff --stat <old-tag>..<new-tag>
```

## 2. Native C kernels — zero translation

```
node scripts/sync-native.mjs upstream/Compositor/Compositor/Rendering
```

The script copies every `.c/.h` verbatim (regenerating `Compositor.Native.def`)
and re-applies the one deterministic patch that converts DitherPixels.c's Apple
blocks to serial MSVC-compatible C. **If it exits with an error, upstream added
new blocks/dispatch usage — extend the patch in the script before continuing.**

If the icon changed: `node scripts/gen-icon.mjs`.

## 3. Swift sources — translate per file

For every changed `.swift`, find its row in `docs/PORTING-MAP.md` and mirror the
diff into the same-named C# file. Rules:

- Keep names, structure and comment intent aligned with upstream; translate Swift
  idioms (actors → classes with locks, SwiftUI → MVVM views), don't redesign.
- A change to what `.comp` saves means: port the manifest schema change, bump
  `ProjectManifest.current` and the format doc, port the upstream round-trip
  tests, and keep v1–v(N-1) readable.
- Changed UI strings: update the en-US resources and the zh-CN translation.
- Changed tests in `CompositorTests/`: port them into `src/Compositor.Tests`.

## 4. Verify & release

```
./scripts/build.ps1          # or push and let CI run
```

Then update PORTING-MAP's "Upstream tag tracked", set the version to
`<upstream-version>-win.<n>`, commit as `Sync with upstream <new-tag>` and push.
Release via the Velopack channel once M4 ships it.
