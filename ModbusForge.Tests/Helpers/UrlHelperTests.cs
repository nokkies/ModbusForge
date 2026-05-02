using ModbusForge.Helpers;
using Xunit;

namespace ModbusForge.Tests.Helpers
{
    public class UrlHelperTests
    {
        [Theory]
        [InlineData("http://example.com")]
        [InlineData("https://example.com")]
        [InlineData("https://www.paypal.com/donate/?hosted_button_id=ELTVNJEYLZE3W")]
        [InlineData("mailto:test@example.com")]
        public void IsValidUrl_ShouldReturnTrue_ForAllowedSchemes(string url)
        {
            // Act
            bool result = UrlHelper.IsValidUrl(url);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-url")]
        [InlineData("C:\\Windows\\System32\\calc.exe")]
        [InlineData("file:///etc/passwd")]
        [InlineData("ftp://example.com")]
        [InlineData("javascript:alert(1)")]
        [InlineData("data:text/plain,hello")]
        public void IsValidUrl_ShouldReturnFalse_ForDisallowedOrInvalidUrls(string? url)
        {
            // Act
            bool result = UrlHelper.IsValidUrl(url);

            // Assert
            Assert.False(result);
        }
    }
}
