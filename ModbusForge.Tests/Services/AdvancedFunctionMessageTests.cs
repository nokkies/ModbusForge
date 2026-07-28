using System;
using ModbusForge.Services.Messages;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class AdvancedFunctionMessageTests
    {
        [Fact]
        public void MaskWriteRegister_BuildsSpecCompliantPdu()
        {
            var message = new MaskWriteRegisterRequestResponse(3, 0x0004, 0x00F2, 0x0025);

            Assert.Equal(new byte[] { 22, 0x00, 0x04, 0x00, 0xF2, 0x00, 0x25 }, message.ProtocolDataUnit);
            Assert.Equal(new byte[] { 3, 22, 0x00, 0x04, 0x00, 0xF2, 0x00, 0x25 }, message.MessageFrame);
        }

        [Fact]
        public void MaskWriteRegister_RoundTripsThroughInitialize()
        {
            var message = new MaskWriteRegisterRequestResponse();
            message.Initialize(new byte[] { 3, 22, 0x00, 0x04, 0x00, 0xF2, 0x00, 0x25 });

            Assert.Equal(3, message.SlaveAddress);
            Assert.Equal(22, message.FunctionCode);
            Assert.Equal(0x0004, message.StartAddress);
            Assert.Equal(0x00F2, message.AndMask);
            Assert.Equal(0x0025, message.OrMask);
        }

        [Fact]
        public void MaskWriteRegister_Initialize_ThrowsOnShortFrame()
        {
            var message = new MaskWriteRegisterRequestResponse();

            Assert.Throws<FormatException>(() => message.Initialize(new byte[] { 3, 22, 0x00 }));
        }

        [Fact]
        public void ReadDeviceIdentificationRequest_BuildsMeiPdu()
        {
            var request = new ReadDeviceIdentificationRequest(1, 0x01, 0x00);

            Assert.Equal(new byte[] { 0x2B, 0x0E, 0x01, 0x00 }, request.ProtocolDataUnit);
        }

        [Fact]
        public void ReadDeviceIdentificationResponse_ParsesObjects()
        {
            // Slave address + PDU: FC, MEI, code, conformity, more follows, next object, count, objects
            var frame = new byte[]
            {
                0x01, 0x2B, 0x0E, 0x01, 0x82, 0x00, 0x00, 0x02,
                0x00, 0x02, (byte)'A', (byte)'B',
                0x01, 0x01, (byte)'C'
            };

            var response = new ReadDeviceIdentificationResponse();
            response.Initialize(frame);

            Assert.Equal(0x82, response.ConformityLevel);
            Assert.Equal(0x00, response.MoreFollows);
            Assert.Equal(2, response.Objects.Count);
            Assert.Equal("AB", response.Objects[0x00]);
            Assert.Equal("C", response.Objects[0x01]);
        }

        [Fact]
        public void ReadDeviceIdentificationResponse_ThrowsOnTruncatedObject()
        {
            var frame = new byte[]
            {
                0x01, 0x2B, 0x0E, 0x01, 0x82, 0x00, 0x00, 0x01,
                0x00, 0x04, (byte)'A'
            };

            var response = new ReadDeviceIdentificationResponse();

            Assert.Throws<FormatException>(() => response.Initialize(frame));
        }
    }
}
