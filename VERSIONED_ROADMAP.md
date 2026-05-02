# ModbusForge Versioned Improvement Roadmap

Based on the v4.5.4 code review, here's the split across releases:

---

## v4.5.4 — Debug Cleanup & Dead Code Removal
**Theme:** "Clean House"  
**Focus:** Remove noise and clutter

### Items:
1. **Remove excessive Debug.WriteLine calls** (75+ instances)
   - File: `VisualNodeEditor.xaml.cs`
   - Action: Delete or wrap in `#if DEBUG` with feature flag
   
2. **Delete commented-out code blocks**
   - File: `VisualNodeEditor.xaml.cs` (ConfigureButton_Click method, lines 858-907)
   - Action: Remove ~100 lines of old ConnectorConfigWindow code
   
3. **Remove or implement placeholder methods**
   - File: `VisualNodeEditorViewModel.cs`
   - Action: `GetNodeSimulationValue()` returns false — implement or remove
   
4. **Remove potentially dead code**
   - File: `VisualNodeEditorViewModel.cs`
   - Action: Verify and remove `ConvertToSimulationElements()` if unused

**Result:** Cleaner codebase, less noise in debug output

---

## v4.5.5 — Error Handling & Logging Consistency
**Theme:** "Reliability"  
**Focus:** Fix silent failures and standardize logging

### Items:
5. **Fix empty catch blocks** (Silent failures)
   - Files: `MainViewModel.cs`, `VisualNodeEditorViewModel.cs`
   - Action: Add proper `ILogger` calls to all catch blocks
   
6. **Standardize logging approach**
   - File: `VisualSimulationService.cs`
   - Action: Replace `DebugLog()` method with proper `ILogger<T>` usage
   - Remove `System.Diagnostics.Debug.WriteLine` mixing
   
7. **Add input validation for address TextBox**
   - File: `VisualNodeEditor.xaml.cs`
   - Action: Add visual feedback (red border) for invalid numeric input
   - Consider numeric-only input restriction

**Result:** Better error visibility, consistent logging, user input validation

---

## v4.5.6 — Code Structure & Maintainability
**Theme:** "Refinement"  
**Focus:** Improve code organization and readability

### Items:
8. **Extract magic numbers to constants**
   - File: `VisualNodeEditor.xaml.cs`
   - Action: Create named constants for connector offsets (6), thresholds (500ms), etc.
   
9. **Break down long methods**
   - File: `VisualNodeEditor.xaml.cs`
   - Action: `CreateNodeElement()` is ~350 lines — extract helpers:
     - `CreateHeader()`
     - `CreateInlineEditor()`
     - `WirePropertyChangedHandlers()`
     
10. **Optimize unnecessary LINQ conversions**
    - File: `VisualSimulationService.cs`
    - Action: Remove redundant `.ToList()` calls on ObservableCollections

**Result:** Better maintainability, clearer code structure

---

## Summary Table

| Version | Theme | Items | Focus |
|---------|-------|-------|-------|
| **v4.5.4** | Clean House | 1-4 | Remove debug noise, dead code |
| **v4.5.5** | Reliability | 5-7 | Error handling, logging, validation |
| **v4.5.6** | Refinement | 8-10 | Structure, constants, organization |

---

## Recommended Timeline

- **v4.5.4**: Start immediately — low risk, high cleanup value
- **v4.5.5**: After 4.5.4 — requires testing error paths
- **v4.5.6**: Polish release — structural improvements

Each version builds on the previous, with v4.5.4 being the easiest and safest to implement first.

---

*Roadmap created: March 27, 2026*
