import sys,pefile,bisect
from paths import GA, METHODS_TSV, DUMMY_RUNTIME_CS
d=open(GA,'rb').read(); pe=pefile.PE(GA,fast_load=True)
rows={}
for l in open(METHODS_TSV):
    r,c,m,_=l.rstrip('\n').split('\t'); rows.setdefault(f"{c}::{m}",[]).append(int(r,16))
starts=sorted(set(x for v in rows.values() for x in v))
def body(rva):
    i=bisect.bisect_right(starts,rva); ln=starts[i]-rva
    o=pe.get_offset_from_rva(rva); return d[o:o+ln]
for name,sig in [a.split('=') for a in sys.argv[1:]]:
    sigb=bytes.fromhex(sig)
    for rva in rows[name]:
        b=body(rva); idx=[i for i in range(len(b)) if b.startswith(sigb,i)]
        print(f"{name} @{rva:X} len=0x{len(b):X}: matches={len(idx)} at {[hex(i) for i in idx]}")
