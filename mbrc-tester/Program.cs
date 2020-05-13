using System;
using System.IO;
using MbrcTester.ApiAdapters;
using MbrcTester.Properties;
using MusicBeeRemote.Core;

namespace MbrcTester
{
    /// <summary>
    /// MusicBeeRemote tester. This is a utility that can run on both .NET and mono
    /// And it provides the MusicBeeRemote API with mock library data.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The main function of the the tester program.
        /// </summary>
        public static void Main()
        {
            var playerState = new MockPlayerState();
            var nowPlaying = new MockNowPlaying();
            var player = new MockPlayer(playerState, nowPlaying);
            var library = new MockLibrary();

            var libraryApiAdapter = new LibraryApiAdapter(library);
            var nowPlayingApiAdapter = new NowPlayingApiAdapter(nowPlaying, player);
            var outputApiAdapter = new MockOutputApiAdapter();
            var playerApiAdapter = new MockPlayerApiAdapter(playerState, player);
            var queueAdapter = new QueueAdapter();
            var trackApiAdapter = new TrackApiAdapter(player);
            var baseStoragePath = Path.GetTempPath();

            var dependencies = new MusicBeeDependencies(
                libraryApiAdapter,
                nowPlayingApiAdapter,
                outputApiAdapter,
                playerApiAdapter,
                queueAdapter,
                trackApiAdapter,
                null,
                baseStoragePath,
                "1.0.0");

            var remoteBootstrap = new RemoteBootstrap();
            var musicBeeRemotePlugin = remoteBootstrap.BootStrap(dependencies);
            musicBeeRemotePlugin.Start();

            while (true)
            {
                Console.WriteLine(Resources.Input);
                var line = Console.ReadLine();
                if (line != "q!")
                {
                    continue;
                }

                musicBeeRemotePlugin.Terminate();
                remoteBootstrap.Dispose();
                Console.WriteLine(Resources.Quitting);
                break;
            }
        }
    }
}
