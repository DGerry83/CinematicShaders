#!/usr/bin/env python3
"""
StructToolset Validation Script

Ensures generated struct files have been properly propagated to all
consumers (C++ header, HLSL shader include, C# struct definition).

Exit code 0 = all validations passed
Exit code 1 = one or more mismatches detected
"""

import sys
from pathlib import Path

# Paths relative to project root
ROOT = Path(__file__).parent.parent.resolve()

GENERATED_CPP = ROOT / "StructToolset" / "generated" / "KartographerParams_cpp.h"
GENERATED_HLSL = ROOT / "StructToolset" / "generated" / "KartographerParams_hlsl.hlsl"
GENERATED_CS = ROOT / "StructToolset" / "generated" / "KartographerParams_cs.cs"

TARGET_CPP = ROOT / "NativePlugin" / "include" / "KartographerParams_generated.h"
TARGET_HLSL = ROOT / "NativePlugin" / "include" / "KartographerParams_hlsl.hlsl"
TARGET_CS = ROOT / "Native" / "StructDefs" / "KartographerParams.cs"

STARFIELD_NATIVE_H = ROOT / "NativePlugin" / "include" / "StarfieldNative.h"
STARFIELD_NATIVE_CPP = ROOT / "NativePlugin" / "src" / "StarfieldNative.cpp"
KARTOGRAPHER_PS = ROOT / "NativePlugin" / "shaders" / "KartographerPS.hlsl"

errors = []


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def check_exact_match(generated: Path, target: Path, name: str):
    gen_text = read_text(generated)
    tgt_text = read_text(target)
    if gen_text != tgt_text:
        errors.append(
            f"[{name}] MISMATCH: {target.name} does not match generated file.\n"
            f"  Generated: {generated}\n"
            f"  Target:    {target}\n"
            f"  ACTION: Copy the generated file to the target location and rebuild."
        )
    else:
        print(f"[OK] {name}: {target.name} matches generated file.")


def check_contains(path: Path, needle: str, description: str, must_exist: bool = True):
    text = read_text(path)
    found = needle in text
    if must_exist and not found:
        errors.append(
            f"[{description}] MISSING in {path.name}: expected to find:\n"
            f"  {needle}\n"
            f"  ACTION: Update {path} to include/reference the generated struct."
        )
    elif not must_exist and found:
        errors.append(
            f"[{description}] STALE CODE found in {path.name}: still contains:\n"
            f"  {needle}\n"
            f"  ACTION: Remove the inline struct and replace with #include of generated header."
        )
    else:
        status = "found" if must_exist else "not found (good)"
        print(f"[OK] {description}: '{needle}' {status} in {path.name}.")


def main():
    print("=" * 60)
    print("StructToolset Validation")
    print("=" * 60)
    print()

    # 1. Exact file matches for copied/generated outputs
    check_exact_match(GENERATED_CPP, TARGET_CPP, "C++ Header")
    check_exact_match(GENERATED_HLSL, TARGET_HLSL, "HLSL Header")
    check_exact_match(GENERATED_CS, TARGET_CS, "C# Struct")

    print()

    # 2. C++ header uses #include instead of inline struct
    check_contains(
        STARFIELD_NATIVE_H,
        '#include "KartographerParams_generated.h"',
        "StarfieldNative.h #include",
        must_exist=True,
    )

    # 3. C++ source no longer defines inline struct
    check_contains(
        STARFIELD_NATIVE_CPP,
        "struct KartographerParams {",
        "StarfieldNative.cpp inline struct",
        must_exist=False,
    )

    # 4. HLSL shader uses #include instead of inline struct
    check_contains(
        KARTOGRAPHER_PS,
        '#include "../include/KartographerParams_hlsl.hlsl"',
        "KartographerPS.hlsl #include",
        must_exist=True,
    )
    check_contains(
        KARTOGRAPHER_PS,
        "struct KartographerParams {",
        "KartographerPS.hlsl inline struct",
        must_exist=False,
    )

    print()
    print("=" * 60)
    if errors:
        print(f"VALIDATION FAILED ({len(errors)} issue(s))")
        print("=" * 60)
        for i, err in enumerate(errors, 1):
            print(f"\n{i}. {err}")
        sys.exit(1)
    else:
        print("VALIDATION PASSED - All struct files are in sync.")
        print("=" * 60)
        sys.exit(0)


if __name__ == "__main__":
    main()
