"""Small, redacted local-preflight helpers. Never print source values or HTTP tokens."""
import argparse
import json
from pathlib import Path
import re
import shutil
import ssl
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from http.cookies import SimpleCookie


def files(root):
    result = subprocess.run(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=root, check=True, capture_output=True)
    return sorted(set(item.decode() for item in result.stdout.split(b'\0') if item))


def copy_worktree(root, target):
    root = root.resolve(); target = target.resolve()
    if target.is_relative_to(root):
        raise ValueError('Temporary copy must be outside the source repository')
    for name in files(root):
        source = root / name
        if source.is_symlink() or not source.resolve().is_relative_to(root):
            raise ValueError('Symlink/external source rejected')
        if not source.exists():
            continue  # A tracked file deleted in the working tree is not resurrected.
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    print('Current tracked and non-ignored untracked files copied outside the repository')


def tracked_env(root):
    names = subprocess.check_output(['git', 'ls-files', '-z'], cwd=root).decode().split('\0')
    bad = [name for name in names if re.search(r'(^|/)\.env(?:\.|$)', name) and not name.endswith(('.example', '.sample'))]
    if bad:
        print('Tracked environment files: ' + ', '.join(bad))
        raise ValueError('Tracked environment files rejected')
    print('No tracked non-template .env files')


def heuristic(root):
    # Only a heuristic; caller still marks missing full/history scanner INCOMPLETE.
    pattern = re.compile(r'-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}')
    findings = []
    for name in files(root):
        path = root / name
        if path.is_symlink() or not path.is_file():
            continue
        for line, text in enumerate(path.read_text(errors='replace').splitlines(), 1):
            if pattern.search(text):
                findings.append((name, line))
    for name, line in findings[:20]:
        print(f'{name}:{line}: possible key [REDACTED]')
    if findings:
        raise ValueError('Possible key material found; no source values printed')
    print('No obvious key markers in tracked/non-ignored untracked files; not a complete secret scan')


def browser_boundary(root):
    import yaml
    contract = yaml.safe_load((root / 'contracts/bff-browser.openapi.yaml').read_text())
    def walk(value):
        if isinstance(value, dict):
            for name in value.get('properties', {}):
                if name.replace('_', '').lower() == 'personid':
                    raise ValueError('Internal person identifier declared as a browser property')
            for item in value.values():
                walk(item)
        elif isinstance(value, list):
            for item in value:
                walk(item)
    walk(contract)
    for extension in sorted((root / 'contracts').glob('bff-browser*.yaml')):
        if extension.name != 'bff-browser.openapi.yaml':
            walk(yaml.safe_load(extension.read_text()))
    # Property declarations/accesses, not explanatory prose or trusted Identity DTOs.
    for path in (root / 'web/identity/src').rglob('*'):
        if path.suffix not in ('.ts', '.tsx'):
            continue
        text = re.sub(r'/\*.*?\*/|//[^\n]*', '', path.read_text(), flags=re.S)
        if re.search(r'(?i)(?:\bperson_?id\s*\??\s*:|\.\s*person_?id\b|[\[\{,]\s*["\']person_?id["\']\s*[\]:])', text):
            raise ValueError('Internal person identifier in browser code')
    print('Browser schema/property scan passed; dynamic response isolation is checked by the real B1 suite')


def cookie_policy(fields):
    if not fields:
        raise ValueError('Fresh CSRF bootstrap did not issue its antiforgery cookie')
    for field in fields:
        parsed = SimpleCookie(); parsed.load(field)
        if len(parsed) != 1 or '__Host-smartcore-antiforgery' not in parsed:
            raise ValueError('Unexpected/malformed bootstrap cookie')
        cookie = parsed['__Host-smartcore-antiforgery']
        if not (cookie['secure'] and cookie['httponly'] and cookie['path'] == '/' and cookie['samesite'].lower() == 'lax' and not cookie['domain']):
            raise ValueError('Antiforgery cookie violates exact policy')
    if len(fields) != 1:
        raise ValueError('Duplicate bootstrap cookie')


def probe(origin, certificate):
    address = urllib.parse.urlsplit(origin)
    if address.scheme != 'https' or address.hostname not in ('localhost', '127.0.0.1', '::1') or address.username or address.password or address.path not in ('', '/') or address.query or address.fragment:
        raise ValueError('Only a fixed local HTTPS origin is accepted; no VPS/public probe')
    origin = origin.rstrip('/')
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args): return None
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(), urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=certificate or None)))
    def request(path, body=None, headers=None):
        call = urllib.request.Request(origin + path, data=body, headers=headers or {}, method='GET' if body is None else 'POST')
        try: response = client.open(call, timeout=15)
        except urllib.error.HTTPError as failure: response = failure
        data = response.read(1048577)
        if len(data) > 1048576: raise ValueError('Oversized local response')
        required = {'Cache-Control':'no-store', 'Referrer-Policy':'no-referrer', 'X-Content-Type-Options':'nosniff', 'X-Frame-Options':'DENY', 'Permissions-Policy':'camera=(), microphone=(), geolocation=()', 'Strict-Transport-Security':'max-age=31536000'}
        for name, value in required.items():
            if response.headers.get_all(name) != [value]: raise ValueError('Missing/conflicting security header')
        return response.status, data, response.headers
    for locale in ('fa', 'en'):
        status, data, headers = request('/' + locale)
        csp = headers.get('Content-Security-Policy', '')
        if status != 200 or not re.search(r"script-src[^;]*'nonce-[^']+'", csp) or "style-src-attr 'none'" not in csp or "'unsafe-inline'" in csp or "'unsafe-eval'" in csp:
            raise ValueError('Local page/CSP policy failed')
        if re.search(r'\d+\.\d+', headers.get('Server', '')): raise ValueError('Server version exposed')
    status, body, headers = request('/bff/csrf')
    if status != 200: raise ValueError('CSRF bootstrap is not available')
    token = json.loads(body).get('requestToken')
    if not isinstance(token, str) or not 32 <= len(token) <= 4096: raise ValueError('Invalid CSRF response')
    cookie_policy(headers.get_all('Set-Cookie', []))
    cases = [({'Content-Type':'application/json'}, 403, 'ORIGIN_REJECTED'),
             ({'Content-Type':'application/json','Origin':origin}, 403, 'CSRF_REJECTED'),
             ({'Content-Type':'text/plain','Origin':origin}, 415, 'UNSUPPORTED_MEDIA_TYPE')]
    for request_headers, expected, code in cases:
        status, body, headers = request('/bff/login', b'{}', request_headers)
        if status != expected or json.loads(body) != {'code':code}: raise ValueError('Exact guard status/code failed; router errors are not evidence')
    print('Local TLS, page headers/CSP, exact antiforgery cookie and three guard probes passed; no login/session-cookie/E2E claim')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['copy', 'env', 'heuristic', 'browser', 'probe'])
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--target', type=Path)
    parser.add_argument('--origin')
    parser.add_argument('--ca')
    args = parser.parse_args()
    try:
        if args.action == 'copy': copy_worktree(args.root, args.target)
        elif args.action == 'env': tracked_env(args.root)
        elif args.action == 'heuristic': heuristic(args.root)
        elif args.action == 'browser': browser_boundary(args.root)
        else: probe(args.origin, args.ca)
    except Exception:
        # No exception repr: source, URL credentials, HTTP cookie/token or certificate data may be sensitive.
        print('Preflight helper rejected this check; diagnostic values suppressed', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
