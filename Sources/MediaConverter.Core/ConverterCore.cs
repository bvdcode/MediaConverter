using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace MediaConverter.Core
{
    public class ConverterCore
    {
        public event EventHandler<string>? LogOutput;
        private readonly DirectoryInfo inputDirectory;
        private readonly string outputFormat;
        private readonly IEnumerable<string> inputFormats;
        private readonly List<FileInfo> inputCache;
        private int processedCounter = 0;
        private long compressedBytes = 0;
        private int errorCounter = 0;
        private TimeSpan totalElapsed = TimeSpan.Zero;
        private readonly string targetCodec;
        private readonly StreamType streamType;
        private const string currentEncoder = "Lavf58.45.100";
        private const string convertedHashesFolder = "MediaConverter";
        private const string convertedHashesFile = "media_converter_hashes.txt";
        private int skipCounter = 0;
        private int progressCounter = 0;
        private HashSet<string>? convertedHashes;
        private bool markBadAsCompleted = false;

        public ConverterCore(string inputDirectory, string outputFormat)
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
            this.outputFormat = outputFormat.Replace(".", string.Empty).Trim();
            inputCache = new List<FileInfo>();
            inputFormats = DetectInputFormats();
            targetCodec = SetupTargetCodec();
            streamType = SetupStreamType();
        }

        private StreamType SetupStreamType()
        {
            if (MediaTypes.Video.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return StreamType.Video;
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return StreamType.Audio;
            }
            throw new NotSupportedException("Output media type is not supported: " + outputFormat);
        }

        private string SetupTargetCodec()
        {
            if (MediaTypes.Video.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return outputFormat switch
                {
                    MediaTypes.Video.Mpeg4 => VideoCodec.h264.ToString(),
                    MediaTypes.Video.Matroska => VideoCodec.h264.ToString(),
                    MediaTypes.Video.AudioVideoInterleave => VideoCodec.mpeg4.ToString(),
                    MediaTypes.Video.FlashVideo => VideoCodec.flv1.ToString(),
                    MediaTypes.Video.QuickTime => VideoCodec.h264.ToString(),
                    _ => throw new NotSupportedException("Output media type is not supported: " + outputFormat)
                };
            }
            if (MediaTypes.Audio.AsEnumerable().Any(x => x == outputFormat.ToLower()))
            {
                return outputFormat switch
                {
                    MediaTypes.Audio.Mpeg3 => AudioCodec.mp3.ToString(),
                    MediaTypes.Audio.Mpeg4 => AudioCodec.aac.ToString(),
                    MediaTypes.Audio.Waveform => AudioCodec.pcm_s16le.ToString(),
                    MediaTypes.Audio.FreeLossless => AudioCodec.flac.ToString(),
                    MediaTypes.Audio.Ogging => AudioCodec.vorbis.ToString(),
                    _ => throw new NotSupportedException("Output media type is not supported: " + outputFormat)
                };
            }
            throw new NotSupportedException("Output media type is not supported: " + outputFormat);
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

        public IEnumerable<FileInfo> GetInputFiles()
        {
            Log("Supported input media types: {0}", string.Join(", ", inputFormats));
            if (inputCache.Count > 0)
            {
                return inputCache;
            }

            return GetInputFilesLazy();
        }

        private IEnumerable<FileInfo> GetInputFilesLazy()
        {
            var allFiles = GetFiles(inputDirectory, inputFormats);
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

        private static IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, IEnumerable<string> extensions)
        {
            List<FileInfo> result = new List<FileInfo>();
            foreach (var ext in extensions)
            {
                var found = directory.GetFiles($"*.{ext}", SearchOption.AllDirectories);
                result.AddRange(found);
            }
            return result;
        }

        public async Task ConvertFilesAsync(int limit = -1, CancellationToken token = default)
        {
            DeleteTempFiles();
            string currentDirectory = string.Empty;
            foreach (var inputFile in GetInputFiles())
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

        private void DeleteTempFiles()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), convertedHashesFolder);
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
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

        public async Task FindInputFilesAsync()
        {
            await Task.Run(() => inputCache.AddRange(GetInputFilesLazy()));
            Log("Found supported input files: {0}", inputCache.Count);
        }

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

        private bool IsConverted(FileInfo file)
        {
            if (IsConvertedByMetadata(file))
            {
                return true;
            }
            try
            {
                var mediaInfo = FFmpeg.GetMediaInfo(file.FullName).Result;
                IStream codec = mediaInfo.Streams
                    .Where(x => x.StreamType == streamType)
                    .FirstOrDefault(x => x.Codec == targetCodec);

                if (codec == null)
                {
                    return false;
                }
                bool hasValidFooter = HasValidFooter(file, currentEncoder);
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

        private string SHA512(string text)
        {
            using var sha = new SHA256Managed();
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            byte[] hashBytes = sha.ComputeHash(textBytes);
            string hash = BitConverter
                .ToString(hashBytes)
                .Replace("-", string.Empty);
            return hash;
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
            foreach ( string line in lines )
            {
                if (_convertedHashes.Contains(line))
                {
                    continue;
                }
                _convertedHashes.Add(line);
            }
            return _convertedHashes;
        }

        private bool HasValidFooter(FileInfo file, string encoderName)
        {
            using var fs = file.OpenRead();
            fs.Seek(0 - encoderName.Length - byte.MaxValue, SeekOrigin.End);
            byte[] footerBytes = new byte[encoderName.Length + byte.MaxValue];
            fs.Read(footerBytes, 0, footerBytes.Length);
            string footer = Encoding.ASCII.GetString(footerBytes);
            return footer.Contains(encoderName);
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

        private FileInfo GetTempFile(string outputFormat)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), convertedHashesFolder, 
                Guid.NewGuid().ToString() + '.' + outputFormat);
            return new FileInfo(path);
        }

        public void SetMarkBadAsCompleted(bool markBadAsCompleted)
        {
            this.markBadAsCompleted = markBadAsCompleted;
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

        private async Task ConvertMediaAsync(FileInfo inputFile, CancellationToken token = default)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string counter = inputCache.Count > 0 ? $" ({processedCounter + 1}/{inputCache.Count})" : string.Empty;
            Log("File: {0}{1}", inputFile.Name, counter);
            FileInfo temp = GetTempFile(outputFormat);
            var snippet = await FFmpeg.Conversions.FromSnippet.Convert(inputFile.FullName, temp.FullName);
            snippet.OnProgress += Snippet_OnProgress;
            IConversionResult result = await snippet.Start(token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            Move(temp, inputFile);
            long newSize = temp.Length;
            OnItemProcessed(inputFile, sw.Elapsed, newSize);
        }

        private void Move(FileInfo temp, FileInfo inputFile)
        {
            string newExtension = temp.Extension;
            int index = inputFile.FullName.LastIndexOf(inputFile.Extension);
            string newPath = inputFile.FullName[0..index] + newExtension;
            File.Delete(inputFile.FullName);
            temp.MoveTo(newPath);
        }
    }
}