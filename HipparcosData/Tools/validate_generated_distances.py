#!/usr/bin/env python3
"""
Validate generated star distances by comparing to HYG catalog.

This script analyzes the distance distribution of generated stars
vs real stars from HYG to assess how realistic our distance model is.
"""
import csv
import struct
import json
import os
from collections import defaultdict

HEADER_SIZE = 256
MAGIC = 0x53545243

# Spectral type names
SPECTRAL_NAMES = {0: 'O', 1: 'B', 2: 'A', 3: 'F', 4: 'G', 5: 'K', 6: 'M', 255: '?'}

def read_generated_catalog(filepath):
    """Read generated catalog and extract data."""
    with open(filepath, 'rb') as f:
        # Read header
        magic = struct.unpack('<I', f.read(4))[0]
        if magic != MAGIC:
            return None
        version = struct.unpack('<H', f.read(2))[0]
        flags = struct.unpack('<H', f.read(2))[0]
        star_count = struct.unpack('<i', f.read(4))[0]
        
        # Determine star size
        star_sizes = {1: 32, 2: 36, 3: 44, 4: 48}
        star_size = star_sizes.get(version, 48)
        
        stars = []
        f.seek(HEADER_SIZE)
        
        for i in range(star_count):
            # Read star data
            if version >= 4:
                hip_id = struct.unpack('<i', f.read(4))[0]
                distance = struct.unpack('<f', f.read(4))[0]
                spectral = struct.unpack('<i', f.read(4))[0]
                flags_val = struct.unpack('<I', f.read(4))[0]
                dx = struct.unpack('<f', f.read(4))[0]
                dy = struct.unpack('<f', f.read(4))[0]
                dz = struct.unpack('<f', f.read(4))[0]
                mag = struct.unpack('<f', f.read(4))[0]
                # Skip color and temp
                f.seek(star_size - 32, 1)
            else:
                f.seek(star_size, 1)
                continue
            
            stars.append({
                'id': hip_id,
                'distance': distance,
                'spectral': spectral,
                'is_hero': (flags_val & 1) != 0,
                'magnitude': mag
            })
        
        return stars

def load_hyg_distances():
    """Load HYG catalog distances."""
    csv_path = 'hyg_v42.csv'
    if not os.path.exists(csv_path):
        csv_path = os.path.join('hyg_v42csv', 'hyg_v42.csv')
    
    stars = []
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row.get('proper') == 'Sol':
                continue
            try:
                dist = float(row['dist'])
                mag = float(row['mag'])
                hip = int(row['hip']) if row['hip'] else 0
                spect = row.get('spect', '')
                # Parse spectral class
                spectral_num = 255
                if spect:
                    s = spect[0].upper()
                    spectral_map = {'O': 0, 'B': 1, 'A': 2, 'F': 3, 'G': 4, 'K': 5, 'M': 6}
                    spectral_num = spectral_map.get(s, 255)
                
                if dist < 100000:  # Valid parallax
                    stars.append({
                        'hip': hip,
                        'distance': dist,
                        'magnitude': mag,
                        'spectral': spectral_num
                    })
            except (ValueError, KeyError):
                continue
    return stars

def analyze_distance_distribution(stars, label):
    """Analyze distance statistics."""
    distances = [s['distance'] for s in stars if s['distance'] > 0]
    
    if not distances:
        return None
    
    distances.sort()
    n = len(distances)
    
    return {
        'count': n,
        'min': min(distances),
        'max': max(distances),
        'mean': sum(distances) / n,
        'median': distances[n // 2],
        'p25': distances[n // 4],
        'p75': distances[3 * n // 4],
        'p90': distances[int(n * 0.9)],
        'p95': distances[int(n * 0.95)],
        'p99': distances[int(n * 0.99)]
    }

def analyze_by_spectral_type(stars, label):
    """Analyze stars grouped by spectral type."""
    by_type = defaultdict(list)
    for s in stars:
        sp = s.get('spectral', 255)
        by_type[sp].append(s)
    
    print(f"\n{label} - By Spectral Type:")
    print(f"{'Type':<6} {'Count':>8} {'Mean Dist':>12} {'Median':>12}")
    print("-" * 45)
    
    for sp in sorted(by_type.keys()):
        if sp == 255:
            continue
        type_stars = by_type[sp]
        distances = [s['distance'] for s in type_stars]
        distances.sort()
        mean_dist = sum(distances) / len(distances)
        median_dist = distances[len(distances) // 2]
        name = SPECTRAL_NAMES.get(sp, '?')
        print(f"{name:<6} {len(type_stars):>8,} {mean_dist:>12.1f} {median_dist:>12.1f}")

def print_comparison(gen_stats, hyg_stats):
    """Print side-by-side comparison."""
    print("\n" + "=" * 70)
    print("DISTANCE COMPARISON: Generated vs HYG Catalog")
    print("=" * 70)
    print(f"{'Metric':<20} {'Generated':<25} {'HYG Real':<25}")
    print("-" * 70)
    
    metrics = ['count', 'min', 'max', 'mean', 'median', 'p25', 'p75', 'p90', 'p95']
    for metric in metrics:
        g = gen_stats.get(metric, 0)
        h = hyg_stats.get(metric, 0)
        
        if metric == 'count':
            print(f"{metric:<20} {g:<25,} {h:<25,}")
        else:
            print(f"{metric:<20} {g:<25.2f} {h:<25.2f}")
    
    print("-" * 70)
    
    # Analysis
    print("\nAnalysis:")
    ratio = gen_stats['mean'] / hyg_stats['mean'] if hyg_stats['mean'] > 0 else 0
    print(f"  Generated mean distance is {ratio:.2f}x HYG mean")
    
    if 0.5 < ratio < 2.0:
        print("  ✓ Distance scale is reasonably realistic (within 2x)")
    elif ratio < 0.5:
        print("  ✗ Generated stars are too CLOSE on average")
    else:
        print("  ✗ Generated stars are too FAR on average")

def main():
    # Find a generated catalog (procedural, not HYG-based)
    gen_files = [f for f in os.listdir('.') if f.endswith('.bin') and 'generated' in f.lower()]
    
    if not gen_files:
        print("No generated catalog files found (*.bin with 'generated' in name)")
        print("Please generate a procedural starfield in-game first")
        return 1
    
    gen_file = gen_files[0]
    print(f"Analyzing: {gen_file}")
    
    # Load generated stars
    gen_stars = read_generated_catalog(gen_file)
    if not gen_stars:
        print(f"Failed to read {gen_file}")
        return 1
    
    print(f"Loaded {len(gen_stars)} generated stars")
    
    # Load HYG stars
    print("Loading HYG catalog...")
    hyg_stars = load_hyg_distances()
    print(f"Loaded {len(hyg_stars)} HYG stars with valid distances")
    
    # Overall analysis
    gen_stats = analyze_distance_distribution(gen_stars, "Generated")
    hyg_stats = analyze_distance_distribution(hyg_stars, "HYG")
    
    print_comparison(gen_stats, hyg_stats)
    
    # Spectral type breakdown
    analyze_by_spectral_type(gen_stars, "Generated")
    analyze_by_spectral_type(hyg_stars, "HYG")
    
    # Hero vs Regular comparison for generated
    print("\n" + "=" * 70)
    print("Generated Stars: Heroes vs Regular")
    print("=" * 70)
    
    heroes = [s for s in gen_stars if s['is_hero']]
    regular = [s for s in gen_stars if not s['is_hero']]
    
    hero_stats = analyze_distance_distribution(heroes, "Heroes")
    reg_stats = analyze_distance_distribution(regular, "Regular")
    
    print(f"{'Metric':<20} {'Heroes':<25} {'Regular':<25}")
    print("-" * 70)
    for metric in ['count', 'mean', 'median', 'p25', 'p75']:
        h = hero_stats.get(metric, 0)
        r = reg_stats.get(metric, 0)
        if metric == 'count':
            print(f"{metric:<20} {h:<25,} {r:<25,}")
        else:
            print(f"{metric:<20} {h:<25.2f} {r:<25.2f}")
    
    print("\nNote: Bright stars (heroes) should have a mix of distances")
    print("      Some close (like Sirius), some far (like Deneb)")
    
    return 0

if __name__ == '__main__':
    exit(main())
