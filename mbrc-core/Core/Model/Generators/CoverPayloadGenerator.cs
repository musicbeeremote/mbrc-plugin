namespace MusicBeeRemote.Core.Model.Generators
{
    internal class CoverPayloadGenerator
    {
        public static CoverPayload Create(string cover, bool include)
        {
            var payload = new CoverPayload();

            if (string.IsNullOrEmpty(cover))
            {
                payload.Status = CoverStatusCodes.NotFound;
            }
            else
            {
                if (include)
                {
                    payload.Status = CoverStatusCodes.CoverAvailable;
                    payload.Cover = cover;
                }
                else
                {
                    payload.Status = CoverStatusCodes.CoverReady;
                }
            }

            return payload;
        }
    }
}
