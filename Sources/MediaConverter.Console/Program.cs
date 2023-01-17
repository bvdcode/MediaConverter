using CommandLine;
using MediaConverter.Core;

namespace MediaConverter.ConsoleClient
{
    internal class Program
    {
        static async Task Main(params string[] args)
        {
            await Parser.Default
                .ParseArguments<Options>(args)
                .WithParsedAsync(StartApplicationAsync);
        }

        private static async Task StartApplicationAsync(Options options)
        {
            if (string.IsNullOrWhiteSpace(options.InputDirectory))
            {
                options.InputDirectory = Environment.CurrentDirectory;
            }
            var cc = new ConverterCore(options.InputDirectory, options.OutputFormat!);
            cc.LogOutput += (sender, e) => Console.WriteLine(e);
            if (options.CalculateCount)
            {
                await cc.FindInputFilesAsync();
            }
            await cc.ConvertFilesAsync(options.Threads);
        }

        public class Options
        {
            [Option('c', "count", Required = false, HelpText = "Calculate input files count.")]
            public bool CalculateCount { get; set; }

            [Option('i', "input", Required = false, HelpText = "Input directory path, if not specified - using current directory.")]
            public string? InputDirectory { get; set; }

            [Option('f', "format", Required = true, HelpText = "Calculate input files count.")]
            public string? OutputFormat { get; set; }

            [Option('t', "threads", Required = false, Min = 1, HelpText = "Threads count, by default - processor threads count.")]
            public int Threads { get; set; }
        }
    }
}