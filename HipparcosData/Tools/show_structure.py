#!/usr/bin/env python3
import json

with open('starnames.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

print('=== FINAL JSON STRUCTURE ===')
print(f"Metadata keys: {list(data['metadata'].keys())}")
print(f"Stars: {len(data['stars'])} entries")
print(f"Constellations: {len(data['constellations'])} entries")
print()
print('=== SAMPLE STAR (Betelgeuse) ===')
print(json.dumps(data['stars']['27989'], indent=2))
print()
print('=== SAMPLE CONSTELLATION (Orion) ===')
orion = data['constellations']['Ori']
print(f"Name: {orion['name']}")
print(f"Number of line segments: {len(orion['lines'])}")
print("Lines (first 2):")
for line in orion['lines'][:2]:
    print(f"  HIP {' -> '.join(map(str, line))}")
print()
print('=== EXTENSIBLE FOR USER CONSTELLATIONS ===')
print('To add a custom constellation for generated catalogs:')
print('  data["constellations"]["Custom1"] = {')
print('    "name": "My Constellation",')
print('    "lines": [[hip1, hip2, hip3], [hip4, hip5]]')
print('  }')
