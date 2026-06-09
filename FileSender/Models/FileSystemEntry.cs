using System;

namespace FileSender.Models
{
    public sealed class FileSystemEntry
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }

        public string DisplaySize
        {
            get
            {
                if (IsDirectory) return "<carpeta>";
                return FormatBytes(Size);
            }
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return string.Format("{0:0.##} {1}", value, units[unit]);
        }
    }
}
