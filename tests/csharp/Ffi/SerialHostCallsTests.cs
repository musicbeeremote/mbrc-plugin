using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using MessagePack;
using MessagePack.Resolvers;
using MusicBeePlugin;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Ffi.Generated;
using MusicBeePlugin.Logging;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Settings;
using NSubstitute;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Ffi
{
    /// <summary>
    ///     Runs every query and command against a fake MusicBee that records its
    ///     cursor walks and the changes those walks read. A kind
    ///     <see cref="SerialHostCalls" /> misses could read another call's results or
    ///     see a change land mid-walk; a kind it names needlessly waits for nothing.
    /// </summary>
    public class SerialHostCallsTests
    {
        public static IEnumerable<object[]> Queries() =>
            Enum.GetValues(typeof(QueryType)).Cast<object>().Select(kind => new[] { kind });

        public static IEnumerable<object[]> Commands() =>
            Enum.GetValues(typeof(CommandType)).Cast<object>().Select(kind => new[] { kind });

        [Theory]
        [MemberData(nameof(Queries))]
        public void Query_IsSerializedExactlyWhenItWalksOrChangesWhatAWalkReads(QueryType kind)
        {
            var host = new FakeMusicBee();
            var queries = new QueryHandlers(new PlayerDataProvider(host.Api), new TrackDataProvider(host.Api),
                new PlaylistDataProvider(host.Api), new LibraryDataProvider(host.Api),
                new PodcastDataProvider(host.Api), Substitute.For<IUserSettings>());

            foreach (var payload in Payloads())
                RunUnlessPayloadMismatch(() => queries.Handle((int)kind, payload));

            SerialHostCalls.Includes(kind).Should().Be(host.SerialCalls.Count > 0,
                "it called [{0}]", string.Join(", ", host.SerialCalls.Distinct()));
        }

        [Theory]
        [MemberData(nameof(Commands))]
        public void Command_IsSerializedExactlyWhenItWalksOrChangesWhatAWalkReads(CommandType kind)
        {
            var host = new FakeMusicBee();
            var commands = new CommandHandlers(new PlayerDataProvider(host.Api), new TrackDataProvider(host.Api),
                new PlaylistDataProvider(host.Api), Substitute.For<IUserSettings>(),
                Substitute.For<ISystemOperations>(), Substitute.For<IPluginLogger>());

            foreach (var payload in Payloads())
                RunUnlessPayloadMismatch(() => commands.Handle((int)kind, payload));

            SerialHostCalls.Includes(kind).Should().Be(host.SerialCalls.Count > 0,
                "it called [{0}]", string.Join(", ", host.SerialCalls.Distinct()));
        }

        /// <summary>
        ///     Every params field filled, so no handler stops at an empty guard, once
        ///     per type and value a <c>value</c> field takes.
        /// </summary>
        private static IEnumerable<byte[]> Payloads()
        {
            var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
            foreach (var value in new object[] { "x", "3", true, false, 1 })
            {
                var fields = new Dictionary<string, object>
                {
                    ["value"] = value,
                    ["query"] = "x",
                    ["path"] = "x",
                    ["paths"] = new[] { "x" },
                    ["files"] = new[] { "x" },
                    ["url"] = "x",
                    ["id"] = "x",
                    ["folder"] = "x",
                    ["name"] = "x",
                    ["offset"] = 0,
                    ["limit"] = 10,
                    ["index"] = 0,
                    ["from"] = 0,
                    ["to"] = 1,
                    ["album_artists"] = false,
                    ["updated_since"] = 0L,
                    ["mode"] = "All",
                    ["status"] = "Love",
                    ["queue_type"] = "Next",
                    ["play"] = "x",
                    ["tag"] = "Artist",
                };
                yield return MessagePackSerializer.Serialize(fields, options);
            }
        }

        private static void RunUnlessPayloadMismatch(Action handle)
        {
            try
            {
                handle();
            }
            catch (MessagePackSerializationException)
            {
            }
        }

        /// <summary>
        ///     A MusicBee whose query calls find nothing and whose other calls succeed,
        ///     so a handler runs as far as it can. Changes to the now playing list, a
        ///     playlist or a file's tags count as serial calls alongside the walks.
        /// </summary>
        private sealed class FakeMusicBee
        {
            private const int MaxCalls = 10000;

            private static readonly Regex QueryCall =
                new Regex(@"^(Library|NowPlayingList|Playlist|Podcasts)_Query(?!SimilarArtists)");

            private static readonly Regex ChangeCall = new Regex(
                @"^((NowPlayingList|Playlist)_(?!Query|Get|Is)|Library_(SetFileTag|CommitTagsToFile|SetArtworkEx|AddFileToLibrary)$)");

            private int _calls;

            public FakeMusicBee()
            {
                object api = new Plugin.MusicBeeApiInterface();
                foreach (var field in typeof(Plugin.MusicBeeApiInterface).GetFields())
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                        field.SetValue(api, Answering(field.Name, field.FieldType));
                Api = (Plugin.MusicBeeApiInterface)api;
            }

            public Plugin.MusicBeeApiInterface Api { get; }

            public List<string> SerialCalls { get; } = new List<string>();

            private Delegate Answering(string name, Type delegateType)
            {
                var invoke = delegateType.GetMethod("Invoke");
                var parameters = invoke.GetParameters()
                    .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                    .ToArray();
                var answer = Expression.Call(Expression.Constant(this),
                    typeof(FakeMusicBee).GetMethod(nameof(Answer), BindingFlags.NonPublic | BindingFlags.Instance),
                    Expression.Constant(name), Expression.Constant(invoke.ReturnType, typeof(Type)));
                var body = invoke.ReturnType == typeof(void)
                    ? (Expression)answer
                    : Expression.Convert(answer, invoke.ReturnType);
                return Expression.Lambda(delegateType, body, parameters).Compile();
            }

            private object Answer(string name, Type returns)
            {
                if (++_calls > MaxCalls)
                    throw new InvalidOperationException("runaway loop calling " + name);
                if (QueryCall.IsMatch(name))
                {
                    SerialCalls.Add(name);
                    return Default(returns);
                }
                if (ChangeCall.IsMatch(name))
                    SerialCalls.Add(name);
                if (returns == typeof(bool))
                    return true;
                if (returns == typeof(string))
                    return "x";
                return Default(returns);
            }

            private static object Default(Type type) =>
                type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
