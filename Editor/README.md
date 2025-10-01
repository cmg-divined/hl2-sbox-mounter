# Half-Life 2 Asset Mounting for S&box

This project provides asset mounting capabilities for Half-Life 2 game files in s&box, allowing you to load and use HL2 models, textures, and sounds.

## Architecture

### Core Components

#### VpkLib
- **VpkFile.cs**: Main VPK archive reader
  - Reads VPK version 1 and 2 formats
  - Supports multi-file VPK archives (pak01_dir.vpk, pak01_000.vpk, etc.)
  - Handles embedded and external file data
  - Support for Titanfall VPK format (version 196610)

- **VpkDirectoryEntry.cs**: Represents individual files within VPK archives
  - Tracks file location (archive index, offset, length)
  - Manages preload data stored in directory file
  - Handles CRC checksums

- **VpkFileData.cs**: VPK file metadata and header information

#### MdlLib
- **MdlHeader.cs**: Parses Source Engine MDL file headers
  - Supports MDL versions 44-49 (HL2, TF2, L4D, Portal 2, CS:GO)
  - Reads model metadata (bones, textures, body parts, sequences)
  - Contains all studio header fields

- **MdlBone.cs**: Bone structure and hierarchy
  - Position, rotation, scale data
  - Bone controllers and procedural rules
  - Physics properties

- **MdlBodyPart.cs**: Body part definitions
- **MdlModel.cs**: Model mesh data

#### Mount System
- **HL2Mount.cs**: Main mount handler
  - Inherits from `BaseGameMount`
  - Detects HL2 installation via Steam (App ID: 220)
  - Scans for VPK files in hl2 directory
  - Registers models (.mdl), textures (.vtf), and sounds (.wav, .mp3)

#### Resource Loaders
- **HL2Model.cs**: MDL model loader
  - Validates MDL format and version
  - Parses model headers
  - TODO: Full model conversion (VVD vertex data, VTX strip data)

- **HL2Texture.cs**: VTF texture loader
  - Validates VTF format
  - TODO: Full texture conversion (format decompression, mipmap generation)

- **HL2Sound.cs**: WAV/MP3 sound loader
  - Passes through to s&box sound system

## Current Status

✅ **Completed:**
- VPK file system reading
- MDL header parsing
- Mount detection and initialization
- File discovery and registration
- Basic resource loader structure

🚧 **In Progress:**
- Full MDL model conversion
- VTF texture decoding
- Material system integration

📋 **TODO:**
- Parse VVD (vertex data) files
- Parse VTX (strip/mesh data) files
- Parse PHY (physics) files
- Decode VTF compressed formats (DXT1, DXT5, etc.)
- Parse VMT (material) files
- Build s&box Model from MDL/VVD/VTX data
- Create proper skeleton/bone system
- Animation support
- LOD support

## File Format Overview

### VPK (Valve Package)
- Directory-based archive format
- `*_dir.vpk`: Contains directory and small files
- `*_XXX.vpk`: Contains larger file data (XXX = archive index)

### MDL (Model)
- Main model file with header, bones, materials, sequences
- Requires companion files:
  - `.vvd`: Vertex data
  - `.vtx`: Optimized mesh strips
  - `.phy`: Physics collision data (optional)
  - `.ani`: Animation data (optional)

### VTF (Valve Texture Format)
- Supports DXT compression, mipmaps, cube maps
- Multiple image formats and flags

### VMT (Valve Material Type)
- Text-based material definitions
- References VTF textures and shaders

## Usage

1. Ensure Half-Life 2 is installed via Steam
2. Load the hl2mounts project in s&box
3. The mount will automatically detect and register HL2 assets
4. Access files via the mounting system

## References

Based on:
- Crowbar decompiler structures
- Source Engine SDK
- Valve Developer Community documentation

