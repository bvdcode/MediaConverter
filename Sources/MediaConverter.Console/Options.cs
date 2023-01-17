using CommandLine;

namespace MediaConverter.ConsoleClient
{
    internal partial class Program
    {
        public class Options
        {
            [Option('c', "count", Required = false, HelpText = "Calculate input files count.")]
            public bool CalculateCount { get; set; }

            [Option('i', "input", Required = false, HelpText = "Input directory path, if not specified - using current directory.")]
            public string? InputDirectory { get; set; }

            [Option('f', "format", Required = true, HelpText = "Calculate input files count.")]
            public string? OutputFormat { get; set; }

            [Option('r', "reset", Required = false, HelpText = "Flush compressed file hashes from cache.")]
            public bool ResetCache { get; set; }

            [Option('m', "mark-bad-as-completed", Required = false, HelpText = "Mark bad files as completed.")]
            public bool MarkBadAsCompleted { get; set; }

            [Option('s', "scan-only", Required = false, HelpText = "Scan only (no convert).")]
            public bool ScanOnly { get; set; }


            [Option('l', "limit", Required = false, HelpText = "Limit files for converting.")]
            public int Limit { get; set; }
        }
    }
}