using CommandLine;
using MediaConverter.Core;

namespace MediaConverter.ConsoleClient
{
    public class Program
    {
        public static async Task Main(params string[] args)
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
            var cc = new ConverterCore(options.InputDirectory, options.OutputFormat!, options.IgnoreErrors);
            cc.SetMarkBadAsCompleted(options.MarkBadAsCompleted);
            if (options.ResetCache)
            {
                cc.ResetCache();
            }
            cc.LogOutput += (sender, e) => Console.WriteLine(e);
            if (options.CalculateCount || options.ScanOnly)
            {
                await cc.FindInputFilesAsync();
            }
            if (!options.ScanOnly)
            {
                CancellationTokenSource cancellationTokenSource = new();
                Console.CancelKeyPress += (sender, args) => { cancellationTokenSource.Cancel(); Thread.Sleep(3000); };
                await cc.ConvertFilesAsync(options.Limit, cancellationTokenSource.Token);
            }
        }
    }
}