using System;
using Serilog;
using Xabe.FFmpeg;

namespace MediaConverter.Core
{
    public class FfmpegDownloadProgress : IProgress<ProgressInfo>
    {
        private readonly ILogger _logger;
        private int latestProgress = 0;

        public FfmpegDownloadProgress(ILogger logger)
        {
            _logger = logger;
        }

        public void Report(ProgressInfo value)
        {
            if (latestProgress == 100)
            {
                return;
            }
            int progress = (int)(100 * value.DownloadedBytes / value.TotalBytes);
            if (progress > latestProgress)
            {
                _logger.Information("Downloading ffmpeg: {0}%", progress);
                latestProgress = progress;
            }
        }
    }
}