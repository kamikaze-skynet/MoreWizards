#!/usr/bin/env python3
"""Disassemble game methods. Usage: gadis.py 'Class::Method' [...]  or  gadis.py 0xRVA:len"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
import pefile

from paths import GA, METHODS_TSV, DUMMY_RUNTIME_CS
data = open(GA, "rb").read()
pe = pefile.PE(GA, fast_load=True)

names = {}      # rva -> "Class::Method"
by_name = {}    # "Class::Method" -> [rva]
for line in open(METHODS_TSV):
    rva, cls, meth, ret = line.rstrip("\n").split("\t")
    rva = int(rva, 16)
    key = f"{cls}::{meth}"
    names.setdefault(rva, key)
    by_name.setdefault(key, []).append(rva)
starts = sorted(names)

import bisect
def method_len(rva):
    i = bisect.bisect_right(starts, rva)
    return (starts[i] - rva) if i < len(starts) else 0x400

md = Cs(CS_ARCH_X86, CS_MODE_64)

def dis(rva, ln=None):
    ln = ln or min(method_len(rva), 0x1800)
    off = pe.get_offset_from_rva(rva)
    for ins in md.disasm(data[off:off + ln], rva):
        extra = ""
        if ins.mnemonic in ("call", "jmp") and ins.op_str.startswith("0x"):
            t = int(ins.op_str, 16)
            if t in names: extra = "   ; " + names[t]
        print(f"  {ins.address:08X}: {ins.mnemonic:6} {ins.op_str}{extra}")

for arg in sys.argv[1:]:
    if arg.startswith("0x"):
        r, _, l = arg.partition(":")
        print(f"### {arg}"); dis(int(r, 16), int(l, 16) if l else None)
        continue
    for rva in by_name.get(arg, []):
        print(f"### {arg} @ {rva:X}"); dis(rva)
