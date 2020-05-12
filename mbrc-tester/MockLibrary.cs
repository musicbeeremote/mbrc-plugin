using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicBeeRemote.Core.Model.Entities;
using Newtonsoft.Json;

namespace MbrcTester
{
    public class MockLibrary
    {
        private readonly List<MockTrackMetadata> _library;

        public MockLibrary()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mock_library.json");

            // deserialize JSON directly from a file
            using (var file = File.OpenText($"{path}"))
            {
                _library = JsonConvert.DeserializeObject<List<MockTrackMetadata>>(file.ReadToEnd());
            }
        }

        public IEnumerable<Track> GetTracks()
        {
            return _library.Select(metadata => new Track
            {
                Album = metadata.Album,
                AlbumArtist = metadata.AlbumArtist,
                Disc = metadata.Disc,
                Artist = metadata.Artist,
                Genre = metadata.Genre,
                Title = metadata.Title,
                Trackno = metadata.TrackNo,
                Year = metadata.Year,
                Src = metadata._id,
            });
        }
    }
}
