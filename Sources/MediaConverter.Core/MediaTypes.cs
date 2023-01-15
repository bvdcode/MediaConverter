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
        }

        public static class Audio
        {
            public const string Mpeg3 = "mp3";
            public const string Mpeg4 = "m4a";
            public const string FreeLossless = "flac";
            public const string Waveform = "wav";
        }        
    }
}