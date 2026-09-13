#!/usr/bin/env python3
"""Read-only verification of the small set of ClassicDB facts used by the profile."""
import gzip
import hashlib
import re
import sys


def main():
    if len(sys.argv) != 2:
        raise SystemExit('Usage: verify_reference.py /path/to/ClassicDB_1_12_1_z2815.sql.gz')
    with open(sys.argv[1], 'rb') as source:
        digest = hashlib.file_digest(source, 'sha256').hexdigest()
    if digest != '4f92db520868ab4e566726f68b5b2e380ae781209beaf22237b4f7f04600d0c0':
        raise SystemExit('FAIL: reference archive hash differs')
    names = {}
    spawns = {}
    relations = {'creature_questrelation': set(), 'creature_involvedrelation': set()}
    wanted_names = {10176: 'Kaltunk', 3143: 'Gornek', 3144: 'Eitrigg', 3098: 'Mottled Boar', 3124: 'Scorpid Worker'}
    wanted_spawns = {
        3442: (10176, 1, -607.434, -4251.33, 39.0393),
        3443: (3143, 1, -600.132, -4186.19, 41.2663),
        12960: (3098, 1, -516.59, -4286.81, 40.0634),
        8552: (3124, 1, -680.309, -4145.69, 36.0694),
    }
    with gzip.open(sys.argv[1], 'rt') as source:
        for line in source:
            if line.startswith('INSERT INTO `creature_template` '):
                for entry, name in re.findall(r"\((10176|3143|3144|3098|3124),'([^']*)'", line):
                    names[int(entry)] = name
            elif line.startswith('INSERT INTO `creature` '):
                for row in re.findall(r'\(([^()]*)\)', line):
                    fields = row.split(',')
                    guid = int(fields[0])
                    if guid in wanted_spawns:
                        spawns[guid] = (int(fields[1]), int(fields[2]), *map(float, fields[4:7]))
            else:
                for table in relations:
                    if line.startswith(f'INSERT INTO `{table}` '):
                        for npc, quest in re.findall(r'\((\d+),(\d+)\)', line):
                            if int(quest) in (4641, 788, 789):
                                relations[table].add((int(npc), int(quest)))
    if names != wanted_names or spawns != wanted_spawns:
        raise SystemExit('FAIL: NPC identity or spawn coordinates differ')
    if relations['creature_questrelation'] != {(10176, 4641), (3143, 788), (3143, 789)}:
        raise SystemExit('FAIL: quest giver relations differ')
    if relations['creature_involvedrelation'] != {(3143, 4641), (3143, 788), (3143, 789)}:
        raise SystemExit('FAIL: turn-in relations differ')
    print('ORC REFERENCE: PASS (archive hash, 5 identities, 4 spawns, 6 quest relations)')


if __name__ == '__main__':
    main()
