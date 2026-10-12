"""Run B2 on the same disposable real Identity/two-BFF/TLS stack, independently of B1."""
import os
from pathlib import Path
import runpy
os.environ['SMARTCORE_B2']='yes'
runpy.run_path(str(Path(__file__).with_name('bff-account-smoke.py')),run_name='__main__')
