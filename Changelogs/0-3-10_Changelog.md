## New Features

### #039 — Kartographer Unit Compression Toggle
- New opt-in `SituationCompressUnits` option in Kartographer display settings (default OFF).
- When enabled, distance readouts compress by magnitude using M / KM / MM / GM / TM thresholds (10,000 M, 10,000,000 M, etc.).
- The OFF path remains byte-identical to previous behavior.

### #034 — Starfield Defaults to ON for New Saves
- New and migrated saves now have the starfield enabled by default.
- The shipped `hyg_v42.bin` catalog is seeded as the default active catalog, so the real-sky field renders immediately.
- Existing per-save values are left untouched.

### #045 — Kartographer Auto-Activates with Mid-Session Starfield Enable
- Enabling the starfield mid-session now brings the Kartographer grid and point-and-click selector online automatically.
- No visit to the Kartographer tab is required.

### #035 — Settings Saved on Scene Switch Request
- Settings are now saved when a scene switch is requested, not only when the UI window closes.
- Fixes per-scene GTAO profile bleed when switching scenes with the window open.

---

## Fixes

### Audio
- **#017** — Fixed hard-stop pops when audio loops are muted by replacing the instant stop with a short fade-out while keeping sources tracked for restart.

### Starfield & Catalog UI
- **#040** — Replaced the invisible read-only lock glyph (`🔒`) in the catalog dropdown with an ASCII `[RO] ` prefix so read-only catalogs are clearly labeled.
- **#025** — Rescan confirmation wording now names all affected data and discloses that a backup is created; the old `_Custom.json` is renamed to `_Custom.old.json` instead of being deleted.

### Cubemap
- **#030** — Cubemap staging is now gated on the native device being ready, eliminating spurious device-init errors on scene load.

### Kartographer
- **#046** — Hover-select no longer breaks when the mod window is opened; the Kartographer selector is now shared between scene-load init and the tab instead of the tab constructor replacing it.

---

## Internal

- **Seam-hardening batch (C1–C4)** — console-side robustness work with no user-visible behavior change:
  - Console glyph-contract inversion (layers now consume a native cell-layout export).
  - Console-dedicated `TextSystem` handle, isolating console text layout from shared atlas churn.
  - Console draw RTV failure hardening (#026) — explicit RTV descriptor + fallback instead of relying on the previous fall-through path.
  - Cell-budget consolidation: a single shared constant (`767`) replaces scattered magic numbers across native and C# console code.
- **#037** — Removed the stale global `EnableStarfield` read/write path in `StarfieldSettings`; enable state is now sourced entirely from per-save settings.

---

## Known Issues

- **#023** — Navball icons may be invisible on the first Flight load. Workaround: swap icon style (Retro ↔ KSP) in the mod UI.
- **#033** — Star selection can break after changing starfield settings or switching catalogs. Workaround: toggle Kartographer off and back on.
- **#044** — After swapping to a user-generated catalog, console search may become unresponsive and selection may stop being bidirectional; swapping back does not always clear the state. No workaround; under triage.
- **#024** — Kartographer STAR Console overlapping text — kept open as a watch item; currently unreproduced.

---

## Condensed Version

- Kartographer unit-compression toggle (#039), starfield default-on + `hyg_v42.bin` seed (#034), Kartographer auto-activation on starfield enable (#045), and settings saved on scene-switch request (#035)
- Audio hard-stop pop fix (#017), read-only `[RO]` catalog label (#040), cubemap device-ready gate (#030), safer rescan wording + backup (#025)
- Console seam-hardening batch and removal of stale global `EnableStarfield` path
