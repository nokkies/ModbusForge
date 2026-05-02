using System;
using Xunit;
using ModbusForge.Helpers;

namespace ModbusForge.Tests.Helpers
{
    public class UrlHelperTests
    {
        [Fact]
        public void OpenUrl_NullOrEmpty_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => UrlHelper.OpenUrl(null));
            Assert.Throws<ArgumentException>(() => UrlHelper.OpenUrl(""));
            Assert.Throws<ArgumentException>(() => UrlHelper.OpenUrl("   "));
        }

        [Fact]
        public void OpenUrl_InvalidFormat_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => UrlHelper.OpenUrl("not-a-url"));
        }

        [Fact]
        public void OpenUrl_InvalidScheme_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => UrlHelper.OpenUrl("file:///C:/Windows/System32/cmd.exe"));
            Assert.Throws<InvalidOperationException>(() => UrlHelper.OpenUrl("ftp://example.com/file.txt"));
        }

        // We can't easily test valid URLs without opening a process, which we want to avoid in unit tests.
        // We could extract the Process.Start call into an interface, but for this simple helper it might be overkill.
    }
}
