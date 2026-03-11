#!/usr/bin/env python3
"""
Generate StarNames.json from HYG v42 CSV.

This creates a JSON database of named stars for use in the Star Catalog View.
Only includes stars with proper names, Bayer designations, or Flamsteed numbers.

Output: starnames.json
"""
import csv
import json
import os
from datetime import datetime

def parse_spectral_class(spectral):
    """Extract just the main spectral class (O, B, A, F, G, K, M, L)."""
    if not spectral:
        return None
    s = spectral[0].upper()
    if s in ['O', 'B', 'A', 'F', 'G', 'K', 'M', 'L']:
        return s
    return None

def format_full_designation(bayer, flamsteed, constellation):
    """Create full designation like 'Alpha Orionis' or '58 Orionis'."""
    greek_names = {
        'Alp': 'Alpha', 'Bet': 'Beta', 'Gam': 'Gamma', 'Del': 'Delta',
        'Eps': 'Epsilon', 'Zet': 'Zeta', 'Eta': 'Eta', 'The': 'Theta',
        'Iot': 'Iota', 'Kap': 'Kappa', 'Lam': 'Lambda', 'Mu': 'Mu',
        'Nu': 'Nu', 'Xi': 'Xi', 'Omi': 'Omicron', 'Pi': 'Pi',
        'Rho': 'Rho', 'Sig': 'Sigma', 'Tau': 'Tau', 'Ups': 'Upsilon',
        'Phi': 'Phi', 'Chi': 'Chi', 'Psi': 'Psi', 'Ome': 'Omega'
    }
    
    parts = []
    if bayer:
        # Bayer is usually 3 letters like 'Alp', 'Bet'
        greek = greek_names.get(bayer, bayer)
        parts.append(greek)
    elif flamsteed:
        parts.append(str(flamsteed))
    
    if constellation:
        parts.append(constellation)
    
    return ' '.join(parts) if parts else None

def main():
    csv_path = 'hyg_v42.csv'
    if not os.path.exists(csv_path):
        csv_path = os.path.join('hyg_v42csv', 'hyg_v42.csv')
    
    if not os.path.exists(csv_path):
        print(f"Error: HYG CSV not found at {csv_path}")
        return 1
    
    print("Reading HYG catalog for named stars...")
    
    stars = {}
    count = 0
    
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            # Skip Sol
            if row.get('proper') == 'Sol':
                continue
            
            # Get Hipparcos ID
            hip_id = 0
            if row.get('hip'):
                try:
                    hip_id = int(row['hip'])
                except ValueError:
                    continue
            if hip_id == 0:
                continue
            
            # Extract fields
            proper = row.get('proper', '').strip()
            bayer = row.get('bayer', '').strip()
            flamsteed = row.get('flam', '').strip()
            con = row.get('con', '').strip()
            spectral = row.get('spect', '').strip()
            
            # Skip if no naming info
            if not proper and not bayer and not flamsteed:
                continue
            
            # Build entry
            entry = {}
            
            if proper:
                entry['proper'] = proper
            
            if bayer:
                entry['bayer'] = bayer
            
            if flamsteed:
                entry['flamsteed'] = int(flamsteed)
            
            if con:
                entry['constellation'] = con
            
            # Full designation (e.g., "Alpha Orionis")
            full_name = format_full_designation(bayer, flamsteed, con)
            if full_name and full_name != proper:
                entry['full_designation'] = full_name
            
            # Spectral class for display
            spec_class = parse_spectral_class(spectral)
            if spec_class:
                entry['spectral'] = spec_class
            
            # Magnitude for sorting
            try:
                mag = float(row.get('mag', '99'))
                entry['magnitude'] = round(mag, 2)
            except ValueError:
                pass
            
            stars[str(hip_id)] = entry  # JSON keys must be strings
            count += 1
    
    # Build output
    output = {
        "metadata": {
            "version": 1,
            "source_catalog": "HYG v42",
            "entry_count": count,
            "generated": datetime.now().isoformat(),
            "description": "Named stars from Hipparcos catalog with proper names, Bayer/Flamsteed designations"
        },
        "stars": stars
    }
    
    # Write JSON
    output_path = 'starnames.json'
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    
    print(f"\nGenerated {output_path}")
    print(f"  Total named stars: {count}")
    print(f"  File size: {os.path.getsize(output_path)} bytes")
    
    # Show some examples
    print("\nExample entries:")
    examples = ['32363', '27989', '24436', '71683', '11767']  # Betelgeuse, Rigel, Bellatrix, Rigil Kentaurus, Polaris
    for hip in examples:
        if hip in stars:
            entry = stars[hip]
            name = entry.get('proper', entry.get('full_designation', f'HIP {hip}'))
            print(f"  HIP {hip}: {name}")
            if 'constellation' in entry:
                print(f"           Constellation: {entry['constellation']}")
    
    return 0

if __name__ == '__main__':
    exit(main())
