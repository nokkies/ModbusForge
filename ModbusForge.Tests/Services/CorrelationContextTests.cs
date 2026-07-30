using System;
using System.Threading.Tasks;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public sealed class CorrelationContextTests
    {
        private readonly ICorrelationContext _context = new CorrelationContext();

        [Fact]
        public void StartNew_Generates_Non_Empty_CorrelationId()
        {
            var id = _context.StartNew();
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.Equal(id, _context.CurrentId);
        }

        [Fact]
        public void Set_Stores_CorrelationId()
        {
            _context.Set("abc-123");
            Assert.Equal("abc-123", _context.CurrentId);
        }

        [Fact]
        public void Clear_Removes_CorrelationId()
        {
            _context.StartNew();
            _context.Clear();
            Assert.Null(_context.CurrentId);
        }

        [Fact]
        public void WithCorrelationId_Restores_Previous_Id()
        {
            _context.Set("outer");
            Assert.Equal("outer", _context.CurrentId);

            _context.WithCorrelationId("inner", () =>
            {
                Assert.Equal("inner", _context.CurrentId);
            });

            Assert.Equal("outer", _context.CurrentId);
        }

        [Fact]
        public async Task WithCorrelationIdAsync_Restores_Previous_Id()
        {
            _context.Set("outer");

            await _context.WithCorrelationIdAsync("inner", async () =>
            {
                await Task.Yield();
                Assert.Equal("inner", _context.CurrentId);
            });

            Assert.Equal("outer", _context.CurrentId);
        }

        [Fact]
        public void WithCorrelationId_Restores_Previous_Id_Even_On_Exception()
        {
            _context.Set("outer");

            Assert.Throws<InvalidOperationException>(() =>
                _context.WithCorrelationId("inner", () => throw new InvalidOperationException()));

            Assert.Equal("outer", _context.CurrentId);
        }
    }
}
