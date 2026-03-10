#!/usr/bin/env python3
"""
Validate binary star catalog files.
Can compare two files or inspect a single file's header.
"""
import struct
import sys
import os

HEADER_SIZE = 256
STAR_SIZE = 48  # Version 4: includes Hipparcos ID, Distance, SpectralType, Flags
MAGIC = 0x53545243

def read_header(f):
    """Read and parse catalog header."""
    f.seek(0)
    
    data = {}
    data['magic'] = struct.unpack('<I', f.read(4))[0]
    data['version'] = struct.unpack('<H', f.read(2))[0]
    data['flags'] = struct.unpack('<H', f.read(2))[0]
    data['star_count'] = struct.unpack('<i', f.read(4))[0]
    data['hero_count'] = struct.unpack('<i', f.read(4))[0]
    data['seed'] = struct.unpack('<i', f.read(4))[0]
    
    # Gen params (8 floats)
    data['gen_params'] = struct.unpack('<8f', f.read(32))
    
    # Display name (64 bytes)
    name_bytes = f.read(64)
    data['display_name'] = name_bytes.split(b'\x00')[0].decode('utf-8', errors='ignore')
    
    # Date (32 bytes)
    date_bytes = f.read(32)
    data['date'] = date_bytes.split(b'\x00')[0].decode('utf-8', errors='ignore')
    
    # Check position
    data['header_end'] = f.tell()
    
    return data

def read_star(f, index):
    """Read a single star record."""
    offset = HEADER_SIZE + (index * STAR_SIZE)
    f.seek(offset)
    
    hip_id, dist_pc, spectral, flags, dx, dy, dz, mag, r, g, b, temp = struct.unpack('<ifiiffffffff', f.read(48))
    return {
        'hip_id': hip_id,
        'dist_pc': dist_pc,
        'spectral': spectral,
        'flags': flags,
        'dir': (dx, dy, dz),
        'mag': mag,
        'color': (r, g, b),
        'temp': temp
    }

def validate_file(filepath):
    """Validate a single catalog file."""
    print(f"Validating: {filepath}")
    print("=" * 60)
    
    if not os.path.exists(filepath):
        print(f"ERROR: File not found: {filepath}")
        return False
    
    file_size = os.path.getsize(filepath)
    
    with open(filepath, 'rb') as f:
        # Read header
        header = read_header(f)
        
        # Check magic
        magic_str = f"{header['magic']:08X}"
        expected_magic = f"{MAGIC:08X}"
        print(f"Magic:           0x{magic_str} ({'OK' if header['magic'] == MAGIC else 'BAD'})")
        print(f"Version:         {header['version']}")
        print(f"Flags:           0x{header['flags']:04X} (ReadOnly: {bool(header['flags'] & 1)})")
        print(f"Star Count:      {header['star_count']}")
        print(f"Hero Count:      {header['hero_count']}")
        print(f"Seed:            {header['seed']}")
        print(f"Display Name:    '{header['display_name']}'")
        print(f"Date:            '{header['date']}'")
        # Note: header_end is where we stopped reading, reserved section is zeros
        print(f"Header read to:  {header['header_end']} bytes (reserved section: {256 - header['header_end']} bytes)")
        
        # Validate file size
        expected_size = HEADER_SIZE + (header['star_count'] * STAR_SIZE)
        print(f"\nFile size:       {file_size} bytes")
        print(f"Expected size:   {expected_size} bytes")
        print(f"Size check:      {'OK' if file_size == expected_size else 'MISMATCH'}")
        
        # Sample some stars
        if header['star_count'] > 0:
            print(f"\nSample stars:")
            for i in [0, min(4, header['star_count']-1), header['star_count']//2, header['star_count']-1]:
                star = read_star(f, i)
                dx, dy, dz = star['dir']
                r, g, b = star['color']
                hip = star.get('hip_id', 0)
                dist = star.get('dist_pc', 0)
                spec = star.get('spectral', 255)
                flags = star.get('flags', 0)
                is_hero = '*' if (flags & 1) else ' '
                spectral_names = {0:'O', 1:'B', 2:'A', 3:'F', 4:'G', 5:'K', 6:'M', 7:'L', 255:'?'}
                spec_name = spectral_names.get(spec, '?')
                print(f"  [{i:6d}]{is_hero}HIP={hip:6d} {spec_name} {dist:7.2f}pc "
                      f"dir=({dx:7.4f},{dy:7.4f},{dz:7.4f}) mag={star['mag']:6.2f} "
                      f"T={star['temp']:6.0f}K")
        
        # Validation summary
        print("\n" + "=" * 60)
        valid = True
        if header['magic'] != MAGIC:
            print("FAIL: Invalid magic number")
            valid = False
        if file_size != expected_size:
            print(f"FAIL: File size mismatch ({file_size} vs {expected_size})")
            valid = False
        
        if valid:
            print("VALIDATION PASSED")
        else:
            print("VALIDATION FAILED")
        
        return valid

def compare_files(file1, file2):
    """Compare two catalog files."""
    print(f"Comparing:")
    print(f"  File 1: {file1}")
    print(f"  File 2: {file2}")
    print("=" * 60)
    
    if not os.path.exists(file1):
        print(f"ERROR: File not found: {file1}")
        return False
    if not os.path.exists(file2):
        print(f"ERROR: File not found: {file2}")
        return False
    
    with open(file1, 'rb') as f1, open(file2, 'rb') as f2:
        h1 = read_header(f1)
        h2 = read_header(f2)
        
        # Compare headers
        print("\nHeader comparison:")
        headers_match = True
        
        if h1['magic'] != h2['magic']:
            print(f"  Magic: {h1['magic']:08X} vs {h2['magic']:08X} DIFFERENT")
            headers_match = False
        else:
            print(f"  Magic: OK")
        
        if h1['version'] != h2['version']:
            print(f"  Version: {h1['version']} vs {h2['version']} DIFFERENT")
            headers_match = False
        else:
            print(f"  Version: OK ({h1['version']})")
        
        if h1['star_count'] != h2['star_count']:
            print(f"  Star Count: {h1['star_count']} vs {h2['star_count']} DIFFERENT")
            headers_match = False
        else:
            print(f"  Star Count: OK ({h1['star_count']})")
        
        print(f"  Display Name: '{h1['display_name']}' vs '{h2['display_name']}'")
        
        # Compare star data
        count = min(h1['star_count'], h2['star_count'])
        print(f"\nComparing {count} stars...")
        
        differences = 0
        max_diff_show = 5
        
        for i in range(count):
            s1 = read_star(f1, i)
            s2 = read_star(f2, i)
            
            # Compare with tolerance for floats
            tol = 0.0001
            diff = False
            diff_fields = []
            
            if abs(s1['dir'][0] - s2['dir'][0]) > tol: diff_fields.append('dx')
            if abs(s1['dir'][1] - s2['dir'][1]) > tol: diff_fields.append('dy')
            if abs(s1['dir'][2] - s2['dir'][2]) > tol: diff_fields.append('dz')
            if abs(s1['mag'] - s2['mag']) > tol: diff_fields.append('mag')
            if abs(s1['color'][0] - s2['color'][0]) > tol: diff_fields.append('r')
            if abs(s1['color'][1] - s2['color'][1]) > tol: diff_fields.append('g')
            if abs(s1['color'][2] - s2['color'][2]) > tol: diff_fields.append('b')
            if abs(s1['temp'] - s2['temp']) > tol: diff_fields.append('temp')
            
            if diff_fields:
                differences += 1
                if differences <= max_diff_show:
                    print(f"  Star {i}: DIFFERENT fields: {', '.join(diff_fields)}")
                    print(f"    File1: dir={s1['dir']}, mag={s1['mag']:.2f}, rgb={s1['color']}, T={s1['temp']:.0f}")
                    print(f"    File2: dir={s2['dir']}, mag={s2['mag']:.2f}, rgb={s2['color']}, T={s2['temp']:.0f}")
        
        if differences > max_diff_show:
            print(f"  ... and {differences - max_diff_show} more differences")
        
        print(f"\nTotal differences: {differences} / {count}")
        
        if differences == 0 and headers_match:
            print("FILES ARE IDENTICAL")
            return True
        else:
            print("FILES DIFFER")
            return False

def main():
    if len(sys.argv) < 2:
        print("Usage:")
        print(f"  {sys.argv[0]} <catalog.bin>           - Validate single file")
        print(f"  {sys.argv[0]} --compare <f1> <f2>     - Compare two files")
        return 1
    
    if sys.argv[1] == '--compare':
        if len(sys.argv) < 4:
            print("Usage: --compare requires two files")
            return 1
        compare_files(sys.argv[2], sys.argv[3])
    else:
        validate_file(sys.argv[1])
    
    return 0

if __name__ == '__main__':
    exit(main())
