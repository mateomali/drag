using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FileSender.Network
{
    internal enum CrocSessionState
    {
        Idle,
        WaitingForPeer,
        PeerConnected,
        Transferring,
        Completed,
        Failed,
        Cancelled
    }

    internal sealed class CrocTransferService
    {
        private Process _process;
        private bool _cancelRequested;

        public bool IsRunning
        {
            get { return _process != null && !_process.HasExited; }
        }

        public event Action<string> OutputReceived;
        public event Action<CrocProgress> ProgressChanged;
        public event Action<CrocSessionState, string> StateChanged;
        public event Action<int> Exited;

        public string CrocPath
        {
            get
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string fileName = Environment.Is64BitOperatingSystem ? "croc-win7-x64.exe" : "croc-win7-x86.exe";
                return Path.Combine(baseDirectory, "Tools", fileName);
            }
        }

        public Task SendAsync(IEnumerable<string> paths, string code)
        {
            var arguments = new StringBuilder();
            arguments.Append("--yes --disable-clipboard send --code ");
            arguments.Append(Quote(code));
            foreach (string path in paths)
            {
                arguments.Append(" ");
                arguments.Append(Quote(path));
            }
            return RunAsync(arguments.ToString(), null);
        }

        public Task ReceiveAsync(string code, string outputDirectory)
        {
            string arguments = "--yes --out " + Quote(outputDirectory) + " " + Quote(code);
            return RunAsync(arguments, outputDirectory);
        }

        public void Cancel()
        {
            try
            {
                _cancelRequested = true;
                if (IsRunning) _process.Kill();
            }
            catch
            {
            }
        }

        public static string GenerateCode()
        {
            const string alphabet = "abcdefghijkmnopqrstuvwxyz23456789";
            var random = new Random();
            var builder = new StringBuilder("fs-");
            for (int i = 0; i < 10; i++)
            {
                builder.Append(alphabet[random.Next(alphabet.Length)]);
            }
            return builder.ToString();
        }

        private Task RunAsync(string arguments, string workingDirectory)
        {
            if (IsRunning) throw new InvalidOperationException("Ya hay una transferencia simple en ejecución.");
            if (!File.Exists(CrocPath)) throw new FileNotFoundException("No se encontró croc.exe.", CrocPath);

            var tcs = new TaskCompletionSource<int>();
            _cancelRequested = false;
            RaiseState(CrocSessionState.WaitingForPeer, "Esperando a que la otra PC use el mismo código.");
            _process = new Process();
            _process.StartInfo = new ProcessStartInfo
            {
                FileName = CrocPath,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? AppDomain.CurrentDomain.BaseDirectory : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += (s, e) => RaiseOutput(e.Data);
            _process.ErrorDataReceived += (s, e) => RaiseOutput(e.Data);
            _process.Exited += (s, e) =>
            {
                int code = _process.ExitCode;
                if (_cancelRequested)
                {
                    RaiseState(CrocSessionState.Cancelled, "Transferencia cancelada.");
                }
                else if (code == 0)
                {
                    RaiseState(CrocSessionState.Completed, "Transferencia finalizada.");
                }
                else
                {
                    RaiseState(CrocSessionState.Failed, "Transferencia finalizó con error.");
                }
                if (Exited != null) Exited(code);
                tcs.TrySetResult(code);
            };

            _process.Start();
            Task.Run(() => ReadStreamLoop(_process.StandardOutput));
            Task.Run(() => ReadStreamLoop(_process.StandardError));
            return tcs.Task;
        }

        private void ReadStreamLoop(TextReader reader)
        {
            var builder = new StringBuilder();
            var buffer = new char[256];
            while (true)
            {
                int count;
                try
                {
                    count = reader.Read(buffer, 0, buffer.Length);
                }
                catch
                {
                    return;
                }
                if (count <= 0)
                {
                    FlushOutput(builder);
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    char ch = buffer[i];
                    if (ch == '\r' || ch == '\n')
                    {
                        FlushOutput(builder);
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                }
            }
        }

        private void FlushOutput(StringBuilder builder)
        {
            if (builder.Length == 0) return;
            string line = builder.ToString();
            builder.Length = 0;
            RaiseOutput(line);
        }

        private void RaiseOutput(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            UpdateStateFromOutput(line);
            UpdateProgressFromOutput(line);
            if (OutputReceived != null)
            {
                OutputReceived(line);
            }
        }

        private void UpdateStateFromOutput(string line)
        {
            string value = line.ToLowerInvariant();
            if (value.Contains("room") && value.Contains("not ready"))
            {
                RaiseState(CrocSessionState.WaitingForPeer, "La otra PC todavía no está lista o se desconectó. Reintentando con el mismo código.");
            }
            else if (value.Contains("waiting") || value.Contains("code is") || value.Contains("receive code"))
            {
                RaiseState(CrocSessionState.WaitingForPeer, "Código listo. Esperando contraparte.");
            }
            else if (value.Contains("connecting"))
            {
                RaiseState(CrocSessionState.WaitingForPeer, "Buscando la otra PC con este código.");
            }
            else if (value.Contains("securing channel"))
            {
                RaiseState(CrocSessionState.PeerConnected, "La otra PC respondió. Asegurando el canal cifrado.");
            }
            else if (value.Contains("connected") || value.Contains("recipient") || value.Contains("sender"))
            {
                RaiseState(CrocSessionState.PeerConnected, "Enlace activo. El otro equipo respondió.");
            }
            else if (value.Contains("sending") || value.Contains("receiving") || value.Contains("%") || value.Contains("eta") || value.Contains("mb/s") || value.Contains("kb/s"))
            {
                RaiseState(CrocSessionState.Transferring, "Transferencia en curso.");
            }
        }

        private void RaiseState(CrocSessionState state, string message)
        {
            if (StateChanged != null) StateChanged(state, message);
        }

        private void UpdateProgressFromOutput(string line)
        {
            var progress = CrocProgress.TryParse(line);
            if (progress != null && ProgressChanged != null)
            {
                ProgressChanged(progress);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class CrocProgress
    {
        public bool HasPercent { get; set; }
        public int Percent { get; set; }
        public string Speed { get; set; }
        public string Eta { get; set; }
        public string RawLine { get; set; }

        public static CrocProgress TryParse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            var progress = new CrocProgress { RawLine = line, Speed = "", Eta = "" };
            Match percent = Regex.Match(line, @"(\d{1,3})(?:\.\d+)?\s*%");
            if (percent.Success)
            {
                int value;
                if (int.TryParse(percent.Groups[1].Value, out value))
                {
                    progress.HasPercent = true;
                    progress.Percent = Math.Max(0, Math.Min(100, value));
                }
            }

            Match speed = Regex.Match(line, @"(\d+(?:\.\d+)?\s*(?:B|KB|MB|GB|TB|KiB|MiB|GiB|TiB)/s)", RegexOptions.IgnoreCase);
            if (speed.Success) progress.Speed = speed.Groups[1].Value;

            Match eta = Regex.Match(line, @"(?:ETA|eta)\s*[: ]\s*([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?|[0-9]+[smhd])", RegexOptions.IgnoreCase);
            if (eta.Success) progress.Eta = eta.Groups[1].Value;

            if (!progress.HasPercent && string.IsNullOrEmpty(progress.Speed) && string.IsNullOrEmpty(progress.Eta))
            {
                return null;
            }
            return progress;
        }
    }
}
