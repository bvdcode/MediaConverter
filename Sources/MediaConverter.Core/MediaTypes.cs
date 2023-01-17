using System;
using System.Collections.Generic;
using System.Text;

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

            public static IEnumerable<string> AsEnumerable()
            {
                return new[]
                {
                    Mpeg4, Matroska, AudioVideoInterleave, FlashVideo, QuickTime
                };
            }
        }

        public static class Audio
        {
            public const string Mpeg3 = "mp3";
            public const string Mpeg4 = "m4a";
            public const string FreeLossless = "flac";
            public const string Waveform = "wav";
            public static IEnumerable<string> AsEnumerable()
            {
                return new[]
                {
                     Mpeg3, Mpeg4, FreeLossless, Waveform
                };
            }
        }        
    }
}