# Struct Generator Toolset

A deterministic struct generator for cross-language (C++, HLSL, C#) constant buffer definitions.

## Overview

This toolset generates struct definitions that are guaranteed to have compatible memory layouts across:
- **C++** (for native plugin)
- **HLSL** (for shaders)
- **C#** (for Unity interop)

The generator automatically handles HLSL constant buffer alignment requirements, inserting padding fields where necessary to ensure all three languages produce identical memory layouts.

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
| `layout_engine.py` | Deterministic layout calculation with auto-padding |
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

### HLSL Constant Buffer Alignment

| Type | Size | Alignment | Notes |
|------|------|-----------|-------|
| `float` | 4 bytes | 4-byte | |
| `int`/`uint`/`bool` | 4 bytes | 4-byte | |
| `float2` | 8 bytes | **8-byte** | Often misunderstood as 4-byte! |
| `float3` | 12 bytes | **16-byte** | Padded to 16 bytes in CB |
| `float4` | 16 bytes | **16-byte** | |
| Arrays | varies | 16-byte | Each element starts new 16-byte row |

**Critical Rule**: HLSL constant buffers arrange data in 16-byte rows. If a member would cross a row boundary, it gets pushed to the next row. Vector types (`float2`, `float3`, `float4`) have specific alignment requirements that are stricter than their component type.

### Auto-Padding Insertion

The generator automatically inserts padding fields when alignment requires it:

**Example**: A `float2` at offset 772 would be misaligned (772 % 8 = 4). The generator inserts 4 bytes of padding:

```cpp
// C++ Output
//   772: _auto_pad1 (float) [PADDING]
//   776: VesselTargetCircleCenterX (float)
//   780: VesselTargetCircleCenterY (float)
float _auto_pad1; // padding
float VesselTargetCircleCenterX;
float VesselTargetCircleCenterY;
```

```hlsl
// HLSL Output - no explicit padding, but offset is 776
//   776: VesselTargetCircleCenter (float2)
float2 VesselTargetCircleCenter;
```

```csharp
// C# Output
//   772: _auto_pad1 (uint) [PADDING]
//   776: VesselTargetCircleCenterX (float)
public uint _auto_pad1; // padding
public float VesselTargetCircleCenterX;
public float VesselTargetCircleCenterY;
```

### C++ Interop

- Uses `#pragma pack(push, 16)` for CB matching
- Vectors expanded to individual scalars for interop safety
- Auto-padding fields emitted as `float` with comment

### C# Interop  

- Uses `[StructLayout(LayoutKind.Sequential, Pack = 16)]`
- `float4` mapped to Unity `Vector4`
- Other vectors expanded to scalars
- Auto-padding fields emitted as `uint` with comment

## Vector Coalescing

### HLSL Vector Detection

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

### Vector Alignment Detection

The generator also detects vector patterns to apply correct alignment:

```yaml
# Input - the generator recognizes this as a float2
- name: PositionX
  type: float  # Offset 772
- name: PositionY
  type: float  # Would be 776, but float2 needs 8-byte alignment
```

Since `float2` requires 8-byte alignment and 772 % 8 = 4, padding is automatically inserted.

## Common Pitfalls

### Misaligned float2

The most common issue is placing a `float2` at an offset not divisible by 8:

```yaml
# PROBLEMATIC - will cause generator to insert padding
- name: SomeField
  type: float    # offset 764
- name: MyVecX
  type: float    # offset 768 (768 % 8 = 0 ✓)
- name: MyVecY
  type: float    # offset 772

# If you add another float before MyVec:
- name: ExtraFloat
  type: float    # offset 768
- name: MyVecX
  type: float    # offset 772 (772 % 8 = 4 ✗ MISALIGNED!)
```

The generator will automatically insert `_auto_pad1` at offset 772 and move `MyVecX` to 776.

### Array Alignment

Arrays in HLSL constant buffers always start on 16-byte boundaries:

```yaml
- name: MyArray
  type: float
  array_size: 4  # Each element at 16-byte interval
```

This means 12 bytes of padding between each 4-byte element.

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
5. Verify sizes match expected values in comments

## Troubleshooting

### Size Mismatch Errors

If you get size mismatch errors:

1. Check that all vector fields (X/Y/Z/W patterns) are properly aligned
2. Verify no manual padding overlaps with auto-inserted padding
3. Ensure array elements start at 16-byte boundaries

### Verifying Output

Check the offset comments in generated files:

```cpp
//   772: _auto_pad1 (float) [PADDING]  ← Auto-inserted padding
//   776: MyVectorX (float)             ← Now properly 8-byte aligned
```

Padding fields are marked with `[PADDING]` in comments.

## Requirements

- Python 3.8+
- PyYAML (`pip install pyyaml`)

## References

- [HLSL Constant Buffer Packing Rules](https://maraneshi.github.io/HLSL-ConstantBufferLayoutVisualizer/)
- [Microsoft: Packing Rules for Constant Variables](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-packing-rules)
