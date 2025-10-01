using System.Collections.Generic;

namespace VpkLib;

public class VpkDirectoryEntry
{
	public string PathFileName { get; set; }
	public uint Crc { get; set; }
	public ushort PreloadBytes { get; set; }
	public long PreloadBytesOffset { get; set; }
	public ushort ArchiveIndex { get; set; }
	public uint EntryOffset { get; set; }
	public uint EntryLength { get; set; }

	// Titanfall fields
	public List<VpkTitanfallEntryBlock> TitanfallEntryBlocks { get; set; }

	public uint TotalLength => PreloadBytes + EntryLength;

	// 0x7FFF means data is in the _dir.vpk file
	public bool IsInDirectory => ArchiveIndex == 0x7FFF;
}

public class VpkTitanfallEntryBlock
{
	public uint EntryFlags { get; set; }
	public ushort TextureFlags { get; set; }
	public long Offset { get; set; }
	public ulong CompressedSize { get; set; }
	public ulong UncompressedSize { get; set; }
}

