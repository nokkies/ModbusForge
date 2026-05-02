# ModbusForge Feature Roadmap

## Current Version
**v4.6.0** - Released with REST API for AI integration, code health improvements

---

## v4.7.0 - Simulation GUI Improvements
**Focus:** Improve user experience and usability of simulation features

### Tasks
- [ ] Redesign simulation tab layout with split panels and collapsible sections
- [ ] Add visual node simulation controls panel (grouped by node type)
- [ ] Improve waveform selection with dropdowns and preset configurations
- [ ] Add real-time value preview with mini-charts for simulation nodes
- [ ] Simplify enable/disable workflow with master toggle and per-node controls

**Estimated Effort:** 2-3 weeks

---

## v4.8.0 - Alarm/Event System
**Focus:** Critical production monitoring and alerting capabilities

### Tasks
- [ ] Design alarm conditions (high/low thresholds, rate of change, deadband)
- [ ] Create IAlarmService interface and AlarmService implementation
- [ ] Add alarm configuration UI with hysteresis and severity levels
- [ ] Implement email notification service (SMTP)
- [ ] Add alarm history logging and viewer with acknowledgment

**Estimated Effort:** 3-4 weeks

---

## v4.9.0 - Device Template Library
**Focus:** Reduce setup time with pre-built device configurations

### Tasks
- [ ] Define JSON template schema (register maps, data types, units, descriptions)
- [ ] Create template parser and validator
- [ ] Build initial library: Siemens VFDs, Allen-Bradley PLCs, common sensors
- [ ] Add template import/export to custom entries
- [ ] Create template browser UI with search/filter by manufacturer

**Estimated Effort:** 3-4 weeks

---

## v5.0.0 - Calculation Engine
**Focus:** Enable derived tags without scripting (Major Feature)

### Tasks
- [ ] Define expression syntax (Excel-like: `=A1+B1`, `=AVG(Tag1,Tag2)`)
- [ ] Create derived tag model and CalculationService
- [ ] Implement operators: +, -, *, /, AVG, MIN, MAX, IF, ROUND
- [ ] Add derived tag UI editor with syntax validation
- [ ] Integrate with custom entries for real-time evaluation

**Estimated Effort:** 4-5 weeks

---

## v5.1.0 - MQTT Support
**Focus:** Enable cloud integration and IIoT connectivity

### Tasks
- [ ] Add MQTTnet library for MQTT client
- [ ] Create IMqttService interface and MqttService
- [ ] Add MQTT settings (broker URL, port, username, password, QoS)
- [ ] Implement tag-to-topic mapping configuration
- [ ] Add bidirectional publish/subscribe with connection status UI

**Estimated Effort:** 3-4 weeks

---

## v5.2.0 - Data Historian
**Focus:** Long-term data storage and analysis

### Tasks
- [ ] Add SQLite database for long-term time-series storage
- [ ] Create historian service with time-range queries and aggregation
- [ ] Add historian configuration UI (retention period, compression level)
- [ ] Integrate with existing trend system for seamless export

**Estimated Effort:** 2-3 weeks

---

## v5.3.0 - Web Dashboard
**Focus:** Remote monitoring and management via browser

### Tasks
- [ ] Create React/Vue frontend served by existing REST API
- [ ] Add real-time dashboard with live tag values (WebSocket updates)
- [ ] Implement trend charts using Chart.js or ECharts
- [ ] Add device configuration web UI for remote management

**Estimated Effort:** 4-5 weeks

---

## v5.4.0 - HMI Widget Library
**Focus:** Visual dashboard widgets for operator interfaces

### Tasks
- [ ] Create gauge controls (circular, linear, digital)
- [ ] Add meter and progress bar controls with styling options
- [ ] Create widget binding system for tags (drag-and-drop assignment)
- [ ] Add widget editor with canvas for dashboard layout

**Estimated Effort:** 4-5 weeks

---

## v5.5.0 - Protocol Analysis
**Focus:** Advanced troubleshooting and diagnostics

### Tasks
- [ ] Add Modbus packet capture and logging (request/response pairs)
- [ ] Create packet decoder with function code and register details
- [ ] Add traffic statistics (throughput, error rate, timing analysis)
- [ ] Create protocol analyzer UI with filter/search and export

**Estimated Effort:** 3-4 weeks

---

## Versioning Strategy

### Patch Versions (x.x.X)
- Bug fixes
- Minor improvements
- Performance optimizations

### Minor Versions (x.X.0)
- New features that don't break existing functionality
- UI improvements
- Service additions

### Major Versions (X.0.0)
- Significant architectural changes
- Breaking changes to existing APIs
- Major new feature categories (e.g., Calculation Engine)

---

## Release Timeline (Estimated)

| Version | Target Quarter | Status |
|---------|---------------|--------|
| v4.7.0 | Q2 2026 | Planned |
| v4.8.0 | Q2 2026 | Planned |
| v4.9.0 | Q3 2026 | Planned |
| v5.0.0 | Q3 2026 | Planned |
| v5.1.0 | Q4 2026 | Planned |
| v5.2.0 | Q4 2026 | Planned |
| v5.3.0 | Q1 2027 | Planned |
| v5.4.0 | Q1 2027 | Planned |
| v5.5.0 | Q2 2027 | Planned |

---

## Notes

- Versions may be adjusted based on user feedback and priorities
- Some features may be split across multiple versions if scope grows
- Community contributions may accelerate timeline
- Critical bug fixes may result in patch versions between planned releases
