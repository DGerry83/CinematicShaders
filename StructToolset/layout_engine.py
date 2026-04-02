#!/usr/bin/env python3
"""
Struct Layout Engine
====================
Deterministic layout calculation for C++, HLSL, and C# interop.

This engine implements HLSL constant buffer packing rules to ensure
identical memory layouts across all three target languages.

HLSL Constant Buffer Alignment Rules:
=====================================
- float:   4-byte align, 4-byte size
- int:     4-byte align, 4-byte size
- uint:    4-byte align, 4-byte size
- bool:    4-byte align, 4-byte size (HLSL bool is 4 bytes!)
- float2:  8-byte align, 8-byte size  ← CRITICAL: NOT 4-byte aligned!
- float3:  16-byte align, 12-byte size (padded to 16)
- float4:  16-byte align, 16-byte size
- Arrays:  Each element starts on new 16-byte row

Row Boundary Rule:
- Constant buffer is arranged as array of 16-byte rows
- If a member would cross a 16-byte boundary, it gets pushed to next row

Auto-Padding:
- The engine automatically inserts padding fields (named _auto_padN)
- This ensures correct alignment without manual padding in schema
- C++ output: padding as float fields
- C# output: padding as uint fields
- HLSL output: no explicit padding (HLSL handles internally), offsets are correct

Vector Pattern Detection:
- Fields ending in X followed by Y (or _X followed by _Y) are detected as float2
- This triggers 8-byte alignment requirement
- Similarly for X,Y,Z → float3 (16-byte alignment)
- And X,Y,Z,W → float4 (16-byte alignment)
"""

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple
import math

# Type information: (size_in_bytes, alignment_in_bytes)
# HLSL Constant Buffer alignment rules:
TYPE_INFO = {
    'float':   (4, 4),
    'int':     (4, 4),
    'uint':    (4, 4),
    'bool':    (4, 4),
    'float2':  (8, 8),   # float2 requires 8-byte alignment
    'float3':  (12, 16), # float3 requires 16-byte alignment
    'float4':  (16, 16), # float4 requires 16-byte alignment
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
    3. Vectors have specific alignment requirements:
       - float2: 8-byte aligned
       - float3: 16-byte aligned (padded to 16)
       - float4: 16-byte aligned
    4. If a member would cross a 16-byte row boundary, push to next row
    5. Arrays: each element starts a new 16-byte row
    """
    
    @staticmethod
    def calculate_layout(struct_def: StructDef) -> StructLayout:
        fields = []
        offset = 0
        pad_counter = 1
        
        for field_def in struct_def.fields:
            type_size, type_align = TYPE_INFO.get(field_def.type_name, (4, 4))
            
            if field_def.array_size > 1:
                # Arrays in HLSL CB: each element starts new 16-byte row
                # Insert padding if needed to align to 16
                if offset % 16 != 0:
                    pad_size = 16 - (offset % 16)
                    fields.append(LayoutField(
                        name=f"_auto_pad{array_counter}",
                        type_name=f"padding_{pad_size}b",
                        size=pad_size,
                        offset=offset,
                        comment="Auto-inserted for array alignment",
                        array_size=1,
                        is_padding=True
                    ))
                    offset += pad_size
                    pad_counter += 1
                
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
                # Check if this field starts a vector pattern (X followed by Y)
                vector_align = HLSLayoutEngine._get_vector_alignment(struct_def.fields, field_def)
                if vector_align > type_align:
                    type_align = vector_align
                
                # Scalar or vector
                # Rule 1: Check type-specific alignment requirement
                if offset % type_align != 0:
                    # Need padding to meet alignment
                    pad_size = type_align - (offset % type_align)
                    fields.append(LayoutField(
                        name=f"_auto_pad{pad_counter}",
                        type_name=f"padding_{pad_size}b",
                        size=pad_size,
                        offset=offset,
                        comment=f"Auto-inserted for alignment",
                        array_size=1,
                        is_padding=True
                    ))
                    offset += pad_size
                    pad_counter += 1
                
                # Rule 2: Check if this would cross a 16-byte row boundary
                end_offset = offset + type_size
                row_start = (offset // 16) * 16
                row_end = row_start + 16
                
                if end_offset > row_end:
                    # Would cross row boundary, align to next row
                    pad_size = row_end - offset
                    fields.append(LayoutField(
                        name=f"_auto_pad{pad_counter}",
                        type_name=f"padding_{pad_size}b",
                        size=pad_size,
                        offset=offset,
                        comment="Auto-inserted for 16-byte row alignment",
                        array_size=1,
                        is_padding=True
                    ))
                    offset = row_end
                    pad_counter += 1
                
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
    
    @staticmethod
    def _get_vector_alignment(fields: List[FieldDef], current_field: FieldDef) -> int:
        """
        Check if current field starts a vector pattern (X followed by Y).
        Returns the alignment requirement for the vector type (8 for float2, 16 for float3).
        Returns 0 if not a vector start.
        """
        if current_field.type_name != 'float':
            return 0
        
        # Check for X suffix pattern
        base_name = None
        if current_field.name.endswith('_X'):
            base_name = current_field.name[:-2]  # Remove '_X'
        elif current_field.name.endswith('X') and not current_field.name.endswith('_X'):
            base_name = current_field.name[:-1]  # Remove 'X'
        
        if not base_name:
            return 0
        
        # Look for Y component in next field
        current_idx = fields.index(current_field)
        if current_idx + 1 < len(fields):
            next_field = fields[current_idx + 1]
            expected_y = f"{base_name}_Y" if current_field.name.endswith('_X') else f"{base_name}Y"
            if next_field.name == expected_y and next_field.type_name == 'float':
                # This is a float2 pattern
                return 8
        
        return 0


class CPPLayoutEngine:
    """
    C++ Layout Engine for HLSL Constant Buffer Interop.
    
    Strategy:
    - Use #pragma pack(16) to match HLSL 16-byte row alignment
    - Expand vectors to individual scalars for interop safety
    - Match HLSL byte offsets exactly (including alignment padding)
    """
    
    @staticmethod
    def calculate_layout(struct_def: StructDef) -> StructLayout:
        # C++ layout matches HLSL exactly in terms of offsets
        # But we expand vectors to individual scalars
        hlsl_layout = HLSLayoutEngine.calculate_layout(struct_def)
        
        cpp_fields = []
        for field in hlsl_layout.fields:
            if field.is_padding:
                # Padding field - represent as appropriate type
                if field.size == 4:
                    cpp_fields.append(LayoutField(
                        name=field.name,
                        type_name='float',
                        size=4,
                        offset=field.offset,
                        comment=field.comment,
                        array_size=1,
                        is_padding=True
                    ))
                elif field.size == 8:
                    cpp_fields.append(LayoutField(
                        name=f"{field.name}_lo",
                        type_name='float',
                        size=4,
                        offset=field.offset,
                        comment=field.comment,
                        array_size=1,
                        is_padding=True
                    ))
                    cpp_fields.append(LayoutField(
                        name=f"{field.name}_hi",
                        type_name='float',
                        size=4,
                        offset=field.offset + 4,
                        comment="",
                        array_size=1,
                        is_padding=True
                    ))
                elif field.size % 4 == 0:
                    # Multiple uints
                    for i in range(field.size // 4):
                        cpp_fields.append(LayoutField(
                            name=f"{field.name}_{i+1}",
                            type_name='float',
                            size=4,
                            offset=field.offset + i * 4,
                            comment=field.comment if i == 0 else "",
                            array_size=1,
                            is_padding=True
                        ))
                continue
            
            if field.array_size > 1:
                # Array - expand elements
                type_size = field.size // field.array_size
                for i in range(field.array_size):
                    cpp_fields.append(LayoutField(
                        name=f"{field.name}[{i}]",
                        type_name=field.type_name,
                        size=type_size,
                        offset=field.offset + i * type_size,
                        comment=field.comment if i == 0 else "",
                        array_size=1
                    ))
            else:
                # Expand vectors to individual components
                expanded = CPPLayoutEngine._expand_field(field)
                cpp_fields.extend(expanded)
        
        return StructLayout(
            name=struct_def.name,
            total_size=hlsl_layout.total_size,
            alignment=struct_def.size_align,
            fields=cpp_fields
        )
    
    @staticmethod
    def _expand_field(field: LayoutField) -> List[LayoutField]:
        """Expand a field to individual scalars for C++ interop."""
        if field.type_name == 'float2':
            return [
                LayoutField(f"{field.name}_x", 'float', 4, field.offset, field.comment),
                LayoutField(f"{field.name}_y", 'float', 4, field.offset + 4, ""),
            ]
        elif field.type_name == 'float3':
            return [
                LayoutField(f"{field.name}_x", 'float', 4, field.offset, field.comment),
                LayoutField(f"{field.name}_y", 'float', 4, field.offset + 4, ""),
                LayoutField(f"{field.name}_z", 'float', 4, field.offset + 8, ""),
            ]
        elif field.type_name == 'float4':
            return [
                LayoutField(f"{field.name}_x", 'float', 4, field.offset, field.comment),
                LayoutField(f"{field.name}_y", 'float', 4, field.offset + 4, ""),
                LayoutField(f"{field.name}_z", 'float', 4, field.offset + 8, ""),
                LayoutField(f"{field.name}_w", 'float', 4, field.offset + 12, ""),
            ]
        elif field.type_name == 'float4x4':
            result = []
            for row in range(4):
                for col in range(4):
                    elem_offset = field.offset + (row * 4 + col) * 4
                    comment = field.comment if row == 0 and col == 0 else ""
                    result.append(LayoutField(
                        f"{field.name}_m{row}{col}", 'float', 4, elem_offset, comment
                    ))
            return result
        else:
            # Scalar types
            cpp_type = {
                'float': 'float',
                'int': 'int32_t',
                'uint': 'uint32_t',
                'bool': 'bool',
            }.get(field.type_name, field.type_name)
            
            return [LayoutField(field.name, cpp_type, field.size, field.offset, field.comment)]


class CSLayoutEngine:
    """
    C# Layout Engine for HLSL Constant Buffer Interop.
    
    Strategy:
    - Use [StructLayout(LayoutKind.Sequential, Pack = 16)]
    - Expand vectors to individual scalars (like C++)
    - Match HLSL byte offsets exactly (including alignment padding)
    """
    
    @staticmethod
    def calculate_layout(struct_def: StructDef, use_unity_types: bool = True) -> StructLayout:
        # C# layout matches HLSL exactly in terms of offsets
        hlsl_layout = HLSLayoutEngine.calculate_layout(struct_def)
        
        cs_fields = []
        for field in hlsl_layout.fields:
            if field.is_padding:
                # Padding field - represent as uint
                if field.size == 4:
                    cs_fields.append(LayoutField(
                        name=field.name,
                        type_name='uint',
                        size=4,
                        offset=field.offset,
                        comment=field.comment,
                        array_size=1,
                        is_padding=True
                    ))
                elif field.size % 4 == 0:
                    # Multiple uints
                    for i in range(field.size // 4):
                        cs_fields.append(LayoutField(
                            name=f"{field.name}_{i+1}",
                            type_name='uint',
                            size=4,
                            offset=field.offset + i * 4,
                            comment=field.comment if i == 0 else "",
                            array_size=1,
                            is_padding=True
                        ))
                continue
            
            if field.array_size > 1:
                # Array - expand elements  
                type_size = field.size // field.array_size
                for i in range(field.array_size):
                    cs_fields.append(LayoutField(
                        name=f"{field.name}[{i}]",
                        type_name=field.type_name,
                        size=type_size,
                        offset=field.offset + i * type_size,
                        comment=field.comment if i == 0 else "",
                        array_size=1
                    ))
            else:
                # Expand to C# representation
                expanded = CSLayoutEngine._expand_field(field, use_unity_types)
                cs_fields.extend(expanded)
        
        return StructLayout(
            name=struct_def.name,
            total_size=hlsl_layout.total_size,
            alignment=struct_def.size_align,
            fields=cs_fields
        )
    
    @staticmethod
    def _expand_field(field: LayoutField, use_unity_types: bool) -> List[LayoutField]:
        """Expand a field to C# representation."""
        if use_unity_types:
            if field.type_name == 'float4':
                return [LayoutField(field.name, 'Vector4', 16, field.offset, field.comment)]
            elif field.type_name == 'float2':
                # Still expand float2 for CB interop safety
                return [
                    LayoutField(f"{field.name}X", 'float', 4, field.offset, field.comment),
                    LayoutField(f"{field.name}Y", 'float', 4, field.offset + 4, ""),
                ]
            elif field.type_name == 'float3':
                return [
                    LayoutField(f"{field.name}X", 'float', 4, field.offset, field.comment),
                    LayoutField(f"{field.name}Y", 'float', 4, field.offset + 4, ""),
                    LayoutField(f"{field.name}Z", 'float', 4, field.offset + 8, ""),
                ]
        
        # Expand to individual floats
        if field.type_name == 'float2':
            return [
                LayoutField(f"{field.name}X", 'float', 4, field.offset, field.comment),
                LayoutField(f"{field.name}Y", 'float', 4, field.offset + 4, ""),
            ]
        elif field.type_name == 'float3':
            return [
                LayoutField(f"{field.name}X", 'float', 4, field.offset, field.comment),
                LayoutField(f"{field.name}Y", 'float', 4, field.offset + 4, ""),
                LayoutField(f"{field.name}Z", 'float', 4, field.offset + 8, ""),
            ]
        elif field.type_name == 'float4' and not use_unity_types:
            return [
                LayoutField(f"{field.name}X", 'float', 4, field.offset, field.comment),
                LayoutField(f"{field.name}Y", 'float', 4, field.offset + 4, ""),
                LayoutField(f"{field.name}Z", 'float', 4, field.offset + 8, ""),
                LayoutField(f"{field.name}W", 'float', 4, field.offset + 12, ""),
            ]
        elif field.type_name == 'float4x4':
            result = []
            for row in range(4):
                for col in range(4):
                    elem_offset = field.offset + (row * 4 + col) * 4
                    comment = field.comment if row == 0 and col == 0 else ""
                    result.append(LayoutField(
                        f"{field.name}_m{row}{col}", 'float', 4, elem_offset, comment
                    ))
            return result
        else:
            # Scalar types
            cs_type = {
                'float': 'float',
                'int': 'int',
                'uint': 'uint',
                'bool': 'int',  # C# bool is 1 byte, use int for HLSL interop
            }.get(field.type_name, field.type_name)
            
            return [LayoutField(field.name, cs_type, field.size, field.offset, field.comment)]


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
        
        return (len(issues) == 0, issues)
