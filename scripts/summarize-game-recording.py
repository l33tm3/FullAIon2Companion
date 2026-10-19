"""Summarize a private gameplay JSONL recording and its --all-frames TSV.

Does not claim to decode unknown opcodes or extract probability tables.
Usage: python scripts/summarize-game-recording.py RECORDING FRAMES OUTPUT
"""
import base64
from collections import Counter
from datetime import datetime
import hashlib
import json
from pathlib import Path
import sys


def summarize(recording: Path, frames_file: Path) -> dict:
    directions = Counter()
    byte_counts = Counter()
    client_sizes = Counter()
    times = []
    for line in recording.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip():
            continue
        segment = json.loads(line)
        direction = "server" if segment["FromServer"] else "client"
        payload = base64.b64decode(segment["Payload"], validate=True)
        directions[direction] += 1
        byte_counts[direction] += len(payload)
        if direction == "client":
            client_sizes[len(payload)] += 1
        times.append(datetime.fromisoformat(segment["Timestamp"]))
    opcodes = Counter()
    minutes = {}
    for line in frames_file.read_text(encoding="utf-8-sig").splitlines():
        fields = line.split("\t")
        if len(fields) != 3 or line.startswith("#") or fields[1].startswith("#"):
            continue
        opcodes[fields[1]] += 1
        minute = fields[0][:5]
        minutes.setdefault(minute, Counter())[fields[1]] += 1
    return {
        "recording": str(recording.resolve()),
        "sha256": hashlib.sha256(recording.read_bytes()).hexdigest(),
        "firstPacket": min(times).isoformat() if times else None,
        "lastPacket": max(times).isoformat() if times else None,
        "durationSeconds": (max(times) - min(times)).total_seconds() if times else 0,
        "tcpSegments": dict(directions),
        "payloadBytes": dict(byte_counts),
        "clientPayloadSizes": dict(client_sizes.most_common()),
        "decodedServerFramesIncludingBundleChildren": sum(opcodes.values()),
        "opcodes": dict(opcodes.most_common()),
        "opcodesByMinute": {m: dict(c.most_common()) for m, c in sorted(minutes.items())},
        "probabilityTablesDecoded": False,
        "note": "Transport/opcode inventory only. Navigation times were not individually marked; no table semantics inferred.",
    }


if __name__ == "__main__":
    result = summarize(Path(sys.argv[1]), Path(sys.argv[2]))
    Path(sys.argv[3]).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("durationSeconds", "tcpSegments", "decodedServerFramesIncludingBundleChildren", "probabilityTablesDecoded")}, indent=2))
