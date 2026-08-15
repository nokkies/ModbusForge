# Archived Documents

These documents were moved here during the 2026.8.x housekeeping pass. They are kept for
historical context only — **they do not describe the current codebase**.

| Document | Status | Notes |
|----------|--------|-------|
| `IMPROVEMENTS.md` | Historical | Records fixes applied during the v2.0.0 era (server thread safety, simulation address bug, etc.). Superseded by the README changelog. |
| `REFACTORING_PLAN.md` | Not executed | v2.3.0 plan to shrink `MainViewModel` via coordinator classes. Never landed — `MainViewModel` has grown instead and no coordinator classes exist in the codebase. |
| `COORDINATOR_INTEGRATION_PLAN.md` | Not executed | v2.4.0 companion plan for the same (unimplemented) coordinator refactoring. |
| `FEATURE_ROADMAP.md` | Stale | Claimed a "current version" of v5.8.11 and listed coordinator extractions that do not exist in the tree. The living roadmap is [`VERSIONED_ROADMAP.md`](../VERSIONED_ROADMAP.md). |
| `THEME_GUIDE.md` | Stale | Describes the obsolete "Light Metallic" theme (v2.0.0 era); those styles no longer exist in the app. Current theming lives in `ModbusForge/Assets/` (Avalonia resources). |
| `CREATE_RELEASE_INSTRUCTIONS.md` | One-shot | Manual release steps for v3.4.2 only; references a `RELEASE-v3.4.2.md` file that was never committed. Releases are now automated — see the "Creating a Release" section of `AGENTS.md` and `.github/workflows/release.yml`. |
| `MANUAL_RELEASE_INSTRUCTIONS.md` | One-shot | Manual release steps for v3.4.3 only; same situation as above. |
| `UNIT_ID_ISOLATION_PLAN.md` | Completed | The per-Unit-ID isolation design was implemented: `IUnitConfigurationStore` / `UnitConfigurationStore` in `ModbusForge.Core`, covered by `UnitConfigurationStoreTests`. |

## Versioning note

Documents from the v2.x–v5.x era reference obsolete versioning schemes. ModbusForge has used
CalVer (`YYYY.M.INCREMENT`, e.g. `2026.8.27`) since the v6.1.0 / headless split; see `AGENTS.md`.
