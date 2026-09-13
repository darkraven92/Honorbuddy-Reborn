#!/usr/bin/env python3
"""Static inspection only. Requires dnfile and OpenSSL's legacy DES provider."""
import base64
import hashlib
from pathlib import Path
import struct
import subprocess
import sys
import zlib


def main():
    if len(sys.argv) != 2:
        raise SystemExit('Usage: python3 verify_honorbuddy_api.py /path/to/Honorbuddy.exe')
    import dnfile
    path = Path(sys.argv[1])
    if hashlib.sha256(path.read_bytes()).hexdigest() != '87ff9ef92f02e89c1d060c0071581730dcb446aae597b6f0d97006f46e118f3a':
        raise SystemExit('FAIL: unsupported reference executable')
    pe = dnfile.dnPE(str(path))
    fields = {row.Field.row_index: row.Rva for row in pe.net.mdtables.FieldRva}
    key = pe.get_data(fields[0x12D0], 8)
    iv = pe.get_data(fields[0x12D1], 8)
    resource = next(r for r in pe.net.resources if r.name == '{ce30cf31-4fc4-42b6-8557-99c45f582bae}')
    data = subprocess.run(['openssl', 'enc', '-d', '-des-cbc', '-provider', 'legacy',
                           '-K', key.hex(), '-iv', iv.hex()], input=resource.data[4:],
                          capture_output=True, check=True).stdout
    if data[:4] != b'{z}\x01':
        raise SystemExit('FAIL: unexpected decompressed resource header')
    total = struct.unpack_from('<I', data, 4)[0]
    position = 8
    strings = bytearray()
    while position < len(data):
        packed_size, raw_size = struct.unpack_from('<II', data, position)
        position += 8
        chunk = zlib.decompress(data[position:position + packed_size], -15)
        if len(chunk) != raw_size:
            raise SystemExit('FAIL: chunk length')
        strings.extend(chunk)
        position += packed_size
    if len(strings) != total:
        raise SystemExit('FAIL: resource length')
    for name, encoded_offset in [('ClickContinue', 155570), ('CompleteQuest', 155232)]:
        offset = encoded_offset - 0x4006 - 38
        size = strings[offset]
        offset += 1
        if size & 128:
            if not size & 64:
                size = ((size & 63) << 8) + strings[offset]
                offset += 1
            else:
                size = ((size & 31) << 24) + int.from_bytes(strings[offset:offset + 3], 'big')
                offset += 3
        value = base64.b64decode(strings[offset:offset + size]).decode('utf-8')
        print(f'QuestFrame.{name}(): {value}')
        if name == 'ClickContinue' and value != 'QuestFrameCompleteButton:Click()':
            raise SystemExit('FAIL: unexpected Continue command')
    print('ORIGINAL API EVIDENCE: PASS (static resource inspection; no execution of Honorbuddy)')


if __name__ == '__main__':
    main()
