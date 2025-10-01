
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
    Forward();
    Depth();
}

COMMON
{
	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	
	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		m.Albedo = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy ).rgb;
		m.Normal = i.vNormalWs;
		m.TextureCoords = i.vTextureCoords.xy;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = 0;
		m.Transmission = 0;

		return ShadingModelStandard::Shade( i, m );
	}
}
