#!/usr/bin/env python3
"""Fixed-origin, bounded Raspberry updater; apply only after agent exit 75."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tarfile
import urllib.request

ORIGIN = "https://github.com/JorgeHenriqueAlvin/NETSUPORTE-SDE/releases/download/raspberry-stable/"
ASSET = "SDE-Suporte-Raspberry-Pi-ARM64.tar.gz"
STATE = Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local/share"))) / "SdeSuporte" / "UpdateCache"
INSTALL = Path.home() / ".local/lib/sde-suporte"
LAUNCHER = Path.home() / ".local/bin/sde-suporte-cliente"
MAX_BYTES = 250_000_000

def version(value):
    if not isinstance(value, str) or not re.fullmatch(r"\d+\.\d+\.\d+", value):
        raise ValueError("Versao invalida")
    return tuple(map(int, value.split(".")))

def status(state, target="", detail=""):
    STATE.mkdir(parents=True, exist_ok=True, mode=0o700)
    current = (INSTALL / "version.txt").read_text().strip() if (INSTALL / "version.txt").exists() else ""
    data = dict(at=datetime.datetime.now(datetime.timezone.utc).isoformat(), role="Agent", state=state,
                targetVersion=target, installedVersion=current, detail=detail)
    temp = STATE / "status.tmp"
    temp.write_text(json.dumps(data))
    temp.replace(STATE / "status.json")

def fetch(name, limit):
    with urllib.request.urlopen(ORIGIN + name, timeout=45) as response:
        if not response.url.startswith("https://"):
            raise ValueError("Download sem HTTPS")
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError("Download excedeu limite")
    return data

def validate_manifest(data):
    target = data["version"]
    version(target)
    if data.get("asset") != ASSET or not re.fullmatch(r"[a-fA-F0-9]{64}", data.get("sha256", "")):
        raise ValueError("Manifesto invalido")
    if type(data.get("size")) is not int or not 1_000_000 <= data["size"] <= MAX_BYTES:
        raise ValueError("Tamanho invalido")
    return target

def safe_extract(package, destination):
    with tarfile.open(package, "r:gz") as archive:
        members = archive.getmembers()
        if len(members) > 3000 or sum(m.size for m in members) > 800_000_000:
            raise ValueError("Arquivo descompactado excedeu limite")
        for member in members:
            path = Path(member.name)
            if path.is_absolute() or ".." in path.parts or not (member.isfile() or member.isdir()):
                raise ValueError("Caminho ou tipo de arquivo nao permitido")
        archive.extractall(destination, members=members, filter="data")
    for name in ("SDE-Suporte-Cliente-Linux", "desktop.py", "update.py", "version.txt"):
        if not (destination / name).is_file():
            raise ValueError("Pacote incompleto")

def check():
    data = json.loads(fetch("linux-update.json", 65536))
    target = validate_manifest(data)
    current = (INSTALL / "version.txt").read_text().strip()
    if version(target) <= version(current):
        (STATE / "pending.json").unlink(missing_ok=True)
        status("Current", current, "Versao instalada confirmada")
        return
    status("Downloading", target, "Preparando pacote verificado")
    package = STATE / ASSET
    valid = package.exists() and package.stat().st_size == data["size"] and hashlib.sha256(package.read_bytes()).hexdigest().lower() == data["sha256"].lower()
    if not valid:
        raw = fetch(ASSET, data["size"])
        if len(raw) != data["size"] or hashlib.sha256(raw).hexdigest().lower() != data["sha256"].lower():
            raise ValueError("SHA256 ou tamanho divergente")
        temp = STATE / "package.tmp"
        temp.write_bytes(raw)
        temp.replace(package)
    (STATE / "pending.json").write_text(json.dumps(data))
    status("WaitingForIdle", target, "Pacote verificado; aguardando fim do atendimento")

def apply():
    data = json.loads((STATE / "pending.json").read_text())
    target = validate_manifest(data)
    current = (INSTALL / "version.txt").read_text().strip()
    if version(target) <= version(current):
        (STATE / "pending.json").unlink(missing_ok=True)
        return
    package = STATE / ASSET
    if package.stat().st_size != data["size"] or hashlib.sha256(package.read_bytes()).hexdigest().lower() != data["sha256"].lower():
        raise ValueError("Integridade recusada antes da instalacao")
    staged = INSTALL.with_name("sde-suporte.next")
    backup = INSTALL.with_name("sde-suporte.previous")
    shutil.rmtree(staged, ignore_errors=True)
    staged.mkdir(mode=0o700)
    safe_extract(package, staged)
    if (staged / "version.txt").read_text().strip() != target:
        raise ValueError("Versao do pacote divergente")
    (staged / "SDE-Suporte-Cliente-Linux").chmod(0o700)
    status("Installing", target)
    shutil.rmtree(backup, ignore_errors=True)
    INSTALL.rename(backup)
    try:
        staged.rename(INSTALL)
    except Exception:
        backup.rename(INSTALL)
        raise
    launcher = INSTALL / "launch.sh"
    if launcher.is_file():
        destination = LAUNCHER
        destination.parent.mkdir(parents=True, exist_ok=True)
        temporary = destination.with_suffix(".next")
        shutil.copyfile(launcher, temporary)
        temporary.chmod(0o700)
        temporary.replace(destination)
    (STATE / "pending.json").unlink(missing_ok=True)
    status("Installed", target, "Arquivos e versao verificados; reiniciando agente")
    package.unlink(missing_ok=True)

def main():
    STATE.mkdir(parents=True, exist_ok=True, mode=0o700)
    import fcntl
    with (STATE / "linux-update.lock").open("w") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return
        try:
            if sys.argv[1:] == ["--apply"]:
                apply()
            elif sys.argv[1:] == ["--check"]:
                check()
            else:
                raise ValueError("Use --check ou --apply")
        except Exception as error:
            # Avoid endlessly asking the agent to exit for a damaged package.
            if sys.argv[1:] == ["--apply"]:
                (STATE / "pending.json").unlink(missing_ok=True)
            status("Failed", detail=str(error))
            print("Atualizacao: " + str(error), file=sys.stderr)
            sys.exit(1)

if __name__ == "__main__":
    main()
