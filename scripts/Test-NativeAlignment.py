"""Inspect actual packaged ARM64 ELF load segments for Android 16 KB alignment."""
import argparse
import json
import struct
import zipfile
parser=argparse.ArgumentParser(); parser.add_argument('apk'); args=parser.parse_args()
results=[]
with zipfile.ZipFile(args.apk) as archive:
    for name in archive.namelist():
        if name.startswith('lib/arm64-v8a/') and name.endswith('.so'):
            data=archive.read(name)
            if data[:6] != b'\x7fELF\x02\x01': raise ValueError(f'Expected little-endian ELF64: {name}')
            offset=struct.unpack_from('<Q',data,32)[0]; size,count=struct.unpack_from('<HH',data,54)
            loads=[struct.unpack_from('<IIQQQQQQ',data,offset+i*size) for i in range(count)]
            loads=[p for p in loads if p[0]==1]
            passed=bool(loads) and all(p[7]>=16384 and p[2]%16384==p[3]%16384 for p in loads)
            results.append({'library':name,'loadAlignment':[p[7] for p in loads],'passed':passed})
print(json.dumps(results,indent=2))
if not results or not all(x['passed'] for x in results): raise SystemExit(1)
