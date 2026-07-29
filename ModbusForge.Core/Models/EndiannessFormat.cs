using System.ComponentModel.DataAnnotations;

namespace ModbusForge.Models
{
    /// <summary>
    /// Describes the byte/word ordering of multi-register 32-bit and 64-bit values.
    /// </summary>
    public enum EndiannessFormat
    {
        [Display(Name = "ABCD (Big-Endian)")]
        ABCD_BigEndian,

        [Display(Name = "BADC (Byte Swap)")]
        BADC_ByteSwap,

        [Display(Name = "CDAB (Word Swap)")]
        CDAB_WordSwap,

        [Display(Name = "DCBA (Little-Endian)")]
        DCBA_LittleEndian
    }
}
