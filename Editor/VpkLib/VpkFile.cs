using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VpkLib;

public class VpkFile : IDisposable
{
	private readonly FileStream dirStream;
	private readonly BinaryReader dirReader;
	private readonly Dictionary<ushort, FileStream> archiveStreams = new Dictionary<ushort, FileStream>();
	private readonly Dictionary<string, VpkDirectoryEntry> fileLookup = new Dictionary<string, VpkDirectoryEntry>(StringComparer.OrdinalIgnoreCase);

	public VpkFileData FileData { get; private set; }
	public string DirectoryPath { get; }
	public string BaseName { get; }

	public VpkFile(string directoryVpkPath)
	{
		DirectoryPath = directoryVpkPath;
		
		// Extract base name (e.g., "pak01_dir.vpk" -> "pak01")
		string fileName = Path.GetFileNameWithoutExtension(directoryVpkPath);
		if (fileName.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
		{
			BaseName = fileName.Substring(0, fileName.Length - 4);
		}
		else
		{
			BaseName = fileName;
		}

		dirStream = File.OpenRead(directoryVpkPath);
		dirReader = new BinaryReader(dirStream, Encoding.UTF8);

		FileData = new VpkFileData();
		ReadHeader();
		ReadDirectory();
	}

	private void ReadHeader()
	{
		FileData.Signature = dirReader.ReadUInt32();

		if (!FileData.IsValid)
		{
			return;
		}

		FileData.Version = dirReader.ReadUInt32();
		FileData.DirectoryLength = dirReader.ReadUInt32();

		if (FileData.Version == 2)
		{
			FileData.EmbeddedDataLength = dirReader.ReadUInt32();
			FileData.ArchiveMD5SectionSize = dirReader.ReadUInt32();
			FileData.OtherMD5SectionSize = dirReader.ReadUInt32();
			FileData.SignatureSectionSize = dirReader.ReadUInt32();
		}
		else if (FileData.Version == 196610) // Titanfall
		{
			FileData.TitanfallUnknown = dirReader.ReadUInt32();
		}

		FileData.DirectoryOffset = dirReader.BaseStream.Position;
	}

	private void ReadDirectory()
	{
		if (!FileData.IsValid)
		{
			return;
		}

		dirReader.BaseStream.Seek(FileData.DirectoryOffset, SeekOrigin.Begin);

		while (true)
		{
			string extension = ReadNullTerminatedString();
			if (string.IsNullOrEmpty(extension))
				break;

			while (true)
			{
				string path = ReadNullTerminatedString();
				if (string.IsNullOrEmpty(path))
					break;

				// Replace " " with empty string for root directory
				if (path == " ")
					path = "";

				while (true)
				{
					string fileName = ReadNullTerminatedString();
					if (string.IsNullOrEmpty(fileName))
						break;

					var entry = new VpkDirectoryEntry();
					entry.Crc = dirReader.ReadUInt32();
					entry.PreloadBytes = dirReader.ReadUInt16();
					entry.ArchiveIndex = dirReader.ReadUInt16();

					if (FileData.Version == 196610) // Titanfall
					{
						entry.TitanfallEntryBlocks = new List<VpkTitanfallEntryBlock>();
						entry.EntryLength = 0;

						while (true)
						{
							var block = new VpkTitanfallEntryBlock
							{
								EntryFlags = dirReader.ReadUInt32(),
								TextureFlags = dirReader.ReadUInt16(),
								Offset = dirReader.ReadInt64(),
								CompressedSize = dirReader.ReadUInt64(),
								UncompressedSize = dirReader.ReadUInt64()
							};
							entry.TitanfallEntryBlocks.Add(block);
							entry.EntryLength += (uint)block.UncompressedSize;

							ushort endMarker = dirReader.ReadUInt16();
							if (endMarker == 0xFFFF)
								break;
						}
					}
					else
					{
						entry.EntryOffset = dirReader.ReadUInt32();
						entry.EntryLength = dirReader.ReadUInt32();
						ushort terminator = dirReader.ReadUInt16();

						// Adjust offset if data is in directory file
						if (entry.ArchiveIndex == 0x7FFF)
						{
							entry.EntryOffset += (uint)(FileData.DirectoryOffset + FileData.DirectoryLength);
						}
					}

					// Store preload bytes offset
					if (entry.PreloadBytes > 0)
					{
						entry.PreloadBytesOffset = dirReader.BaseStream.Position;
						dirReader.BaseStream.Position += entry.PreloadBytes;
					}

					// Build full path
					if (string.IsNullOrEmpty(path))
					{
						entry.PathFileName = $"{fileName}.{extension}";
					}
					else
					{
						entry.PathFileName = $"{path}/{fileName}.{extension}";
					}

					FileData.Entries.Add(entry);
					fileLookup[entry.PathFileName] = entry;
				}
			}
		}
	}

	private string ReadNullTerminatedString()
	{
		var bytes = new List<byte>();
		byte b;
		while ((b = dirReader.ReadByte()) != 0)
		{
			bytes.Add(b);
		}
		return bytes.Count > 0 ? Encoding.UTF8.GetString(bytes.ToArray()) : string.Empty;
	}

	public bool FileExists(string path)
	{
		return fileLookup.ContainsKey(path);
	}

	public byte[] GetFileBytes(string path)
	{
		if (!fileLookup.TryGetValue(path, out var entry))
		{
			return null;
		}

		using var ms = new MemoryStream((int)entry.TotalLength);

		// Read preload bytes from directory file
		if (entry.PreloadBytes > 0)
		{
			dirReader.BaseStream.Seek(entry.PreloadBytesOffset, SeekOrigin.Begin);
			byte[] preloadData = dirReader.ReadBytes(entry.PreloadBytes);
			ms.Write(preloadData, 0, preloadData.Length);
		}

		// Read main data
		if (entry.EntryLength > 0)
		{
			FileStream archiveStream = GetArchiveStream(entry.ArchiveIndex);
			if (archiveStream != null)
			{
				archiveStream.Seek(entry.EntryOffset, SeekOrigin.Begin);
				byte[] buffer = new byte[entry.EntryLength];
				archiveStream.Read(buffer, 0, buffer.Length);
				ms.Write(buffer, 0, buffer.Length);
			}
		}

		return ms.ToArray();
	}

	private FileStream GetArchiveStream(ushort archiveIndex)
	{
		// 0x7FFF means data is in the directory file itself
		if (archiveIndex == 0x7FFF)
		{
			return dirStream;
		}

		// Check if we already have this archive open
		if (archiveStreams.TryGetValue(archiveIndex, out var stream))
		{
			return stream;
		}

		// Open the archive file
		string archivePath = Path.Combine(
			Path.GetDirectoryName(DirectoryPath),
			$"{BaseName}_{archiveIndex:D3}.vpk"
		);

		if (File.Exists(archivePath))
		{
			stream = File.OpenRead(archivePath);
			archiveStreams[archiveIndex] = stream;
			return stream;
		}

		return null;
	}

	public void Dispose()
	{
		dirReader?.Dispose();
		dirStream?.Dispose();

		foreach (var stream in archiveStreams.Values)
		{
			stream?.Dispose();
		}
		archiveStreams.Clear();
	}
}

