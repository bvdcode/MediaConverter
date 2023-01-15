using CommandLine;

namespace MediaConverter.ConsoleClient
{
    internal class Program
    {
        static void Main(params string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(async options =>
                {
                    await StartApplicationAsync(options);
                });
        }

        private static async Task StartApplicationAsync(Options options)
        {
            if (options.Version)
            {
                PrintVersion();
                return;
            }
        }

        private static void PrintVersion()
        {
            throw new NotImplementedException();
        }

        public class Options
        {
            [Option('v', "version", Required = false, HelpText = "Show application version.")]
            public bool Version { get; set; }
        }
    }
}