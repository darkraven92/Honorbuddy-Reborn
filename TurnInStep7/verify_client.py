#!/usr/bin/env python3
"""Verify the static evidence against the supplied executable; standard library only."""
import hashlib
import json
import struct
import sys
from pathlib import Path


def main():
    if len(sys.argv) != 2:
        raise SystemExit('Usage: python3 TurnInStep7/verify_client.py /path/to/WoW.exe')
    data = Path(sys.argv[1]).read_bytes()
    evidence = json.loads(Path(__file__).with_name('client-anchors.json').read_text())
    if hashlib.sha256(data).hexdigest() != evidence['sha256']:
        raise SystemExit('FAIL: executable SHA-256 differs')
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[pe:pe + 4] != b'PE\0\0':
        raise SystemExit('FAIL: missing PE signature')
    count = struct.unpack_from('<H', data, pe + 6)[0]
    optional_size = struct.unpack_from('<H', data, pe + 20)[0]
    section_start = pe + 24 + optional_size
    sections = [struct.unpack_from('<IIII', data, section_start + 40 * i + 8) for i in range(count)]
    for anchor in evidence['anchors']:
        rva = int(anchor['va'], 16) - evidence['image_base']
        expected = bytes.fromhex(anchor['bytes'])
        for virtual_size, virtual_start, raw_size, raw_start in sections:
            if virtual_start <= rva and rva + len(expected) <= virtual_start + raw_size:
                offset = raw_start + rva - virtual_start
                if data[offset:offset + len(expected)] != expected:
                    raise SystemExit('FAIL: anchor ' + anchor['va'])
                break
        else:
            raise SystemExit('FAIL: unmapped anchor ' + anchor['va'])
    print(f"CLIENT STATIC EVIDENCE: PASS (SHA-256 and {len(evidence['anchors'])} anchors; not a live test)")


if __name__ == '__main__':
    main()
