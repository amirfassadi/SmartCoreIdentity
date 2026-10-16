"""Emit only enumerated test phase/type markers; never error messages, paths or raw log tails."""
from pathlib import Path
import re
import sys
PHASES={'Registration','AuthenticationStorageChecks','AuthenticationChecks','PasswordChangeChecks','AuthenticationAdmissionChecks','RecoveryEnrollmentChecks','PasswordResetChecks','BffRefreshChecks','Startup','Enrollment','Initiation','Completion','TimingInitiation'}
KINDS={'TimeoutException','ApiError','PostgresException','NpgsqlException','InvalidOperationException','Exception','AssertionError','RuntimeError','OSError'}
def summarize(text):
    for line in text.splitlines():
        match=re.fullmatch(r'SAFE-TEST-FAIL phase=([A-Za-z]+) type=([A-Za-z]+)',line)
        if match and match[1] in PHASES and match[2] in KINDS:
            print('[DIAGNOSTIC] test phase='+match[1]+'; failure category='+match[2])
if __name__=='__main__':summarize(Path(sys.argv[1]).read_text(errors='replace'))
