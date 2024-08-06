using System.Collections.Generic;

namespace MediaConverter.Core
{
    public static class MediaTypes
    {
        public static class Video
        {
            public const string Mpeg4 = "mp4";
            public const string Matroska = "mkv";
            public const string AudioVideoInterleave = "avi";
            public const string FlashVideo = "flv";
            public const string QuickTime = "mov";
            public const string WindowsMedia = "wmv";
            public const string WebM = "webm";
            public const string TransportStream = "ts";
            public const string ProgramStream = "mpg";
            public const string Mpeg2TransportStream = "m2ts";
            public const string Mpeg2TransportStream1 = "mts";
            public const string Mpeg2TransportStream2 = "m2t";
            public const string Mpeg2ProgramStream = "vob";

            public static IEnumerable<string> AsEnumerable()
            {
                return new[]
                { 
                    WebM,
                    Mpeg4,
                    Matroska,
                    QuickTime,
                    FlashVideo,
                    WindowsMedia,
                    ProgramStream,
                    TransportStream,
                    AudioVideoInterleave,
                    Mpeg2TransportStream,
                    Mpeg2TransportStream1,
                    Mpeg2TransportStream2,
                };
            }
        }

        public static class Audio
        {
            public const string Mpeg3 = "mp3";
            public const string Mpeg4 = "m4a";
            public const string FreeLossless = "flac";
            public const string Waveform = "wav";
            public const string Ogging = "ogg";

            public static IEnumerable<string> AsEnumerable()
            {
                return new[]
                {
                     Mpeg3, Mpeg4, FreeLossless, Waveform, Ogging
                };
            }
        }        
    }
}