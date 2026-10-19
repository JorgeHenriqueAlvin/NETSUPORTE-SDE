import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('desktop',Path(__file__).parents[1]/'desktop.py')
d=importlib.util.module_from_spec(spec);spec.loader.exec_module(d)

class DesktopTests(unittest.TestCase):
    def test_monitors_and_primary(self):
        with patch.object(d,'run',return_value='Monitors: 2\n 0: +*HDMI-1 1920/509x1080/286+0+0 HDMI-1\n 1: +HDMI-2 1280/339x720/190+1920+0 HDMI-2'):
            screens=d.monitors()
        self.assertEqual(len(screens),2)
        self.assertTrue(screens[0]['primary'])
        self.assertEqual(screens[1]['x'],1920)

    def test_win32_right_mouse_protocol(self):
        screens=[dict(id='HDMI-1',name='HDMI-1',x=0,y=0,width=1920,height=1080,primary=True)]
        with patch.object(d,'monitors',return_value=screens),patch.object(d,'run') as run:
            d.apply_input(dict(kind='down',x=12,y=34,key=2,layout=d.layout(screens)))
            self.assertEqual(run.call_args.args,('xdotool','mousedown','3'))

    def test_stale_layout_rejected_before_input(self):
        screens=[dict(x=0,y=0,width=1920,height=1080)]
        with patch.object(d,'monitors',return_value=screens),patch.object(d,'run') as run:
            with self.assertRaises(ValueError):d.apply_input(dict(kind='move',layout='old'))
            run.assert_not_called()

    def test_no_shell_or_option_interpretation_for_text(self):
        with patch.object(d,'run') as run:
            d.apply_input(dict(kind='text',text='--window $(touch /tmp/bad)'))
            self.assertEqual(run.call_args.args,('xdotool','type','--clearmodifiers','--','--window $(touch /tmp/bad)'))

    def test_unknown_key_and_out_of_bounds_rejected(self):
        with self.assertRaises(ValueError):d.apply_input(dict(kind='keyDown',key=999))
        with patch.object(d,'monitors',return_value=[dict(x=0,y=0,width=100,height=100)]),patch.object(d,'run') as run:
            with self.assertRaises(ValueError):d.apply_input(dict(kind='move',x=100,y=0))
            run.assert_not_called()

if __name__=='__main__':unittest.main()
