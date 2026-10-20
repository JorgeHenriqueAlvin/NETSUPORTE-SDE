"""Publish only the installers produced and tested by this exact main-branch CI run."""
import hashlib
import json
import os
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET


def request(method, url, token, value=None, data=None, content_type="application/json"):
    if urllib.parse.urlparse(url).hostname not in {"api.github.com", "uploads.github.com"}:
        raise ValueError("Unexpected publication host")
    if value is not None:
        data = json.dumps(value).encode()
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "Content-Type": content_type,
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "SDE-tested-installer-publisher",
    })
    with urllib.request.urlopen(req, timeout=300) as response:
        body = response.read()
        return json.loads(body) if body else None


def main():
    if os.environ["GITHUB_REF"] != "refs/heads/main":
        raise ValueError("Only main can publish an automatic update")
    repo = os.environ["GITHUB_REPOSITORY"]
    sha = os.environ["GITHUB_SHA"]
    token = os.environ["GH_TOKEN"]
    api = "https://api.github.com/repos/" + repo
    call = lambda method, path, **kw: request(method, api + path, token, **kw)
    if call("GET", "/git/ref/heads/main")["object"]["sha"] != sha:
        print("Main changed during testing; this stale build will not be published.")
        return
    version = ET.parse("src/Directory.Build.props").findtext(".//Version")
    import re
    if not version or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Invalid release version")
    if '#define AppVersion "' + version + '"' not in Path("installer/SDE-Suporte.iss").read_text():
        raise ValueError("Installer and application versions differ")
    tag = "v" + version
    files = {name: Path("release") / name for name in (
        "SDE-Suporte-Cliente-Instalar.exe", "SDE-Suporte-Tecnico-Base.exe")}
    assets = {}
    for name, path in files.items():
        data = path.read_bytes()
        if not 1_000_000 <= len(data) <= 250_000_000 or data[:2] != b"MZ":
            raise ValueError("Invalid installer: " + name)
        assets[name] = {"sha256": hashlib.sha256(data).hexdigest(), "size": len(data)}
    manifest = Path("release/update.json")
    manifest.write_text(json.dumps({"version": version, "assets": assets}), encoding="utf-8")
    files["update.json"] = manifest
    files["QUICKSTART.md"] = Path("QUICKSTART.md")
    try:
        release = call("GET", "/releases/tags/" + tag)
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        release = call("POST", "/releases", value={
            "tag_name": tag, "target_commitish": sha, "draft": True,
            "name": "SDE Suporte " + version + " — atualização e diagnóstico",
            "body": "Interface renovada: tema azul e turquesa, tabelas e fichas padronizadas, ações por tarefa e páginas internas com mais espaço. Inventário, avisos e atualizações têm rolagem vertical em janelas compactas; remoção de cadastros exige confirmação. Mantém autenticação, certificados e consentimento. Capturas das seis áreas em 900×560, 1024×768 e 1360×850 e testes de regressão executados. Detalhes e limites em docs/INTERFACE-0.12.20.md. O agente Raspberry é distribuído separadamente na release raspberry-stable. Validação em máquinas reais do setor, DPI, toque, leitor de tela e Raspberry físico continua necessária.",
        })
    if release["target_commitish"] != sha:
        raise ValueError("Version already belongs to a different source commit; increment it")
    if not release["draft"]:
        print("This source version is already published; it will not be overwritten.")
        return
    upload = release["upload_url"].split("{", 1)[0]
    # Resuming a failed upload may replace only assets in this unpublished draft.
    for asset in release.get("assets", []):
        if asset["name"] in files:
            call("DELETE", "/releases/assets/" + str(asset["id"]))
    for name, path in files.items():
        data = path.read_bytes()
        uploaded = request("POST", upload + "?name=" + urllib.parse.quote(name), token,
                           data=data, content_type="application/octet-stream")
        if uploaded.get("size") != len(data) or uploaded.get("state") != "uploaded":
            raise ValueError("Upload incomplete: " + name)
        digest = uploaded.get("digest")
        if digest and digest != "sha256:" + hashlib.sha256(data).hexdigest():
            raise ValueError("Uploaded hash mismatch: " + name)
    if call("GET", "/git/ref/heads/main")["object"]["sha"] != sha:
        raise ValueError("Main changed; leaving the draft unpublished")
    call("PATCH", "/releases/" + str(release["id"]), value={"draft": False, "make_latest": "true"})
    print("Published tested Windows update " + tag)


if __name__ == "__main__":
    main()
