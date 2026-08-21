## New Features

### Per-Scene GTAO Profiles
- GTAO settings are now stored per scene — Flight, Space Center, Tracking Station, and Editor each get their own profile (enable toggle, quality preset, radius, intensity, fade settings).
- Scene selector added to the GTAO tab; editing a non-live scene does not touch the active renderer.
- Existing settings migrate automatically from the old flat format.

### Kartographer Time-to-Encounter
- The target info HUD now shows predictive encounter timing instead of "TTE: N/A":
  - `CA:` closest approach with separation distance (vessels and bodies)
  - `SOI+:`/`SOI-:` sphere-of-influence entry/exit times (target-relevant only) with a `P/E:` periapsis line for the new SOI
  - `IMPACT:` countdown for collision trajectories (sea-level estimate)
- Maneuver-node aware — predictions follow your flight plan, not just the current patch.

### Starfield Extinction & Dimming Controls
- New Atmospheric Extinction and Glare Dimming factor sliders in the Starfield tab (0–2, default 1), persisted per save.

### Misc
- New `RestoreOriginalSkyboxOnDisable` toggle for cubemap/skybox handling.
- Star catalog JSON sidecar data regenerated with the fixed generator (further name/constellation corrections in progress).

---

## Fixes

### Per-Save Starfield Settings Actually Persist
- Starfield visual settings (enable, exposure, blur, bloom, saturation, extinction/dimming, active catalog) are now genuinely saved per save file via a properly registered ScenarioModule — previously they lived in RAM and were lost on exit.
- Fixed stale-write timing so saved values are current, and new saves seed proper defaults instead of inheriting the previous save's values.

### Native Rendering Thread Safety
- All remaining main-thread D3D11 work (navball icon and catalog uploads, cubemap rendering) moved to Unity's render thread — eliminates a class of GPU driver crashes caused by cross-thread immediate-context calls.

### Cubemap
- Fixed cubemap face orientation and ensured catalog upload on first render.
- Defensive load guards to prevent a first-use race condition; original-skybox restoration re-enabled.

### Navball
- Fixed normal/radial icons rotating incorrectly and icon-swap texture mismatches.

### Star Info Box
- Fixed spectral class K stars displaying "L - ORANGE" — now correctly "K - ORANGE".
- HIP identifiers now render consistently with a space (`HIP 32349`).

---

## Internal

- All user-facing strings and UI resources (styles, colors, fonts, texture paths) centralized; dead debug code removed.
- Struct layout tooling fixes; stale debug build script removed.
- KSP-AVC version file added for update checkers.

---

## Known Issues

- **Navball icons invisible on first Flight load** — workaround: swap icon style (Retro ↔ KSP) in the mod UI. Under investigation.
- Star selection can break after changing starfield settings or switching catalogs — workaround: toggle Kartographer off/on.
- Starfield defaults off for new saves; the default catalog may not render until switched once.
- Settings save on window close; edits can bleed across a scene switch made with the window open.

---

## Condensed Version

- Per-scene GTAO profiles; Kartographer time-to-encounter display; starfield extinction/dimming sliders
- Per-save starfield settings persistence fixed
- Native render-thread safety migration (GPU crash class eliminated)
- Cubemap orientation/lifecycle fixes; navball icon rotation fixes
- String/UI resource centralization
