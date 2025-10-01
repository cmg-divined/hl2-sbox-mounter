using Sandbox;

namespace Sandbox;

internal struct HL2SkinnedVertex
{
	[VertexLayout.Position]
	public Vector3 position;

	[VertexLayout.Normal]
	public Vector3 normal;

	[VertexLayout.Tangent]
	public Vector4 tangent;

	[VertexLayout.TexCoord]
	public Vector2 texcoord;

	[VertexLayout.BlendIndices]
	public Color32 blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 blendWeights;

	public static readonly VertexAttribute[] Layout = new VertexAttribute[]
	{
		new VertexAttribute(VertexAttributeType.Position, VertexAttributeFormat.Float32),
		new VertexAttribute(VertexAttributeType.Normal, VertexAttributeFormat.Float32),
		new VertexAttribute(VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4),
		new VertexAttribute(VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2),
		new VertexAttribute(VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4),
		new VertexAttribute(VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4)
	};

	public HL2SkinnedVertex(Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights)
	{
		this.position = position;
		this.normal = normal;
		this.tangent = tangent;
		this.texcoord = texcoord;
		this.blendIndices = blendIndices;
		this.blendWeights = blendWeights;
	}
}

