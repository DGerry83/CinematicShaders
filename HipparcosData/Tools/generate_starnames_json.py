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

def load_iau_names():
    """Load official IAU proper names from iau_proper_stars.csv."""
    script_dir = os.path.dirname(os.path.abspath(__file__))
    iau_csv_path = os.path.join(script_dir, '..', '..', 'ReferenceNotes', 'StarNamesFix', 'iau_proper_stars.csv')
    
    if not os.path.exists(iau_csv_path):
        iau_csv_path = os.path.join(script_dir, 'iau_proper_stars.csv')
    
    if not os.path.exists(iau_csv_path):
        print("Warning: iau_proper_stars.csv not found — using HYG names only")
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
        print(f"Loaded {len(iau_names)} IAU proper names from {os.path.basename(iau_csv_path)}")
    except Exception as e:
        print(f"Warning: Could not load IAU names: {e}")
        return {}
    
    return iau_names

def format_full_designation(bayer, flamsteed, con_abbr):
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
    
    if con_abbr:
        parts.append(CONSTELLATION_GENITIVE.get(con_abbr, con_abbr))
    
    return ' '.join(parts) if parts else None

def main():
    csv_path = 'hyg_v42.csv'
    if not os.path.exists(csv_path):
        csv_path = os.path.join('hyg_v42csv', 'hyg_v42.csv')
    
    if not os.path.exists(csv_path):
        print(f"Error: HYG CSV not found at {csv_path}")
        return 1
    
    print("Loading IAU proper names...")
    iau_names = load_iau_names()
    print()
    
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
            
            # Use IAU proper name if available, otherwise fall back to HYG
            iau_name = iau_names.get(hip_id)
            if iau_name:
                entry['proper'] = iau_name
            elif proper:
                entry['proper'] = proper
            
            if bayer:
                entry['bayer'] = bayer
            
            if flamsteed:
                entry['flamsteed'] = int(flamsteed)
            
            if con:
                entry['constellation'] = CONSTELLATION_NOMINATIVE.get(con, con)
            
            # Full designation (e.g., "Alpha Orionis")
            full_name = format_full_designation(bayer, flamsteed, con)
            if full_name and full_name != entry.get('proper'):
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
