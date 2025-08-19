# Build script to capture detailed build output
$ErrorActionPreference = "Stop"

try {
    Write-Host "=== Cleaning solution ===" -ForegroundColor Cyan
    dotnet clean -v detailed
    
    Write-Host "`n=== Restoring packages ===" -ForegroundColor Cyan
    dotnet restore -v detailed
    
    Write-Host "`n=== Building solution ===" -ForegroundColor Cyan
    dotnet build -v detailed -p:GenerateFullPaths=true
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n=== Build succeeded! ===" -ForegroundColor Green
        
        # Run the application if build was successful
        Write-Host "`n=== Starting application ===" -ForegroundColor Cyan
        dotnet run --project ModbusForge
    } else {
        Write-Host "`n=== Build failed with exit code $LASTEXITCODE ===" -ForegroundColor Red
    }
} catch {
    Write-Host "`n=== Error: $_ ===" -ForegroundColor Red
}

Read-Host -Prompt "`nPress Enter to exit"
