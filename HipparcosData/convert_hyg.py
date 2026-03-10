#!/usr/bin/env python3
"""
Convert HYG v42 CSV to CinematicShaders binary star catalog format.

Input: hyg_v42.csv (full catalog with all columns)
Output: hyg_v42.bin (full), hyg_v42_80k.bin, hyg_v42_50k.bin, hyg_v42_20k.bin

CSV columns expected: id,hip,hd,hr,gl,bf,proper,ra,dec,dist,pmra,pmdec,rv,mag,absmag,spect,ci,x,y,z,vx,vy,vz,rarad,decrad,pmrarad,pmdecrad,bayer,flam,con,comp,comp_primary,base,lum,var,var_min,var_max

Uses only: id (stored but not used), x, y, z, mag, ci, dist, proper
"""
import csv
import struct
import os
import math
from datetime import datetime

# Constants
HEADER_SIZE = 256
STAR_SIZE = 48  # 12 values × 4 bytes = 48 bytes (ID, Dist, Spectral, Flags, DirXYZ, Mag, RGB, Temp)
MAGIC = 0x53545243  # 'STRC'
VERSION = 4  # Version 4: Added Flags field (IsHero for named/generated stars)

# Flags bitfield
FLAG_IS_HERO = 1  # Bit 0: Star can be named/is important enough for billboard PSF

def blackbody_rgb(temperature):
    """
    Convert temperature to RGB using Tanner Helland's algorithm.
    Valid range: 1000K - 40000K
    Returns: (r, g, b) tuple with values 0-1
    """
    t = max(1000.0, min(40000.0, temperature))
    tmp = t / 100.0
    
    # Red
    if tmp <= 66.0:
        r = 255.0
    else:
        r = 329.698727446 * pow(tmp - 60.0, -0.1332047592)
        r = max(0.0, min(255.0, r))
    
    # Green
    if tmp <= 66.0:
        g = 99.4708025861 * math.log(tmp) - 161.1195681661
        g = max(0.0, min(255.0, g))
    else:
        g = 288.1221695283 * pow(tmp - 60.0, -0.0755148492)
        g = max(0.0, min(255.0, g))
    
    # Blue
    if tmp >= 66.0:
        b = 255.0
    elif tmp <= 19.0:
        b = 0.0
    else:
        b = 138.5177312231 * math.log(tmp - 10.0) - 305.0447927307
        b = max(0.0, min(255.0, b))
    
    return (r / 255.0, g / 255.0, b / 255.0)

def spectral_type_to_enum(spectral):
    """Convert spectral type string to enum value."""
    if not spectral:
        return 255  # Unknown
    
    s = spectral[0].upper()
    spectral_map = {
        'O': 0,
        'B': 1,
        'A': 2,
        'F': 3,
        'G': 4,
        'K': 5,
        'M': 6,
        'L': 7,
    }
    return spectral_map.get(s, 255)  # 255 for unknown/other

def color_index_to_temp(ci):
    """
    Convert B-V color index to temperature using Ballesteros formula.
    T = 4600 * (1/(0.92*B-V + 1.7) + 1/(0.92*B-V + 0.62))
    """
    if ci is None or ci == '':
        return 5778.0  # Sun default
    
    try:
        bv = float(ci)
        # Clamp to valid range
        bv = max(-0.5, min(3.0, bv))
        
        # Ballesteros formula
        t = 4600.0 * (1.0 / (0.92 * bv + 1.7) + 1.0 / (0.92 * bv + 0.62))
        
        # Clamp output
        return max(1000.0, min(40000.0, t))
    except (ValueError, ZeroDivisionError):
        return 5778.0

def normalize_direction(x, y, z):
    """Normalize vector to unit length."""
    length = math.sqrt(x*x + y*y + z*z)
    if length < 0.0001:
        return (0.0, 0.0, 1.0)
    return (x/length, y/length, z/length)

def parse_star(row):
    """
    Parse a CSV row into star data tuple.
    Returns: (hip_id, dist_pc, spectral_enum, flags, dx, dy, dz, mag, r, g, b, temp) or None if filtered
    """
    try:
        # Skip Sun
        if row.get('proper') == 'Sol':
            return None
        
        # Check distance (filter bad parallax)
        dist_str = row.get('dist', '')
        if dist_str:
            try:
                dist = float(dist_str)
                if dist >= 100000:  # Bad parallax
                    return None
            except ValueError:
                pass  # Continue if dist is invalid
        
        # Get position
        x = float(row['x'])
        y = float(row['y'])
        z = float(row['z'])
        
        # Skip if no position data
        if abs(x) < 0.0001 and abs(y) < 0.0001 and abs(z) < 0.0001:
            return None
        
        # Get magnitude
        mag = float(row['mag'])
        
        # Calculate temperature from color index
        ci = row.get('ci', '')
        temp = color_index_to_temp(ci)
        
        # Get blackbody color
        r, g, b = blackbody_rgb(temp)
        
        # Normalize direction
        dx, dy, dz = normalize_direction(x, y, z)
        
        # Get Hipparcos ID (column 'id' or 'hip')
        hip_id = 0
        if 'id' in row and row['id']:
            try:
                hip_id = int(row['id'])
            except ValueError:
                hip_id = 0
        elif 'hip' in row and row['hip']:
            try:
                hip_id = int(row['hip'])
            except ValueError:
                hip_id = 0
        
        # Get distance in parsecs
        dist_pc = 0.0
        if 'dist' in row and row['dist']:
            try:
                dist_pc = float(row['dist'])
            except ValueError:
                dist_pc = 0.0
        
        # Get spectral type
        spectral = row.get('spect', '')
        spectral_enum = spectral_type_to_enum(spectral)
        
        # Set IsHero flag for named stars (proper name exists and not Sol)
        flags = 0
        proper = row.get('proper', '').strip()
        if proper and proper != 'Sol':
            flags |= FLAG_IS_HERO
        
        return (hip_id, dist_pc, spectral_enum, flags, dx, dy, dz, mag, r, g, b, temp)
        
    except (KeyError, ValueError) as e:
        # Missing required field or bad number
        return None

def write_catalog(filename, stars, display_name, read_only=True):
    """
    Write stars to binary catalog file.
    
    Header format (256 bytes total):
    - Offset 0:   Magic (4 bytes) - 'STRC'
    - Offset 4:   Version (2 bytes) - 1
    - Offset 6:   Flags (2 bytes) - Bit 0=ReadOnly
    - Offset 8:   StarCount (4 bytes)
    - Offset 12:  HeroCount (4 bytes) - 0 for real sky
    - Offset 16:  GenerationSeed (4 bytes) - 42 for real sky
    - Offset 20:  GenParams (32 bytes) - 8 floats, all 0 for real sky
    - Offset 52:  DisplayName (64 bytes) - UTF-8, null-padded
    - Offset 116: Date (32 bytes) - ISO-8601 timestamp
    - Offset 148: Reserved (108 bytes) - zeros
    
    Star records (48 bytes each):
    - HipparcosID (1 int32)
    - DistancePc (1 float) - distance in parsecs
    - SpectralType (1 int32) - 0=O,1=B,2=A,3=F,4=G,5=K,6=M,7=L,255=Unknown
    - Flags (1 uint32) - Bit 0=IsHero (can be named)
    - DirectionX, DirectionY, DirectionZ, Magnitude (4 floats)
    - ColorR, ColorG, ColorB, Temperature (4 floats)
    """
    
    with open(filename, 'wb') as f:
        # Write header
        
        # Magic (4 bytes)
        f.write(struct.pack('<I', MAGIC))
        
        # Version (2 bytes)
        f.write(struct.pack('<H', VERSION))
        
        # Flags (2 bytes) - Read-only
        flags = 1 if read_only else 0
        f.write(struct.pack('<H', flags))
        
        # Star count (4 bytes)
        f.write(struct.pack('<i', len(stars)))
        
        # Hero count (4 bytes) - 0 for real sky
        f.write(struct.pack('<i', 0))
        
        # Generation seed (4 bytes) - 42 for real sky
        f.write(struct.pack('<i', 42))
        
        # Generation params (32 bytes = 8 floats) - all zeros for real sky
        for _ in range(8):
            f.write(struct.pack('<f', 0.0))
        
        # Display name (64 bytes) - UTF-8, null-padded
        name_bytes = display_name.encode('utf-8')[:63]
        f.write(name_bytes + b'\x00' * (64 - len(name_bytes)))
        
        # Date (32 bytes) - ISO-8601 timestamp
        date_str = datetime.now().isoformat()
        date_bytes = date_str.encode('utf-8')[:31]
        f.write(date_bytes + b'\x00' * (32 - len(date_bytes)))
        
        # Reserved (108 bytes) - zeros to reach 256 byte header
        f.write(b'\x00' * 108)
        
        # Write star data (48 bytes each)
        for star in stars:
            hip_id, dist_pc, spectral_enum, flags, dx, dy, dz, mag, r, g, b, temp = star
            f.write(struct.pack('<ifiiffffffff', hip_id, dist_pc, spectral_enum, flags, dx, dy, dz, mag, r, g, b, temp))
    
    file_size = os.path.getsize(filename)
    expected_size = HEADER_SIZE + (len(stars) * STAR_SIZE)
    print(f"Wrote {len(stars)} stars to {filename}")
    print(f"  File size: {file_size} bytes (expected: {expected_size})")

def main():
    # Try current directory first, then subdirectory
    csv_path = 'hyg_v42.csv'
    if not os.path.exists(csv_path):
        csv_path = os.path.join('hyg_v42csv', 'hyg_v42.csv')
    
    if not os.path.exists(csv_path):
        print(f"Error: {csv_path} not found")
        print("Please download HYG v42 from https://github.com/astronexus/HYG-Database")
        return 1
    
    print("Reading HYG catalog...")
    stars = []
    skipped_bad_dist = 0
    skipped_sol = 0
    skipped_bad_pos = 0
    parse_errors = 0
    
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            # Pre-filter for statistics
            if row.get('proper') == 'Sol':
                skipped_sol += 1
                continue
            
            try:
                dist = float(row.get('dist', '0'))
                if dist >= 100000:
                    skipped_bad_dist += 1
                    continue
            except ValueError:
                pass
            
            # Full parse
            star = parse_star(row)
            if star:
                stars.append((star, star[7]))  # (data, mag for sorting) - mag is now at index 7
            else:
                skipped_bad_pos += 1
    
    print(f"\nProcessing complete:")
    print(f"  Valid stars: {len(stars)}")
    print(f"  Skipped (Sol): {skipped_sol}")
    print(f"  Skipped (bad parallax): {skipped_bad_dist}")
    print(f"  Skipped (bad position): {skipped_bad_pos}")
    
    if len(stars) == 0:
        print("Error: No valid stars found!")
        return 1
    
    # Sort by magnitude (brightest first - lower magnitude = brighter)
    stars.sort(key=lambda x: x[1])
    
    # Extract just the star data
    all_stars = [s[0] for s in stars]
    
    # Create output files
    print("\nWriting catalog files...")
    
    # Full catalog
    write_catalog('hyg_v42.bin', all_stars, 'HYG v42 Full Catalog (Real Sky)', read_only=True)
    
    # 80k brightest
    if len(all_stars) >= 80000:
        write_catalog('hyg_v42_80k.bin', all_stars[:80000], 'HYG v42 Brightest 80k', read_only=True)
    
    # 50k brightest
    if len(all_stars) >= 50000:
        write_catalog('hyg_v42_50k.bin', all_stars[:50000], 'HYG v42 Brightest 50k', read_only=True)
    
    # 20k brightest
    if len(all_stars) >= 20000:
        write_catalog('hyg_v42_20k.bin', all_stars[:20000], 'HYG v42 Brightest 20k', read_only=True)
    
    print("\n" + "="*60)
    print("Magnitude ranges:")
    print(f"  Full:     {stars[0][1]:.2f} to {stars[-1][1]:.2f}")
    if len(stars) >= 80000:
        print(f"  Top 80k:  {stars[0][1]:.2f} to {stars[79999][1]:.2f}")
    if len(stars) >= 50000:
        print(f"  Top 50k:  {stars[0][1]:.2f} to {stars[49999][1]:.2f}")
    if len(stars) >= 20000:
        print(f"  Top 20k:  {stars[0][1]:.2f} to {stars[19999][1]:.2f}")
    
    print("\nDone!")
    return 0

if __name__ == '__main__':
    exit(main())
