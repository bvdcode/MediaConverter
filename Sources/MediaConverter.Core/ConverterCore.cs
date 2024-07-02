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
        public event EventHandler<string>? LogOutput;
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
            Log("Found supported input files: {0}", _inputCache.Count);
        }

        public void ResetCache()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
                Log("Cache folder was deleted.");
            }
            Directory.CreateDirectory(folder);
        }

        private IEnumerable<FileInfo> GetInputFilesLazy()
        {
            var allFiles = FileHelpers.GetFiles(_inputDirectory, _inputFormats);
            Log("Search for input files from {0} files count...", allFiles.Count());
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
                        Log("Skipped {0} files", skipCounter);
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
            string hash = SHA512(file.Name + file.Length + file.LastWriteTimeUtc);
            string lightHash = SHA512(file.Name + file.Length);
            return _convertedHashes.Contains(hash) || _convertedHashes.Contains(lightHash);
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
                bool hasFfmpegFooter = FileHelpers.HasValidFooter(file, "Lavf58.45.100", applicationName);
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
                Log(ex, "Bad file - " + file.Name);
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
            Log("Supported input media types: {0}", string.Join(", ", _inputFormats));
            var inputFiles = _inputCache.Count > 0 ? _inputCache : GetInputFilesLazy();
            foreach (var inputFile in inputFiles)
            {
                if (currentDirectory != inputFile.DirectoryName)
                {
                    currentDirectory = inputFile.DirectoryName;
                    Log("Current directory: {0}", currentDirectory.Replace(_inputDirectory.FullName, string.Empty));
                }
                try
                {
                    await ConvertMediaAsync(inputFile, token);
                }
                catch (Exception ex)
                {
                    Log(ex, "Error when file converting");
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
            Log("File: {0}{1}", inputFile.Name, counter);
            FileInfo temp = FileHelpers.GetTempFile(_outputFormat, applicationName);
            var snippet = await Xabe.FFmpeg.FFmpeg.Conversions.FromSnippet.Convert(inputFile.FullName, temp.FullName);
            snippet.OnProgress += Snippet_OnProgress;
            snippet.AddParameter($"-metadata {applicationName}={true}");
            if (_ignoreErrors)
            {
                snippet.AddParameter("-err_detect ignore_err", Xabe.FFmpeg.ParameterPosition.PreInput);
            }
            if (_copyCodec)
            {
                snippet.AddParameter("-c copy", Xabe.FFmpeg.ParameterPosition.PostInput);
            }
            Xabe.FFmpeg.IConversionResult result = await snippet.Start(token);
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
            bool ffmpegExists = File.Exists("ffmpeg.exe");
            bool ffprobeExists = File.Exists("ffprobe.exe");
            if (!ffmpegExists || !ffprobeExists)
            {
                FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
            }
            FFmpeg.SetExecutablesPath(Environment.CurrentDirectory);
        }

        #endregion

        #region Logging

        private void Log(Exception exception, string caption)
        {
            errorCounter++;
            LogOutput?.Invoke(this, string.Format("[ERROR] {0} - {1} ({2})", DateTime.Now.ToString("dd MMM HH:mm:ss"), caption, exception.ToString()));
        }

        private void Log(string message)
        {
            LogOutput?.Invoke(this, string.Format("[INFO] {0} - {1}", DateTime.Now.ToString("dd MMM HH:mm:ss"), message));
        }

        private void Log(string message, params object[] args)
        {
            LogOutput?.Invoke(this, string.Format("[INFO] {0} - {1}", DateTime.Now.ToString("dd MMM HH:mm:ss"), string.Format(message, args)));
        }

        #endregion

        #region Hooks

        private void OnWorkCompleted()
        {
            Log("Done. Processed {0} files. Compressed {1} MBytes. Errors: {2}. Elapsed: {3}", processedCounter, compressedBytes / 1024 / 1024, errorCounter, _totalElapsed);
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
            Log("Compressed file: {0}, {1}Mb => {2}Mb ({3}%), elapsed: {4}", inputFile.Name, oldSizeMb, newSizeMb, compressionRate, elapsed);
            SetAsConvertedByMetadata(inputFile);
        }

        private void Snippet_OnProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {
            if (progressCounter == args.Percent)
            {
                return;
            }
            progressCounter = args.Percent;
            Log("Progress: {0}% ({1} - {2}) PID: {3}", args.Percent, args.Duration, args.TotalLength, args.ProcessId);
        }

        #endregion
    }
}