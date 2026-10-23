import struct,sys,pefile
from paths import GA, METHODS_TSV, DUMMY_RUNTIME_CS
d=open(GA,'rb').read(); pe=pefile.PE(GA,fast_load=True)
for a in sys.argv[1:]:
    ins,_,disp=a.partition('+')   # next-instruction rva + displacement
    r=int(ins,16)+int(disp,16)
    o=pe.get_offset_from_rva(r)
    print(a, hex(r), struct.unpack('<f',d[o:o+4])[0])
