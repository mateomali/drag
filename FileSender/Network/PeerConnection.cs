using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FileSender.Models;

namespace FileSender.Network
{
    internal sealed class TransferProgress
    {
        public string FileName { get; set; }
        public string BatchName { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public long CurrentFileBytesTransferred { get; set; }
        public long CurrentFileTotalBytes { get; set; }
        public int CurrentFileIndex { get; set; }
        public int TotalFiles { get; set; }
        public bool IsFolder { get; set; }
        public bool IsAggregate { get; set; }
        public double BytesPerSecond { get; set; }

        public TimeSpan EstimatedRemaining
        {
            get
            {
                if (BytesPerSecond <= 0 || TotalBytes <= BytesTransferred) return TimeSpan.Zero;
                return TimeSpan.FromSeconds((TotalBytes - BytesTransferred) / BytesPerSecond);
            }
        }
    }

    internal sealed class PeerConnection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly string _sharedKey;
        private readonly bool _acceptedClient;
        private readonly object _writeLock = new object();
        private readonly Dictionary<Guid, TaskCompletionSource<TransferDecision>> _pendingDecisions = new Dictionary<Guid, TaskCompletionSource<TransferDecision>>();
        private readonly Dictionary<Guid, FileStream> _incomingFiles = new Dictionary<Guid, FileStream>();
        private readonly Dictionary<Guid, long> _incomingTotals = new Dictionary<Guid, long>();
        private readonly Dictionary<Guid, long> _incomingReceived = new Dictionary<Guid, long>();
        private readonly Dictionary<string, string> _destinationRemaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);

        private BinaryReader _reader;
        private BinaryWriter _writer;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;

        public string PeerName { get; private set; }
        public bool IsConnected { get { return _client != null && _client.Connected; } }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<string> StatusChanged;
        public event Action<ListResponse> RemoteListReceived;
        public event Func<TransferOffer, TransferDecision> TransferDecisionRequested;
        public event Action<TransferProgress> ProgressChanged;

        private PeerConnection(TcpClient client, string sharedKey, bool acceptedClient)
        {
            _client = client;
            _sharedKey = sharedKey;
            _acceptedClient = acceptedClient;
        }

        public static PeerConnection FromAcceptedClient(TcpClient client, string sharedKey)
        {
            return new PeerConnection(client, sharedKey, true);
        }

        public static async Task<PeerConnection> ConnectAsync(string host, int port, string sharedKey)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port);
            var connection = new PeerConnection(client, sharedKey, false);
            connection.Start();
            return connection;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _stream = _client.GetStream();
            _reader = new BinaryReader(_stream);
            _writer = new BinaryWriter(_stream);
            Task.Run(() => ReadLoop(_cts.Token));
            if (!_acceptedClient)
            {
                Send(writer => FileTransferProtocol.WriteHello(writer, _sharedKey, Environment.MachineName));
            }
        }

        public void RequestRemoteList(string path)
        {
            Send(writer => FileTransferProtocol.WriteListRequest(writer, path ?? ""));
        }

        public void RequestSendPaths(IEnumerable<string> paths, string destinationDirectory)
        {
            Send(writer => FileTransferProtocol.WriteSendRequest(writer, paths, destinationDirectory));
        }

        public async Task SendPathsAsync(IEnumerable<string> paths, string destinationDirectory)
        {
            await _sendSemaphore.WaitAsync();
            try
            {
                List<string> pathList = new List<string>(paths ?? new string[0]);
                List<SendItem> items = BuildSendItems(pathList);
                long totalBytes = 0;
                int totalFiles = 0;
                foreach (SendItem item in items)
                {
                    if (item.IsDirectory) continue;
                    totalBytes += item.Length;
                    totalFiles++;
                }

                var context = new SendBatchContext
                {
                    BatchName = BuildBatchName(items),
                    TotalBytes = totalBytes,
                    TotalFiles = totalFiles,
                    IsFolder = ContainsFolder(pathList)
                };
                RaiseProgress(context, context.BatchName, 0, 0, 0, 0, Stopwatch.StartNew());

                await CreateDirectoriesForBatchAsync(items, destinationDirectory);

                var stopwatch = Stopwatch.StartNew();
                if (context.TotalFiles == 0)
                {
                    RaiseProgress(context, context.BatchName, 0, 0, 0, 0, stopwatch);
                    return;
                }

                int fileIndex = 0;
                foreach (SendItem item in items)
                {
                    if (item.IsDirectory) continue;
                    fileIndex++;
                    await SendFileAsync(item, destinationDirectory, context, fileIndex, stopwatch);
                }
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        private async Task CreateDirectoriesForBatchAsync(IEnumerable<SendItem> items, string destinationDirectory)
        {
            var offeredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SendItem item in items)
            {
                string directory = item.IsDirectory ? item.RelativePath : Path.GetDirectoryName(item.RelativePath);
                List<string> directories = new List<string>();
                while (!string.IsNullOrEmpty(directory))
                {
                    directories.Add(directory);
                    directory = Path.GetDirectoryName(directory);
                }

                for (int i = directories.Count - 1; i >= 0; i--)
                {
                    if (offeredDirectories.Add(directories[i]))
                    {
                        await SendDirectoryOfferAsync(directories[i], destinationDirectory);
                    }
                }
            }
        }

        private async Task SendDirectoryOfferAsync(string relativePath, string destinationDirectory)
        {
            var offer = new TransferOffer
            {
                TransferId = Guid.NewGuid(),
                RelativePath = relativePath,
                IsDirectory = true,
                Length = 0,
                DestinationDirectory = destinationDirectory
            };
            TransferDecision decision = await OfferAsync(offer);
            if (decision.Action == ConflictAction.Skip) return;
        }

        private async Task SendFileAsync(SendItem item, string destinationDirectory, SendBatchContext context, int fileIndex, Stopwatch stopwatch)
        {
            var offer = new TransferOffer
            {
                TransferId = Guid.NewGuid(),
                RelativePath = item.RelativePath,
                IsDirectory = false,
                Length = item.Length,
                DestinationDirectory = destinationDirectory
            };

            TransferDecision decision = await OfferAsync(offer);
            if (decision.Action == ConflictAction.Skip)
            {
                RaiseProgress(context, item.RelativePath, context.BytesTransferred, item.Length, item.Length, fileIndex, stopwatch);
                return;
            }

            long sent = 0;
            byte[] buffer = new byte[FileTransferProtocol.ChunkSize];
            using (FileStream input = File.OpenRead(item.FullPath))
            {
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    Send(writer => FileTransferProtocol.WriteFileChunk(writer, offer.TransferId, buffer, read));
                    sent += read;
                    RaiseProgress(context, item.RelativePath, context.BytesTransferred + sent, sent, item.Length, fileIndex, stopwatch);
                }
            }
            Send(writer => FileTransferProtocol.WriteTransferComplete(writer, offer.TransferId));
            context.BytesTransferred += item.Length;
            RaiseProgress(context, item.RelativePath, context.BytesTransferred, item.Length, item.Length, fileIndex, stopwatch);
        }

        private static List<SendItem> BuildSendItems(IEnumerable<string> paths)
        {
            var items = new List<SendItem>();
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    FileInfo file = new FileInfo(path);
                    items.Add(new SendItem
                    {
                        FullPath = path,
                        RelativePath = Path.GetFileName(path),
                        Length = file.Length,
                        IsDirectory = false
                    });
                }
                else if (Directory.Exists(path))
                {
                    string rootName = new DirectoryInfo(path).Name;
                    AddDirectoryItems(items, path, rootName);
                }
            }
            return items;
        }

        private static void AddDirectoryItems(List<SendItem> items, string directory, string relativePath)
        {
            items.Add(new SendItem
            {
                FullPath = directory,
                RelativePath = relativePath,
                Length = 0,
                IsDirectory = true
            });

            foreach (string childDirectory in Directory.GetDirectories(directory))
            {
                AddDirectoryItems(items, childDirectory, Path.Combine(relativePath, Path.GetFileName(childDirectory)));
            }

            foreach (string filePath in Directory.GetFiles(directory))
            {
                FileInfo file = new FileInfo(filePath);
                items.Add(new SendItem
                {
                    FullPath = filePath,
                    RelativePath = Path.Combine(relativePath, Path.GetFileName(filePath)),
                    Length = file.Length,
                    IsDirectory = false
                });
            }
        }

        private static bool ContainsFolder(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (Directory.Exists(path)) return true;
            }
            return false;
        }

        private static string BuildBatchName(List<SendItem> items)
        {
            if (items.Count == 0) return "Transferencia";
            string first = items[0].RelativePath;
            return items.Count == 1 ? first : first + " y " + (items.Count - 1) + " elementos más";
        }

        private Task<TransferDecision> OfferAsync(TransferOffer offer)
        {
            var tcs = new TaskCompletionSource<TransferDecision>();
            lock (_pendingDecisions)
            {
                _pendingDecisions[offer.TransferId] = tcs;
            }
            Send(writer => FileTransferProtocol.WriteTransferOffer(writer, offer));
            return tcs.Task;
        }

        private void ReadLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    MessageType type = FileTransferProtocol.ReadMessageType(_reader);
                    switch (type)
                    {
                        case MessageType.Hello:
                            HandleHello();
                            break;
                        case MessageType.HelloResult:
                            HandleHelloResult();
                            break;
                        case MessageType.ListRequest:
                            HandleListRequest();
                            break;
                        case MessageType.ListResponse:
                            RaiseRemoteList(FileTransferProtocol.ReadListResponse(_reader));
                            break;
                        case MessageType.TransferOffer:
                            HandleTransferOffer(FileTransferProtocol.ReadTransferOffer(_reader));
                            break;
                        case MessageType.TransferDecision:
                            HandleTransferDecision(FileTransferProtocol.ReadTransferDecision(_reader));
                            break;
                        case MessageType.FileChunk:
                            HandleFileChunk();
                            break;
                        case MessageType.TransferComplete:
                            HandleTransferComplete(new Guid(_reader.ReadBytes(16)));
                            break;
                        case MessageType.Error:
                            RaiseStatus(_reader.ReadString());
                            break;
                        case MessageType.SendRequest:
                            HandleSendRequest();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseStatus("Conexión cerrada: " + ex.Message);
            }
            finally
            {
                Dispose();
                if (Disconnected != null) Disconnected();
            }
        }

        private void HandleHello()
        {
            string magic = _reader.ReadString();
            int version = _reader.ReadInt32();
            string key = _reader.ReadString();
            string peerName = _reader.ReadString();
            bool accepted = magic == FileTransferProtocol.Magic && version == FileTransferProtocol.Version && key == _sharedKey;
            if (accepted)
            {
                PeerName = peerName;
                Send(writer => FileTransferProtocol.WriteHelloResult(writer, true, "OK", Environment.MachineName));
                RaiseStatus("Conectado con " + PeerName);
                if (Connected != null) Connected();
            }
            else
            {
                Send(writer => FileTransferProtocol.WriteHelloResult(writer, false, "Clave compartida o protocolo inválido.", Environment.MachineName));
                Dispose();
            }
        }

        private void HandleHelloResult()
        {
            bool accepted = _reader.ReadBoolean();
            string message = _reader.ReadString();
            string peerName = _reader.ReadString();
            if (!accepted)
            {
                RaiseStatus("Conexión rechazada: " + message);
                Dispose();
                return;
            }
            PeerName = peerName;
            RaiseStatus("Conectado con " + PeerName);
            if (Connected != null) Connected();
        }

        private void HandleListRequest()
        {
            string path = _reader.ReadString();
            Send(writer => FileTransferProtocol.WriteListResponse(writer, BuildListResponse(path)));
        }

        private static ListResponse BuildListResponse(string requestedPath)
        {
            string path = requestedPath;
            var response = new ListResponse { Path = path };

            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                    {
                        string label = drive.IsReady ? drive.VolumeLabel : "";
                        string name = string.IsNullOrWhiteSpace(label)
                            ? drive.Name
                            : drive.Name + " " + label;
                        response.Entries.Add(new FileSystemEntry { Name = name, FullPath = drive.Name, IsDirectory = true, Size = 0 });
                    }
                    return response;
                }

                DirectoryInfo directory = new DirectoryInfo(path);
                DirectoryInfo parent = directory.Parent;
                if (parent != null)
                {
                    response.Entries.Add(new FileSystemEntry { Name = "..", FullPath = parent.FullName, IsDirectory = true, Size = 0 });
                }
                else
                {
                    response.Entries.Add(new FileSystemEntry { Name = "..", FullPath = "", IsDirectory = true, Size = 0 });
                }

                foreach (DirectoryInfo child in directory.GetDirectories())
                {
                    response.Entries.Add(new FileSystemEntry { Name = child.Name, FullPath = child.FullName, IsDirectory = true, Size = 0 });
                }
                foreach (FileInfo file in directory.GetFiles())
                {
                    response.Entries.Add(new FileSystemEntry { Name = file.Name, FullPath = file.FullName, IsDirectory = false, Size = file.Length });
                }
            }
            catch
            {
            }

            return response;
        }

        private sealed class SendItem
        {
            public string FullPath { get; set; }
            public string RelativePath { get; set; }
            public long Length { get; set; }
            public bool IsDirectory { get; set; }
        }

        private sealed class SendBatchContext
        {
            public string BatchName { get; set; }
            public long BytesTransferred { get; set; }
            public long TotalBytes { get; set; }
            public int TotalFiles { get; set; }
            public bool IsFolder { get; set; }
        }

        private void HandleSendRequest()
        {
            List<string> paths;
            string destinationDirectory;
            FileTransferProtocol.ReadSendRequest(_reader, out paths, out destinationDirectory);
            Task.Run(async () =>
            {
                try
                {
                    await SendPathsAsync(paths, destinationDirectory);
                }
                catch (Exception ex)
                {
                    RaiseStatus("Error enviando a pedido remoto: " + ex.Message);
                }
            });
        }

        private void HandleTransferOffer(TransferOffer offer)
        {
            TransferDecision decision;
            if (TransferDecisionRequested != null)
            {
                decision = TransferDecisionRequested(offer);
            }
            else
            {
                decision = BuildDefaultDecision(offer, ConflictAction.Rename);
            }

            Send(writer => FileTransferProtocol.WriteTransferDecision(writer, decision));
            if (decision.Action == ConflictAction.Skip) return;

            if (offer.IsDirectory)
            {
                Directory.CreateDirectory(decision.DestinationPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(decision.DestinationPath));
            var output = new FileStream(decision.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            lock (_incomingFiles)
            {
                _incomingFiles[offer.TransferId] = output;
                _incomingTotals[offer.TransferId] = offer.Length;
                _incomingReceived[offer.TransferId] = 0;
            }
        }

        private TransferDecision BuildDefaultDecision(TransferOffer offer, ConflictAction action)
        {
            string originalDestination = ResolveDestinationPath(offer);
            string destination = originalDestination;
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                if (action == ConflictAction.Rename) destination = BuildRenamedPath(destination);
            }
            if (offer.IsDirectory && action != ConflictAction.Skip && destination != originalDestination)
            {
                lock (_destinationRemaps)
                {
                    _destinationRemaps[originalDestination] = destination;
                }
            }
            return new TransferDecision { TransferId = offer.TransferId, Action = action, DestinationPath = destination };
        }

        private string ResolveDestinationPath(TransferOffer offer)
        {
            string directory = string.IsNullOrEmpty(offer.DestinationDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : offer.DestinationDirectory;
            string destination = Path.Combine(directory, offer.RelativePath);
            lock (_destinationRemaps)
            {
                string bestSource = null;
                foreach (string source in _destinationRemaps.Keys)
                {
                    if (destination.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                        destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        if (bestSource == null || source.Length > bestSource.Length) bestSource = source;
                    }
                }
                if (bestSource != null)
                {
                    string suffix = destination.Length == bestSource.Length ? "" : destination.Substring(bestSource.Length).TrimStart(Path.DirectorySeparatorChar);
                    return string.IsNullOrEmpty(suffix) ? _destinationRemaps[bestSource] : Path.Combine(_destinationRemaps[bestSource], suffix);
                }
            }
            return destination;
        }

        public TransferDecision CreateDecision(TransferOffer offer, ConflictAction action)
        {
            return BuildDefaultDecision(offer, action);
        }

        private static string BuildRenamedPath(string path)
        {
            string directory = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            if (Directory.Exists(path)) extension = "";

            int index = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directory, name + " (" + index + ")" + extension);
                index++;
            } while (File.Exists(candidate) || Directory.Exists(candidate));
            return candidate;
        }

        private void HandleTransferDecision(TransferDecision decision)
        {
            TaskCompletionSource<TransferDecision> tcs = null;
            lock (_pendingDecisions)
            {
                if (_pendingDecisions.TryGetValue(decision.TransferId, out tcs))
                {
                    _pendingDecisions.Remove(decision.TransferId);
                }
            }
            if (tcs != null) tcs.SetResult(decision);
        }

        private void HandleFileChunk()
        {
            Guid transferId = new Guid(_reader.ReadBytes(16));
            int length = _reader.ReadInt32();
            byte[] data = _reader.ReadBytes(length);

            FileStream output = null;
            long total = 0;
            long received = 0;
            lock (_incomingFiles)
            {
                if (_incomingFiles.TryGetValue(transferId, out output))
                {
                    output.Write(data, 0, data.Length);
                    _incomingReceived[transferId] += data.Length;
                    received = _incomingReceived[transferId];
                    total = _incomingTotals[transferId];
                }
            }
            if (output != null)
            {
                RaiseProgress("Recibiendo", received, total, null);
            }
        }

        private void HandleTransferComplete(Guid transferId)
        {
            lock (_incomingFiles)
            {
                FileStream output;
                if (_incomingFiles.TryGetValue(transferId, out output))
                {
                    output.Dispose();
                    _incomingFiles.Remove(transferId);
                    _incomingTotals.Remove(transferId);
                    _incomingReceived.Remove(transferId);
                }
            }
            RaiseStatus("Transferencia recibida.");
        }

        private void Send(Action<BinaryWriter> write)
        {
            lock (_writeLock)
            {
                write(_writer);
                _writer.Flush();
            }
        }

        private void RaiseRemoteList(ListResponse response)
        {
            if (RemoteListReceived != null) RemoteListReceived(response);
        }

        private void RaiseStatus(string message)
        {
            if (StatusChanged != null) StatusChanged(message);
        }

        private void RaiseProgress(string fileName, long transferred, long total, Stopwatch stopwatch)
        {
            double speed = 0;
            if (stopwatch != null && stopwatch.Elapsed.TotalSeconds > 0)
            {
                speed = transferred / stopwatch.Elapsed.TotalSeconds;
            }
            if (ProgressChanged != null)
            {
                ProgressChanged(new TransferProgress
                {
                    FileName = fileName,
                    BytesTransferred = transferred,
                    TotalBytes = total,
                    BytesPerSecond = speed
                });
            }
        }

        private void RaiseProgress(SendBatchContext context, string fileName, long totalTransferred, long fileTransferred, long fileTotal, int fileIndex, Stopwatch stopwatch)
        {
            double speed = 0;
            if (stopwatch != null && stopwatch.Elapsed.TotalSeconds > 0)
            {
                speed = totalTransferred / stopwatch.Elapsed.TotalSeconds;
            }
            if (ProgressChanged != null)
            {
                ProgressChanged(new TransferProgress
                {
                    FileName = fileName,
                    BatchName = context.BatchName,
                    BytesTransferred = totalTransferred,
                    TotalBytes = context.TotalBytes,
                    CurrentFileBytesTransferred = fileTransferred,
                    CurrentFileTotalBytes = fileTotal,
                    CurrentFileIndex = fileIndex,
                    TotalFiles = context.TotalFiles,
                    IsFolder = context.IsFolder,
                    IsAggregate = true,
                    BytesPerSecond = speed
                });
            }
        }

        public void Dispose()
        {
            if (_cts != null) _cts.Cancel();
            lock (_incomingFiles)
            {
                foreach (FileStream stream in _incomingFiles.Values)
                {
                    stream.Dispose();
                }
                _incomingFiles.Clear();
            }
            if (_stream != null) _stream.Dispose();
            if (_client != null) _client.Close();
        }
    }
}
