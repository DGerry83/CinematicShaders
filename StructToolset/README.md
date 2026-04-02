# Struct Generator Toolset

A deterministic struct generator for cross-language (C++, HLSL, C#) constant buffer definitions.

## Overview

This toolset generates struct definitions that are guaranteed to have compatible memory layouts across:
- **C++** (for native plugin)
- **HLSL** (for shaders)
- **C#** (for Unity interop)

## Quick Start

1. **Define your struct** in `structs.yaml`:
```yaml
structs:
  - name: MyParams
    size_align: 16
    fields:
      - name: ResolutionX
        type: float
        comment: Screen width
      - name: ResolutionY
        type: float
        comment: Screen height
```

2. **Run the generator**:
```bash
cd StructToolset
python generator.py
```

3. **Copy-paste** the generated files from `generated/` into your source code.

## Files

| File | Purpose |
|------|---------|
| `structs.yaml` | Define your structs here |
| `generator.py` | Main entry point |
| `layout_engine.py` | Deterministic layout calculation |
| `generators.py` | C++, HLSL, C# code generators |
| `generated/` | Output folder for generated code |

## Type Mapping

| Schema Type | C++ Output | HLSL Output | C# Output |
|-------------|------------|-------------|-----------|
| `float` | `float` | `float` | `float` |
| `int` | `int32_t` | `int` | `int` |
| `uint` | `uint32_t` | `uint` | `uint` |
| `bool` | `bool` | `bool` | `int` |
| `float2` | `float x, y` | `float2` | `float X, Y` |
| `float3` | `float x, y, z` | `float3` | `float X, Y, Z` |
| `float4` | `struct { x,y,z,w }` | `float4` | `Vector4` |
| `float4x4` | 16 floats | `float4x4` | 16 floats |

## Alignment Rules

### HLSL Constant Buffer
- 16-byte row alignment
- Scalars align to 4 bytes
- Vectors align to component type (4 bytes)
- No member can cross 16-byte boundary
- Arrays: each element starts new 16-byte row

### C++ Interop
- `#pragma pack(push, 16)` for CB matching
- Vectors expanded to individual scalars

### C# Interop  
- `[StructLayout(LayoutKind.Sequential, Pack = 16)]`
- `float4` mapped to Unity `Vector4`
- Other vectors expanded to scalars

## Vector Coalescing (HLSL)

The generator automatically coalesces consecutive float fields into HLSL vectors:

```yaml
# Input
- name: CameraRightX
  type: float
- name: CameraRightY
  type: float
- name: CameraRightZ
  type: float
```

```hlsl
// Output
float3 CameraRight;
```

Supports both patterns:
- `NameX, NameY, NameZ` → `float3 Name`
- `Name_X, Name_Y, Name_Z` → `float3 Name`

## Usage Examples

### Generate all structs
```bash
python generator.py
```

### Generate specific struct only
```bash
python generator.py --struct KartographerParams
```

### Dry run (preview without writing files)
```bash
python generator.py --dry-run
```

## Adding a New Struct

1. Edit `structs.yaml`
2. Add struct definition with fields
3. Run `python generator.py`
4. Copy-paste from `generated/` to your source files

## Requirements

- Python 3.8+
- PyYAML (`pip install pyyaml`)
