"""Independently parse this release's v18 project through EOF against SDK export.

No editor session or game process is opened. Standard library only.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys
import uuid


class Reader:
    def __init__(self, data):
        self.data, self.at = data, 0

    def take(self, size):
        if not 0 <= size <= len(self.data)-self.at:
            raise ValueError('Project read exceeds file bounds')
        data = self.data[self.at:self.at+size]
        self.at += size
        return data

    def n(self, fmt='i'):
        return struct.unpack('<'+fmt, self.take(struct.calcsize('<'+fmt)))[0]

    def s(self):
        end = self.data.index(0, self.at)
        return self.take(end-self.at+1)[:-1].decode('utf-8')

    def guid(self):
        return str(uuid.UUID(bytes_le=self.take(16)))

    def blob(self):
        return self.take(self.n())


def verify(project, report):
    expected = {c['guid']: c for c in report['chunks']}
    r = Reader(project)
    assert r.take(8) == b'FROSTY\0\0' and r.n('I') == 18
    assert r.s() == 'CollegeFB27'
    assert r.n('q') > 0 and r.n('q') > 0 and r.n('I') == report['head']
    details = [r.s() for _ in range(5)]
    assert details[:2] == [report['title'], report['authors']]
    assert details[2] == 'TeamBuilder' and details[3] == report['version']
    for _ in range(5):
        r.blob()
    assert [r.n() for _ in range(4)] == [0]*4
    assert r.n() == len(expected) == 3
    added = set()
    for _ in expected:
        guid = r.guid()
        assert guid in expected and guid not in added
        assert (r.n(), r.n('q')) == (expected[guid]['h32'], expected[guid]['h64'])
        added.add(guid)
    assert r.n() == 0 and r.n() == 0
    assert r.n() == len(expected)
    modified = set()
    for _ in expected:
        guid = r.guid()
        assert guid in expected and guid not in modified
        c = expected[guid]
        assert r.n() == 0  # no additional bundle memberships
        assert [r.s() for _ in range(r.n())] == c['superbundles']
        assert (r.n(), r.n('q')) == (c['h32'], c['h64']) and r.n('B') == 1
        digest = r.take(20).hex()
        assert [r.n('I') for _ in range(4)] == [0, c['logicalSize'], 0, 0]
        assert r.n() == -1
        assert (r.n(), r.n('q')) == (c['h32'], c['h64']) and r.n('B') == 1
        assert r.s() == 'legacy;'+c['name']
        payload = r.blob()
        assert len(payload) == c['payloadBytes']
        assert hashlib.sha1(payload).hexdigest() == digest == c['payloadSha1']
        modified.add(guid)
    assert modified == added == set(expected)
    assert r.n() == 2 and r.s() == 'legacy' and r.n() == 3
    names = set()
    for _ in expected:
        name = r.s()
        assert r.n() == 1 and r.s() == 'chunk'
        guid = r.guid()
        c = expected[guid]
        assert name == c['name'] and name not in names and r.guid() == guid
        assert [r.n('q') for _ in range(4)] == [0, 0, c['payloadBytes'], c['logicalSize']]
        names.add(name)
    assert r.s() == 'fs' and r.n() == 0
    assert r.at == len(project)
    assert report['collectorLinks'] == 6
    return dict(format=18, chunks=len(modified), legacyFiles=sorted(names),
                collectorLinks=report['collectorLinks'], parsedThroughEof=True,
                sdkDecodedPayloadsMatch=True, sha256=hashlib.sha256(project).hexdigest())


if __name__ == '__main__':
    result = verify(Path(sys.argv[1]).read_bytes(), json.loads(Path(sys.argv[2]).read_text(encoding='utf-8-sig')))
    print(json.dumps(result, indent=2))
