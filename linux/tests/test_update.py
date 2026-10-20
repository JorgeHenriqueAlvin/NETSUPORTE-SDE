import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("updater", Path(__file__).parents[1] / "update.py")
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)

class UpdaterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        updater.INSTALL = self.root / "install"
        updater.LAUNCHER = self.root / "bin" / "launcher"
        updater.STATE = self.root / "state"
        updater.INSTALL.mkdir()
        updater.STATE.mkdir()
        (updater.INSTALL / "version.txt").write_text("0.12.14")
        self.old_fetch = updater.fetch

    def tearDown(self):
        updater.fetch = self.old_fetch
        self.temp.cleanup()

    def package(self, name="SDE-Suporte-Cliente-Linux", version="0.12.15", link=False):
        output = io.BytesIO()
        with tarfile.open(fileobj=output, mode="w:gz") as archive:
            for path, value in [(name, b"binary"), ("desktop.py", b"desktop"), ("update.py", b"updater"), ("launch.sh", b"launcher"), ("version.txt", version.encode())]:
                item = tarfile.TarInfo(path)
                if link and path == name:
                    item.type = tarfile.SYMTYPE
                    item.linkname = "/etc/passwd"
                    archive.addfile(item)
                else:
                    item.size = len(value)
                    archive.addfile(item, io.BytesIO(value))
        # Production compressed payloads are >=1MB; deterministic padding lets
        # tests exercise the same size validation without mocking the limits.
        return output.getvalue() + bytes(1_000_000)

    def prepare(self, raw, target="0.12.15"):
        data = dict(version=target, asset=updater.ASSET, size=len(raw), sha256=hashlib.sha256(raw).hexdigest())
        (updater.STATE / updater.ASSET).write_bytes(raw)
        (updater.STATE / "pending.json").write_text(json.dumps(data))
        return data

    def test_success_and_preserved_identity(self):
        identity = self.root / "agent.json"
        identity.write_text("persistent identity")
        self.prepare(self.package())
        updater.apply()
        self.assertEqual((updater.INSTALL / "version.txt").read_text(), "0.12.15")
        self.assertEqual(identity.read_text(), "persistent identity")
        self.assertEqual(json.loads((updater.STATE / "status.json").read_text())["state"], "Installed")
        self.assertTrue(updater.INSTALL.with_name("sde-suporte.previous").exists())

    def test_reject_corruption_before_replacement(self):
        self.prepare(self.package())
        (updater.STATE / updater.ASSET).write_bytes(b"bad")
        with self.assertRaises(ValueError):
            updater.apply()
        self.assertEqual((updater.INSTALL / "version.txt").read_text(), "0.12.14")

    def test_reject_traversal_and_symlink(self):
        for raw in (self.package("../outside"), self.package(link=True)):
            self.prepare(raw)
            with self.assertRaises(ValueError):
                updater.apply()
            self.assertFalse((self.root / "outside").exists())
            self.assertEqual((updater.INSTALL / "version.txt").read_text(), "0.12.14")

    def test_downgrade_does_not_replace(self):
        self.prepare(self.package(version="0.12.13"), "0.12.13")
        updater.apply()
        self.assertEqual((updater.INSTALL / "version.txt").read_text(), "0.12.14")

    def test_failed_check_is_not_current(self):
        updater.fetch = lambda *_: json.dumps(dict(version="0.12.15", asset=updater.ASSET, size=1_000_001, sha256="0"*64)).encode()
        with self.assertRaises(ValueError):
            updater.check()
        self.assertFalse((updater.STATE / "pending.json").exists())

if __name__ == "__main__":
    unittest.main()
