#!/usr/bin/env python3
"""
Merge Stellarium constellation lines into starnames.json.
Creates the foundation for constellation visualization.
"""
import json
import re

def main():
    # Load existing starnames
    with open('starnames.json', 'r', encoding='utf-8') as f:
        starnames = json.load(f)
    
    # Load Stellarium constellation data
    with open('../StellariumReference/modern_st_index.json', 'r', encoding='utf-8') as f:
        stellarium = json.load(f)
    
    # Build constellation lines structure
    constellations = {}
    
    for con in stellarium['constellations']:
        # Extract abbreviation from ID (e.g., "CON modern_st Ori" -> "Ori")
        con_id = con['id']
        match = re.search(r'CON modern_st (\w+)', con_id)
        if not match:
            continue
        abbr = match.group(1)
        
        # Get constellation info
        common = con.get('common_name', {})
        name = common.get('native', abbr)
        
        # Build lines - convert HIP IDs to strings for JSON keys
        lines = []
        for line in con.get('lines', []):
            # Each line is a connected sequence of HIP IDs
            lines.append([int(hip) for hip in line])
        
        constellations[abbr] = {
            'name': name,
            'lines': lines
        }
    
    # Merge into starnames
    starnames['constellations'] = constellations
    starnames['metadata']['constellation_source'] = 'Stellarium modern_st'
    starnames['metadata']['constellation_count'] = len(constellations)
    
    # Write updated JSON
    with open('starnames.json', 'w', encoding='utf-8') as f:
        json.dump(starnames, f, indent=2, ensure_ascii=False)
    
    print(f"Merged {len(constellations)} constellations into starnames.json")
    
    # Show examples
    examples = ['Ori', 'UMa', 'Cas', 'Cru']
    print("\nExample constellations:")
    for abbr in examples:
        if abbr in constellations:
            con = constellations[abbr]
            print(f"\n{abbr} ({con['name']}):")
            print(f"  Lines: {len(con['lines'])} polylines")
            for i, line in enumerate(con['lines'][:2]):
                print(f"    Line {i+1}: HIP {' -> '.join(map(str, line))}")
            if len(con['lines']) > 2:
                print(f"    ... and {len(con['lines'])-2} more")
    
    # Check for stars that are in lines but not in our star database
    all_hip_ids = set(starnames['stars'].keys())
    missing_in_lines = set()
    
    for con in constellations.values():
        for line in con['lines']:
            for hip in line:
                hip_str = str(hip)
                if hip_str not in all_hip_ids:
                    missing_in_lines.add(hip)
    
    if missing_in_lines:
        print(f"\nWarning: {len(missing_in_lines)} HIP IDs in constellation lines not in named stars database")
        print(f"These are dim stars that define constellation shapes but aren't 'named'")
        print(f"Examples: {list(missing_in_lines)[:10]}")

if __name__ == '__main__':
    main()
