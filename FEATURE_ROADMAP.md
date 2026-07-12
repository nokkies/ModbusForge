# ModbusForge Feature Roadmap

## Current Version
**v5.8.11** - Tag hierarchy stable, service-locator globals removed, local API hardened, MainViewModel slimmed, dependencies reviewed

---

## Recently Completed (v5.8.1 - v5.8.11)

- **v5.8.1** - Stabilized tag hierarchy with stable GroupId/ParentGroupId, versioned persistence schema, and atomic saves.
- **v5.8.2** - Refactored local API: removed WPF service-provider resolution, added facade/DTOs, validation, rate limiting, and optional API-key auth.
- **v5.8.3** - Removed window globals from visual editor workflows via ITagWindowService and IWindowOwnerProvider.
- **v5.8.4** - Implemented transactional tag-group deletion with preview dialog and rollback on save failure.
- **v5.8.5** - Replaced MainWindow service locator with IShellWindowService and IApplicationLifetime.
- **v5.8.6** - Reduced MainViewModel by extracting project persistence and custom-entry operations into coordinators.
- **v5.8.8** - Extracted monitoring lifecycle from MainViewModel into MonitoringCoordinator.
- **v5.8.9** - Extracted Unit ID configuration into IUnitConfigurationStore.
- **v5.8.10** - Added API-key UI in Preferences window.
- **v5.8.11** - Dependency review: updated CommunityToolkit.Mvvm, MahApps.Metro, LiveCharts, and SkiaSharp; no vulnerable packages.

Test baseline: **358 passed, 0 failed**.

---

## v5.3.0 - UX Quick Wins
**Focus:** Immediate user experience improvements with high impact, low effort

### Tasks
- [ ] Automatically enable 'continuous read' when trend line is added
- [x] Replace generic catch blocks with proper logging and user feedback in conversion operations
- [ ] Add global keyboard shortcuts (Ctrl+R read, Ctrl+W write, Ctrl+T trends, Ctrl+S save, F5 refresh)
- [ ] Improve error messages with recovery suggestions

**Estimated Effort:** 1-2 weeks

---

## v5.4.0 - Performance & Reliability
**Focus:** Application performance, memory management, and robustness

### Tasks
- [ ] Implement data grid virtualization for large address ranges
- [ ] Add connection pooling for multi-device support
- [x] Refactor MainViewModel into smaller, focused coordinators
- [ ] Add structured logging with correlation IDs
- [x] Implement comprehensive input sanitization

**Estimated Effort:** 3-4 weeks

---

## v5.5.0 - Enhanced Simulation
**Focus:** Improve simulation GUI and controls (from original roadmap)

### Tasks
- [ ] Redesign simulation tab layout with split panels and collapsible sections
- [ ] Add visual node simulation controls panel (grouped by node type)
- [ ] Improve waveform selection with dropdowns and preset configurations
- [ ] Add real-time value preview with mini-charts for simulation nodes
- [ ] Simplify enable/disable workflow with master toggle and per-node controls

**Estimated Effort:** 2-3 weeks

---

## v5.6.0 - Data Management
**Focus:** Enhanced import/export and data handling capabilities

### Tasks
- [ ] Bulk export of all data (registers, coils, custom entries, trends)
- [x] Import validation with conflict resolution
- [x] Configuration versioning and migration logic
- [ ] Excel export format for business users
- [ ] Enhanced save/load dialogs with recent files

**Estimated Effort:** 2-3 weeks

---

## v5.7.0 - Testing & Monitoring
**Focus:** Test coverage expansion and built-in performance monitoring

### Tasks
- [x] Expand unit test coverage for API server endpoints
- [x] Add error handling scenario tests
- [x] Implement concurrent operations testing
- [ ] Add built-in performance metrics (latency tracking, memory monitoring, operation timing)
- [ ] Performance dashboard with real-time metrics

**Estimated Effort:** 3-4 weeks

---

## v5.8.0 - Code Quality & Architecture
**Focus:** Technical debt reduction and architecture improvements

### Tasks
- [x] Dependency injection cleanup and service-locator removal
- [ ] Plugin architecture foundation for extensibility
- [ ] API documentation with OpenAPI/Swagger (Swagger opt-in only; disabled by default)
- [ ] Code signing investigation for installer
- [x] Security audit and hardening

**Estimated Effort:** 3-4 weeks

---

## v5.9.0 - Documentation & User Experience
**Focus:** Comprehensive documentation and user guidance

### Tasks
- [ ] Create comprehensive user guide with screenshots and tutorials
- [ ] Add in-application help system with context-sensitive documentation
- [ ] Video tutorials for common workflows
- [ ] Troubleshooting guide with common issues and solutions
- [ ] Improved onboarding experience for new users

**Estimated Effort:** 2-3 weeks

---

## v6.0.0 - Major Feature Integration
**Focus:** Integration of major features from original roadmap (Breaking Changes)

### Tasks
- [ ] Alarm/Event System (from v4.8.0)
- [ ] Device Template Library (from v4.9.0)
- [ ] Calculation Engine (from v5.0.0)
- [ ] MQTT Support (from v5.1.0)
- [ ] Data Historian (from v5.2.0)

**Estimated Effort:** 8-10 weeks

---

## Versioning Strategy

### Patch Versions (x.x.X)
- Bug fixes
- Minor improvements
- Performance optimizations
- Critical security patches

### Minor Versions (x.X.0)
- Focused feature sets (single theme per version)
- UI/UX improvements
- Service additions
- Code quality enhancements
- Performance optimizations

### Major Versions (X.0.0)
- Significant architectural changes
- Breaking changes to existing APIs
- Integration of multiple major feature categories
- Foundation shifts (e.g., plugin architecture)

---

## Release Timeline (Estimated)

| Version | Target Quarter | Status |
|---------|---------------|--------|
| v5.3.0 | Q3 2026 | Planned |
| v5.4.0 | Q3 2026 | Planned |
| v5.5.0 | Q4 2026 | Planned |
| v5.6.0 | Q4 2026 | Planned |
| v5.7.0 | Q1 2027 | Planned |
| v5.8.0 | Q1 2027 | Planned |
| v5.9.0 | Q2 2027 | Planned |
| v6.0.0 | Q2 2027 | Planned |

---

## Notes

- Each minor version focuses on a specific theme (UX, Performance, Simulation, etc.)
- Versions may be adjusted based on user feedback and priorities
- Original roadmap features (Alarm System, Device Templates, Calculation Engine, MQTT, Historian) consolidated into v6.0.0
- Community contributions may accelerate timeline
- Critical bug fixes may result in patch versions between planned releases
- This roadmap prioritizes incremental improvements and technical debt reduction before major feature additions
