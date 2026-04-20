## Native Plugin Stability & Performance

### Thread-Safety and COM Leak Fixes
- Fixed race conditions in `CR_TextDispatch`/`CR_TextDispatchEx` by moving mutex to cover glyph-buffer acquisition.
- Fixed cubemap rendering race condition by adding `stateMutex` lock during face loop.
- Fixed COM reference leak in `CR_StarfieldSetCameraMatrices` — missing `device->Release()` was leaking one device refcount per call.

### Shutdown and Invalidation Leak Fixes
- Added missing `Release()` calls in `CR_StarfieldShutdown` for kartographer shaders, text system objects, navball icon textures, explicit render target, and all 12 grid label slot SRVs.
- Added missing releases in `CR_StarfieldInvalidateResources` for grid label slots and explicit render target.
- Fixes memory leaks on every scene change/shutdown.

### Text Dispatch Render-Thread Migration
- Converted `CR_TextDispatch`/`CR_TextDispatchEx` from immediate main-thread D3D11 dispatches to staging queues flushed via `GL.IssuePluginEvent` on the render thread.
- Fixes sporadic `0xc0000005` GPU driver crashes during text overlay rendering (Kartographer, navball, grid labels, vessel target).
- All C# text consumers updated: `GridLabelSystem`, `KartographerSelector`, `NavballLabelManager`, `VesselTargetSelector`.

### Font Atlas Render-Thread Migration
- Moved font atlas `UpdateSubresource` calls from main thread to render thread via staging queue.
- Fixes AMD driver crashes (`amdxx64.dll`) during glyph packing and atlas updates.
- `TextSystem` is shared infrastructure — benefits all text rendering paths.

### HUCK Label Race Condition Fix
- Capture glyph snapshot in `TextDispatchJob` at queue time instead of referencing mutable `m_instances`.
- Render from temporary immutable D3D11 buffer on render thread.
- Fixes race condition where `CR_TextLayoutEx` overwrote glyph data before render thread executed queued jobs, causing corrupted or missing HUCK grid labels.

---

## GTAO Performance

### GTAO SRV Caching
- Cached GTAO depth/normal/AO/scene SRVs in `g_GTAOState` to eliminate per-frame D3D11 resource creation thrashing.

### GTAO SRV Invalidation
- Invalidates cached SRVs when input textures change (prevents ghost/frozen AO after toggling GTAO off/on).
- Removed unsuitable `sceneSRV` caching (scene texture changes every frame).

---

## Starfield Performance

### Star Catalog SRV Caching
- Cached the star catalog structured-buffer SRV in `g_StarfieldState` to avoid per-frame recreation.
- Benefits starfield rendering and cubemap generation.

---

## GPU Resource Race Condition Fixes

### Kartographer Text Rendering Race Condition
- Fixed AMD driver crash during rapid star selection by keeping `RenderTexture.active` alive through entire native text operation.

### Extended RenderTexture Lifecycle Fixes
- Extended the same `try/finally` RenderTexture pattern to `BuildGridLabelTexture()` and Star Console border/layer-2 rendering.
- Applied defensive `try/finally` RenderTexture management to `GridLabelSystem`, `NavballLabelManager`, `VesselTargetSelector`, and `StarfieldCubemapRenderer`.

---

## C# Resource Leak Fixes

### Cubemap, Kartographer, and GTAO Leaks
- `KSPCubemapInjector`: Destroyed old injected textures before assigning new ones (prevented unbounded GPU memory growth).
- `KartographerSelector`: Clear native SRV references before texture destruction.
- `StarfieldCubemapRenderer`: `try/finally` cleanup for RenderTextures on all exit paths.
- `GTAOCompositor`: Nullify native depth/normal pointers in `Cleanup()`.
- `KartographerTab` / `CinematicShadersWindow`: Explicit `Dispose()` and callback cleanup on window close.

### Cubemap Restoration Regression Fix
- Fixed regression from cubemap leak fix: texture-destruction logic could destroy original KSP skybox backup textures, preventing skybox restoration when mod disabled.
- Added `IsOriginalSkyboxTexture()` guard.

---

## Kartographer Overlay Fixes

### KartographerSelector Text Rendering Regression
- Fixed misplaced defensive `_textTexture` check that prevented star name text from rendering on click.

### Kartographer Toggle Breaking Mouse Hover
- Re-register `KartographerSelectorCallback` when Kartographer is re-enabled via UI toggle.
- `StopTracking()` cleared the callback on disable, but it was only registered in the constructor which never re-ran.

### KartographerSelector Native Resource Leak
- Added `~KartographerSelector()` finalizer and standard `Dispose(bool)` pattern with `_disposed` guard.
- Prevents native D3D11 resource leaks on KSP scene switches when explicit `Dispose()` was not called.

---

## Log Spam Reduction

### Native Log Spam
- Disabled per-frame texture-binding log spam in `ExecuteStarfieldRender`.

---

## Notes

- **Star Console feature** (new holographic UI, screen system, layout engine, audio, etc.) is documented separately.
- **Dev infrastructure** commits (debug logging, `.gitignore`, XML docs, `csproj` updates) are excluded.
- For the complete commit list including Star Console development, see GitHub PR #15 or the full branch history.

---

## Condensed Version

- Native Plugin Stability & Performance
- GTAO Performance
- Starfield Performance
- GPU Resource Race Condition Fixes
- C# Resource Leak Fixes
- Kartographer Overlay Fixes
- Log Spam Reduction
- For the complete commit list including Star Console development, see GitHub PR #15 or the full branch history, or see 0-3-5_Changelog.md for a report.