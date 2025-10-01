using System;
using System.IO;
using Sandbox;

namespace Sandbox;

// VTF Image Formats (from imageformat.h)
public enum VtfImageFormat
{
	RGBA8888 = 0,
	ABGR8888 = 1,
	RGB888 = 2,
	BGR888 = 3,
	RGB565 = 4,
	I8 = 5,
	IA88 = 6,
	P8 = 7,
	A8 = 8,
	RGB888_BLUESCREEN = 9,
	BGR888_BLUESCREEN = 10,
	ARGB8888 = 11,
	BGRA8888 = 12,
	DXT1 = 13,
	DXT3 = 14,
	DXT5 = 15,
	BGRX8888 = 16,
	BGR565 = 17,
	BGRX5551 = 18,
	BGRA4444 = 19,
	DXT1_ONEBITALPHA = 20,
	BGRA5551 = 21,
	UV88 = 22,
	UVWQ8888 = 23,
	RGBA16161616F = 24,
	RGBA16161616 = 25,
	UVLX8888 = 26
}

// Basic VTF (Valve Texture Format) parser
// VTF format documentation: https://developer.valvesoftware.com/wiki/Valve_Texture_Format
public class VtfFile
{
	public const int VTF_SIGNATURE = 0x00465456; // "VTF\0"
	
	public int Width { get; set; }
	public int Height { get; set; }
	public int MipmapCount { get; set; }
	public VtfImageFormat Format { get; set; }
	public byte[] ImageData { get; set; }
	
	public static VtfFile Read(BinaryReader reader)
	{
		var vtf = new VtfFile();
		
		try
		{
			// Read header
			int signature = reader.ReadInt32();
			if (signature != VTF_SIGNATURE)
			{
				// Log.Warning($"[VtfFile] Invalid VTF signature: 0x{signature:X}");
				return null;
			}
			
			int versionMajor = reader.ReadInt32();
			int versionMinor = reader.ReadInt32();
			int headerSize = reader.ReadInt32();
			
			vtf.Width = reader.ReadUInt16();
			vtf.Height = reader.ReadUInt16();
			int flags = reader.ReadInt32();
			int frameCount = reader.ReadUInt16();
			int firstFrame = reader.ReadUInt16();
			
			reader.ReadBytes(4); // padding
			
			// Reflectivity vector (3 floats)
			reader.ReadSingle(); // r
			reader.ReadSingle(); // g
			reader.ReadSingle(); // b
			
			reader.ReadBytes(4); // padding
			
			float bumpScale = reader.ReadSingle();
			vtf.Format = (VtfImageFormat)reader.ReadInt32();
			vtf.MipmapCount = reader.ReadByte();
			
			int lowResFormat = reader.ReadInt32();
			byte lowResWidth = reader.ReadByte();
			byte lowResHeight = reader.ReadByte();
			
			// For version 7.2+, read depth
			int depth = 1;
			if (versionMajor > 7 || (versionMajor == 7 && versionMinor >= 2))
			{
				depth = reader.ReadUInt16();
			}
			
			// Seek to end of header
			reader.BaseStream.Seek(headerSize, SeekOrigin.Begin);
			
			// Skip low-res image if present
			if (lowResWidth > 0 && lowResHeight > 0)
			{
				int lowResSize = GetImageSize(lowResWidth, lowResHeight, (VtfImageFormat)lowResFormat);
				reader.ReadBytes(lowResSize);
			}
			
		// Read main image data (mipmap 0)
		// Skip smaller mipmaps and read the largest one
		for (int mip = vtf.MipmapCount - 1; mip >= 0; mip--)
		{
			int mipWidth = Math.Max(1, vtf.Width >> mip);
			int mipHeight = Math.Max(1, vtf.Height >> mip);
			int mipSize = GetImageSize(mipWidth, mipHeight, vtf.Format);
			
			if (mip == 0)
			{
				// This is the full-size image we want
				vtf.ImageData = reader.ReadBytes(mipSize);
			}
			else
			{
				// Skip this mipmap
				reader.ReadBytes(mipSize);
			}
			}
			
			// Log.Info($"[VtfFile] Loaded VTF: {vtf.Width}x{vtf.Height}, format={vtf.Format} ({(int)vtf.Format}), mipmaps={vtf.MipmapCount}, dataSize={vtf.ImageData?.Length ?? 0}");
		}
		catch (Exception ex)
		{
			// Log.Warning($"[VtfFile] Failed to parse VTF: {ex.Message}");
			return null;
		}
		
		return vtf;
	}
	
	private static int GetImageSize(int width, int height, VtfImageFormat format)
	{
		switch (format)
		{
			case VtfImageFormat.DXT1:
			case VtfImageFormat.DXT1_ONEBITALPHA:
				return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8;
			case VtfImageFormat.DXT3:
			case VtfImageFormat.DXT5:
				return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16;
			case VtfImageFormat.RGBA8888:
			case VtfImageFormat.ABGR8888:
			case VtfImageFormat.ARGB8888:
			case VtfImageFormat.BGRA8888:
			case VtfImageFormat.BGRX8888:
				return width * height * 4;
			case VtfImageFormat.RGB888:
			case VtfImageFormat.BGR888:
			case VtfImageFormat.RGB888_BLUESCREEN:
			case VtfImageFormat.BGR888_BLUESCREEN:
				return width * height * 3;
			case VtfImageFormat.RGB565:
			case VtfImageFormat.BGR565:
			case VtfImageFormat.BGRA5551:
			case VtfImageFormat.BGRX5551:
			case VtfImageFormat.BGRA4444:
			case VtfImageFormat.IA88:
			case VtfImageFormat.UV88:
				return width * height * 2;
			case VtfImageFormat.I8:
			case VtfImageFormat.A8:
			case VtfImageFormat.P8:
				return width * height;
			default:
				// Assume 4 bytes per pixel for unknown formats
				return width * height * 4;
		}
	}
	
	// Convert VTF data to RGBA32 for s&box
	public Color32[] ToRGBA32()
	{
		if (ImageData == null || Width == 0 || Height == 0)
		{
			// Log.Warning($"[VtfFile.ToRGBA32] Invalid data: ImageData={(ImageData == null ? "null" : ImageData.Length.ToString())}, Width={Width}, Height={Height}");
			return null;
		}

		// Log.Info($"[VtfFile.ToRGBA32] Converting {Width}x{Height} texture, format={Format}, input size={ImageData.Length}");

		byte[] rgbaData = null;
		
		switch (Format)
		{
			case VtfImageFormat.RGBA8888:
				// Log.Info($"[VtfFile.ToRGBA32] Using RGBA8888 (no conversion)");
				rgbaData = ImageData;
				break;
			case VtfImageFormat.RGB888:
				// Log.Info($"[VtfFile.ToRGBA32] Converting RGB888 to RGBA");
				rgbaData = ConvertRGB888ToRGBA(ImageData, Width, Height);
				break;
			case VtfImageFormat.BGR888:
				// Log.Info($"[VtfFile.ToRGBA32] Converting BGR888 to RGBA");
				rgbaData = ConvertBGR888ToRGBA(ImageData, Width, Height);
				break;
			case VtfImageFormat.BGRA8888:
				// Log.Info($"[VtfFile.ToRGBA32] Converting BGRA8888 to RGBA");
				rgbaData = ConvertBGRA8888ToRGBA(ImageData, Width, Height);
				break;
			case VtfImageFormat.DXT1:
			case VtfImageFormat.DXT1_ONEBITALPHA:
				// Log.Info($"[VtfFile.ToRGBA32] Decompressing DXT1");
				rgbaData = DecompressDXT1(ImageData, Width, Height);
				break;
			case VtfImageFormat.DXT3:
				// Log.Info($"[VtfFile.ToRGBA32] Decompressing DXT3");
				rgbaData = DecompressDXT3(ImageData, Width, Height);
				break;
			case VtfImageFormat.DXT5:
				// Log.Info($"[VtfFile.ToRGBA32] Decompressing DXT5");
				rgbaData = DecompressDXT5(ImageData, Width, Height);
				break;
			default:
				// Log.Warning($"[VtfFile.ToRGBA32] Unsupported VTF format {Format}");
				return null;
		}
		
		if (rgbaData == null)
		{
			// Log.Warning($"[VtfFile.ToRGBA32] Failed to decode VTF format {Format}");
			return null;
		}
		
		// Log.Info($"[VtfFile.ToRGBA32] Decompression successful, output size={rgbaData.Length}, expected={(Width * Height * 4)}");
		
		// Convert byte array to Color32 array
		var pixels = new Color32[Width * Height];
		for (int i = 0; i < pixels.Length; i++)
		{
			pixels[i] = new Color32(
				rgbaData[i * 4 + 0],
				rgbaData[i * 4 + 1],
				rgbaData[i * 4 + 2],
				rgbaData[i * 4 + 3]
			);
		}
		
		// Sample a few pixels for debugging
		if (pixels.Length > 0)
		{
			// Log.Info($"[VtfFile.ToRGBA32] Sample pixels: [0]={pixels[0].r},{pixels[0].g},{pixels[0].b},{pixels[0].a}");
			if (pixels.Length > 100)
			{
				// Log.Info($"[VtfFile.ToRGBA32] Sample pixels: [100]={pixels[100].r},{pixels[100].g},{pixels[100].b},{pixels[100].a}");
			}
		}
		
		return pixels;
	}
	
	private static byte[] ConvertRGB888ToRGBA(byte[] src, int width, int height)
	{
		byte[] dst = new byte[width * height * 4];
		for (int i = 0; i < width * height; i++)
		{
			dst[i * 4 + 0] = src[i * 3 + 0]; // R
			dst[i * 4 + 1] = src[i * 3 + 1]; // G
			dst[i * 4 + 2] = src[i * 3 + 2]; // B
			dst[i * 4 + 3] = 255; // A
		}
		return dst;
	}
	
	private static byte[] ConvertBGR888ToRGBA(byte[] src, int width, int height)
	{
		byte[] dst = new byte[width * height * 4];
		for (int i = 0; i < width * height; i++)
		{
			dst[i * 4 + 0] = src[i * 3 + 2]; // R
			dst[i * 4 + 1] = src[i * 3 + 1]; // G
			dst[i * 4 + 2] = src[i * 3 + 0]; // B
			dst[i * 4 + 3] = 255; // A
		}
		return dst;
	}
	
	private static byte[] ConvertBGRA8888ToRGBA(byte[] src, int width, int height)
	{
		byte[] dst = new byte[width * height * 4];
		for (int i = 0; i < width * height; i++)
		{
			dst[i * 4 + 0] = src[i * 4 + 2]; // R
			dst[i * 4 + 1] = src[i * 4 + 1]; // G
			dst[i * 4 + 2] = src[i * 4 + 0]; // B
			dst[i * 4 + 3] = src[i * 4 + 3]; // A
		}
		return dst;
	}
	
	// DXT1 decompression
	private static byte[] DecompressDXT1(byte[] src, int width, int height)
	{
		int blocksX = (width + 3) / 4;
		int blocksY = (height + 3) / 4;
		byte[] dst = new byte[width * height * 4];
		int offset = 0;
		
		for (int by = 0; by < blocksY; by++)
		{
			for (int bx = 0; bx < blocksX; bx++)
			{
				if (offset + 8 > src.Length)
					break;
				
				ushort c0 = (ushort)(src[offset] | (src[offset + 1] << 8));
				ushort c1 = (ushort)(src[offset + 2] | (src[offset + 3] << 8));
				uint indices = (uint)(src[offset + 4] | (src[offset + 5] << 8) | (src[offset + 6] << 16) | (src[offset + 7] << 24));
				
				var colors = new (byte r, byte g, byte b, byte a)[4];
				colors[0] = Convert565(c0);
				colors[1] = Convert565(c1);
				
				if (c0 > c1)
				{
					colors[2] = Lerp(colors[0], colors[1], 2, 3);
					colors[3] = Lerp(colors[0], colors[1], 1, 3);
				}
				else
				{
					colors[2] = ((byte)((colors[0].r + colors[1].r) / 2), 
					             (byte)((colors[0].g + colors[1].g) / 2), 
					             (byte)((colors[0].b + colors[1].b) / 2), 
					             255);
					colors[3] = (0, 0, 0, 0);
				}
				
				WriteBlock(dst, width, bx * 4, by * 4, colors, indices);
				offset += 8;
			}
		}
		
		return dst;
	}
	
	// DXT3 decompression
	private static byte[] DecompressDXT3(byte[] src, int width, int height)
	{
		int blocksX = (width + 3) / 4;
		int blocksY = (height + 3) / 4;
		byte[] dst = new byte[width * height * 4];
		int offset = 0;
		
		for (int by = 0; by < blocksY; by++)
		{
			for (int bx = 0; bx < blocksX; bx++)
			{
				if (offset + 16 > src.Length)
					break;
				
				ulong alphaBits = (ulong)src[offset] 
					| ((ulong)src[offset + 1] << 8) 
					| ((ulong)src[offset + 2] << 16) 
					| ((ulong)src[offset + 3] << 24)
					| ((ulong)src[offset + 4] << 32) 
					| ((ulong)src[offset + 5] << 40) 
					| ((ulong)src[offset + 6] << 48) 
					| ((ulong)src[offset + 7] << 56);
				
				ushort c0 = (ushort)(src[offset + 8] | (src[offset + 9] << 8));
				ushort c1 = (ushort)(src[offset + 10] | (src[offset + 11] << 8));
				uint indices = (uint)(src[offset + 12] | (src[offset + 13] << 8) | (src[offset + 14] << 16) | (src[offset + 15] << 24));
				
				var colors = new (byte r, byte g, byte b, byte a)[4];
				colors[0] = Convert565(c0);
				colors[1] = Convert565(c1);
				colors[2] = Lerp(colors[0], colors[1], 2, 3);
				colors[3] = Lerp(colors[0], colors[1], 1, 3);
				
				WriteBlockWithExplicitAlpha(dst, width, bx * 4, by * 4, colors, indices, alphaBits);
				offset += 16;
			}
		}
		
		return dst;
	}
	
	// DXT5 decompression
	private static byte[] DecompressDXT5(byte[] src, int width, int height)
	{
		int blocksX = (width + 3) / 4;
		int blocksY = (height + 3) / 4;
		byte[] dst = new byte[width * height * 4];
		int offset = 0;
		
		for (int by = 0; by < blocksY; by++)
		{
			for (int bx = 0; bx < blocksX; bx++)
			{
				if (offset + 16 > src.Length)
					break;
				
				byte a0 = src[offset];
				byte a1 = src[offset + 1];
				ulong aIdx = (ulong)src[offset + 2] 
					| ((ulong)src[offset + 3] << 8) 
					| ((ulong)src[offset + 4] << 16)
					| ((ulong)src[offset + 5] << 24) 
					| ((ulong)src[offset + 6] << 32) 
					| ((ulong)src[offset + 7] << 40);
				
				ushort c0 = (ushort)(src[offset + 8] | (src[offset + 9] << 8));
				ushort c1 = (ushort)(src[offset + 10] | (src[offset + 11] << 8));
				uint indices = (uint)(src[offset + 12] | (src[offset + 13] << 8) | (src[offset + 14] << 16) | (src[offset + 15] << 24));
				
				var alphaVals = new byte[8];
				alphaVals[0] = a0;
				alphaVals[1] = a1;
				
				if (a0 > a1)
				{
					alphaVals[2] = (byte)((6 * a0 + 1 * a1) / 7);
					alphaVals[3] = (byte)((5 * a0 + 2 * a1) / 7);
					alphaVals[4] = (byte)((4 * a0 + 3 * a1) / 7);
					alphaVals[5] = (byte)((3 * a0 + 4 * a1) / 7);
					alphaVals[6] = (byte)((2 * a0 + 5 * a1) / 7);
					alphaVals[7] = (byte)((1 * a0 + 6 * a1) / 7);
				}
				else
				{
					alphaVals[2] = (byte)((4 * a0 + 1 * a1) / 5);
					alphaVals[3] = (byte)((3 * a0 + 2 * a1) / 5);
					alphaVals[4] = (byte)((2 * a0 + 3 * a1) / 5);
					alphaVals[5] = (byte)((1 * a0 + 4 * a1) / 5);
					alphaVals[6] = 0;
					alphaVals[7] = 255;
				}
				
				var colors = new (byte r, byte g, byte b, byte a)[4];
				colors[0] = Convert565(c0);
				colors[1] = Convert565(c1);
				colors[2] = Lerp(colors[0], colors[1], 2, 3);
				colors[3] = Lerp(colors[0], colors[1], 1, 3);
				
				WriteBlockWithIndexedAlpha(dst, width, bx * 4, by * 4, colors, indices, alphaVals, aIdx);
				offset += 16;
			}
		}
		
		return dst;
	}
	
	// Helper functions
	private static (byte r, byte g, byte b, byte a) Convert565(ushort c)
	{
		byte r = (byte)(((c >> 11) & 0x1F) * 255 / 31);
		byte g = (byte)(((c >> 5) & 0x3F) * 255 / 63);
		byte b = (byte)((c & 0x1F) * 255 / 31);
		return (r, g, b, 255);
	}
	
	private static (byte r, byte g, byte b, byte a) Lerp((byte r, byte g, byte b, byte a) a, (byte r, byte g, byte b, byte a) b, int na, int d)
	{
		return (
			(byte)((a.r * na + b.r * (d - na)) / d),
			(byte)((a.g * na + b.g * (d - na)) / d),
			(byte)((a.b * na + b.b * (d - na)) / d),
			255
		);
	}
	
	private static void WriteBlock(byte[] dst, int width, int x, int y, (byte r, byte g, byte b, byte a)[] colors, uint indices)
	{
		int height = dst.Length / (4 * width);
		for (int row = 0; row < 4; row++)
		{
			for (int col = 0; col < 4; col++)
			{
				int idx = (int)((indices >> (2 * (row * 4 + col))) & 0x3);
				int px = x + col;
				int py = y + row;
				if (px >= width || py >= height) continue;
				int di = (py * width + px) * 4;
				dst[di + 0] = colors[idx].r;
				dst[di + 1] = colors[idx].g;
				dst[di + 2] = colors[idx].b;
				dst[di + 3] = colors[idx].a;
			}
		}
	}
	
	private static void WriteBlockWithExplicitAlpha(byte[] dst, int width, int x, int y, (byte r, byte g, byte b, byte a)[] colors, uint indices, ulong alphaBits)
	{
		int height = dst.Length / (4 * width);
		for (int row = 0; row < 4; row++)
		{
			for (int col = 0; col < 4; col++)
			{
				int idx = (int)((indices >> (2 * (row * 4 + col))) & 0x3);
				int px = x + col;
				int py = y + row;
				if (px >= width || py >= height) continue;
				int di = (py * width + px) * 4;
				byte a4 = (byte)((alphaBits >> (4 * (row * 4 + col))) & 0xF);
				byte a = (byte)(a4 * 17);
				dst[di + 0] = colors[idx].r;
				dst[di + 1] = colors[idx].g;
				dst[di + 2] = colors[idx].b;
				dst[di + 3] = a;
			}
		}
	}
	
	private static void WriteBlockWithIndexedAlpha(byte[] dst, int width, int x, int y, (byte r, byte g, byte b, byte a)[] colors, uint indices, byte[] alphaVals, ulong aIdx)
	{
		int height = dst.Length / (4 * width);
		for (int row = 0; row < 4; row++)
		{
			for (int col = 0; col < 4; col++)
			{
				int idx = (int)((indices >> (2 * (row * 4 + col))) & 0x3);
				int px = x + col;
				int py = y + row;
				if (px >= width || py >= height) continue;
				int di = (py * width + px) * 4;
				int aIndex = (int)((aIdx >> (3 * (row * 4 + col))) & 0x7);
				byte a = alphaVals[aIndex];
				dst[di + 0] = colors[idx].r;
				dst[di + 1] = colors[idx].g;
				dst[di + 2] = colors[idx].b;
				dst[di + 3] = a;
			}
		}
	}
}

