import re, bisect, pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from paths import GA, METHODS_TSV, DUMMY_RUNTIME_CS
d=open(GA,'rb').read(); pe=pefile.PE(GA,fast_load=True)
src=open(DUMMY_RUNTIME_CS,encoding='utf-8',errors='ignore').read()
rt=set(re.findall(r'\b(?:class|struct) (\w+)',src))
rows=[]
for l in open(METHODS_TSV):
    r,c,m,_=l.rstrip('\n').split('\t'); rows.append((int(r,16),c,m))
starts=sorted(set(r for r,_,_ in rows))
md=Cs(CS_ARCH_X86,CS_MODE_64)
THREE=0x3C12BA0
seen=set(); hits=[]
for rva,c,m in rows:
    base=c.split('+')[0].split('.')[-1].split('`')[0]
    if base not in rt or rva in seen: continue
    seen.add(rva)
    i=bisect.bisect_right(starts,rva); ln=min((starts[i]-rva) if i<len(starts) else 0x200, 0x3000)
    o=pe.get_offset_from_rva(rva)
    ins=list(md.disasm(d[o:o+ln],rva))
    tags=[]
    for k,x in enumerate(ins):
        if x.mnemonic in('divss','mulss') and '[rip' in x.op_str:
            disp=int(re.search(r'rip \+ (0x[0-9a-f]+)',x.op_str).group(1),16) if 'rip +' in x.op_str else None
            if disp is not None and x.address+x.size+disp==THREE and x.mnemonic=='divss': tags.append('div3')
        if x.mnemonic=='sub' and x.op_str.endswith(', 1') and k+1<len(ins) and ins[k+1].mnemonic=='je':
            # chain of sub/je = small switch
            chain=1; j=k+2
            while j+1<len(ins) and ins[j].mnemonic=='sub' and ins[j].op_str.endswith(', 1') and ins[j+1].mnemonic=='je': chain+=1; j+=2
            if chain>=2: tags.append(f'switch{chain}')
    if tags: hits.append((c,m,rva,sorted(set(tags))))
for h in hits: print(f"{h[2]:X}\t{h[0]}::{h[1]}\t{','.join(h[3])}")
