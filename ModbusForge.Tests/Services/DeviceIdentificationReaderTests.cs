using System.Collections.Generic;
using System.Text;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services;

public class DeviceIdentificationReaderTests
{
    private static byte[] BuildResponse(params (byte ObjectId, string Value)[] objects)
    {
        var pdu = new List<byte> { 0x2B, 0x0E, 0x01, 0x01, 0x00, 0x00, (byte)objects.Length };

        foreach (var (objectId, value) in objects)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            pdu.Add(objectId);
            pdu.Add((byte)bytes.Length);
            pdu.AddRange(bytes);
        }

        return pdu.ToArray();
    }

    [Fact]
    public void BuildRequest_IsBasicDeviceIdentificationFrame()
    {
        var request = DeviceIdentificationReader.BuildRequest(17);

        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x05, 17, 0x2B, 0x0E, 0x01, 0x00 }, request);
    }

    [Fact]
    public void Parse_BasicObjects_ReturnsVendorProductAndRevision()
    {
        var pdu = BuildResponse((0x00, "Acme Corp"), (0x01, "MF-1000"), (0x02, "V1.2"));

        var identification = DeviceIdentificationReader.Parse(pdu);

        Assert.NotNull(identification);
        Assert.Equal("Acme Corp", identification!.VendorName);
        Assert.Equal("MF-1000", identification.ProductCode);
        Assert.Equal("V1.2", identification.Revision);
    }

    [Fact]
    public void Parse_PartialObjects_LeavesMissingFieldsEmpty()
    {
        var identification = DeviceIdentificationReader.Parse(BuildResponse((0x00, "Acme Corp")));

        Assert.NotNull(identification);
        Assert.Equal("Acme Corp", identification!.VendorName);
        Assert.Equal(string.Empty, identification.ProductCode);
    }

    [Fact]
    public void Parse_ExceptionResponse_ReturnsNull()
    {
        Assert.Null(DeviceIdentificationReader.Parse(new byte[] { 0xAB, 0x01 }));
    }

    [Fact]
    public void Parse_TruncatedObjectValue_IgnoresIncompleteObject()
    {
        var pdu = new byte[] { 0x2B, 0x0E, 0x01, 0x01, 0x00, 0x00, 0x02, 0x00, 0x04, (byte)'A', (byte)'c', (byte)'m', (byte)'e', 0x01, 0x08, (byte)'M' };

        var identification = DeviceIdentificationReader.Parse(pdu);

        Assert.NotNull(identification);
        Assert.Equal("Acme", identification!.VendorName);
        Assert.Equal(string.Empty, identification.ProductCode);
    }

    [Fact]
    public void Parse_WrongMeiType_ReturnsNull()
    {
        Assert.Null(DeviceIdentificationReader.Parse(new byte[] { 0x2B, 0x0D, 0x01, 0x01, 0x00, 0x00, 0x01 }));
    }
}
