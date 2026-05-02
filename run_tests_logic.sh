#!/bin/bash
dotnet new xunit -n LogicTests
cd LogicTests
dotnet add package Moq
dotnet add package Microsoft.Extensions.Logging.Abstractions
dotnet add package NModbus
rm UnitTest1.cs
mkdir -p Services Models Helpers Coordinators
cp -r ../ModbusForge.Tests/Services/*.cs Services/
cp -r ../ModbusForge.Tests/Models/*.cs Models/ 2>/dev/null || true
cp -r ../ModbusForge.Tests/Helpers/*.cs Helpers/ 2>/dev/null || true
cp -r ../ModbusForge.Tests/Coordinators/*.cs Coordinators/ 2>/dev/null || true

# Copy source files needed for tests (only non-WPF ones)
mkdir -p Source/Services Source/Models Source/Helpers Source/Coordinators
cp -r ../ModbusForge/Services/*.cs Source/Services/
cp -r ../ModbusForge/Models/*.cs Source/Models/
cp -r ../ModbusForge/Helpers/*.cs Source/Helpers/
cp -r ../ModbusForge/Coordinators/*.cs Source/Coordinators/ 2>/dev/null || true

# Remove tests that depend on WPF
rm -f Services/VisualSimulationServiceTests.cs
rm -f Services/VisualNodeEditorViewModelTests.cs 2>/dev/null || true
rm -f Source/Services/VisualSimulationService.cs
rm -f Source/Services/PreferencesWindow.xaml.cs 2>/dev/null || true

sed -i 's/using ModbusForge.Views;//g' Source/Services/*.cs 2>/dev/null || true

dotnet test
