﻿using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MediaConverter.Core
{
    public static class WinApi
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]

        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;

            [MarshalAs(UnmanagedType.U4)]
            public int wFunc;
            public string pFrom;
            public string pTo;
            public short fFlags;

            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const int FO_DELETE = 0x0003;
        private const int FOF_ALLOWUNDO = 0x0040; // Preserve undo information, if possible.
        private const int FOF_NOCONFIRMATION = 0x0010; // Show no confirmation dialog box to the user

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]

        static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        static bool DeleteFileOrFolder(string path)
        {
            SHFILEOPSTRUCT fileop = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
            };
            var rc = SHFileOperation(ref fileop);
            return rc == 0;
        }

        public static bool ToRecycleBin(this DirectoryInfo dir)
        {
            dir?.Refresh();
            if (dir is null || !dir.Exists)
            {
                return false;
            }
            else
            {
                return DeleteFileOrFolder(dir.FullName);
            }
        }

        public static bool ToRecycleBin(this FileInfo file)
        {
            file?.Refresh();
            if (file is null || !file.Exists)
            {
                return false;
            }
            return DeleteFileOrFolder(file.FullName);
        }
    }
}
