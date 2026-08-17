#!/usr/bin/env python3
"""
LEGACY — superseded 2026-08-17 by the current generate_catalog_jsons.py (formerly
generate_catalog_jsons_v2.py). Kept for reference only; do not run.

This is the generator that produced the 2026-03-29 and 2026-04-17 catalog JSONs.
Known defects fixed in the successor (see ISSUES #006 verification session):
- CrA mapped to "Corona Austrina"/"Coronae Austrinae" (IAU: "Corona Australis"/"Coronae Australis")
- load_iau_names() silently fell back to HYG names when the IAU CSV was missing
  (this is how the stale 2026-03-29 hyg_v42.json lost its IAU name overlay)
- Inclusion gate required a HYG proper/bayer/flamsteed designation, suppressing
  IAU-only names (e.g. HIP 36948 "Moth", HIP 102626 "Speedy Mic")
- star_sizes lacked an explicit v6 entry; metadata.bin_file stored the raw -i path

Generate matching JSON files for each star catalog bin.
Each JSON contains only the named stars and constellation lines relevant to that specific bin.

Run from HipparcosData root: python Tools/generate_catalog_jsons.py

Output: hyg_v42.json, hyg_v42_80k.json, hyg_v42_50k.json, hyg_v42_20k.json
"""
import csv
import json
import os
import re
import struct
import argparse
from datetime import datetime

# Constants
MAGIC = 0x53545243
HEADER_SIZE = 256

# Constellation nominative forms for display (e.g., "Constellation: Orion")
CONSTELLATION_NOMINATIVE = {
    'And': 'Andromeda', 'Ant': 'Antlia', 'Aps': 'Apus', 'Aqr': 'Aquarius',
    'Aql': 'Aquila', 'Ara': 'Ara', 'Ari': 'Aries', 'Aur': 'Auriga',
    'Boo': 'Boötes', 'Cae': 'Caelum', 'Cam': 'Camelopardalis', 'Cnc': 'Cancer',
    'CVn': 'Canes Venatici', 'CMa': 'Canis Major', 'CMi': 'Canis Minor',
    'Cap': 'Capricornus', 'Car': 'Carina', 'Cas': 'Cassiopeia', 'Cen': 'Centaurus',
    'Cep': 'Cepheus', 'Cet': 'Cetus', 'Cha': 'Chamaeleon', 'Cir': 'Circinus',
    'Col': 'Columba', 'Com': 'Coma Berenices', 'CrA': 'Corona Austrina',
    'CrB': 'Corona Borealis', 'Crv': 'Corvus', 'Crt': 'Crater', 'Cru': 'Crux',
    'Cyg': 'Cygnus', 'Del': 'Delphinus', 'Dor': 'Dorado', 'Dra': 'Draco',
    'Equ': 'Equuleus', 'Eri': 'Eridanus', 'For': 'Fornax', 'Gem': 'Gemini',
    'Gru': 'Grus', 'Her': 'Hercules', 'Hor': 'Horologium', 'Hya': 'Hydra',
    'Hyi': 'Hydrus', 'Ind': 'Indus', 'Lac': 'Lacerta', 'Leo': 'Leo',
    'LMi': 'Leo Minor', 'Lep': 'Lepus', 'Lib': 'Libra', 'Lup': 'Lupus',
    'Lyn': 'Lynx', 'Lyr': 'Lyra', 'Men': 'Mensa', 'Mic': 'Microscopium',
    'Mon': 'Monoceros', 'Mus': 'Musca', 'Nor': 'Norma', 'Oct': 'Octans',
    'Oph': 'Ophiuchus', 'Ori': 'Orion', 'Pav': 'Pavo', 'Peg': 'Pegasus',
    'Per': 'Perseus', 'Phe': 'Phoenix', 'Pic': 'Pictor', 'Psc': 'Pisces',
    'PsA': 'Piscis Austrinus', 'Pup': 'Puppis', 'Pyx': 'Pyxis', 'Ret': 'Reticulum',
    'Sge': 'Sagitta', 'Sgr': 'Sagittarius', 'Sco': 'Scorpius', 'Scl': 'Sculptor',
    'Sct': 'Scutum', 'Ser': 'Serpens', 'Sex': 'Sextans', 'Tau': 'Taurus',
    'Tel': 'Telescopium', 'Tri': 'Triangulum', 'TrA': 'Triangulum Australe',
    'Tuc': 'Tucana', 'UMa': 'Ursa Major', 'UMi': 'Ursa Minor', 'Vel': 'Vela',
    'Vir': 'Virgo', 'Vol': 'Volans', 'Vul': 'Vulpecula',
}

# Constellation genitive forms for Bayer designations (e.g., "Alpha Orionis")
# Source: IAU standard + hipparcos_name_converter.py cross-reference
CONSTELLATION_GENITIVE = {
    'And': 'Andromedae', 'Ant': 'Antliae', 'Aps': 'Apodis', 'Aqr': 'Aquarii',
    'Aql': 'Aquilae', 'Ara': 'Arae', 'Ari': 'Arietis', 'Aur': 'Aurigae',
    'Boo': 'Boötis', 'Cae': 'Caeli', 'Cam': 'Camelopardalis', 'Cap': 'Capricorni',
    'Car': 'Carinae', 'Cas': 'Cassiopeiae', 'Cen': 'Centauri', 'Cep': 'Cephei',
    'Cet': 'Ceti', 'Cha': 'Chamaeleontis', 'Cir': 'Circini', 'CMa': 'Canis Majoris',
    'CMi': 'Canis Minoris', 'Cnc': 'Cancri', 'Col': 'Columbae',
    'Com': 'Comae Berenices', 'CrA': 'Coronae Austrinae', 'CrB': 'Coronae Borealis',
    'Crt': 'Crateris', 'Cru': 'Crucis', 'Crv': 'Corvi', 'CVn': 'Canum Venaticorum',
    'Cyg': 'Cygni', 'Del': 'Delphini', 'Dor': 'Doradus', 'Dra': 'Draconis',
    'Equ': 'Equulei', 'Eri': 'Eridani', 'For': 'Fornacis', 'Gem': 'Geminorum',
    'Gru': 'Gruis', 'Her': 'Herculis', 'Hor': 'Horologii', 'Hya': 'Hydrae',
    'Hyi': 'Hydri', 'Ind': 'Indi', 'Lac': 'Lacertae', 'Leo': 'Leonis',
    'Lep': 'Leporis', 'Lib': 'Librae', 'LMi': 'Leonis Minoris', 'Lup': 'Lupi',
    'Lyn': 'Lyncis', 'Lyr': 'Lyrae', 'Men': 'Mensae', 'Mic': 'Microscopii',
    'Mon': 'Monocerotis', 'Mus': 'Muscae', 'Nor': 'Normae', 'Oct': 'Octantis',
    'Oph': 'Ophiuchi', 'Ori': 'Orionis', 'Pav': 'Pavonis', 'Peg': 'Pegasi',
    'Per': 'Persei', 'Phe': 'Phoenicis', 'Pic': 'Pictoris', 'PsA': 'Piscis Austrini',
    'Psc': 'Piscium', 'Pup': 'Puppis', 'Pyx': 'Pyxidis', 'Ret': 'Reticuli',
    'Scl': 'Sculptoris', 'Sco': 'Scorpii', 'Sct': 'Scuti', 'Ser': 'Serpentis',
    'Sex': 'Sextantis', 'Sge': 'Sagittae', 'Sgr': 'Sagittarii', 'Tau': 'Tauri',
    'Tel': 'Telescopii', 'TrA': 'Trianguli Australis', 'Tri': 'Trianguli',
    'Tuc': 'Tucanae', 'UMa': 'Ursae Majoris', 'UMi': 'Ursae Minoris',
    'Vel': 'Velorum', 'Vir': 'Virginis', 'Vol': 'Volantis', 'Vul': 'Vulpeculae',
}

def read_catalog_header(filepath):
    """Read header from binary catalog to get star count."""
    with open(filepath, 'rb') as f:
        magic = struct.unpack('<I', f.read(4))[0]
        if magic != MAGIC:
            return None
        version = struct.unpack('<H', f.read(2))[0]
        flags = struct.unpack('<H', f.read(2))[0]
        star_count = struct.unpack('<i', f.read(4))[0]
        hero_count = struct.unpack('<i', f.read(4))[0]
        return {
            'version': version,
            'star_count': star_count,
            'hero_count': hero_count
        }

def read_hip_ids_from_catalog(filepath, star_count, star_size):
    """Read just the Hipparcos IDs from a binary catalog."""
    hip_ids = set()
    with open(filepath, 'rb') as f:
        # Skip header
        f.seek(HEADER_SIZE)
        for i in range(star_count):
            # Read just the HipparcosID (first 4 bytes of each star record)
            hip_id = struct.unpack('<i', f.read(4))[0]
            if hip_id > 0:  # Skip procedural stars (ID=0)
                hip_ids.add(hip_id)
            # Skip rest of star record
            f.seek(star_size - 4, 1)
    return hip_ids

def parse_spectral_class(spectral):
    """Extract main spectral class."""
    if not spectral:
        return None
    s = spectral[0].upper()
    if s in ['O', 'B', 'A', 'F', 'G', 'K', 'M', 'L']:
        return s
    return None

def format_full_designation(bayer, flamsteed, con_abbr):
    """Create full designation like 'Alpha Orionis' using genitive forms."""
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
        greek = greek_names.get(bayer, bayer)
        parts.append(greek)
    elif flamsteed:
        parts.append(str(flamsteed))
    
    if con_abbr:
        # Use genitive form for correct Bayer designation grammar
        full_name = CONSTELLATION_GENITIVE.get(con_abbr, con_abbr)
        parts.append(full_name)
    
    return ' '.join(parts) if parts else None

def load_iau_names():
    """Load official IAU proper names from iau_proper_stars.csv.
    
    Returns a dict mapping HIP number (int) to official proper name.
    Falls back to empty dict if the CSV is not found.
    """
    script_dir = os.path.dirname(os.path.abspath(__file__))
    # Try project-relative path first
    iau_csv_path = os.path.join(script_dir, '..', '..', 'ReferenceNotes', 'StarNamesFix', 'iau_proper_stars.csv')
    
    if not os.path.exists(iau_csv_path):
        # Fallback: same directory as script
        iau_csv_path = os.path.join(script_dir, 'iau_proper_stars.csv')
    
    if not os.path.exists(iau_csv_path):
        print("  Warning: iau_proper_stars.csv not found — using HYG names only")
        return {}
    
    iau_names = {}
    try:
        with open(iau_csv_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                hip_str = row.get('HIP', '').strip()
                if hip_str and hip_str.isdigit():
                    hip = int(hip_str)
                    name = row.get('Proper Names', '').strip()
                    if name:
                        iau_names[hip] = name
        print(f"  Loaded {len(iau_names)} IAU proper names from {os.path.basename(iau_csv_path)}")
    except Exception as e:
        print(f"  Warning: Could not load IAU names: {e}")
        return {}
    
    return iau_names

def load_stellarium_constellations():
    """Load constellation line data from Stellarium."""
    # StellariumReference is at same level as HipparcosData (parent of Tools)
    script_dir = os.path.dirname(os.path.abspath(__file__))
    ref_path = os.path.join(script_dir, '..', '..', 'StellariumReference', 'modern_st_index.json')
    with open(ref_path, 'r', encoding='utf-8') as f:
        stellarium = json.load(f)
    
    constellations = {}
    for con in stellarium['constellations']:
        con_id = con['id']
        match = re.search(r'CON modern_st (\w+)', con_id)
        if not match:
            continue
        abbr = match.group(1)
        
        common = con.get('common_name', {})
        name = common.get('native', abbr)
        
        lines = []
        for line in con.get('lines', []):
            lines.append([int(hip) for hip in line])
        
        constellations[abbr] = {
            'name': name,
            'lines': lines
        }
    
    return constellations

def generate_catalog_json(bin_file, csv_path, constellations, output_name, iau_names=None):
    """Generate a JSON file matching a specific bin catalog."""
    print(f"\nProcessing {bin_file}...")
    
    # Determine star size based on version in header
    header = read_catalog_header(bin_file)
    if not header:
        print(f"  ERROR: Could not read {bin_file}")
        return False
    
    version = header['version']
    star_count = header['star_count']
    
    # Star record sizes by version
    star_sizes = {1: 32, 2: 36, 3: 44, 4: 48}
    star_size = star_sizes.get(version, 48)
    
    print(f"  Version: {version}, Stars: {star_count}, Record size: {star_size}")
    
    # Get HIP IDs in this catalog
    catalog_hip_ids = read_hip_ids_from_catalog(bin_file, star_count, star_size)
    print(f"  Real sky stars (HIP > 0): {len(catalog_hip_ids)}")
    
    # Load HYG CSV and filter to stars in this catalog
    stars = {}
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row.get('proper') == 'Sol':
                continue
            
            hip_str = row.get('hip', '')
            if not hip_str:
                continue
            
            try:
                hip_id = int(hip_str)
            except ValueError:
                continue
            
            # Only include if this star is in the catalog
            if hip_id not in catalog_hip_ids:
                continue
            
            # Only include if it has naming data
            proper = row.get('proper', '').strip()
            bayer = row.get('bayer', '').strip()
            flamsteed = row.get('flam', '').strip()
            con = row.get('con', '').strip()
            
            if not proper and not bayer and not flamsteed:
                continue
            
            # Build entry
            entry = {}
            
            # Use IAU proper name if available, otherwise fall back to HYG
            iau_name = iau_names.get(hip_id) if iau_names else None
            if iau_name:
                entry['proper'] = iau_name
            elif proper:
                entry['proper'] = proper
            
            if bayer:
                entry['bayer'] = bayer
            if flamsteed:
                entry['flamsteed'] = int(flamsteed)
            if con:
                # Store nominative form for display ("Constellation: Orion")
                entry['constellation'] = CONSTELLATION_NOMINATIVE.get(con, con)
            
            full_name = format_full_designation(bayer, flamsteed, con)
            if full_name and full_name != entry.get('proper'):
                entry['full_designation'] = full_name
            
            spectral = row.get('spect', '')
            spec_class = parse_spectral_class(spectral)
            if spec_class:
                entry['spectral'] = spec_class
            
            try:
                mag = float(row.get('mag', '99'))
                entry['magnitude'] = round(mag, 2)
            except ValueError:
                pass
            
            # Add distance in light years (convert from parsecs)
            try:
                dist_pc = float(row.get('dist', '0'))
                if dist_pc > 0 and dist_pc < 100000:  # Valid distance range
                    dist_ly = round(dist_pc * 3.26156, 2)
                    entry['distance_ly'] = dist_ly
            except ValueError:
                pass
            
            # Add Cartesian direction vectors for GPU projection (x, y, z in world space)
            # Must match convert_hyg.py which flips Y to correct mirroring
            try:
                entry['x'] = float(row.get('x', '0'))
                entry['y'] = -float(row.get('y', '0'))
                entry['z'] = float(row.get('z', '0'))
            except ValueError:
                entry['x'] = 0.0
                entry['y'] = 0.0
                entry['z'] = 0.0
            
            stars[str(hip_id)] = entry
    
    print(f"  Named stars in catalog: {len(stars)}")
    
    # Filter constellations to only include lines with stars in this catalog
    filtered_constellations = {}
    for abbr, con_data in constellations.items():
        filtered_lines = []
        for line in con_data['lines']:
            # Keep line if ANY star in the line is in our catalog
            # (This preserves partial constellation shapes)
            if any(hip in catalog_hip_ids for hip in line):
                filtered_lines.append(line)
        
        if filtered_lines:
            filtered_constellations[abbr] = {
                'name': con_data['name'],
                'lines': filtered_lines
            }
    
    print(f"  Constellations with visible lines: {len(filtered_constellations)}")
    
    # Build output
    output = {
        "metadata": {
            "version": 1,
            "source_catalog": "HYG v42",
            "bin_file": bin_file,
            "star_count": star_count,
            "named_star_count": len(stars),
            "constellation_count": len(filtered_constellations),
            "generated": datetime.now().isoformat()
        },
        "stars": stars,
        "constellations": filtered_constellations
    }
    
    # Write JSON
    output_file = output_name + '.json'
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    
    print(f"  Written: {os.path.basename(output_file)} ({os.path.getsize(output_file)} bytes)")
    return True

def main():
    parser = argparse.ArgumentParser(description='Generate JSON metadata files for star catalogs')
    parser.add_argument('--input', '-i', default='.',
                        help='Input directory containing .bin files (default: current directory)')
    parser.add_argument('--output', '-o', default='.',
                        help='Output directory for .json files (default: current directory)')
    args = parser.parse_args()
    
    script_dir = os.path.dirname(os.path.abspath(__file__))
    
    # CSV is always in hyg_v42csv/ subdirectory
    csv_path = os.path.join(script_dir, '..', 'hyg_v42csv', 'hyg_v42.csv')
    
    if not os.path.exists(csv_path):
        print(f"Error: HYG CSV not found at {csv_path}")
        return 1
    
    # Ensure output directory exists
    os.makedirs(args.output, exist_ok=True)
    
    print(f"CSV path: {csv_path}")
    print(f"Input directory: {args.input}")
    print(f"Output directory: {args.output}")
    print()
    
    print("Loading constellation data...")
    constellations = load_stellarium_constellations()
    print(f"Loaded {len(constellations)} constellations")
    
    print("Loading IAU proper names...")
    iau_names = load_iau_names()
    print()
    
    # Generate JSON for each bin file
    catalogs = [
        ('hyg_v42.bin', 'hyg_v42'),
        ('hyg_v42_guides.bin', 'hyg_v42_guides'),
        ('hyg_v42_80k.bin', 'hyg_v42_80k'),
        ('hyg_v42_50k.bin', 'hyg_v42_50k'),
        ('hyg_v42_20k.bin', 'hyg_v42_20k'),
        ('hyg_v42_polaris_debug.bin', 'hyg_v42_polaris_debug'),
    ]
    
    for bin_file, output_name in catalogs:
        bin_path = os.path.join(args.input, bin_file)
        if os.path.exists(bin_path):
            generate_catalog_json(bin_path, csv_path, constellations, 
                                  os.path.join(args.output, output_name),
                                  iau_names)
        else:
            print(f"\nSkipping {bin_file} (not found in {args.input})")
    
    print("\n" + "="*60)
    print("JSON generation complete!")
    print("Each .bin file now has a matching .json file")
    
    return 0

if __name__ == '__main__':
    exit(main())
