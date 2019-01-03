using System;
using System.IO;
using MbrcTester.ApiAdapters;
using MusicBeeRemote.Core;

namespace MbrcTester
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var playerState = new MockPlayerState();
            var mockPlayer = new MockPlayer();
            var mockLibrary = new MockLibrary();
            var mockNowPlaying = new MockNowPlaying();
            //todo some in memory/json
            var libraryApiAdapter = new LibraryApiAdapter(mockLibrary);
            var nowPlayingApiAdapter = new NowPlayingApiAdapter(mockNowPlaying);
            var outputApiAdapter = new MockOutputApiAdapter();
            var playerApiAdapter = new MockPlayerApiAdapter(playerState, mockPlayer);
            var queueAdapter = new QueueAdapter();
            var trackApiAdapter = new TrackApiAdapter();
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
                "1.0.0"
            );

            var remoteBootstrap = new RemoteBootstrap();
            var musicBeeRemotePlugin = remoteBootstrap.BootStrap(dependencies);
            musicBeeRemotePlugin.Start();

            while (true)
            {
                Console.WriteLine(@"Input:");
                var line = Console.ReadLine();
                if (line != "q!") continue;
                Console.WriteLine(@"Quiting");
                break;
            }
        }
    }
}