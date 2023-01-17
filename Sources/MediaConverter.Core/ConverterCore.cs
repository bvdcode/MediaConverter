using System;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;

namespace MediaConverter.Core
{
    public class ConverterCore
    {
        public event EventHandler<string>? LogOutput;
        private readonly string inputDirectory;
        private readonly string outputFormat;

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

            if (!Directory.Exists(inputDirectory))
            {
                throw new DirectoryNotFoundException(inputDirectory);
            }

            this.inputDirectory = inputDirectory;
            this.outputFormat = outputFormat;
        }

        public Task ConvertFilesAsync()
        {
            return ConvertFilesAsync(Environment.ProcessorCount);
        }

        public async Task ConvertFilesAsync(int threads)
        {
            await Task.Delay(10000);
        }

        public async Task FindInputFilesAsync()
        {
            var info = await Xabe.FFmpeg.FFmpeg.GetMediaInfo("");
            
        }

        private void Log(Exception exception)
        {
            LogOutput?.Invoke(this, string.Format("[ERROR] {0} - {1}", DateTime.Now, exception.Message));
        }

        private void Log(string message)
        {
            LogOutput?.Invoke(this, string.Format("[INFO] {0} - {1}", DateTime.Now, message));
        }
    }
}
