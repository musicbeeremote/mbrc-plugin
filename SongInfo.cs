using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace MusicBeePlugin
{
    public class SongInfo
    {
        public string Artist { get; set; }

        public string Title { get; set; }

        public string Album { get; set; }

        public string Year { get; set; }

        public string ImageData { get; set; }

        public string ResizedImage()
        {
            byte[] imageBytes = Convert.FromBase64String(ImageData);
            Image imageCover;
            using (MemoryStream ms = new MemoryStream(imageBytes, 0, imageBytes.Length))
            {
                imageCover = Image.FromStream(ms, true);
            }

            var sourceWidth = imageCover.Width;
            var sourceHeight = imageCover.Height;

            var nPercentW = (300 / (float)sourceWidth);
            var nPercentH = (300 / (float)sourceHeight);

            var nPercent = nPercentH < nPercentW ? nPercentH : nPercentW;
            var destWidth = (int)(sourceWidth * nPercent);
            var destHeight = (int)(sourceHeight * nPercent);


            MemoryStream ms2;
            using (var bmp = new Bitmap(destWidth, destHeight))
            {
                var graph = Graphics.FromImage(bmp);
                graph.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graph.DrawImage(imageCover, 0, 0, destWidth, destHeight);
                graph.Dispose();

                ms2 = new MemoryStream();
                bmp.Save(ms2, System.Drawing.Imaging.ImageFormat.Png);
            }

            return Convert.ToBase64String(ms2.ToArray());
        }

    }
}
