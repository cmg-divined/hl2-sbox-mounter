using System.IO;

namespace MdlLib;

// mstudiomesh_t - Mesh structure in MDL
public class MdlMesh
{
	public const int SIZE = 116; // bytes for v44
	
	public int MaterialIndex { get; set; }  // Index into texture array
	public int ModelOffset { get; set; }
	public int VertexCount { get; set; }
	public int VertexIndexStart { get; set; }
	public int FlexCount { get; set; }
	public int FlexOffset { get; set; }
	public int MaterialType { get; set; }
	public int MaterialParam { get; set; }
	public int Id { get; set; }
	
	// Center point
	public float CenterX { get; set; }
	public float CenterY { get; set; }
	public float CenterZ { get; set; }
	
	// Mesh vertex data (LOD info)
	public int[] VertexDataModelLodCount { get; set; } = new int[8];
	public int[] VertexDataLodVertexCount { get; set; } = new int[8];
	
	public int Unused { get; set; }
	
	public static MdlMesh Read(BinaryReader reader)
	{
		var mesh = new MdlMesh();
		
		mesh.MaterialIndex = reader.ReadInt32();
		mesh.ModelOffset = reader.ReadInt32();
		mesh.VertexCount = reader.ReadInt32();
		mesh.VertexIndexStart = reader.ReadInt32();
		mesh.FlexCount = reader.ReadInt32();
		mesh.FlexOffset = reader.ReadInt32();
		mesh.MaterialType = reader.ReadInt32();
		mesh.MaterialParam = reader.ReadInt32();
		mesh.Id = reader.ReadInt32();
		
		mesh.CenterX = reader.ReadSingle();
		mesh.CenterY = reader.ReadSingle();
		mesh.CenterZ = reader.ReadSingle();
		
		// Mesh vertex data (8 LODs)
		for (int i = 0; i < 8; i++)
		{
			mesh.VertexDataModelLodCount[i] = reader.ReadInt32();
		}
		for (int i = 0; i < 8; i++)
		{
			mesh.VertexDataLodVertexCount[i] = reader.ReadInt32();
		}
		
		mesh.Unused = reader.ReadInt32();
		
		return mesh;
	}
}

