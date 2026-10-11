"""HTTP smoke checks. Requires a disposable migrated database and dotnet on PATH.
No secrets or OTPs are written to stdout. Starts/stops its own loopback API process.
"""
import base64
import json
import os
import secrets
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path
import yaml
from jsonschema import Draft202012Validator, FormatChecker

root = Path(__file__).resolve().parents[1]
env = os.environ.copy()
assert env.get("ConnectionStrings__Identity"), "Provide a disposable test database"
env.update(ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS="http://127.0.0.1:5155",
           Identity__MacKey=base64.b64encode(secrets.token_bytes(32)).decode(),
           Identity__MaterialKey=base64.b64encode(secrets.token_bytes(32)).decode(),
           Identity__DevInboxKey=secrets.token_urlsafe(32))
project = str(root / "src/SmartCore.Identity.Api")
dotnet = env.get("SMARTCORE_DOTNET", "dotnet")
subprocess.run([dotnet, "run", "--project", project, "--no-build", "--", "--migrate"], env=env, check=True)
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
contract = yaml.safe_load((root / "contracts/registration.openapi.yaml").read_text())
def call(path, body=None, headers=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request("http://127.0.0.1:5155" + path, data=data,
        headers={"Content-Type": "application/json", **(headers or {})})
    try:
        response = opener.open(request, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    content = response.read()
    result = json.loads(content) if content else None
    if path in contract["paths"]:
        expected = contract["paths"][path]["post"]["responses"][str(response.status)]["content"]["application/json"]["schema"]
        schema = {**expected, "components": contract["components"]}
        Draft202012Validator(schema, format_checker=FormatChecker()).validate(result)
    return response.status, result, response.headers

with subprocess.Popen([dotnet, "run", "--project", project, "--no-build"], env=env,
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as process:
    try:
        for _ in range(100):
            try:
                if call("/health/ready")[0] == 200:
                    break
            except OSError:
                pass
            time.sleep(0.1)
        else:
            raise RuntimeError("API did not become ready")
        binding = secrets.token_urlsafe(32)
        key = secrets.token_urlsafe(32)
        body = dict(email=secrets.token_hex(8) + "@example.test", password="safe sample test password", displayName="Amir", bindingSecret=binding)
        status, pending, headers = call("/auth/register", body, {"Idempotency-Key": key})
        assert status == 202 and pending["status"] == "AwaitingVerification"
        assert headers["Cache-Control"] == "no-store"
        verification = pending["verificationSessionId"]
        assert call("/dev/inbox/" + verification)[0] == 404
        status, message, _ = call("/dev/inbox/" + verification, headers={"X-Dev-Inbox-Key": env["Identity__DevInboxKey"]})
        assert status == 200
        proof = dict(verificationSessionId=verification, code=message["code"], bindingSecret=binding)
        status, result, _ = call("/auth/register/verify", proof)
        assert status == 201 and result["status"] == "PendingCredential"
        # Bounded replay budget: allow worker time before polling, then at most 3 replays.
        for _ in range(3):
            time.sleep(1.2)
            status, ready, _ = call("/auth/register/verify", proof)
            if ready.get("status") == "Ready":
                break
        assert status == 200 and ready["status"] == "Ready" and ready["registrationId"] == result["registrationId"]
        assert not any(key in ready for key in ("accessToken", "refreshToken", "session"))
        assert call("/auth/register", {**body, "extra": "field"}, {"Idempotency-Key": secrets.token_urlsafe(32)})[0] == 400
        assert call("/auth/register", {**body, "mobile": None}, {"Idempotency-Key": secrets.token_urlsafe(32)})[0] == 400
        assert call("/auth/register", {**body, "password": "short"}, {"Idempotency-Key": secrets.token_urlsafe(32)})[0] == 400
        assert call("/auth/register", body)[0] == 400
        assert call("/auth/register/" + result["registrationId"])[0] == 404
        print("HTTP PASS: pending/ready flow, no tokens, no-store, protected inbox, unknown/null input, password/header validation, no public lookup")
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
