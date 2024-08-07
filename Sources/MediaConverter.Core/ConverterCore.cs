using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using MediaConverter.Core.Helpers;
using Xabe.FFmpeg.Downloader;
using Xabe.FFmpeg;
using Serilog;

namespace MediaConverter.Core
{
    public sealed class ConverterCore
    {
        private readonly ILogger _logger;
        private readonly bool _copyCodec;
        private readonly bool _checkCodec;
        private readonly bool _checkFooter;
        private readonly bool _ignoreErrors;
        private readonly string _targetCodec;
        private readonly string _outputFormat;
        private readonly StreamType _streamType;
        private bool _markBadAsCompleted = false;
        private HashSet<string>? _convertedHashes;
        private readonly List<FileInfo> _inputCache;
        private readonly DirectoryInfo _inputDirectory;
        private TimeSpan _totalElapsed = TimeSpan.Zero;
        private readonly IEnumerable<string> _inputFormats;

        #region Constants

        private const string applicationName = nameof(MediaConverter);
        private const string convertedHashesFile = "media_converter_hashes.txt";

        #endregion

        #region Counters

        private long compressedBytes = 0;
        private int processedCounter = 0;
        private int progressCounter = 0;
        private int errorCounter = 0;
        private int skipCounter = 0;

        #endregion

        public ConverterCore(string inputDirectory, string outputFormat,
            bool ignoreErrors, bool checkCodec, bool checkFooter,
            bool copyCodec, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(inputDirectory))
            {
                throw new ArgumentException($"'{nameof(inputDirectory)}' cannot be null or whitespace.",
                    nameof(inputDirectory));
            }
            if (string.IsNullOrWhiteSpace(outputFormat))
            {
                throw new ArgumentException($"'{nameof(outputFormat)}' cannot be null or whitespace.",
                    nameof(outputFormat));
            }
            _inputDirectory = new DirectoryInfo(inputDirectory);
            if (!_inputDirectory.Exists)
            {
                throw new DirectoryNotFoundException(inputDirectory);
            }
            _logger = logger;
            CheckLibraries();
            _outputFormat = outputFormat.Replace(".", string.Empty).Trim();
            _inputCache = new List<FileInfo>();
            _inputFormats = DetectInputFormats();
            _targetCodec = SetupTargetCodec();
            _streamType = SetupStreamType();
            _ignoreErrors = ignoreErrors;
            _checkCodec = checkCodec;
            _checkFooter = checkFooter;
            _copyCodec = copyCodec;
        }

        public void SetMarkBadAsCompleted(bool markBadAsCompleted)
        {
            _markBadAsCompleted = markBadAsCompleted;
        }

        private StreamType SetupStreamType()
        {
            if (MediaTypes.Video.AsEnumerable().Any(x => x == _outputFormat.ToLower()))
            {
                return StreamType.Video;
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == _outputFormat.ToLower()))
            {
                return StreamType.Audio;
            }
            errorCounter++;
            throw new NotSupportedException("Output media type is not supported: " + _outputFormat);
        }

        private string SetupTargetCodec()
        {
            if (MediaTypes.Video.AsEnumerable().Any(x => x == _outputFormat.ToLower()))
            {
                return _outputFormat switch
                {
                    MediaTypes.Video.Mpeg4 => VideoCodec.h264.ToString(),
                    MediaTypes.Video.Matroska => VideoCodec.h264.ToString(),
                    MediaTypes.Video.AudioVideoInterleave => VideoCodec.mpeg4.ToString(),
                    MediaTypes.Video.FlashVideo => VideoCodec.flv1.ToString(),
                    MediaTypes.Video.QuickTime => VideoCodec.h264.ToString(),
                    MediaTypes.Video.WindowsMedia => VideoCodec.wmv3.ToString(),
                    MediaTypes.Video.WebM => VideoCodec.vp9.ToString(),
                    MediaTypes.Video.TransportStream => VideoCodec.h264.ToString(),
                    MediaTypes.Video.ProgramStream => VideoCodec.mpeg2video.ToString(),
                    MediaTypes.Video.Mpeg2TransportStream => VideoCodec.mpeg2video.ToString(),
                    MediaTypes.Video.Mpeg2TransportStream1 => VideoCodec.mpeg2video.ToString(),
                    MediaTypes.Video.Mpeg2TransportStream2 => VideoCodec.mpeg2video.ToString(),
                    _ => throw new NotSupportedException("Output media type is not supported: " + _outputFormat)
                };
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == _outputFormat.ToLower()))
            {
                return _outputFormat switch
                {
                    MediaTypes.Audio.Mpeg3 => AudioCodec.mp3.ToString(),
                    MediaTypes.Audio.Mpeg4 => AudioCodec.aac.ToString(),
                    MediaTypes.Audio.Waveform => AudioCodec.pcm_s16le.ToString(),
                    MediaTypes.Audio.FreeLossless => AudioCodec.flac.ToString(),
                    MediaTypes.Audio.Ogging => AudioCodec.vorbis.ToString(),
                    _ => throw new NotSupportedException("Output media type is not supported: " + _outputFormat)
                };
            }
            errorCounter++;
            throw new NotSupportedException("Output media type is not supported: " + _outputFormat);
        }

        private string SHA512(string text)
        {
            using var sha = System.Security.Cryptography.SHA512.Create();
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            byte[] hashBytes = sha.ComputeHash(textBytes);
            string hash = BitConverter
                .ToString(hashBytes)
                .Replace("-", string.Empty);
            return hash;
        }

        #region File system actions

        public async Task FindInputFilesAsync()
        {
            await Task.Run(() => _inputCache.AddRange(GetInputFilesLazy()));
            _logger.Information("Found supported input files: {0}", _inputCache.Count);
        }

        public void ResetCache()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
                _logger.Information("Cache folder was deleted.");
            }
            Directory.CreateDirectory(folder);
        }

        private IEnumerable<FileInfo> GetInputFilesLazy()
        {
            var allFiles = FileHelpers.GetFiles(_inputDirectory, _inputFormats);
            _logger.Information("Search for input files from {0} files count...", allFiles.Count());
            foreach (var file in allFiles)
            {
                if (!IsConverted(file))
                {
                    yield return file;
                }
                else
                {
                    skipCounter++;
                    if (skipCounter % 100 == 0)
                    {
                        _logger.Information("Skipped {0} files", skipCounter);
                    }
                }
            }
        }

        private void DeleteTempFiles()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            if (!directoryInfo.Exists)
            {
                return;
            }
            var files = directoryInfo.GetFiles();
            foreach (var file in files)
            {
                if (file.Name.ToLower().Contains(convertedHashesFile))
                {
                    continue;
                }
                try
                {
                    File.Delete(file.FullName);
                }
                catch (Exception) { }
            }
        }

        private void SetAsConvertedByMetadata(FileInfo file)
        {
            string hash = SHA512(file.Name + file.Length);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string filePath = Path.Combine(folder, convertedHashesFile);
            File.AppendAllText(filePath, hash + Environment.NewLine);
        }

        private bool IsConvertedByMetadata(FileInfo file)
        {
            _convertedHashes ??= InitializeConvertedHashes();
            string hash = SHA512(file.Name + file.Length);
            return _convertedHashes.Contains(hash);
        }

        private bool IsConverted(FileInfo file)
        {
            if (!file.Name.EndsWith(_outputFormat))
            {
                return false;
            }
            if (IsConvertedByMetadata(file))
            {
                return true;
            }

            if (_checkFooter)
            {
                bool hasFfmpegFooter = FileHelpers.HasValidFooter(file, "Lavf60.16.100", applicationName);
                if (hasFfmpegFooter)
                {
                    SetAsConvertedByMetadata(file);
                    return true;
                }
            }

            if (!_checkCodec)
            {
                return false;
            }

            try
            {
                var mediaInfo = FFmpeg.GetMediaInfo(file.FullName).Result;
                IStream codec = mediaInfo.Streams
                    .Where(x => x.StreamType == _streamType)
                    .FirstOrDefault(x => x.Codec == _targetCodec);

                if (codec != null)
                {
                    SetAsConvertedByMetadata(file);
                }

                return codec != null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error when checking codec of file: {0}", file.Name);
                if (_markBadAsCompleted)
                {
                    SetAsConvertedByMetadata(file);
                }
                return true;
            }
        }

        public HashSet<string> InitializeConvertedHashes()
        {
            HashSet<string> _convertedHashes = new HashSet<string>();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string filePath = Path.Combine(folder, convertedHashesFile);
            if (!File.Exists(filePath))
            {
                return _convertedHashes;
            }
            var lines = File.ReadAllLines(filePath);
            foreach (string line in lines)
            {
                if (_convertedHashes.Contains(line))
                {
                    continue;
                }
                _convertedHashes.Add(line);
            }
            return _convertedHashes;
        }

        private IEnumerable<string> DetectInputFormats()
        {
            if (MediaTypes.Video.AsEnumerable().Any(x => x == _outputFormat.ToLower()))
            {
                return MediaTypes.Video.AsEnumerable();
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == _outputFormat.ToLower()))
            {
                return MediaTypes.Audio.AsEnumerable();
            }
            throw new NotSupportedException("Output media type is not supported: " + _outputFormat);
        }

        #endregion

        #region Media actions

        public async Task ConvertFilesAsync(int limit = -1, CancellationToken token = default)
        {
            DeleteTempFiles();
            string currentDirectory = string.Empty;
            _logger.Information("Output format: {0}", _outputFormat);
            var inputFiles = _inputCache.Count > 0 ? _inputCache : GetInputFilesLazy();
            foreach (var inputFile in inputFiles)
            {
                if (currentDirectory != inputFile.DirectoryName)
                {
                    currentDirectory = inputFile.DirectoryName;
                    _logger.Information("Current directory: {0}", currentDirectory.Replace(_inputDirectory.FullName, string.Empty));
                }
                try
                {
                    await ConvertMediaAsync(inputFile, token);
                }
                catch (Exception ex)
                {
                    errorCounter++;
                    _logger.Error(ex, "Error when file converting");
                }
                if (limit > 0)
                {
                    if (processedCounter >= limit)
                    {
                        break;
                    }
                }
                if (token != default && token.IsCancellationRequested)
                {
                    break;
                }
            }
            OnWorkCompleted();
        }

        private async Task ConvertMediaAsync(FileInfo inputFile, CancellationToken token = default)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string counter = _inputCache.Count > 0 ? $" ({processedCounter + 1}/{_inputCache.Count})" : string.Empty;
            _logger.Information("Processing file: {0}{1}", inputFile.Name, counter);
            FileInfo temp = FileHelpers.GetTempFile(_outputFormat, applicationName);
            var snippet = await FFmpeg.Conversions.FromSnippet.Convert(inputFile.FullName, temp.FullName);
            snippet.OnProgress += Snippet_OnProgress;
            snippet.AddParameter($"-metadata comment={applicationName}");
            if (_ignoreErrors)
            {
                snippet.AddParameter("-err_detect ignore_err", ParameterPosition.PreInput);
            }
            if (_copyCodec)
            {
                snippet.AddParameter("-c copy", ParameterPosition.PostInput);
            }
            IConversionResult result = await snippet.Start(token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            long oldSize = inputFile.Length;
            FileHelpers.Move(temp, inputFile);
            long newSize = temp.Length;
            OnItemProcessed(temp, sw.Elapsed, newSize, oldSize);
        }

        private void CheckLibraries()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName, 
                "ffmpeg-libraries");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            FFmpeg.SetExecutablesPath(folder);
            string ffmpegPath = Path.Combine(folder, "ffmpeg");
            string ffprobePath = Path.Combine(folder, "ffprobe");
            bool ffmpegExists = File.Exists(ffmpegPath);
            bool ffprobeExists = File.Exists(ffprobePath);
            if (!ffmpegExists || !ffprobeExists)
            {
                FfmpegDownloadProgress progress = new FfmpegDownloadProgress(_logger);
                FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, folder, progress).Wait();
            }
            _logger.Information("FFmpeg path: {0}", folder);
        }

        #endregion

        #region Hooks

        private void OnWorkCompleted()
        {
            _logger.Information("Done. Processed {0} files. Compressed {1} MBytes. Errors: {2}. Elapsed: {3}",
                processedCounter, compressedBytes / 1024 / 1024, errorCounter, _totalElapsed);
        }

        private void OnItemProcessed(FileInfo inputFile, TimeSpan elapsed, long newSize, long oldSize)
        {
            processedCounter++;
            long compressed = oldSize - newSize;
            compressedBytes += compressed;
            _totalElapsed += elapsed;
            long oldSizeMb = oldSize / 1024 / 1024;
            long newSizeMb = newSize / 1024 / 1024;
            int compressionRate = (int)(compressed * 100 / oldSize);
            _logger.Information("Compressed file: {0}, {1} => {2} {3}, elapsed: {4}", 
                inputFile.Name, oldSizeMb + "Mb", newSizeMb + "Mb", $"({compressionRate}%)", elapsed);
            SetAsConvertedByMetadata(inputFile);
        }

        private void Snippet_OnProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {
            if (progressCounter == args.Percent)
            {
                return;
            }
            progressCounter = args.Percent;
            string percentText = (args.Percent.ToString() + '%').PadLeft(4, ' ');
            _logger.Information("Progress: {0} ({1} - {2}) PID: {3}", percentText, args.Duration, args.TotalLength, args.ProcessId);
        }

        public void ResetCompletedFiles()
        {
            int counter = 0;
            var files = _inputDirectory.GetFiles();
            _convertedHashes ??= InitializeConvertedHashes();
            foreach (var file in files)
            {
                string hash = SHA512(file.Name + file.Length);
                if (_convertedHashes.Contains(hash))
                {
                    _convertedHashes.Remove(hash);
                    counter++;
                }
            }
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
            string filePath = Path.Combine(folder, convertedHashesFile);
            File.WriteAllLines(filePath, _convertedHashes);
            _logger.Information("File statuses in directory were reset for {0} files", counter);
        }

        #endregion
    }
}