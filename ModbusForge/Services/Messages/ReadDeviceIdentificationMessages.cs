using System;
using System.Collections.Generic;
using System.Text;
using Modbus.Message;

namespace ModbusForge.Services.Messages
{
    /// <summary>
    /// FC43 / MEI type 14 (0x2B/0x0E) Read Device Identification request.
    /// PDU: [0x2B][0x0E][read device ID code][object ID]
    /// </summary>
    internal sealed class ReadDeviceIdentificationRequest : IModbusMessage
    {
        internal const byte ReadDeviceIdentificationFunctionCode = 0x2B;
        internal const byte MeiTypeDeviceIdentification = 0x0E;
        private const int FrameSize = 5; // slave address + PDU

        public ReadDeviceIdentificationRequest()
        {
            FunctionCode = ReadDeviceIdentificationFunctionCode;
        }

        public ReadDeviceIdentificationRequest(byte slaveAddress, byte readDeviceIdCode, byte objectId)
            : this()
        {
            SlaveAddress = slaveAddress;
            ReadDeviceIdCode = readDeviceIdCode;
            ObjectId = objectId;
        }

        public byte FunctionCode { get; set; }

        public byte SlaveAddress { get; set; }

        public ushort TransactionId { get; set; }

        public byte ReadDeviceIdCode { get; set; }

        public byte ObjectId { get; set; }

        public byte[] ProtocolDataUnit => new[]
        {
            FunctionCode, MeiTypeDeviceIdentification, ReadDeviceIdCode, ObjectId
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
                throw new FormatException($"Read Device Identification request frame must be at least {FrameSize} bytes.");

            SlaveAddress = frame[0];
            FunctionCode = frame[1];
            ReadDeviceIdCode = frame[3];
            ObjectId = frame[4];
        }
    }

    /// <summary>
    /// FC43 / MEI type 14 (0x2B/0x0E) Read Device Identification response.
    /// PDU: [0x2B][0x0E][code][conformity level][more follows][next object ID][object count]([id][length][ASCII value])*
    /// </summary>
    internal sealed class ReadDeviceIdentificationResponse : IModbusMessage
    {
        private const int HeaderFrameSize = 8; // slave address + 7 PDU header bytes

        public ReadDeviceIdentificationResponse()
        {
            FunctionCode = ReadDeviceIdentificationRequest.ReadDeviceIdentificationFunctionCode;
        }

        public byte FunctionCode { get; set; }

        public byte SlaveAddress { get; set; }

        public ushort TransactionId { get; set; }

        public byte ReadDeviceIdCode { get; set; }

        public byte ConformityLevel { get; set; }

        public byte MoreFollows { get; set; }

        public byte NextObjectId { get; set; }

        public Dictionary<byte, string> Objects { get; } = new();

        public byte[] ProtocolDataUnit
        {
            get
            {
                var pdu = new List<byte>
                {
                    FunctionCode,
                    ReadDeviceIdentificationRequest.MeiTypeDeviceIdentification,
                    ReadDeviceIdCode,
                    ConformityLevel,
                    MoreFollows,
                    NextObjectId,
                    (byte)Objects.Count
                };
                foreach (var pair in Objects)
                {
                    var value = Encoding.ASCII.GetBytes(pair.Value);
                    pdu.Add(pair.Key);
                    pdu.Add((byte)value.Length);
                    pdu.AddRange(value);
                }
                return pdu.ToArray();
            }
        }

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
            if (frame.Length < HeaderFrameSize)
                throw new FormatException($"Read Device Identification response frame must be at least {HeaderFrameSize} bytes.");

            SlaveAddress = frame[0];
            FunctionCode = frame[1];
            ReadDeviceIdCode = frame[3];
            ConformityLevel = frame[4];
            MoreFollows = frame[5];
            NextObjectId = frame[6];

            int objectCount = frame[7];
            int offset = 8;
            Objects.Clear();
            for (int i = 0; i < objectCount; i++)
            {
                if (offset + 1 >= frame.Length)
                    throw new FormatException("Truncated object header in Read Device Identification response.");

                byte objectId = frame[offset];
                int length = frame[offset + 1];
                offset += 2;
                if (offset + length > frame.Length)
                    throw new FormatException("Truncated object value in Read Device Identification response.");

                Objects[objectId] = Encoding.ASCII.GetString(frame, offset, length);
                offset += length;
            }
        }
    }
}
