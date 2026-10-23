"""Shared paths for the analysis tools. Override with environment variables:
   SPELLBRIGADE_DIR  - game install folder (contains GameAssembly.dll)
   METHODS_TSV       - output of the `methods` dumper (default: tools/methods.tsv)
   DUMMY_RUNTIME_CS  - decompiled Cpp2IL dummy Runtime.dll (only needed by scan.py)
"""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
GAME_DIR = os.environ.get("SPELLBRIGADE_DIR", r"C:\Program Files (x86)\Steam\steamapps\common\The Spell Brigade")
GA = os.path.join(GAME_DIR, "GameAssembly.dll")
METHODS_TSV = os.environ.get("METHODS_TSV", os.path.join(HERE, "methods.tsv"))
DUMMY_RUNTIME_CS = os.environ.get("DUMMY_RUNTIME_CS", os.path.join(HERE, "Runtime.cs"))
