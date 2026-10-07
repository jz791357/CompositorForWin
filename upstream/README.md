# Upstream source checkout

This folder holds a read-only checkout of the macOS original that the port is
translated from. It is not committed (see .gitignore); after cloning this repo:

```
git clone https://github.com/robbietilton/Compositor.git upstream/Compositor
```

The tooling expects it at exactly that path:

- `scripts/sync-native.mjs` copies the C pixel kernels from
  `upstream/Compositor/Compositor/Rendering` into `src/Compositor.Native/c`.
- `scripts/gen-icon.mjs` reads the app icon from
  `upstream/Compositor/Compositor/Assets.xcassets`.

Keep it on the upstream tag being tracked; `docs/SYNC-UPSTREAM.md` describes the
full update flow.
