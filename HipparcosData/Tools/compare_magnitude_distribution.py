#!/usr/bin/env python3
"""
Compare magnitude distributions between star catalogs.
Analyzes how brightness is distributed across different catalog files.
"""
import struct
import sys
import os
from collections import defaultdict

HEADER_SIZE = 256
STAR_SIZE = 48

def read_star_magnitudes(filepath):
    """Read all star magnitudes from a catalog file."""
    magnitudes = []
    hero_magnitudes = []
    
    with open(filepath, 'rb') as f:
        # Read header: magic(4) + version(2) + flags(2) + star_count(4) + hero_count(4) + seed(4)...
        f.seek(8)  # Skip to star count at offset 8
        star_count = struct.unpack('<i', f.read(4))[0]
        hero_count = struct.unpack('<i', f.read(4))[0]
        
        # Read all stars
        for i in range(star_count):
            offset = HEADER_SIZE + (i * STAR_SIZE)
            f.seek(offset)
            
            # Read star data: hip_id(4), dist(4), spectral(4), flags(4), dir(12), mag(4), color(12), temp(4)
            data = struct.unpack('<ifiiffffffff', f.read(48))
            mag = data[7]
            flags = int(data[3])
            is_hero = (flags & 1) != 0
            
            magnitudes.append(mag)
            if is_hero:
                hero_magnitudes.append(mag)
    
    return magnitudes, hero_magnitudes

def analyze_distribution(magnitudes, label, total_stars=None):
    """Analyze and print magnitude distribution statistics."""
    if not magnitudes:
        return
    
    count = len(magnitudes)
    total = total_stars if total_stars else count
    
    # Create magnitude bins (1 mag intervals)
    bins = defaultdict(int)
    for mag in magnitudes:
        bin_key = int(mag)
        bins[bin_key] += 1
    
    # Statistics
    avg = sum(magnitudes) / count
    min_mag = min(magnitudes)
    max_mag = max(magnitudes)
    
    # Percentiles
    sorted_mags = sorted(magnitudes)
    p50 = sorted_mags[count // 2]
    p90 = sorted_mags[int(count * 0.9)]
    p99 = sorted_mags[int(count * 0.99)] if count >= 100 else sorted_mags[-1]
    
    print(f"\n{label}")
    print("-" * 50)
    print(f"  Count: {count} ({100*count/total:.1f}% of catalog)")
    print(f"  Range: {min_mag:.2f} to {max_mag:.2f}")
    print(f"  Average: {avg:.2f}")
    print(f"  Median (P50): {p50:.2f}")
    print(f"  P90: {p90:.2f}  P99: {p99:.2f}")
    
    # Distribution by magnitude bins
    print(f"  Distribution by magnitude:")
    for mag_bin in range(int(min_mag), int(max_mag) + 2):
        bin_count = bins[mag_bin]
        bar = "#" * (bin_count * 50 // count)
        print(f"    mag {mag_bin:3d}: {bin_count:5d} ({100*bin_count/count:5.1f}%) {bar}")
    
    return {
        'count': count,
        'avg': avg,
        'median': p50,
        'p90': p90,
        'min': min_mag,
        'max': max_mag,
        'bins': dict(bins)
    }

def compare_files(file1, file2, reference=None):
    """Compare magnitude distributions between files."""
    print("=" * 60)
    print("MAGNITUDE DISTRIBUTION COMPARISON")
    print("=" * 60)
    
    # Read all files
    mags1, heroes1 = read_star_magnitudes(file1)
    mags2, heroes2 = read_star_magnitudes(file2)
    
    ref_mags = None
    ref_heroes = None
    if reference and os.path.exists(reference):
        ref_mags, ref_heroes = read_star_magnitudes(reference)
    
    # Get file names for display
    name1 = os.path.basename(file1)
    name2 = os.path.basename(file2)
    ref_name = os.path.basename(reference) if reference else None
    
    # Analyze each file
    stats1 = analyze_distribution(mags1, f"ALL STARS: {name1}")
    stats2 = analyze_distribution(mags2, f"ALL STARS: {name2}")
    
    if ref_mags:
        stats_ref = analyze_distribution(ref_mags, f"REFERENCE: {ref_name}")
    
    # Hero stars analysis
    if heroes1 or heroes2:
        print("\n" + "=" * 60)
        print("HERO STARS (brightest/named stars)")
        print("=" * 60)
        analyze_distribution(heroes1, f"HEROES: {name1}", len(mags1))
        analyze_distribution(heroes2, f"HEROES: {name2}", len(mags2))
        if ref_heroes:
            analyze_distribution(ref_heroes, f"HEROES: {ref_name}", len(ref_mags))
    
    # Comparison summary
    print("\n" + "=" * 60)
    print("COMPARISON SUMMARY")
    print("=" * 60)
    
    print(f"\n{'Metric':<20} {name1:<20} {name2:<20}", end="")
    if ref_name:
        print(f"{ref_name:<20}", end="")
    print()
    
    print(f"{'-'*20} {'-'*20} {'-'*20}", end="")
    if ref_name:
        print(f" {'-'*20}", end="")
    print()
    
    print(f"{'Average Mag':<20} {stats1['avg']:<20.2f} {stats2['avg']:<20.2f}", end="")
    if ref_mags:
        print(f"{stats_ref['avg']:<20.2f}", end="")
    print()
    
    print(f"{'Median Mag':<20} {stats1['median']:<20.2f} {stats2['median']:<20.2f}", end="")
    if ref_mags:
        print(f"{stats_ref['median']:<20.2f}", end="")
    print()
    
    print(f"{'P90 Mag':<20} {stats1['p90']:<20.2f} {stats2['p90']:<20.2f}", end="")
    if ref_mags:
        print(f"{stats_ref['p90']:<20.2f}", end="")
    print()
    
    print(f"{'Brightest':<20} {stats1['min']:<20.2f} {stats2['min']:<20.2f}", end="")
    if ref_mags:
        print(f"{stats_ref['min']:<20.2f}", end="")
    print()
    
    print(f"{'Dimmest':<20} {stats1['max']:<20.2f} {stats2['max']:<20.2f}", end="")
    if ref_mags:
        print(f"{stats_ref['max']:<20.2f}", end="")
    print()
    
    # Determine which is closer to reference
    if ref_mags:
        print("\n" + "=" * 60)
        print("CLOSENESS TO REFERENCE (lower = closer)")
        print("=" * 60)
        
        diff1_avg = abs(stats1['avg'] - stats_ref['avg'])
        diff2_avg = abs(stats2['avg'] - stats_ref['avg'])
        diff1_med = abs(stats1['median'] - stats_ref['median'])
        diff2_med = abs(stats2['median'] - stats_ref['median'])
        
        print(f"\nAverage magnitude difference from {ref_name}:")
        print(f"  {name1}: {diff1_avg:.3f}")
        print(f"  {name2}: {diff2_avg:.3f}")
        print(f"  Winner: {name1 if diff1_avg < diff2_avg else name2}")
        
        print(f"\nMedian magnitude difference from {ref_name}:")
        print(f"  {name1}: {diff1_med:.3f}")
        print(f"  {name2}: {diff2_med:.3f}")
        print(f"  Winner: {name1 if diff1_med < diff2_med else name2}")

def main():
    if len(sys.argv) < 3:
        print("Usage:")
        print(f"  {sys.argv[0]} <catalog1.bin> <catalog2.bin> [reference.bin]")
        print("\nExample:")
        print(f"  {sys.argv[0]} BrightnessDefault.bin BrightnessPOINT25.bin hyg_v42_50k.bin")
        return 1
    
    file1 = sys.argv[1]
    file2 = sys.argv[2]
    reference = sys.argv[3] if len(sys.argv) > 3 else None
    
    if not os.path.exists(file1):
        print(f"ERROR: File not found: {file1}")
        return 1
    if not os.path.exists(file2):
        print(f"ERROR: File not found: {file2}")
        return 1
    if reference and not os.path.exists(reference):
        print(f"WARNING: Reference file not found: {reference}")
        reference = None
    
    compare_files(file1, file2, reference)
    return 0

if __name__ == '__main__':
    exit(main())
