using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using MediaConverter.Core.Helpers;

namespace MediaConverter.Core
{
    public sealed class ConverterCore
    {
        private readonly bool ignoreErrors;
        private readonly string targetCodec;
        private readonly string outputFormat;
        private bool markBadAsCompleted = false;
        private HashSet<string>? convertedHashes;
        private readonly List<FileInfo> inputCache;
        public event EventHandler<string>? LogOutput;
        private readonly DirectoryInfo inputDirectory;
        private TimeSpan totalElapsed = TimeSpan.Zero;
        private readonly IEnumerable<string> inputFormats;
        private readonly Xabe.FFmpeg.StreamType streamType;

        #region Constants

        private const string currentEncoder = "Lavf58.45.100";
        private const string convertedHashesFolder = "MediaConverter";
        private const string convertedHashesFile = "media_converter_hashes.txt";

        #endregion

        #region Counters

        private long compressedBytes = 0;
        private int processedCounter = 0;
        private int progressCounter = 0;
        private int errorCounter = 0;
        private int skipCounter = 0;

        #endregion

        public ConverterCore(string inputDirectory, string outputFormat, bool ignoreErrors)
        {
            if (string.IsNullOrWhiteSpace(inputDirectory))
            {
                throw new ArgumentException($"'{nameof(inputDirectory)}' cannot be null or whitespace.", nameof(inputDirectory));
            }
            if (string.IsNullOrWhiteSpace(outputFormat))
            {
                throw new ArgumentException($"'{nameof(outputFormat)}' cannot be null or whitespace.", nameof(outputFormat));
            }
            this.inputDirectory = new DirectoryInfo(inputDirectory);
            if (!this.inputDirectory.Exists)
            {
                throw new DirectoryNotFoundException(inputDirectory);
            }
            CheckLibraries();
            this.outputFormat = outputFormat.Replace(".", string.Empty).Trim();
            inputCache = new List<FileInfo>();
            inputFormats = DetectInputFormats();
            targetCodec = SetupTargetCodec();
            streamType = SetupStreamType();
            this.ignoreErrors = ignoreErrors;
        }

        public void SetMarkBadAsCompleted(bool markBadAsCompleted)
        {
            this.markBadAsCompleted = markBadAsCompleted;
        }

        private Xabe.FFmpeg.StreamType SetupStreamType()
        {
            if (MediaTypes.Video.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return Xabe.FFmpeg.StreamType.Video;
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return Xabe.FFmpeg.StreamType.Audio;
            }
            throw new NotSupportedException("Output media type is not supported: " + outputFormat);
        }

        private string SetupTargetCodec()
        {
            if (MediaTypes.Video.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return outputFormat switch
                {
                    MediaTypes.Video.Mpeg4 => Xabe.FFmpeg.VideoCodec.h264.ToString(),
                    MediaTypes.Video.Matroska => Xabe.FFmpeg.VideoCodec.h264.ToString(),
                    MediaTypes.Video.AudioVideoInterleave => Xabe.FFmpeg.VideoCodec.mpeg4.ToString(),
                    MediaTypes.Video.FlashVideo => Xabe.FFmpeg.VideoCodec.flv1.ToString(),
                    MediaTypes.Video.QuickTime => Xabe.FFmpeg.VideoCodec.h264.ToString(),
                    MediaTypes.Video.WindowsMedia => Xabe.FFmpeg.VideoCodec.wmv3.ToString(),
                    MediaTypes.Video.WebM => Xabe.FFmpeg.VideoCodec.vp9.ToString(),
                    _ => throw new NotSupportedException("Output media type is not supported: " + outputFormat)
                };
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return outputFormat switch
                {
                    MediaTypes.Audio.Mpeg3 => Xabe.FFmpeg.AudioCodec.mp3.ToString(),
                    MediaTypes.Audio.Mpeg4 => Xabe.FFmpeg.AudioCodec.aac.ToString(),
                    MediaTypes.Audio.Waveform => Xabe.FFmpeg.AudioCodec.pcm_s16le.ToString(),
                    MediaTypes.Audio.FreeLossless => Xabe.FFmpeg.AudioCodec.flac.ToString(),
                    MediaTypes.Audio.Ogging => Xabe.FFmpeg.AudioCodec.vorbis.ToString(),
                    _ => throw new NotSupportedException("Output media type is not supported: " + outputFormat)
                };
            }
            throw new NotSupportedException("Output media type is not supported: " + outputFormat);
        }

        private string SHA512(string text)
        {
            using var sha = new System.Security.Cryptography.SHA256Managed();
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
            await Task.Run(() => inputCache.AddRange(GetInputFilesLazy()));
            Log("Found supported input files: {0}", inputCache.Count);
        }

        public void ResetCache()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), convertedHashesFolder);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
                Log("Cache folder was deleted.");
            }
            Directory.CreateDirectory(folder);
        }

        private IEnumerable<FileInfo> GetInputFilesLazy()
        {
            var allFiles = FileHelpers.GetFiles(inputDirectory, inputFormats);
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
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), convertedHashesFolder);
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
                File.Delete(file.FullName);
            }
        }
        
        private void SetAsConvertedByMetadata(FileInfo file)
        {
            string hash = SHA512(file.Name + file.Length + file.LastWriteTimeUtc);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), convertedHashesFolder);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string filePath = Path.Combine(folder, convertedHashesFile);
            File.AppendAllText(filePath, hash + Environment.NewLine);
        }

        private bool IsConvertedByMetadata(FileInfo file)
        {
            convertedHashes ??= InitializeConvertedHashes();
            string hash = SHA512(file.Name + file.Length + file.LastWriteTimeUtc);
            return convertedHashes.Contains(hash);
        }
        
        private bool IsConverted(FileInfo file)
        {
            if (IsConvertedByMetadata(file))
            {
                return true;
            }
            try
            {
                var mediaInfo = Xabe.FFmpeg.FFmpeg.GetMediaInfo(file.FullName).Result;
                Xabe.FFmpeg.IStream codec = mediaInfo.Streams
                    .Where(x => x.StreamType == streamType)
                    .FirstOrDefault(x => x.Codec == targetCodec);

                if (codec == null)
                {
                    return false;
                }
                bool hasValidFooter = FileHelpers.HasValidFooter(file, currentEncoder);
                if (hasValidFooter)
                {
                    SetAsConvertedByMetadata(file);
                }
                return hasValidFooter;
            }
            catch (Exception ex)
            {
                Log(ex, "Bad file - " + file.Name);
                if (markBadAsCompleted)
                {
                    SetAsConvertedByMetadata(file);
                }
                return true;
            }
        }

        private HashSet<string> InitializeConvertedHashes()
        {
            HashSet<string> _convertedHashes = new HashSet<string>();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), convertedHashesFolder);
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
            if (MediaTypes.Video.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return MediaTypes.Video.AsEnumerable();
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return MediaTypes.Audio.AsEnumerable();
            }
            throw new NotSupportedException("Output media type is not supported: " + outputFormat);
        }

        #endregion

        #region Media actions

        public async Task ConvertFilesAsync(int limit = -1, CancellationToken token = default)
        {
            DeleteTempFiles();
            string currentDirectory = string.Empty;
            Log("Supported input media types: {0}", string.Join(", ", inputFormats));
            var inputFiles = inputCache.Count > 0 ? inputCache : GetInputFilesLazy();
            foreach (var inputFile in inputFiles)
            {
                if (currentDirectory != inputFile.DirectoryName)
                {
                    currentDirectory = inputFile.DirectoryName;
                    Log("Current directory: {0}", currentDirectory.Replace(inputDirectory.FullName, string.Empty));
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
            string counter = inputCache.Count > 0 ? $" ({processedCounter + 1}/{inputCache.Count})" : string.Empty;
            Log("File: {0}{1}", inputFile.Name, counter);
            FileInfo temp = FileHelpers.GetTempFile(outputFormat, convertedHashesFolder);
            var snippet = await Xabe.FFmpeg.FFmpeg.Conversions.FromSnippet.Convert(inputFile.FullName, temp.FullName);
            snippet.OnProgress += Snippet_OnProgress;
            if (ignoreErrors)
            {
                snippet.AddParameter("-err_detect ignore_err", Xabe.FFmpeg.ParameterPosition.PreInput);
            }
            Xabe.FFmpeg.IConversionResult result = await snippet.Start(token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            FileHelpers.Move(temp, inputFile);
            long newSize = temp.Length;
            OnItemProcessed(inputFile, sw.Elapsed, newSize);
        }

        private void CheckLibraries()
        {
            bool ffmpegExists = File.Exists("ffmpeg.exe");
            bool ffprobeExists = File.Exists("ffprobe.exe");
            if (!ffmpegExists || !ffprobeExists)
            {
                return;
            }
            Xabe.FFmpeg.FFmpeg.SetExecutablesPath(Environment.CurrentDirectory);
        }

        #endregion

        #region Logging

        private void Log(Exception exception, string caption)
        {
            errorCounter++;
            LogOutput?.Invoke(this, string.Format("[ERROR] {0} - {1} ({2})", DateTime.Now, caption, exception.Message));
        }

        private void Log(string message)
        {
            LogOutput?.Invoke(this, string.Format("[INFO] {0} - {1}", DateTime.Now, message));
        }

        private void Log(string message, params object[] args)
        {
            LogOutput?.Invoke(this, string.Format("[INFO] {0} - {1}", DateTime.Now, string.Format(message, args)));
        }

        #endregion

        #region Hooks

        private void OnWorkCompleted()
        {
            Log("Done. Processed {0} files. Compressed {1} MBytes. Errors: {2}. Elapsed: {3}", processedCounter, compressedBytes / 1024 / 1024, errorCounter, totalElapsed);
        }

        private void OnItemProcessed(FileInfo inputFile, TimeSpan elapsed, long newSize)
        {
            processedCounter++;
            long compressed = inputFile.Length - newSize;
            compressedBytes += compressed;
            totalElapsed += elapsed;
            long oldSizeMb = inputFile.Length / 1024 / 1024;
            long newSizeMb = newSize / 1024 / 1024;
            int compressionRate = (int)(compressed * 100 / inputFile.Length);
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