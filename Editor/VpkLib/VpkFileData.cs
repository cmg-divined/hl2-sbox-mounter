using System.Collections.Generic;

namespace VpkLib;

public class VpkFileData
{
	// VPK signature: 0x55aa1234
	public const uint VPK_SIGNATURE = 0x55aa1234;
	public const uint FPX_SIGNATURE = 0x33ff4132;

	public uint Signature { get; set; }
	public uint Version { get; set; }
	public uint DirectoryLength { get; set; }

	// Version 2+ fields
	public uint EmbeddedDataLength { get; set; }
	public uint ArchiveMD5SectionSize { get; set; }
	public uint OtherMD5SectionSize { get; set; }
	public uint SignatureSectionSize { get; set; }

	// Titanfall (version 196610)
	public uint TitanfallUnknown { get; set; }

	public long DirectoryOffset { get; set; }
	public List<VpkDirectoryEntry> Entries { get; set; } = new List<VpkDirectoryEntry>();

	public bool IsValid => Signature == VPK_SIGNATURE || Signature == FPX_SIGNATURE;
}

