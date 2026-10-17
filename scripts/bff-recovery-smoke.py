"""Private B3 opt-in on isolated real Identity/two-BFF/HTTPS infrastructure."""
import os
from pathlib import Path
import runpy
os.environ['SMARTCORE_B3']='yes'
runpy.run_path(str(Path(__file__).with_name('bff-account-smoke.py')),run_name='__main__')
