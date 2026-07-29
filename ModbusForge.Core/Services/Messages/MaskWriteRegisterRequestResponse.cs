using System;
using Modbus.Message;

namespace ModbusForge.Services.Messages
{
    /// <summary>
    /// FC22 (0x16) Mask Write Register. Request and response share the same layout,
    /// the slave echoes the request back after applying the masks.
    /// PDU: [FC][address hi/lo][AND mask hi/lo][OR mask hi/lo]
    /// </summary>
    internal sealed class MaskWriteRegisterRequestResponse : IModbusMessage
    {
        internal const byte MaskWriteRegisterFunctionCode = 22;
        private const int FrameSize = 8; // slave address + PDU

        public MaskWriteRegisterRequestResponse()
        {
            FunctionCode = MaskWriteRegisterFunctionCode;
        }

        public MaskWriteRegisterRequestResponse(byte slaveAddress, ushort startAddress, ushort andMask, ushort orMask)
            : this()
        {
            SlaveAddress = slaveAddress;
            StartAddress = startAddress;
            AndMask = andMask;
            OrMask = orMask;
        }

        public byte FunctionCode { get; set; }

        public byte SlaveAddress { get; set; }

        public ushort TransactionId { get; set; }

        public ushort StartAddress { get; set; }

        public ushort AndMask { get; set; }

        public ushort OrMask { get; set; }

        public byte[] ProtocolDataUnit => new[]
        {
            FunctionCode,
            (byte)(StartAddress >> 8), (byte)(StartAddress & 0xFF),
            (byte)(AndMask >> 8), (byte)(AndMask & 0xFF),
            (byte)(OrMask >> 8), (byte)(OrMask & 0xFF)
        };

        public byte[] MessageFrame
        {
            get
            {
                var pdu = ProtocolDataUnit;
                var frame = new byte[pdu.Length + 1];
                frame[0] = SlaveAddress;
                Buffer.BlockCopy(pdu, 0, frame, 1, pdu.Length);
                return frame;
            }
        }

        public void Initialize(byte[] frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (frame.Length < FrameSize)
                throw new FormatException($"Mask Write Register frame must be at least {FrameSize} bytes.");

            SlaveAddress = frame[0];
            FunctionCode = frame[1];
            StartAddress = (ushort)((frame[2] << 8) | frame[3]);
            AndMask = (ushort)((frame[4] << 8) | frame[5]);
            OrMask = (ushort)((frame[6] << 8) | frame[7]);
        }
    }
}
