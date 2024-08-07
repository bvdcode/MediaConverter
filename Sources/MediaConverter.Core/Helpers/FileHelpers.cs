using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;

namespace MediaConverter.Core.Helpers
{
    public static class FileHelpers
    {
        public static IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, IEnumerable<string> extensions)
        {
            foreach (var ext in extensions)
            {
                var found = directory.GetFiles($"*.{ext}", SearchOption.AllDirectories);
                foreach (var file in found)
                {
                    yield return file;
                }
            }
        }

        public static bool HasValidFooter(FileInfo file, bool checkEncoder = false)
        {
            // , "Lavf60.16.100", applicationName
            string applicationName = nameof(MediaConverter);
            using var fs = file.OpenRead();
            int nameLength = applicationName.Length;
            int add = ushort.MaxValue;
            fs.Seek(0 - nameLength - add, SeekOrigin.End);
            byte[] footerBytes = new byte[nameLength + add];
            fs.Read(footerBytes, 0, footerBytes.Length);
            string footer = Encoding.ASCII.GetString(footerBytes);
            // Lavf[2 or 3 digits].[2 or 3 digits].[2 or 3 digits]
            const string pattern = @"Lavf\d{2,3}\.\d{2,3}\.\d{2,3}";
            bool hasEncoderFooter = checkEncoder && Regex.IsMatch(footer, pattern);
            return hasEncoderFooter || footer.Contains(applicationName);
        }

        public static FileInfo GetTempFile(string outputFormat, string folder)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder,
                Guid.NewGuid().ToString() + '.' + outputFormat);
            return new FileInfo(path);
        }

        public static void Move(FileInfo from, FileInfo to)
        {
            string newExtension = from.Extension;
            int index = to.FullName.LastIndexOf(to.Extension);
            string newPath = to.FullName[0..index] + newExtension;
            Thread.Sleep(1000);
            if (!to.Exists)
            {
                from.MoveTo(newPath);
                return;
            }
            try
            {
                to.Delete();
            }
            catch (Exception)
            {
                Thread.Sleep(5_000);
                to.Delete();
            }
            from.MoveTo(newPath);
        }
    }
}