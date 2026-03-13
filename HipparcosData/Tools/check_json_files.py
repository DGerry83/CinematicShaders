#!/usr/bin/env python3
import json
import os

files = ['hyg_v42.json', 'hyg_v42_80k.json', 'hyg_v42_50k.json', 'hyg_v42_20k.json']

print("JSON File Summary:")
print("="*70)

for f in files:
    if os.path.exists(f):
        with open(f, 'r', encoding='utf-8') as fp:
            data = json.load(fp)
        
        meta = data['metadata']
        stars = len(data['stars'])
        cons = len(data['constellations'])
        
        print(f"{f:20} | Stars: {meta['star_count']:>6} | Named: {stars:>4} | Constellations: {cons:>2}")
    else:
        print(f"{f:20} | NOT FOUND")

print("\nSample from hyg_v42_20k.json:")
with open('hyg_v42_20k.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

# Show first few stars
print("\nFirst 3 stars:")
for i, (hip, star) in enumerate(list(data['stars'].items())[:3]):
    print(f"  HIP {hip}: {star}")

# Show first constellation
print("\nFirst constellation:")
first_con = list(data['constellations'].items())[0]
print(f"  {first_con[0]}: {first_con[1]['name']}")
print(f"    Lines: {len(first_con[1]['lines'])}")
