using ModbusForge.Helpers;
using Xunit;

namespace ModbusForge.Tests.Helpers
{
    public class UrlHelperTests
    {
        [Theory]
        [InlineData("https://www.google.com")]
        [InlineData("http://example.com")]
        [InlineData("mailto:test@example.com")]
        [InlineData("HTTPS://WWW.PAYPAL.COM")]
        public void IsSafeUrl_ValidUrls_ReturnsTrue(string url)
        {
            // Act
            bool result = UrlHelper.IsSafeUrl(url);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-url")]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("ftp://example.com")]
        [InlineData("javascript:alert('XSS')")]
        [InlineData("C:\\path\\to\\file.exe")]
        public void IsSafeUrl_InvalidOrUnsafeUrls_ReturnsFalse(string url)
        {
            // Act
            bool result = UrlHelper.IsSafeUrl(url);

            // Assert
            Assert.False(result);
        }
    }
}
