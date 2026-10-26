using System;
using System.Linq;
using System.Threading.Tasks;
using ModbusForge.Data;
using Xunit;

namespace ModbusForge.Tests.Models
{
    /// <summary>
    /// The 1-based data collections the server, the simulation and the PLC runtime
    /// share: index n is PDU address n-1, index 0 is an unused placeholder.
    /// </summary>
    public class DataStoreTests
    {
        [Fact]
        public void NewStore_ReadsZeroEverywhere()
        {
            var store = new DataStore();

            Assert.Equal(0, store.HoldingRegisters[1]);
            Assert.Equal(0, store.InputRegisters[65535]);
            Assert.False(store.CoilDiscretes[100]);
            Assert.False(store.InputDiscretes[65535]);
        }

        [Fact]
        public void Indexer_WriteThenRead_RoundTrips()
        {
            var store = new DataStore();

            store.HoldingRegisters[1] = 0x1234;
            store.HoldingRegisters[65535] = 7;
            store.CoilDiscretes[42] = true;

            Assert.Equal(0x1234, store.HoldingRegisters[1]);
            Assert.Equal(7, store.HoldingRegisters[65535]);
            Assert.True(store.CoilDiscretes[42]);
            Assert.False(store.CoilDiscretes[41]);
            Assert.Equal(0, store.InputRegisters[1]); // the tables are independent
        }

        [Fact]
        public void IndexZero_ReadsDefault_AndCannotBeWritten()
        {
            var store = new DataStore();

            Assert.Equal(0, store.HoldingRegisters[0]);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.HoldingRegisters[0] = 1);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(65536)]
        public void Indexer_OutOfRange_Throws(int index)
        {
            var store = new DataStore();

            Assert.Throws<ArgumentOutOfRangeException>(() => store.HoldingRegisters[index]);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.HoldingRegisters[index] = 1);
        }

        [Fact]
        public void SmallStore_HasItsSizePlusThePlaceholder()
        {
            var store = new DataStore(10);

            Assert.Equal(11, store.HoldingRegisters.Count);
            store.HoldingRegisters[10] = 5;
            Assert.Equal(5, store.HoldingRegisters[10]);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.HoldingRegisters[11] = 1);
        }

        [Fact]
        public void Ranges_ReadAndWriteConsecutivePoints()
        {
            var store = new DataStore();

            store.HoldingRegisters.WriteRange(10, new ushort[] { 1, 2, 3 });

            Assert.Equal(new ushort[] { 0, 1, 2, 3, 0 }, store.HoldingRegisters.ReadRange(9, 5));
            Assert.Equal(2, store.HoldingRegisters[11]);
            Assert.Empty(store.HoldingRegisters.ReadRange(1, 0));
        }

        [Fact]
        public void ReadRange_ReturnsACopy()
        {
            var store = new DataStore();
            store.HoldingRegisters[1] = 9;

            var copy = store.HoldingRegisters.ReadRange(1, 1);
            copy[0] = 100;

            Assert.Equal(9, store.HoldingRegisters[1]);
        }

        [Fact]
        public void Ranges_OutsideTheTable_Throw()
        {
            var store = new DataStore();

            Assert.Throws<ArgumentOutOfRangeException>(() => store.HoldingRegisters.ReadRange(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.HoldingRegisters.ReadRange(65535, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.HoldingRegisters.WriteRange(65535, new ushort[2]));
            Assert.Throws<ArgumentNullException>(() => store.HoldingRegisters.WriteRange(1, null!));
        }

        [Fact]
        public void Clear_ZeroesEveryPoint()
        {
            var store = new DataStore();
            store.HoldingRegisters[1] = 1;
            store.HoldingRegisters[65535] = 2;

            store.HoldingRegisters.Clear();

            Assert.Equal(0, store.HoldingRegisters[1]);
            Assert.Equal(0, store.HoldingRegisters[65535]);
        }

        [Fact]
        public void EnumerationAndIndexOf_UseThe1BasedIndex()
        {
            var store = new DataStore(4);
            store.HoldingRegisters[3] = 8;

            Assert.Equal(new ushort[] { 0, 0, 0, 8, 0 }, store.HoldingRegisters.ToArray());
            Assert.Equal(3, store.HoldingRegisters.IndexOf(8));
            Assert.Contains((ushort)8, store.HoldingRegisters);
            Assert.Equal(-1, store.HoldingRegisters.IndexOf(9));
        }

        [Fact]
        public async Task ConcurrentRangeWrites_AreNeverSeenHalfDone()
        {
            // A 32-bit value written as two registers in one call must be read whole.
            var store = new DataStore();
            var writer = Task.Run(() =>
            {
                for (ushort i = 0; i < 20000; i++) store.HoldingRegisters.WriteRange(1, new[] { i, i });
            });

            var torn = 0;
            while (!writer.IsCompleted)
            {
                var words = store.HoldingRegisters.ReadRange(1, 2);
                if (words[0] != words[1]) torn++;
            }
            await writer;

            Assert.Equal(0, torn);
        }
    }
}
