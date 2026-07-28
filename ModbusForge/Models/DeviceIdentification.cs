using System.Collections.Generic;
using System.Linq;

namespace ModbusForge.Models
{
    /// <summary>
    /// Category of objects requested by / returned from Modbus FC43/MEI 14
    /// (Read Device Identification).
    /// </summary>
    public enum DeviceIdCategory : byte
    {
        Basic = 0x01,
        Regular = 0x02,
        Extended = 0x03,
        Individual = 0x04
    }

    /// <summary>
    /// Well-known object IDs defined by the Modbus specification for
    /// Read Device Identification.
    /// </summary>
    public static class DeviceIdObject
    {
        public const byte VendorName = 0x00;
        public const byte ProductCode = 0x01;
        public const byte MajorMinorRevision = 0x02;
        public const byte VendorUrl = 0x03;
        public const byte ProductName = 0x04;
        public const byte ModelName = 0x05;
        public const byte UserApplicationName = 0x06;
    }

    /// <summary>
    /// Device identity as exchanged by FC43 (Read Device Identification).
    /// Object values are ASCII strings keyed by object ID.
    /// </summary>
    public class DeviceIdentification
    {
        public Dictionary<byte, string> Objects { get; } = new();

        public byte ConformityLevel { get; set; } = 0x82; // Regular identification, stream + individual access

        public string VendorName
        {
            get => GetObject(DeviceIdObject.VendorName);
            set => Objects[DeviceIdObject.VendorName] = value;
        }

        public string ProductCode
        {
            get => GetObject(DeviceIdObject.ProductCode);
            set => Objects[DeviceIdObject.ProductCode] = value;
        }

        public string MajorMinorRevision
        {
            get => GetObject(DeviceIdObject.MajorMinorRevision);
            set => Objects[DeviceIdObject.MajorMinorRevision] = value;
        }

        public string VendorUrl
        {
            get => GetObject(DeviceIdObject.VendorUrl);
            set => Objects[DeviceIdObject.VendorUrl] = value;
        }

        public string ProductName
        {
            get => GetObject(DeviceIdObject.ProductName);
            set => Objects[DeviceIdObject.ProductName] = value;
        }

        public string ModelName
        {
            get => GetObject(DeviceIdObject.ModelName);
            set => Objects[DeviceIdObject.ModelName] = value;
        }

        public string UserApplicationName
        {
            get => GetObject(DeviceIdObject.UserApplicationName);
            set => Objects[DeviceIdObject.UserApplicationName] = value;
        }

        public string GetObject(byte objectId)
            => Objects.TryGetValue(objectId, out var value) ? value : string.Empty;

        /// <summary>
        /// Object IDs belonging to the requested category: basic is 0x00-0x02,
        /// regular is 0x03-0x7F, extended is 0x80-0xFF.
        /// </summary>
        public IEnumerable<byte> ObjectIdsFor(DeviceIdCategory category)
        {
            var ids = Objects.Keys.OrderBy(id => id);
            return category switch
            {
                DeviceIdCategory.Basic => ids.Where(id => id <= 0x02),
                DeviceIdCategory.Regular => ids.Where(id => id <= 0x7F),
                _ => ids
            };
        }

        public static DeviceIdentification CreateDefault(string revision) => new()
        {
            VendorName = "ModbusForge",
            ProductCode = "MF-TCP",
            MajorMinorRevision = revision,
            VendorUrl = "https://github.com/nokkies/ModbusForge",
            ProductName = "ModbusForge Modbus TCP Server",
            ModelName = "ModbusForge"
        };
    }
}
