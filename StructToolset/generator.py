#!/usr/bin/env python3
"""
Struct Generator Toolset
========================
Main entry point for struct generation.

Usage:
    python generator.py                    # Generate all structs
    python generator.py --struct NAME      # Generate specific struct
    python generator.py --verify           # Verify existing files
    python generator.py --dry-run          # Show what would be generated

Output:
    Generated files are written to ./generated/ for manual copy-paste
"""

import sys
import os
import argparse

# Add lib folder for PyYAML
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'lib'))

try:
    import yaml
except ImportError:
    print("ERROR: PyYAML not found. Run: python -m pip install pyyaml -t lib")
    sys.exit(1)

from layout_engine import (
    StructDef, FieldDef, 
    HLSLayoutEngine, CPPLayoutEngine, CSLayoutEngine,
    LayoutVerifier
)
from generators import CPPGenerator, HLSLGenerator, CSGenerator


def load_schema(schema_path: str) -> list:
    """Load struct definitions from YAML schema file."""
    with open(schema_path, 'r') as f:
        data = yaml.safe_load(f)
    
    structs = []
    for struct_data in data.get('structs', []):
        fields = []
        for field_data in struct_data.get('fields', []):
            fields.append(FieldDef(
                name=field_data['name'],
                type_name=field_data['type'],
                comment=field_data.get('comment', ''),
                array_size=field_data.get('array_size', 1)
            ))
        
        structs.append(StructDef(
            name=struct_data['name'],
            size_align=struct_data.get('size_align', 16),
            fields=fields
        ))
    
    return structs


def generate_struct(struct_def: StructDef, output_dir: str, dry_run: bool = False):
    """Generate all three language outputs for a struct."""
    print(f"\n{'='*80}")
    print(f"Generating: {struct_def.name}")
    print(f"{'='*80}")
    
    # Calculate layouts
    hlsl_layout = HLSLayoutEngine.calculate_layout(struct_def)
    cpp_layout = CPPLayoutEngine.calculate_layout(struct_def)
    cs_layout = CSLayoutEngine.calculate_layout(struct_def, use_unity_types=True)
    
    # Verify compatibility
    is_valid, issues = LayoutVerifier.verify(hlsl_layout, cpp_layout, cs_layout)
    if not is_valid:
        print("WARNING: Layout verification failed:")
        for issue in issues:
            print(f"  - {issue}")
    else:
        print(f"  Layout verification: PASSED (size={hlsl_layout.total_size} bytes)")
    
    # Generate code
    cpp_code = CPPGenerator.generate(struct_def, cpp_layout)
    hlsl_code = HLSLGenerator.generate(struct_def, hlsl_layout)
    cs_code = CSGenerator.generate(struct_def, cs_layout, use_unity_types=True)
    
    if dry_run:
        print("\n--- C++ ----------------------------------------------------------------------")
        print(cpp_code[:500] + "..." if len(cpp_code) > 500 else cpp_code)
        print("\n--- HLSL ----------------------------------------------------------------------")
        print(hlsl_code[:500] + "..." if len(hlsl_code) > 500 else hlsl_code)
        print("\n--- C# ------------------------------------------------------------------------")
        print(cs_code[:500] + "..." if len(cs_code) > 500 else cs_code)
        return
    
    # Write output files
    os.makedirs(output_dir, exist_ok=True)
    
    cpp_path = os.path.join(output_dir, f"{struct_def.name}_cpp.h")
    hlsl_path = os.path.join(output_dir, f"{struct_def.name}_hlsl.hlsl")
    cs_path = os.path.join(output_dir, f"{struct_def.name}_cs.cs")
    
    with open(cpp_path, 'w') as f:
        f.write(cpp_code)
    with open(hlsl_path, 'w') as f:
        f.write(hlsl_code)
    with open(cs_path, 'w') as f:
        f.write(cs_code)
    
    print(f"  Written: {cpp_path}")
    print(f"  Written: {hlsl_path}")
    print(f"  Written: {cs_path}")
    
    # Also print summary
    print(f"\n  Summary:")
    print(f"    - Total size: {hlsl_layout.total_size} bytes")
    print(f"    - Field count: {len(struct_def.fields)}")
    print(f"\n  Copy-paste locations:")
    print(f"    C++   -> NativePlugin/include/{struct_def.name}_generated.h")
    print(f"    HLSL  -> NativePlugin/shaders/ (cbuffer section)")
    print(f"    C#    -> Native/StarfieldNative.cs (or separate file)")


def main():
    parser = argparse.ArgumentParser(description='Struct Generator Toolset')
    parser.add_argument('--struct', help='Generate specific struct only')
    parser.add_argument('--verify', action='store_true', help='Verify existing files')
    parser.add_argument('--dry-run', action='store_true', help='Show output without writing')
    parser.add_argument('--schema', default='structs.yaml', help='Schema file path')
    parser.add_argument('--output', default='generated', help='Output directory')
    
    args = parser.parse_args()
    
    # Find schema file
    script_dir = os.path.dirname(os.path.abspath(__file__))
    schema_path = os.path.join(script_dir, args.schema)
    
    if not os.path.exists(schema_path):
        print(f"ERROR: Schema file not found: {schema_path}")
        print(f"Create {args.schema} with your struct definitions.")
        sys.exit(1)
    
    # Load structs
    try:
        structs = load_schema(schema_path)
    except Exception as e:
        print(f"ERROR: Failed to load schema: {e}")
        sys.exit(1)
    
    if not structs:
        print("No structs defined in schema.")
        sys.exit(0)
    
    print(f"Loaded {len(structs)} struct(s) from {schema_path}")
    
    # Filter if specific struct requested
    if args.struct:
        structs = [s for s in structs if s.name == args.struct]
        if not structs:
            print(f"ERROR: Struct '{args.struct}' not found in schema.")
            sys.exit(1)
    
    # Generate
    output_dir = os.path.join(script_dir, args.output)
    
    for struct in structs:
        generate_struct(struct, output_dir, dry_run=args.dry_run)
    
    print(f"\n{'='*80}")
    print("Generation complete!")
    print(f"{'='*80}")
    
    if not args.dry_run:
        print(f"\nGenerated files are in: {output_dir}")
        print("Copy-paste these into your source files as needed.")


if __name__ == "__main__":
    main()
