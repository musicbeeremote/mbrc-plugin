using System.IO;
using FluentAssertions;
using MusicBeePlugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Serialization
{
    /// <summary>
    ///     DTO drift guard — C# side.
    ///
    ///     The JSON fixtures under <c>mbrc-core/tests/schemas/</c> are the
    ///     canonical shape exchanged between the Rust core and the C# plugin.
    ///     If a C# property name drifts from its Rust serde counterpart, the
    ///     round-trip below loses the mismatched field and the test fails.
    ///     The matching Rust-side test lives in
    ///     <c>mbrc-core/tests/schema_drift.rs</c> — either side drifting
    ///     turns CI red.
    /// </summary>
    public class NativeDtoDriftTests
    {
        private static string SchemaPath(string name) =>
            Path.Combine(System.AppContext.BaseDirectory, "schemas", name);

        private static void AssertRoundTrip<T>(string schemaFile)
        {
            var expected = JObject.Parse(File.ReadAllText(SchemaPath(schemaFile)));
            var dto = JsonConvert.DeserializeObject<T>(expected.ToString());
            var actual = JObject.FromObject(dto);
            actual.Should().BeEquivalentTo(expected,
                $"round-trip drift for {schemaFile}: a C# property name doesn't match the Rust serde field");
        }

        [Fact]
        public void PlayerStateDto_roundtrips_canonical_schema()
        {
            AssertRoundTrip<PlayerStateDto>("player_state.json");
        }

        [Fact]
        public void TrackInfoDto_roundtrips_canonical_schema()
        {
            AssertRoundTrip<TrackInfoDto>("track_info.json");
        }

        [Fact]
        public void SetBoolParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<SetBoolParams>("set_bool_params.json");
        }

        [Fact]
        public void SetRepeatParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<SetRepeatParams>("set_repeat_params.json");
        }

        [Fact]
        public void StringValueParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<StringValueParams>("string_value_params.json");
        }

        [Fact]
        public void SetLfmRatingParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<SetLfmRatingParams>("set_lfm_rating_params.json");
        }

        [Fact]
        public void IndexParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<IndexParams>("index_params.json");
        }

        [Fact]
        public void MoveParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<MoveParams>("move_params.json");
        }

        [Fact]
        public void LibraryQueueParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<LibraryQueueParams>("library_queue_params.json");
        }

        [Fact]
        public void PaginationParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<PaginationParams>("pagination_params.json");
        }

        [Fact]
        public void PlaylistListResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<PlaylistListResponse>("playlist_list_response.json");
        }

        [Fact]
        public void NowPlayingListResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<NowPlayingListResponse>("now_playing_list_response.json");
        }

        [Fact]
        public void RadioStationsResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<RadioStationsResponse>("radio_stations_response.json");
        }

        [Fact]
        public void OutputDevicesResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<OutputDevicesResponse>("output_devices_response.json");
        }

        [Fact]
        public void QueryParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<QueryParams>("query_params.json");
        }

        [Fact]
        public void BrowseParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<BrowseParams>("browse_params.json");
        }

        [Fact]
        public void AlbumCoverParams_roundtrips_canonical_schema()
        {
            AssertRoundTrip<AlbumCoverParams>("album_cover_params.json");
        }

        [Fact]
        public void GenreListResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<GenreListResponse>("genre_list_response.json");
        }

        [Fact]
        public void ArtistListResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<ArtistListResponse>("artist_list_response.json");
        }

        [Fact]
        public void AlbumListResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<AlbumListResponse>("album_list_response.json");
        }

        [Fact]
        public void TrackListResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<TrackListResponse>("track_list_response.json");
        }

        [Fact]
        public void NowPlayingDetailsResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<NowPlayingDetailsResponse>("now_playing_details_response.json");
        }

        [Fact]
        public void AlbumCoverResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<AlbumCoverResponse>("album_cover_response.json");
        }

        [Fact]
        public void CoverCacheBuildStatusResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<CoverCacheBuildStatusResponse>(
                "cover_cache_build_status_response.json");
        }

        [Fact]
        public void PlaybackPositionResponse_roundtrips_canonical_schema()
        {
            AssertRoundTrip<PlaybackPositionResponse>(
                "playback_position_response.json");
        }
    }
}
