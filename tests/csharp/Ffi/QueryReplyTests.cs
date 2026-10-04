using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using MusicBeePlugin.Ffi;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Ffi
{
    /// <summary>
    ///     A reply is packed after the API lock is released, so it must already
    ///     hold its data rather than a query still to run.
    /// </summary>
    public class QueryReplyTests
    {
        [Fact]
        public void Of_RefusesALazySequence()
        {
            var calls = 0;
            IEnumerable<string> lazy = new[] { "a", "b" }.Select(x => { calls++; return x; });

            Action wrap = () => QueryReply.Of(lazy);

            wrap.Should().Throw<InvalidOperationException>();
            calls.Should().Be(0, "nothing was enumerated");
        }

        [Fact]
        public void Of_PacksACompleteList()
        {
            var paths = new List<string> { "a.mp3", "b.mp3" };

            MessagePack.MessagePackSerializer.Deserialize<List<string>>(
                    QueryReply.Of(paths).Pack(), cancellationToken: TestContext.Current.CancellationToken)
                .Should().Equal(paths);
        }

        [Fact]
        public void Of_AcceptsAStringAndAnArray()
        {
            QueryReply.Of("x").Pack().Should().NotBeEmpty();
            QueryReply.Of(new[] { "x" }).Pack().Should().NotBeEmpty();
        }
    }
}
