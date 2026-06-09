using System;
using System.Collections.Generic;
using System.IO;
using FileSender.Models;

namespace FileSender.Network
{
    internal enum MessageType : byte
    {
        Hello = 1,
        HelloResult = 2,
        ListRequest = 3,
        ListResponse = 4,
        TransferOffer = 5,
        TransferDecision = 6,
        FileChunk = 7,
        TransferComplete = 8,
        Error = 9,
        SendRequest = 10
    }

    internal enum ConflictAction : byte
    {
        Overwrite = 1,
        Rename = 2,
        Skip = 3
    }

    internal sealed class ListResponse
    {
        public string Path { get; set; }
        public List<FileSystemEntry> Entries { get; private set; }

        public ListResponse()
        {
            Entries = new List<FileSystemEntry>();
        }
    }

    internal sealed class TransferOffer
    {
        public Guid TransferId { get; set; }
        public string RelativePath { get; set; }
        public long Length { get; set; }
        public bool IsDirectory { get; set; }
        public string DestinationDirectory { get; set; }
    }

    internal sealed class TransferDecision
    {
        public Guid TransferId { get; set; }
        public ConflictAction Action { get; set; }
        public string DestinationPath { get; set; }
    }

    internal static class FileTransferProtocol
    {
        public const int DefaultTcpPort = 50505;
        public const int DiscoveryPort = 50506;
        public const int Version = 1;
        public const string Magic = "FILE_SENDER";
        public const int ChunkSize = 1024 * 256;

        public static void WriteHello(BinaryWriter writer, string key, string peerName)
        {
            writer.Write((byte)MessageType.Hello);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(key ?? "");
            writer.Write(peerName ?? "");
        }

        public static void WriteHelloResult(BinaryWriter writer, bool accepted, string message, string peerName)
        {
            writer.Write((byte)MessageType.HelloResult);
            writer.Write(accepted);
            writer.Write(message ?? "");
            writer.Write(peerName ?? "");
        }

        public static void WriteListRequest(BinaryWriter writer, string path)
        {
            writer.Write((byte)MessageType.ListRequest);
            writer.Write(path ?? "");
        }

        public static void WriteListResponse(BinaryWriter writer, ListResponse response)
        {
            writer.Write((byte)MessageType.ListResponse);
            writer.Write(response.Path ?? "");
            writer.Write(response.Entries.Count);
            foreach (FileSystemEntry entry in response.Entries)
            {
                writer.Write(entry.Name ?? "");
                writer.Write(entry.FullPath ?? "");
                writer.Write(entry.IsDirectory);
                writer.Write(entry.Size);
            }
        }

        public static void WriteTransferOffer(BinaryWriter writer, TransferOffer offer)
        {
            writer.Write((byte)MessageType.TransferOffer);
            writer.Write(offer.TransferId.ToByteArray());
            writer.Write(offer.RelativePath ?? "");
            writer.Write(offer.Length);
            writer.Write(offer.IsDirectory);
            writer.Write(offer.DestinationDirectory ?? "");
        }

        public static void WriteTransferDecision(BinaryWriter writer, TransferDecision decision)
        {
            writer.Write((byte)MessageType.TransferDecision);
            writer.Write(decision.TransferId.ToByteArray());
            writer.Write((byte)decision.Action);
            writer.Write(decision.DestinationPath ?? "");
        }

        public static void WriteFileChunk(BinaryWriter writer, Guid transferId, byte[] buffer, int count)
        {
            writer.Write((byte)MessageType.FileChunk);
            writer.Write(transferId.ToByteArray());
            writer.Write(count);
            writer.Write(buffer, 0, count);
        }

        public static void WriteTransferComplete(BinaryWriter writer, Guid transferId)
        {
            writer.Write((byte)MessageType.TransferComplete);
            writer.Write(transferId.ToByteArray());
        }

        public static void WriteError(BinaryWriter writer, string message)
        {
            writer.Write((byte)MessageType.Error);
            writer.Write(message ?? "");
        }

        public static void WriteSendRequest(BinaryWriter writer, IEnumerable<string> paths, string destinationDirectory)
        {
            writer.Write((byte)MessageType.SendRequest);
            writer.Write(destinationDirectory ?? "");
            var list = new List<string>(paths ?? new string[0]);
            writer.Write(list.Count);
            foreach (string path in list)
            {
                writer.Write(path ?? "");
            }
        }

        public static MessageType ReadMessageType(BinaryReader reader)
        {
            return (MessageType)reader.ReadByte();
        }

        public static string ReadStringValue(BinaryReader reader)
        {
            return reader.ReadString();
        }

        public static ListResponse ReadListResponse(BinaryReader reader)
        {
            var response = new ListResponse();
            response.Path = reader.ReadString();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                response.Entries.Add(new FileSystemEntry
                {
                    Name = reader.ReadString(),
                    FullPath = reader.ReadString(),
                    IsDirectory = reader.ReadBoolean(),
                    Size = reader.ReadInt64()
                });
            }
            return response;
        }

        public static TransferOffer ReadTransferOffer(BinaryReader reader)
        {
            return new TransferOffer
            {
                TransferId = new Guid(reader.ReadBytes(16)),
                RelativePath = reader.ReadString(),
                Length = reader.ReadInt64(),
                IsDirectory = reader.ReadBoolean(),
                DestinationDirectory = reader.ReadString()
            };
        }

        public static TransferDecision ReadTransferDecision(BinaryReader reader)
        {
            return new TransferDecision
            {
                TransferId = new Guid(reader.ReadBytes(16)),
                Action = (ConflictAction)reader.ReadByte(),
                DestinationPath = reader.ReadString()
            };
        }

        public static void ReadSendRequest(BinaryReader reader, out List<string> paths, out string destinationDirectory)
        {
            destinationDirectory = reader.ReadString();
            int count = reader.ReadInt32();
            paths = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string path = reader.ReadString();
                if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
            }
        }
    }
}
