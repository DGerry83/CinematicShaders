#!/usr/bin/env python3
"""
Compare generated star distances to HYG catalog for similar stars.
"""
import csv
import struct
import os

HEADER_SIZE = 256
STAR_SIZE = 48
MAGIC = 0x53545243

SPECTRAL_NAMES = {0:'O', 1:'B', 2:'A', 3:'F', 4:'G', 5:'K', 6:'M', 7:'L', 255:'?'}

def read_generated_catalog(filepath):
    """Read generated catalog."""
    stars = []
    with open(filepath, 'rb') as f:
        magic = struct.unpack('<I', f.read(4))[0]
        version = struct.unpack('<H', f.read(2))[0]
        flags = struct.unpack('<H', f.read(2))[0]
        star_count = struct.unpack('<i', f.read(4))[0]
        
        f.seek(HEADER_SIZE)
        for i in range(star_count):
            hip = struct.unpack('<i', f.read(4))[0]
            dist = struct.unpack('<f', f.read(4))[0]
            spec = struct.unpack('<i', f.read(4))[0]
            flags_val = struct.unpack('<I', f.read(4))[0]
            dx = struct.unpack('<f', f.read(4))[0]
            dy = struct.unpack('<f', f.read(4))[0]
            dz = struct.unpack('<f', f.read(4))[0]
            mag = struct.unpack('<f', f.read(4))[0]
            f.read(16)  # Skip color + temp
            
            stars.append({
                'id': hip,
                'dist': dist,
                'spectral': spec,
                'is_hero': (flags_val & 1) != 0,
                'mag': mag,
                'index': i
            })
    return stars

def load_hyg_stars():
    """Load HYG stars with their distances."""
    csv_path = '../hyg_v42.csv'
    if not os.path.exists(csv_path):
        csv_path = '../hyg_v42csv/hyg_v42.csv'
    
    stars = []
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row.get('proper') == 'Sol':
                continue
            try:
                dist = float(row.get('dist', '0'))
                mag = float(row.get('mag', '99'))
                hip = int(row.get('hip', '0')) if row.get('hip') else 0
                
                spect = row.get('spect', '')
                spec_num = 255
                if spect:
                    s = spect[0].upper()
                    spec_map = {'O': 0, 'B': 1, 'A': 2, 'F': 3, 'G': 4, 'K': 5, 'M': 6}
                    spec_num = spec_map.get(s, 255)
                
                temp = 5778.0
                if row.get('ci'):
                    try:
                        bv = float(row['ci'])
                        # Ballesteros formula approximation
                        temp = 4600 * (1/(0.92*bv + 1.7) + 1/(0.92*bv + 0.62))
                    except:
                        pass
                
                if dist < 100000 and dist > 0:  # Valid parallax
                    stars.append({
                        'hip': hip,
                        'dist': dist,
                        'mag': mag,
                        'spectral': spec_num,
                        'temp': temp,
                        'name': row.get('proper', '')
                    })
            except (ValueError, KeyError):
                continue
    return stars

def find_similar_hyg_stars(hyg_stars, mag_range, spectral_type, count=5):
    """Find HYG stars with similar magnitude and spectral type."""
    min_mag, max_mag = mag_range
    matches = []
    for s in hyg_stars:
        if min_mag <= s['mag'] <= max_mag and s['spectral'] == spectral_type:
            matches.append(s)
    
    # Sort by closest magnitude
    matches.sort(key=lambda x: abs(x['mag'] - (min_mag + max_mag)/2))
    return matches[:count]

def main():
    print("="*70)
    print("GENERATED vs HYG DISTANCE COMPARISON")
    print("="*70)
    print()
    
    # Load catalogs
    gen_stars = read_generated_catalog('../MyStarfield.bin')
    print(f"Loaded {len(gen_stars)} generated stars")
    
    hyg_stars = load_hyg_stars()
    print(f"Loaded {len(hyg_stars)} HYG stars with valid distances")
    print()
    
    # Pick sample stars from generated catalog
    samples = [
        (0, "Brightest hero"),
        (4, "Bright hero (blue)"),
        (100, "Dim hero"),
        (50000, "Middle range"),
        (99999, "Dimmest star"),
    ]
    
    for idx, desc in samples:
        gen = gen_stars[idx]
        spec_name = SPECTRAL_NAMES.get(gen['spectral'], '?')
        
        print(f"Generated Star #{idx} - {desc}")
        print(f"  ID: {gen['id']}, Type: {spec_name}, Mag: {gen['mag']:.2f}, Dist: {gen['dist']:.2f} pc")
        print(f"  Hero: {'Yes' if gen['is_hero'] else 'No'}")
        
        # Find similar HYG stars
        mag_range = (gen['mag'] - 0.5, gen['mag'] + 0.5)
        similar = find_similar_hyg_stars(hyg_stars, mag_range, gen['spectral'], 3)
        
        if similar:
            print(f"  Similar HYG stars (Mag {mag_range[0]:.1f} to {mag_range[1]:.1f}, Type {spec_name}):")
            for s in similar:
                name = f" ({s['name']})" if s['name'] else ""
                print(f"    HIP {s['hip']}{name}: Mag={s['mag']:.2f}, Dist={s['dist']:.2f} pc")
        else:
            # Try without spectral filter
            similar = find_similar_hyg_stars(hyg_stars, mag_range, None, 3)
            if similar:
                print(f"  Similar HYG stars by magnitude (any type):")
                for s in similar:
                    s_name = SPECTRAL_NAMES.get(s['spectral'], '?')
                    name = f" ({s['name']})" if s['name'] else ""
                    print(f"    HIP {s['hip']}{name}: Type={s_name}, Mag={s['mag']:.2f}, Dist={s['dist']:.2f} pc")
        
        print()
    
    # Statistical comparison
    print("="*70)
    print("STATISTICAL COMPARISON")
    print("="*70)
    print()
    
    # Group by magnitude bins
    bins = [
        (-3, -1, "Very bright (< -1)"),
        (-1, 2, "Bright (-1 to 2)"),
        (2, 5, "Medium (2 to 5)"),
        (5, 8, "Dim (5 to 8)"),
        (8, 15, "Very dim (> 8)"),
    ]
    
    print(f"{'Mag Range':<20} | {'Generated':<25} | {'HYG':<25}")
    print(f"{'':20} | {'(mean dist, count)':<25} | {'(mean dist, count)':<25}")
    print("-" * 75)
    
    for min_mag, max_mag, label in bins:
        # Generated
        gen_matches = [s for s in gen_stars if min_mag <= s['mag'] < max_mag]
        if gen_matches:
            gen_mean = sum(s['dist'] for s in gen_matches) / len(gen_matches)
            gen_str = f"{gen_mean:.1f} pc ({len(gen_matches)} stars)"
        else:
            gen_str = "N/A"
        
        # HYG
        hyg_matches = [s for s in hyg_stars if min_mag <= s['mag'] < max_mag]
        if hyg_matches:
            hyg_mean = sum(s['dist'] for s in hyg_matches) / len(hyg_matches)
            hyg_str = f"{hyg_mean:.1f} pc ({len(hyg_matches)} stars)"
        else:
            hyg_str = "N/A"
        
        print(f"{label:<20} | {gen_str:<25} | {hyg_str:<25}")
    
    print()
    print("NOTES:")
    print("- Generated distances should be in the same ballpark as HYG")
    print("- Bright stars can be close OR far (Sirius ~2.6pc, Deneb ~800pc)")
    print("- Dim stars should generally be nearby")
    print("- Very dim stars (>10 mag) should be very close (<100pc)")

if __name__ == '__main__':
    main()
