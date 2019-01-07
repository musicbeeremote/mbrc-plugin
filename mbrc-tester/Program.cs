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