using System;
using System.Text.RegularExpressions;
using Xunit;

namespace SRNSMudApp.Tests {
    public class RegexTest {
        [Fact]
        public void TestRegex() {
            var text = "hello http://localhost:5165/ItemDetail/123 world";
            var regex = new Regex(@"https?:\/\/(?:localhost(?:\:\d+)?|(?:www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6})\b(?:[-a-zA-Z0-9()@:%_\+.~#?&//=]*)");
            var matches = regex.Matches(text);
            Assert.Single(matches);
            Assert.Equal("http://localhost:5165/ItemDetail/123", matches[0].Value);
        }
    }
}
