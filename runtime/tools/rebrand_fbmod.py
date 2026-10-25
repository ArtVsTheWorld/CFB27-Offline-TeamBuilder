"""Update only an existing Frosty v9 mod's display details. Standard library only.

The resource records, game compatibility fields, collector data and all embedded
payloads remain byte-identical. The source is never modified.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

AUTHORS = "JDHalfRack / Ace / Timberline Tex"
DESCRIPTION = (
    "Adds TeamBuilder teams to offline Dynasty. Use with the included Offline "
    "TeamBuilder Runtime plugin to restore uniforms, stadiums, logos and custom "
    "stadium sounds when loading your Dynasty, without resetting team records, "
    "rosters or Dynasty progress. Teams must already be downloaded. Offline only."
)


def header(data):
    if len(data) < 50 or struct.unpack_from("<QI", data) != (72155812747760198, 9):
        raise ValueError("Expected a Frosty v9 fbmod")
    manifest, count = struct.unpack_from("<qi", data, 12)
    at = 25 + data[24]
    details_start = at + 24  # game head (4) plus SHA-1 (20), not an EXE checksum
    if count < 0 or count > 10000 or not details_start < manifest <= len(data) - count * 16:
        raise ValueError("Invalid fbmod header/manifest bounds")
    fields = []
    at = details_start
    for _ in range(6):
        end = data.index(0, at, manifest)
        fields.append(data[at:end].decode("utf-8"))
        at = end + 1
    return dict(manifest=manifest, count=count, details_start=details_start,
                resources_start=at, details=fields)


def rebrand(original, version):
    before = header(original)
    fields = ["Offline TeamBuilder", AUTHORS, "TeamBuilder", version, DESCRIPTION, ""]
    strings = b"".join(value.encode("utf-8") + b"\0" for value in fields)
    output = bytearray(original[:before["details_start"]] + strings + original[before["resources_start"]:])
    delta = len(output) - len(original)
    struct.pack_into("<q", output, 12, before["manifest"] + delta)
    after = header(output)
    if after["details"] != fields or after["count"] != before["count"]:
        raise ValueError("Display details did not round-trip")
    if output[24:before["details_start"]] != original[24:before["details_start"]]:
        raise ValueError("Game compatibility fields changed")
    if output[after["resources_start"]:after["manifest"]] != original[before["resources_start"]:before["manifest"]]:
        raise ValueError("Resource records changed")
    if output[after["manifest"]:] != original[before["manifest"]:]:
        raise ValueError("Manifest or embedded payloads changed")
    return bytes(output), fields


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--expected-source", help="Optional known-working fbmod SHA256; never the game EXE")
    args = parser.parse_args()
    if args.destination.exists() or args.source.resolve() == args.destination.resolve():
        raise ValueError("Destination must be a new file")
    original = args.source.read_bytes()
    source_sha = hashlib.sha256(original).hexdigest()
    if args.expected_source and source_sha.lower() != args.expected_source.lower():
        raise ValueError("Source does not match the selected working fbmod")
    output, details = rebrand(original, args.version)
    args.destination.parent.mkdir(parents=True, exist_ok=True)
    args.destination.write_bytes(output)
    print(json.dumps(dict(source_sha256=source_sha, output_sha256=hashlib.sha256(output).hexdigest(),
                          details=details, game_compatibility_unchanged=True,
                          resource_records_unchanged=True, manifest_and_payloads_unchanged=True), indent=2))


if __name__ == "__main__":
    main()
