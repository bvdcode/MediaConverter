using CommandLine;

namespace MediaConverter.ConsoleClient
{
    public class Options
    {
        [Option('c', "count", Required = false, HelpText = "Calculate input files count.")]
        public bool CalculateCount { get; set; }

        [Option('i', "input", Required = false, HelpText = "Input directory path, if not specified - using current directory.")]
        public string? InputDirectory { get; set; }

        [Option('f', "format", Required = true, HelpText = "Output format. Input files will be found by this type (Audio or Video).")]
        public string OutputFormat { get; set; } = string.Empty;

        [Option('r', "reset", Required = false, HelpText = "Flush compressed file hashes from cache.")]
        public bool ResetCache { get; set; }

        [Option('m', "mark-bad-as-completed", Required = false, HelpText = "Mark bad files or non-convertable files as completed.")]
        public bool MarkBadAsCompleted { get; set; }

        [Option('s', "scan-only", Required = false, HelpText = "Scan only (no convert).")]
        public bool ScanOnly { get; set; }

        [Option('l', "limit", Required = false, HelpText = "Limit files for converting.")]
        public int Limit { get; set; }

        [Option('e', "ignore-errors", Required = false, HelpText = "Ignore errors in source stream.")]
        public bool IgnoreErrors { get; set; }

        [Option('x', "check-codec", Required = false, HelpText = "Check existing codec of target file.")]
        public bool CheckCodec { get; set; }

        [Option('p', "export", Required = false, HelpText = "Export saved hashes.")]
        public bool Export { get; set; }

        [Option('h', "check-footer", Required = false, HelpText = "Check codec footer.")]
        public bool CheckFooter { get; set; }

        [Option("copy-codec", Required = false, HelpText = "Add '-c copy' argument.")]
        public bool CopyCodec { get; set; }
    }
}