using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileSender.Network;

namespace FileSender.Models
{
    internal sealed class AppSettings
    {
        public int TcpPort { get; set; }
        public string SharedKey { get; set; }
        public string ReceiveFolder { get; set; }
        public string LocalStartFolder { get; set; }

        public static string SettingsPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FileSender.settings"); }
        }

        public static AppSettings Default()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return new AppSettings
            {
                TcpPort = FileTransferProtocol.DefaultTcpPort,
                SharedKey = "admin",
                ReceiveFolder = desktop,
                LocalStartFolder = desktop
            };
        }

        public static AppSettings Load()
        {
            AppSettings settings = Default();
            if (!File.Exists(SettingsPath)) return settings;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
            {
                int index = line.IndexOf('=');
                if (index <= 0) continue;
                values[line.Substring(0, index)] = line.Substring(index + 1);
            }

            int port;
            if (values.ContainsKey("TcpPort") && int.TryParse(values["TcpPort"], out port) && port >= 1 && port <= 65535)
            {
                settings.TcpPort = port;
            }
            if (values.ContainsKey("SharedKey")) settings.SharedKey = values["SharedKey"];
            if (values.ContainsKey("ReceiveFolder") && Directory.Exists(values["ReceiveFolder"])) settings.ReceiveFolder = values["ReceiveFolder"];
            if (values.ContainsKey("LocalStartFolder") && Directory.Exists(values["LocalStartFolder"])) settings.LocalStartFolder = values["LocalStartFolder"];
            return settings;
        }

        public void Save()
        {
            var lines = new[]
            {
                "TcpPort=" + TcpPort,
                "SharedKey=" + (SharedKey ?? ""),
                "ReceiveFolder=" + (ReceiveFolder ?? ""),
                "LocalStartFolder=" + (LocalStartFolder ?? "")
            };
            File.WriteAllLines(SettingsPath, lines, Encoding.UTF8);
        }
    }
}
