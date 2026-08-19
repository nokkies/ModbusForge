# AI Agent Instructions

This file provides instructions for AI coding assistants working on this project.

## Git Workflow

### Pushing Changes to GitHub

**IMPORTANT**: PowerShell uses `;` for command chaining, not `&&`.

1. **Always verify status first**:
   ```powershell
   git status
   ```

2. **Stage and commit**:
   ```powershell
   git add -A
   git commit -m "your message"
   ```

3. **Push to master**:
   ```powershell
   git push origin master
   ```

4. **Verify the push succeeded**:
   ```powershell
   git log origin/master --oneline -3
   ```

### Creating a Release

The release workflow is automated via `.github/workflows/release.yml`. It triggers on `v*` tag pushes and can also be started manually from the Actions tab (`workflow_dispatch`).

1. **Update version in all three `.csproj` files** (`ModbusForge`, `ModbusForge.Core`, `ModbusForge.Headless`):
   ```xml
   <Version>2026.8.27</Version>
   <AssemblyVersion>2026.8.27</AssemblyVersion>
   <FileVersion>2026.8.27</FileVersion>
   ```

2. **Commit version bump**:
   ```powershell
   git add -A; git commit -m "v2026.8.27: Description of changes"
   ```

3. **Create and push tag**:
   ```powershell
   git tag -a v2026.8.27 -m "v2026.8.27 release"
   git push origin master
   git push origin v2026.8.27
   ```

   Pushing the tag will automatically build, test, package, and create a GitHub Release with the installer, Windows/Linux Avalonia zips, and Windows/Linux Headless zips.

4. **Local installer build** (optional):
   ```powershell
   .\build.ps1 -Task Installer
   ```

## Versioning

As of the v6.1.0 / headless split work, ModbusForge uses **CalVer-style** versions in the form `YYYY.M.INCREMENT` (e.g. `2026.8.27`):

- `YYYY` = year
- `M` = month (no leading zero)
- `INCREMENT` = release number within that month, starting at 1

Update the version in all three `.csproj` files before tagging (`ModbusForge`, `ModbusForge.Core`, `ModbusForge.Headless`):

```xml
<Version>2026.8.27</Version>
<AssemblyVersion>2026.8.27</AssemblyVersion>
<FileVersion>2026.8.27</FileVersion>
```

Tags should still be prefixed with `v`, e.g. `v2026.8.27`.

## Release Files Policy

**Do NOT create extra markdown files for releases.**

- ❌ Don't create: `RELEASE_v2026.7.12.md`, `RELEASE_SUMMARY.md`, etc.
- ✅ Update: `README.md` changelog section
- ✅ Upload: Only the installer `.exe` to GitHub Releases

## Project Structure

- `ModbusForge/` - Main Avalonia desktop application (`net8.0`)
- `ModbusForge.Core/` - Cross-platform view-agnostic class library (`net8.0`)
- `ModbusForge.Avalonia.Tests/` - Avalonia integration/unit tests (`net8.0`)
- `ModbusForge.Headless/` - Linux/headless console runtime (`net8.0`)
- `ModbusForge.Tests/` - Core/headless unit tests (`net8.0-windows`)
- `setup/ModbusForge.iss` - Inno Setup installer script
- `installers/` - Built installers (gitignored)
- `.windsurf/workflows/` - Workflow definitions

## Build & Test

```powershell
# Build the solution
dotnet build c:/Users/rvn/source/repos/ModbusForge/ModbusForge.sln

# Run unit tests (excluding UI automation and smoke tests)
dotnet test c:/Users/rvn/source/repos/ModbusForge/ModbusForge.Tests/ModbusForge.Tests.csproj --filter "FullyQualifiedName!~UITests & FullyQualifiedName!~SmokeTests" --no-build

# Run Avalonia tests
dotnet test c:/Users/rvn/source/repos/ModbusForge/ModbusForge.Avalonia.Tests/ModbusForge.Avalonia.Tests.csproj --no-build
```

If the build fails because `obj\Debug\net8.0-windows\ModbusForge.dll` is locked, terminate any lingering `dotnet.exe` processes and retry:

```powershell
taskkill /f /im dotnet.exe
dotnet build c:/Users/rvn/source/repos/ModbusForge/ModbusForge.sln
```

## Code Style

- Use `ILogger` for all logging (no `Debug.WriteLine` or custom file logging)
- Constants for magic numbers
- Proper event handler cleanup to prevent memory leaks
- Input validation with visual feedback for user inputs
