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

1. **Update version in `.csproj`**:
   ```xml
   <Version>4.5.15</Version>
   <AssemblyVersion>4.5.15.0</AssemblyVersion>
   <FileVersion>4.5.15.0</FileVersion>
   ```

2. **Commit version bump**:
   ```powershell
   git add -A; git commit -m "v4.5.15: Description of changes"
   ```

3. **Create and push tag**:
   ```powershell
   git tag -a v4.5.15 -m "v4.5.15 release"
   git push origin master
   git push origin v4.5.15
   ```

4. **Build installer** (optional - for full release):
   ```powershell
   dotnet publish .\ModbusForge\ModbusForge.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish\win-x64
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "setup\ModbusForge.iss"
   ```

5. **Create GitHub release** manually at:
   https://github.com/nokkies/ModbusForge/releases/new

   - Select the tag (e.g., v4.5.15)
   - Title: `ModbusForge v4.5.15`
   - Upload: `installers\ModbusForge-4.5.15-setup.exe`
   - Description: Brief changelog

## Versioning

As of the v6.2.0 / headless split work, ModbusForge uses **CalVer-style** versions in the form `YYYY.M.INCREMENT` (e.g. `2026.7.1`):

- `YYYY` = year
- `M` = month (no leading zero)
- `INCREMENT` = release number within that month, starting at 1

Update the version in all three `.csproj` files before tagging (`ModbusForge`, `ModbusForge.Core`, `ModbusForge.Headless`):

```xml
<Version>2026.7.1</Version>
<AssemblyVersion>2026.7.1</AssemblyVersion>
<FileVersion>2026.7.1</FileVersion>
```

Tags should still be prefixed with `v`, e.g. `v2026.7.1`.

## Release Files Policy

**Do NOT create extra markdown files for releases.**

- ❌ Don't create: `RELEASE_v4.5.15.md`, `RELEASE_SUMMARY.md`, etc.
- ✅ Update: `README.md` changelog section
- ✅ Upload: Only the installer `.exe` to GitHub Releases

## Project Structure

- `ModbusForge/` - Main WPF application
- `ModbusForge.Core/` - Cross-platform view-agnostic class library (`net8.0`)
- `ModbusForge.Avalonia/` - Cross-platform Avalonia UI spike (`net8.0`)
- `ModbusForge.Avalonia.Tests/` - Avalonia integration/unit tests (`net8.0`)
- `ModbusForge.Headless/` - Linux/headless console runtime (`net8.0`)
- `ModbusForge.Tests/` - Unit tests
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
