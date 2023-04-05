using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

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

        public static bool HasValidFooter(FileInfo file, string encoderName)
        {
            using var fs = file.OpenRead();
            fs.Seek(0 - encoderName.Length - byte.MaxValue, SeekOrigin.End);
            byte[] footerBytes = new byte[encoderName.Length + byte.MaxValue];
            fs.Read(footerBytes, 0, footerBytes.Length);
            string footer = Encoding.ASCII.GetString(footerBytes);
            return footer.Contains(encoderName);
        }

        public static FileInfo GetTempFile(string outputFormat, string folder)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder,
                Guid.NewGuid().ToString() + '.' + outputFormat);
            return new FileInfo(path);
        }

        public static void Move(FileInfo temp, FileInfo inputFile)
        {
            string newExtension = temp.Extension;
            int index = inputFile.FullName.LastIndexOf(inputFile.Extension);
            string newPath = inputFile.FullName[0..index] + newExtension;
            File.Delete(inputFile.FullName);
            temp.MoveTo(newPath);
        }
    }
}