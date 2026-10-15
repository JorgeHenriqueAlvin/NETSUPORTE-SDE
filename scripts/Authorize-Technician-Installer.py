"""Create the private IT installer locally. Never publish its output or grant file."""
import argparse
import base64
import hashlib
import json
import re
import struct
from datetime import datetime, timezone
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ec


def main():
    parser = argparse.ArgumentParser(description="Prepare a private technician installer with automatic authorization.")
    parser.add_argument("--base", required=True, type=Path)
    parser.add_argument("--authorization-file", required=True, type=Path)
    parser.add_argument("--team-profile", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.base.resolve() == args.output.resolve():
        raise SystemExit("Choose a separate output file.")
    match = re.search(r"SDE1-[A-Za-z0-9_-]{43}", args.authorization_file.read_text(encoding="utf-8"))
    if not match:
        raise SystemExit("The authorization file does not contain a valid team credential.")
    code = match.group(0)
    scalar = int.from_bytes(base64.urlsafe_b64decode(code[5:] + "="), "big")
    issuer = ec.derive_private_key(scalar, ec.SECP256R1())
    public = issuer.public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
    profile = json.loads(args.team_profile.read_text(encoding="utf-8"))["approval"]["permission"]
    team_id = hashlib.sha256(public).hexdigest().upper()
    if team_id != profile["issuer"]["id"]:
        raise SystemExit("Authorization belongs to another team.")
    expires = datetime.fromisoformat(profile["expires"])
    if expires <= datetime.now(timezone.utc):
        raise SystemExit("Team authorization has expired.")
    data = args.base.read_bytes()
    if data[:2] != b"MZ":
        raise SystemExit("The installer is not a Windows executable.")
    grant = {"teamId": team_id, "teamCode": code, "expires": expires.isoformat(), "installerSha256": hashlib.sha256(data).hexdigest().upper()}
    metadata = json.dumps(grant, separators=(",", ":")).encode("utf-8")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(data + metadata + struct.pack("<q", len(metadata)) + b"SDE-TECH-SETUP-1")
    args.output.chmod(0o600)
    # No grant, private key, or authorization text is printed.
    print(f"Private technician installer created: {args.output.name} ({args.output.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
