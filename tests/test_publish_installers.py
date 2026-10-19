import contextlib
import hashlib
import importlib.util
import io
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

source=Path(__file__).with_name('Publish-Installers.py')
if not source.exists():source=Path(__file__).parents[1]/'scripts'/'Publish-Installers.py'
spec=importlib.util.spec_from_file_location('publisher',source)
p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)

class PublicationTests(unittest.TestCase):
    def run_publication(self,wrong_hash=False,move_main=False,wrong_source=False):
        with tempfile.TemporaryDirectory() as directory:
            previous=Path.cwd();os.chdir(directory)
            try:
                Path('src').mkdir();Path('installer').mkdir();Path('release').mkdir()
                Path('src/Directory.Build.props').write_text('<Project><PropertyGroup><Version>0.12.6</Version></PropertyGroup></Project>')
                Path('installer/SDE-Suporte.iss').write_text('#define AppVersion "0.12.6"')
                for name in ('SDE-Suporte-Cliente-Instalar.exe','SDE-Suporte-Tecnico-Base.exe'):Path('release',name).write_bytes(b'MZ'+b'0'*1_000_000)
                Path('QUICKSTART.md').write_text('instructions')
                calls=[];reads=0
                def api(method,url,token,**kw):
                    nonlocal reads
                    calls.append((method,url,kw))
                    if '/git/ref/heads/main' in url:
                        reads+=1;return {'object':{'sha':'advanced' if move_main and reads>1 else 'tested'}}
                    if '/releases/tags/' in url:return {'id':1,'target_commitish':'other' if wrong_source else 'tested','draft':True,'assets':[],'upload_url':'https://uploads.github.com/repos/owner/repo/releases/1/assets{?name,label}'}
                    if method=='POST' and 'uploads.github.com' in url:
                        data=kw['data'];return {'size':len(data),'state':'uploaded','digest':'sha256:'+('0'*64 if wrong_hash else hashlib.sha256(data).hexdigest())}
                    if method=='PATCH':return {'draft':False}
                    raise AssertionError('Unexpected request')
                with patch.dict(os.environ,{'GITHUB_REF':'refs/heads/main','GITHUB_REPOSITORY':'owner/repo','GITHUB_SHA':'tested','GH_TOKEN':'test'}),patch.object(p,'request',side_effect=api),contextlib.redirect_stdout(io.StringIO()):
                    if wrong_hash or move_main or wrong_source:
                        with self.assertRaises(ValueError):p.main()
                        self.assertFalse(any(m=='PATCH' for m,_,_ in calls))
                    else:
                        p.main();self.assertEqual(sum(m=='POST' for m,_,_ in calls),4)
                        self.assertEqual(calls[-1][0],'PATCH');self.assertFalse(calls[-1][2]['value']['draft'])
            finally:os.chdir(previous)
    def test_publish_after_all_uploads(self):self.run_publication()
    def test_hash_mismatch_keeps_draft(self):self.run_publication(wrong_hash=True)
    def test_stale_source_keeps_draft(self):self.run_publication(move_main=True)
    def test_version_of_another_source_rejected(self):self.run_publication(wrong_source=True)
    def test_non_main_cannot_publish(self):
        with patch.dict(os.environ,{'GITHUB_REF':'refs/heads/feature'}),patch.object(p,'request') as api:
            with self.assertRaises(ValueError):p.main()
            api.assert_not_called()

if __name__=='__main__':unittest.main()
