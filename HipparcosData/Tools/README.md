# Python Tools - Catalog Management Scripts

This folder contains Python scripts for managing the HYG star catalog conversion and validation.

## Essential Scripts (Keep)

| Script | Purpose | When to Use |
|--------|---------|-------------|
| **convert_hyg.py** | Converts HYG CSV to binary catalogs | When HYG CSV is updated or format changes |
| **generate_catalog_jsons.py** | Creates JSON metadata for each .bin | After running convert_hyg.py |
| **validate_catalog.py** | Validates binary catalog integrity | When checking catalog health, debugging issues |

## Useful Validation Scripts (Keep)

| Script | Purpose | When to Use |
|--------|---------|-------------|
| **compare_distances.py** | Compares generated vs HYG distances | Validating distance model realism |
| **validate_generated_distances.py** | Statistical distance analysis | Detailed distance distribution comparison |
| **check_json_files.py** | Summary of all JSON files | Quick overview of generated JSONs |
| **check_generated_fields.py** | Check Version 4 fields in catalogs | Verify new fields (ID, Distance, SpectralType) |

## Optional/Debug Scripts (Keep for Reference)

| Script | Purpose | Notes |
|--------|---------|-------|
| **generate_starnames_json.py** | Creates starnames.json database | One-time use, already generated |
| **merge_constellation_lines.py** | Merges Stellarium constellation data | One-time use for starnames.json |
| **verify_names.py** | Verifies star names against reference | Optional validation of name database |
| **show_structure.py** | Displays JSON structure | Documentation/example purposes |

## Scripts to Remove (Redundant/Debug)

The following scripts were created during development/debugging and can be removed:

- ~~check_hyg_cols.py~~ - Simple CSV column lister (debug only)
- ~~check_json.py~~ - Partial JSON check (replaced by check_json_files.py)
- ~~check_my_stars.py~~ - Custom debug for MyStarfield.bin
- ~~check_struct_layout.py~~ - Struct debugging (issue resolved)
- ~~debug_struct.py~~ - Binary debugging (issue resolved)
- ~~decode_like_ksp.py~~ - Marshal debugging (issue resolved)
- ~~verify_mystarfield.py~~ - Specific file validation (use validate_catalog.py instead)

## Typical Workflow

```bash
# 1. Convert HYG CSV to binary catalogs
python convert_hyg.py

# 2. Generate JSON metadata files
python generate_catalog_jsons.py

# 3. Validate the results
python validate_catalog.py ../hyg_v42.bin
python check_json_files.py
```

## Validation Workflow (for generated catalogs)

```bash
# Check distances are realistic
python compare_distances.py

# Validate specific file
python validate_catalog.py ../MyStarfield.bin

# Check Version 4 fields
python check_generated_fields.py ../MyStarfield.bin
```
