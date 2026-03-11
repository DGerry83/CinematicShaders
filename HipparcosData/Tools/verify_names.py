#!/usr/bin/env python3
"""Verify star names in generated JSON against reference file."""
import json

def main():
    with open('starnames.json', 'r', encoding='utf-8') as f:
        data = json.load(f)
    stars = data['stars']
    
    # Reference data from starnamereference.txt
    reference = [
        ('27989', 'Betelgeuse'),
        ('24436', 'Rigel'),
        ('71683', 'Rigil Kentaurus'),  # Alpha Centauri
        ('11767', 'Polaris'),
        ('32349', 'Sirius'),
        ('24608', 'Capella'),
        ('21421', 'Aldebaran'),
        ('97649', 'Altair'),
        ('91262', 'Vega'),
        ('65474', 'Spica'),
        ('69673', 'Arcturus'),
        ('7588', 'Achernar'),
        ('60718', 'Acrux'),
        ('33579', 'Adhara'),
        ('68702', 'Hadar'),  # Also known as Agena
        ('95947', 'Albireo'),
        ('65477', 'Alcor'),
        ('17702', 'Alcyone'),
        ('105199', 'Alderamin'),
        ('1067', 'Algenib'),
        ('50583', 'Algieba'),
        ('14576', 'Algol'),
        ('31681', 'Alhena'),
        ('62956', 'Alioth'),
        ('67301', 'Alkaid'),
        ('9640', 'Almaak'),
        ('109268', 'Alnair'),
        ('25428', 'Alnath'),
        ('26311', 'Alnilam'),
        ('26727', 'Alnitak'),
        ('46390', 'Alphard'),
        ('76267', 'Alphekka'),
        ('677', 'Alpheratz'),
        ('98036', 'Alshain'),
        ('2081', 'Ankaa'),
        ('80763', 'Antares'),
        ('25985', 'Arneb'),
        ('113368', 'Fomalhaut'),
        ('85927', 'Shaula'),
        ('113881', 'Scheat'),
        ('27366', 'Saiph'),
        ('62956', 'Alioth'),
    ]
    
    print("Star Name Verification")
    print("=" * 60)
    
    ok_count = 0
    mismatch_count = 0
    not_found_count = 0
    
    for hip, expected in reference:
        if hip in stars:
            actual = stars[hip].get('proper', '')
            if actual == expected:
                status = "OK"
                ok_count += 1
            else:
                status = f"MISMATCH (got: {actual})"
                mismatch_count += 1
        else:
            status = "NOT FOUND"
            not_found_count += 1
        
        print(f"{status:20} HIP {hip:>6} = {expected}")
    
    print("=" * 60)
    print(f"Summary: {ok_count} OK, {mismatch_count} MISMATCH, {not_found_count} NOT FOUND")

if __name__ == '__main__':
    main()
