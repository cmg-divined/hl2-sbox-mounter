using System;
using System.IO;
using System.Collections.Generic;

namespace MdlLib;

// VTX = Optimized mesh strip data
public class VtxFile
{
	public const int VTX_SIGNATURE = 0x56545356; // "VSTV"
	public const int VERSION_7 = 7;

	public int Version { get; set; }
	public int VertCacheSize { get; set; }
	public ushort MaxBonesPerStrip { get; set; }
	public ushort MaxBonesPerTri { get; set; }
	public int MaxBonesPerVert { get; set; }
	public int Checksum { get; set; }
	public int NumLods { get; set; }

	public VtxBodyPart[] BodyParts { get; set; }

	public static VtxFile Read(BinaryReader reader)
	{
		var vtx = new VtxFile();

		vtx.Version = reader.ReadInt32();
		vtx.VertCacheSize = reader.ReadInt32();
		vtx.MaxBonesPerStrip = reader.ReadUInt16();
		vtx.MaxBonesPerTri = reader.ReadUInt16();
		vtx.MaxBonesPerVert = reader.ReadInt32();
		vtx.Checksum = reader.ReadInt32();
		vtx.NumLods = reader.ReadInt32();

		// Material replacement header offset (not used)
		reader.ReadInt32();

		int numBodyParts = reader.ReadInt32();
		int bodyPartOffset = reader.ReadInt32();

		// Read body parts
		vtx.BodyParts = new VtxBodyPart[numBodyParts];
		long basePos = bodyPartOffset;

		for (int i = 0; i < numBodyParts; i++)
		{
			reader.BaseStream.Seek(basePos + (i * 8), SeekOrigin.Begin);
			int numModels = reader.ReadInt32();
			int modelOffset = reader.ReadInt32();

			vtx.BodyParts[i] = new VtxBodyPart
			{
				Models = new VtxModel[numModels]
			};

			long modelBase = basePos + (i * 8) + modelOffset;
			for (int j = 0; j < numModels; j++)
			{
				reader.BaseStream.Seek(modelBase + (j * 8), SeekOrigin.Begin);
				int numLods = reader.ReadInt32();
				int lodOffset = reader.ReadInt32();

				vtx.BodyParts[i].Models[j] = new VtxModel
				{
					Lods = new VtxModelLod[numLods]
				};

				long lodBase = modelBase + (j * 8) + lodOffset;
				for (int k = 0; k < numLods; k++)
				{
					reader.BaseStream.Seek(lodBase + (k * 12), SeekOrigin.Begin);
					int numMeshes = reader.ReadInt32();
					int meshOffset = reader.ReadInt32();
					float switchPoint = reader.ReadSingle();

					vtx.BodyParts[i].Models[j].Lods[k] = new VtxModelLod
					{
						Meshes = new VtxMesh[numMeshes],
						SwitchPoint = switchPoint
					};

					long meshBase = lodBase + (k * 12) + meshOffset;
					for (int m = 0; m < numMeshes; m++)
					{
						reader.BaseStream.Seek(meshBase + (m * 9), SeekOrigin.Begin);
						int numStripGroups = reader.ReadInt32();
						int stripGroupOffset = reader.ReadInt32();
						byte flags = reader.ReadByte();

						vtx.BodyParts[i].Models[j].Lods[k].Meshes[m] = new VtxMesh
						{
							StripGroups = new VtxStripGroup[numStripGroups],
							Flags = flags
						};

						long stripGroupBase = meshBase + (m * 9) + stripGroupOffset;
						for (int sg = 0; sg < numStripGroups; sg++)
						{
							reader.BaseStream.Seek(stripGroupBase + (sg * 25), SeekOrigin.Begin);
							
							int numVerts = reader.ReadInt32();
							int vertOffset = reader.ReadInt32();
							int numIndices = reader.ReadInt32();
							int indexOffset = reader.ReadInt32();
							int numStrips = reader.ReadInt32();
							int stripOffset = reader.ReadInt32();
							byte stripGroupFlags = reader.ReadByte();

							var stripGroup = new VtxStripGroup
							{
								Vertices = new ushort[numVerts],
								Indices = new ushort[numIndices],
								Flags = stripGroupFlags
							};

							// Read vertices (indices into VVD)
							// VTX Vertex structure (9 bytes):
							// - 3 bytes: bone weight indices
							// - 1 byte: numBones
							// - 2 bytes: origMeshVertID (VVD index) <- what we want
							// - 3 bytes: bone IDs
							long vertBase = stripGroupBase + (sg * 25) + vertOffset;
							reader.BaseStream.Seek(vertBase, SeekOrigin.Begin);
							for (int v = 0; v < numVerts; v++)
							{
								reader.ReadBytes(3); // Skip bone weight indices
								reader.ReadByte();    // Skip numBones
								stripGroup.Vertices[v] = reader.ReadUInt16(); // Read VVD index
								reader.ReadBytes(3); // Skip bone IDs
							}

							// Read indices
							long indexBase = stripGroupBase + (sg * 25) + indexOffset;
							reader.BaseStream.Seek(indexBase, SeekOrigin.Begin);
							for (int idx = 0; idx < numIndices; idx++)
							{
								stripGroup.Indices[idx] = reader.ReadUInt16();
							}

							vtx.BodyParts[i].Models[j].Lods[k].Meshes[m].StripGroups[sg] = stripGroup;
						}
					}
				}
			}
		}

		return vtx;
	}
}

public class VtxBodyPart
{
	public VtxModel[] Models { get; set; }
}

public class VtxModel
{
	public VtxModelLod[] Lods { get; set; }
}

public class VtxModelLod
{
	public VtxMesh[] Meshes { get; set; }
	public float SwitchPoint { get; set; }
}

public class VtxMesh
{
	public VtxStripGroup[] StripGroups { get; set; }
	public byte Flags { get; set; }
}

public class VtxStripGroup
{
	public ushort[] Vertices { get; set; }  // Indices into VVD vertex array
	public ushort[] Indices { get; set; }   // Indices into StripGroup's vertex array
	public byte Flags { get; set; }
}

