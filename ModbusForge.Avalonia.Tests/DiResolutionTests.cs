using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.Avalonia;
using ModbusForge.Avalonia.ViewModels;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class DiResolutionTests
    {
        [Fact]
        public void VisualNodeEditorViewModel_Resolves_WithAllDependencies()
        {
            var configureMethod = typeof(App).GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(configureMethod);

            var serviceProvider = (IServiceProvider?)configureMethod.Invoke(null, null)
                ?? throw new InvalidOperationException("ConfigureServices returned null.");

            var exception = Record.Exception(() => serviceProvider.GetRequiredService<VisualNodeEditorViewModel>());
            Assert.Null(exception);

            var vm = serviceProvider.GetRequiredService<VisualNodeEditorViewModel>();
            Assert.NotNull(vm);
            Assert.NotNull(vm.OpenTagBrowserCommand);
            Assert.NotNull(vm.OpenWatchWindowCommand);
        }
    }
}
