#!/usr/bin/env python3
"""
Check generated catalog for new fields (ID, Distance, SpectralType).
"""
import struct
import sys

def read_catalog(filename):
    with open(filename, 'rb') as f:
        # Header
        magic = struct.unpack('<I', f.read(4))[0]
        version = struct.unpack('<H', f.read(2))[0]
        flags = struct.unpack('<H', f.read(2))[0]
        star_count = struct.unpack('<i', f.read(4))[0]
        
        print(f"File: {filename}")
        print(f"Version: {version}")
        print(f"Star count: {star_count}")
        
        if version < 4:
            print("ERROR: Version < 4, new fields not present")
            return
        
        # Read first few stars
        f.seek(256)  # Skip header
        
        print("\nFirst 5 stars:")
        print(f"{'ID':>6} | {'Dist(pc)':>10} | {'Spec':>4} | {'Flags':>5} | {'DirX':>8} | {'Mag':>6}")
        print("-" * 65)
        
        spectral_names = {0:'O', 1:'B', 2:'A', 3:'F', 4:'G', 5:'K', 6:'M', 7:'L', 255:'?'}
        
        for i in range(min(5, star_count)):
            hip_id = struct.unpack('<i', f.read(4))[0]
            dist = struct.unpack('<f', f.read(4))[0]
            spec = struct.unpack('<i', f.read(4))[0]
            flags_val = struct.unpack('<I', f.read(4))[0]
            dx = struct.unpack('<f', f.read(4))[0]
            mag = struct.unpack('<f', f.read(4))[0]
            # Skip rest (32 bytes total for these fields)
            f.seek(48 - 24, 1)
            
            spec_name = spectral_names.get(spec, '?')
            print(f"{hip_id:>6} | {dist:>10.2f} | {spec_name:>4} | {flags_val:>5} | {dx:>8.4f} | {mag:>6.2f}")

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print("Usage: python check_generated_fields.py <catalog.bin>")
        sys.exit(1)
    
    read_catalog(sys.argv[1])
