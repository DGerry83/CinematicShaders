#!/usr/bin/env python3
"""
Struct Layout Engine
====================
Deterministic layout calculation for C++, HLSL, and C# interop.

Key Rules Implemented:
- HLSL Constant Buffer: 16-byte rows, 4-byte scalar alignment
- C++: #pragma pack(16) for constant buffer matching
- C#: [StructLayout(LayoutKind.Sequential, Pack = 16)]
"""

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple
import math

# Type information: (size_in_bytes, alignment_in_bytes)
TYPE_INFO = {
    'float':   (4, 4),
    'int':     (4, 4),
    'uint':    (4, 4),
    'bool':    (4, 4),
    'float2':  (8, 4),   # float3 aligns by component type (4 bytes) in HLSL
    'float3':  (12, 4),  # NOT 16-byte aligned like GLSL std140!
    'float4':  (16, 4),
    'float4x4': (64, 16), # 4x4 floats, 16-byte aligned
}


@dataclass
class FieldDef:
    """Definition of a single struct field from schema."""
    name: str
    type_name: str
    comment: str = ""
    array_size: int = 1


@dataclass
class StructDef:
    """Definition of a struct from schema."""
    name: str
    size_align: int  # Target alignment (usually 16 for HLSL CB)
    fields: List[FieldDef]


@dataclass
class LayoutField:
    """A field with calculated layout information."""
    name: str
    type_name: str
    size: int
    offset: int
    comment: str = ""
    array_size: int = 1
    is_padding: bool = False


@dataclass
class StructLayout:
    """Complete layout for a struct in one language."""
    name: str
    total_size: int
    alignment: int
    fields: List[LayoutField]


class HLSLayoutEngine:
    """
    HLSL Constant Buffer Layout Engine.
    
    Rules:
    1. Constant buffer is arranged like array of 16-byte rows
    2. Scalars (float, int, uint, bool) are 4-byte aligned
    3. Vectors (float2, float3, float4) align by component type (4 bytes)
    4. If a member would cross a 16-byte row boundary, push to next row
    5. Arrays: each element starts a new 16-byte row
    6. Inner structs: must start at 16-byte aligned offset
    """
    
    @staticmethod
    def calculate_layout(struct_def: StructDef) -> StructLayout:
        fields = []
        offset = 0
        
        for field_def in struct_def.fields:
            type_size, type_align = TYPE_INFO.get(field_def.type_name, (4, 4))
            
            if field_def.array_size > 1:
                # Arrays in HLSL CB: each element starts new 16-byte row
                offset = HLSLayoutEngine._align_up(offset, 16)
                element_size = type_size
                
                for i in range(field_def.array_size):
                    elem_offset = offset + i * 16  # Each element on new row
                    fields.append(LayoutField(
                        name=f"{field_def.name}[{i}]",
                        type_name=field_def.type_name,
                        size=element_size,
                        offset=elem_offset,
                        comment=field_def.comment if i == 0 else "",
                        array_size=1
                    ))
                offset = offset + field_def.array_size * 16
            else:
                # Scalar or vector
                # Check if this would cross a 16-byte boundary
                end_offset = offset + type_size
                row_start = (offset // 16) * 16
                row_end = row_start + 16
                
                if end_offset > row_end:
                    # Would cross row boundary, align to next row
                    offset = HLSLayoutEngine._align_up(offset, 16)
                
                fields.append(LayoutField(
                    name=field_def.name,
                    type_name=field_def.type_name,
                    size=type_size,
                    offset=offset,
                    comment=field_def.comment,
                    array_size=1
                ))
                offset += type_size
        
        # Total size must be multiple of 16
        total_size = HLSLayoutEngine._align_up(offset, struct_def.size_align)
        
        return StructLayout(
            name=struct_def.name,
            total_size=total_size,
            alignment=struct_def.size_align,
            fields=fields
        )
    
    @staticmethod
    def _align_up(offset: int, alignment: int) -> int:
        """Align offset up to the nearest multiple of alignment."""
        return ((offset + alignment - 1) // alignment) * alignment


class CPPLayoutEngine:
    """
    C++ Layout Engine for HLSL Constant Buffer Interop.
    
    Strategy:
    - Use #pragma pack(16) to match HLSL 16-byte row alignment
    - Expand vectors to individual scalars for interop safety
    - Match HLSL byte offsets exactly
    """
    
    @staticmethod
    def calculate_layout(struct_def: StructDef) -> StructLayout:
        fields = []
        offset = 0
        
        for field_def in struct_def.fields:
            if field_def.array_size > 1:
                fields.extend(CPPLayoutEngine._expand_array(field_def, offset))
                # Arrays in HLSL: each element starts new 16-byte row
                offset += field_def.array_size * 16
            else:
                expanded = CPPLayoutEngine._expand_field(field_def, offset)
                fields.extend(expanded)
                type_size, _ = TYPE_INFO.get(field_def.type_name, (4, 4))
                offset += type_size
        
        # Match HLSL total size (16-byte aligned)
        total_size = ((offset + 15) // 16) * 16
        
        return StructLayout(
            name=struct_def.name,
            total_size=total_size,
            alignment=struct_def.size_align,
            fields=fields
        )
    
    @staticmethod
    def _expand_field(field_def: FieldDef, base_offset: int) -> List[LayoutField]:
        """Expand a field to individual scalars for C++ interop."""
        type_size, _ = TYPE_INFO.get(field_def.type_name, (4, 4))
        
        if field_def.type_name == 'float2':
            return [
                LayoutField(f"{field_def.name}.x", 'float', 4, base_offset, field_def.comment),
                LayoutField(f"{field_def.name}.y", 'float', 4, base_offset + 4, ""),
            ]
        elif field_def.type_name == 'float3':
            return [
                LayoutField(f"{field_def.name}.x", 'float', 4, base_offset, field_def.comment),
                LayoutField(f"{field_def.name}.y", 'float', 4, base_offset + 4, ""),
                LayoutField(f"{field_def.name}.z", 'float', 4, base_offset + 8, ""),
            ]
        elif field_def.type_name == 'float4':
            return [
                LayoutField(f"{field_def.name}.x", 'float', 4, base_offset, field_def.comment),
                LayoutField(f"{field_def.name}.y", 'float', 4, base_offset + 4, ""),
                LayoutField(f"{field_def.name}.z", 'float', 4, base_offset + 8, ""),
                LayoutField(f"{field_def.name}.w", 'float', 4, base_offset + 12, ""),
            ]
        elif field_def.type_name == 'float4x4':
            # 4x4 matrix as 16 floats
            result = []
            for row in range(4):
                for col in range(4):
                    elem_offset = base_offset + (row * 4 + col) * 4
                    comment = field_def.comment if row == 0 and col == 0 else ""
                    result.append(LayoutField(
                        f"{field_def.name}.m{row}{col}", 'float', 4, elem_offset, comment
                    ))
            return result
        else:
            # Scalar types
            cpp_type = {
                'float': 'float',
                'int': 'int32_t',
                'uint': 'uint32_t',
                'bool': 'bool',
            }.get(field_def.type_name, field_def.type_name)
            
            return [LayoutField(field_def.name, cpp_type, type_size, base_offset, field_def.comment)]
    
    @staticmethod
    def _expand_array(field_def: FieldDef, base_offset: int) -> List[LayoutField]:
        """Expand an array field to individual elements."""
        type_size, _ = TYPE_INFO.get(field_def.type_name, (4, 4))
        result = []
        
        for i in range(field_def.array_size):
            elem_offset = base_offset + i * 16  # Each element on new 16-byte row
            
            if field_def.type_name == 'float4':
                comment = field_def.comment if i == 0 else ""
                result.append(LayoutField(
                    f"{field_def.name}[{i}].x", 'float', 4, elem_offset, comment
                ))
                result.append(LayoutField(
                    f"{field_def.name}[{i}].y", 'float', 4, elem_offset + 4, ""
                ))
                result.append(LayoutField(
                    f"{field_def.name}[{i}].z", 'float', 4, elem_offset + 8, ""
                ))
                result.append(LayoutField(
                    f"{field_def.name}[{i}].w", 'float', 4, elem_offset + 12, ""
                ))
            else:
                comment = field_def.comment if i == 0 else ""
                result.append(LayoutField(
                    f"{field_def.name}[{i}]", field_def.type_name, type_size, elem_offset, comment
                ))
        
        return result


class CSLayoutEngine:
    """
    C# Layout Engine for HLSL Constant Buffer Interop.
    
    Strategy:
    - Use [StructLayout(LayoutKind.Sequential, Pack = 16)]
    - Expand vectors to individual scalars (like C++)
    - Can also use Vector4 when appropriate
    """
    
    @staticmethod
    def calculate_layout(struct_def: StructDef, use_unity_types: bool = True) -> StructLayout:
        fields = []
        offset = 0
        
        for field_def in struct_def.fields:
            if field_def.array_size > 1:
                fields.extend(CSLayoutEngine._expand_array(field_def, offset, use_unity_types))
                offset += field_def.array_size * 16
            else:
                expanded = CSLayoutEngine._expand_field(field_def, offset, use_unity_types)
                fields.extend(expanded)
                type_size, _ = TYPE_INFO.get(field_def.type_name, (4, 4))
                offset += type_size
        
        # Match HLSL total size
        total_size = ((offset + 15) // 16) * 16
        
        return StructLayout(
            name=struct_def.name,
            total_size=total_size,
            alignment=struct_def.size_align,
            fields=fields
        )
    
    @staticmethod
    def _expand_field(field_def: FieldDef, base_offset: int, use_unity_types: bool) -> List[LayoutField]:
        """Expand a field to C# representation."""
        type_size, _ = TYPE_INFO.get(field_def.type_name, (4, 4))
        
        if use_unity_types:
            if field_def.type_name == 'float4':
                # Use Unity Vector4 for float4
                return [LayoutField(
                    field_def.name, 'Vector4', 16, base_offset, field_def.comment
                )]
        
        # Expand to individual floats for everything else
        if field_def.type_name == 'float2':
            return [
                LayoutField(f"{field_def.name}X", 'float', 4, base_offset, field_def.comment),
                LayoutField(f"{field_def.name}Y", 'float', 4, base_offset + 4, ""),
            ]
        elif field_def.type_name == 'float3':
            return [
                LayoutField(f"{field_def.name}X", 'float', 4, base_offset, field_def.comment),
                LayoutField(f"{field_def.name}Y", 'float', 4, base_offset + 4, ""),
                LayoutField(f"{field_def.name}Z", 'float', 4, base_offset + 8, ""),
            ]
        elif field_def.type_name == 'float4' and not use_unity_types:
            return [
                LayoutField(f"{field_def.name}X", 'float', 4, base_offset, field_def.comment),
                LayoutField(f"{field_def.name}Y", 'float', 4, base_offset + 4, ""),
                LayoutField(f"{field_def.name}Z", 'float', 4, base_offset + 8, ""),
                LayoutField(f"{field_def.name}W", 'float', 4, base_offset + 12, ""),
            ]
        elif field_def.type_name == 'float4x4':
            # 4x4 matrix as 16 floats
            result = []
            for row in range(4):
                for col in range(4):
                    elem_offset = base_offset + (row * 4 + col) * 4
                    comment = field_def.comment if row == 0 and col == 0 else ""
                    result.append(LayoutField(
                        f"{field_def.name}_m{row}{col}", 'float', 4, elem_offset, comment
                    ))
            return result
        else:
            # Scalar types
            cs_type = {
                'float': 'float',
                'int': 'int',
                'uint': 'uint',
                'bool': 'int',  # C# bool is 1 byte, use int for HLSL bool
            }.get(field_def.type_name, field_def.type_name)
            
            return [LayoutField(field_def.name, cs_type, type_size, base_offset, field_def.comment)]
    
    @staticmethod
    def _expand_array(field_def: FieldDef, base_offset: int, use_unity_types: bool) -> List[LayoutField]:
        """Expand an array field to C# representation."""
        type_size, _ = TYPE_INFO.get(field_def.type_name, (4, 4))
        result = []
        
        for i in range(field_def.array_size):
            elem_offset = base_offset + i * 16
            
            if field_def.type_name == 'float4' and use_unity_types:
                comment = field_def.comment if i == 0 else ""
                result.append(LayoutField(
                    f"{field_def.name}[{i}]", 'Vector4', 16, elem_offset, comment
                ))
            else:
                comment = field_def.comment if i == 0 else ""
                result.append(LayoutField(
                    f"{field_def.name}{i}", field_def.type_name, type_size, elem_offset, comment
                ))
        
        return result


class LayoutVerifier:
    """Verifies cross-language compatibility of layouts."""
    
    @staticmethod
    def verify(hlsl: StructLayout, cpp: StructLayout, cs: StructLayout) -> Tuple[bool, List[str]]:
        """
        Verify that all three layouts are compatible.
        Returns (is_valid, list_of_issues).
        """
        issues = []
        
        # Check total sizes match
        if not (hlsl.total_size == cpp.total_size == cs.total_size):
            issues.append(f"Size mismatch: HLSL={hlsl.total_size}, C++={cpp.total_size}, C#={cs.total_size}")
        
        # Check alignment
        if not (hlsl.total_size % 16 == 0):
            issues.append(f"HLSL size {hlsl.total_size} is not 16-byte aligned")
        
        # Build offset maps for comparison
        hlsl_offsets = {f.name: f.offset for f in hlsl.fields}
        cpp_offsets = {f.name: f.offset for f in cpp.fields}
        cs_offsets = {f.name: f.offset for f in cs.fields}
        
        # TODO: More sophisticated verification
        
        return (len(issues) == 0, issues)
