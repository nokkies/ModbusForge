using System;
using System.Threading.Tasks;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class DispatcherTests
    {
        [Fact]
        public void ImmediateDispatcher_Invoke_ExecutesActionSynchronously()
        {
            var dispatcher = new ImmediateDispatcher();
            var value = 0;

            dispatcher.Invoke(() => value = 42);

            Assert.Equal(42, value);
        }

        [Fact]
        public void ImmediateDispatcher_Invoke_T_ReturnsValue()
        {
            var dispatcher = new ImmediateDispatcher();

            var result = dispatcher.Invoke(() => 42);

            Assert.Equal(42, result);
        }

        [Fact]
        public async Task ImmediateDispatcher_InvokeAsync_ExecutesAction()
        {
            var dispatcher = new ImmediateDispatcher();
            var value = 0;

            await dispatcher.InvokeAsync(() => value = 42);

            Assert.Equal(42, value);
        }

        [Fact]
        public async Task ImmediateDispatcher_InvokeAsync_T_ReturnsValue()
        {
            var dispatcher = new ImmediateDispatcher();

            var result = await dispatcher.InvokeAsync(() => 42);

            Assert.Equal(42, result);
        }

        [Fact]
        public async Task ImmediateDispatcher_InvokeAsync_PropagatesException()
        {
            var dispatcher = new ImmediateDispatcher();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.InvokeAsync(() => throw new InvalidOperationException("boom")));
        }
    }
}
