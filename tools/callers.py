import sys,bisect,pefile,re
from paths import GA, METHODS_TSV, DUMMY_RUNTIME_CS
d=open(GA,'rb').read(); pe=pefile.PE(GA,fast_load=True)
rows=[l.rstrip('\n').split('\t') for l in open(METHODS_TSV)]
names={}
for r in rows: names.setdefault(int(r[0],16),f"{r[1]}::{r[2]}")
starts=sorted(names)
targets={int(a,16) for a in sys.argv[1:]}
# scan .text for E8/E9 rel32 to target
sec=[s for s in pe.sections if s.Name.startswith(b'.text') or s.Name.startswith(b'il2cpp')]
for s in pe.sections:
    if not (s.Characteristics & 0x20000000): continue
    va=s.VirtualAddress; b=d[s.PointerToRawData:s.PointerToRawData+s.SizeOfRawData]
    for m in re.finditer(b'[\xe8\xe9]',b):
        k=m.start()
        if k+5>len(b): break
        t=va+k+5+int.from_bytes(b[k+1:k+5],'little',signed=True)
        if t in targets:
            i=bisect.bisect_right(starts,va+k)-1
            print(hex(va+k), names.get(starts[i]), '->', names.get(t))
