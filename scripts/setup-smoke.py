"""HTTP setup/completion checks against an exclusively disposable test database."""
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
env.update(ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS="http://127.0.0.1:5156",
           Identity__MacKey=base64.b64encode(secrets.token_bytes(32)).decode(),
           Identity__MaterialKey=base64.b64encode(secrets.token_bytes(32)).decode(),
           Identity__DevInboxKey=secrets.token_urlsafe(32), Identity__WorkerEnabled="false")
project = str(root / "src/SmartCore.Identity.Api")
dotnet = env.get("SMARTCORE_DOTNET", "dotnet")
subprocess.run([dotnet, "run", "--project", project, "--no-build", "--", "--migrate"], env=env, check=True)
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
contract = yaml.safe_load((root / "contracts/registration.openapi.yaml").read_text())

def call(path, body=None, headers=None):
    request = urllib.request.Request("http://127.0.0.1:5156" + path,
        data=None if body is None else json.dumps(body).encode(),
        headers={"Content-Type": "application/json", **(headers or {})})
    try:
        response = opener.open(request, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    data = response.read()
    result = json.loads(data) if data else None
    if path in contract["paths"]:
        schema = contract["paths"][path]["post"]["responses"][str(response.status)]["content"]["application/json"]["schema"]
        Draft202012Validator({**schema, "components": contract["components"]}, format_checker=FormatChecker()).validate(result)
        assert response.headers["Cache-Control"] == "no-store"
    return response.status, result

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
        contact = secrets.token_hex(8) + "@example.test"
        inbox = {"X-Dev-Inbox-Key": env["Identity__DevInboxKey"]}

        def start(binding):
            status, result = call("/auth/register", dict(email=contact, password="safe original test password",
                displayName="Example", bindingSecret=binding), {"Idempotency-Key": secrets.token_urlsafe(32)})
            assert status == 202
            return result["verificationSessionId"]

        original_binding = secrets.token_urlsafe(32)
        original = start(original_binding)
        original_code = call("/dev/inbox/" + original, headers=inbox)[1]["code"]
        status, registration = call("/auth/register/verify", dict(verificationSessionId=original, code=original_code, bindingSecret=original_binding))
        assert status == 201 and registration["status"] == "PendingCredential"
        binding = secrets.token_urlsafe(32)
        verification = start(binding)
        code = call("/dev/inbox/" + verification, headers=inbox)[1]["code"]
        proof = dict(verificationSessionId=verification, code=code, bindingSecret=binding)
        status, conflict = call("/auth/register/verify", proof)
        assert status == 409 and conflict["error"]["nextAction"] == "RequestSetup"
        assert call("/auth/register/setup", {**proof, "code": None})[0] == 400
        assert call("/auth/register/setup", {**proof, "extra": "rejected"})[0] == 400
        status, challenge = call("/auth/register/setup", proof)
        assert status == 202
        assert call("/auth/register/setup", proof)[1] == challenge
        setup_id = challenge["setupChallengeId"]
        assert call("/dev/setup-inbox/" + setup_id)[0] == 404
        setup_code = call("/dev/setup-inbox/" + setup_id, headers=inbox)[1]["code"]
        complete = dict(setupChallengeId=setup_id, code=setup_code, bindingSecret=binding, newPassword="safe completion test password")
        key = {"Idempotency-Key": secrets.token_urlsafe(32)}
        assert call("/auth/register/complete", {**complete, "code": None}, key)[0] == 400
        assert call("/auth/register/complete", {**complete, "extra": "rejected"}, key)[0] == 400
        assert call("/auth/register/complete", complete)[0] == 400
        status, result = call("/auth/register/complete", complete, key)
        assert status == 200 and result["status"] == "Ready" and result["credentialOutcome"] == "CandidateSelected"
        assert result["registrationId"] == registration["registrationId"]
        assert call("/auth/register/complete", complete, key)[1] == result
        assert call("/auth/register/complete", {**complete, "newPassword": "different safe test password"}, key)[0] == 409
        assert not any(k in result for k in ("accessToken", "refreshToken", "session", "credentialId"))
        print("HTTP SETUP PASS: proof-gated conflict, separate protected inbox, null/unknown/header rejection, completion/replay and no tokens")
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
