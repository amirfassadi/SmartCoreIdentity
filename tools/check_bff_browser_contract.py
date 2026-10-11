#!/usr/bin/env python3
"""B1 candidate schemas/examples; reusable wire response check, not a live-host test."""
import json
from pathlib import Path
import re
from http.cookies import SimpleCookie
import yaml
from jsonschema import Draft202012Validator, FormatChecker
from jsonschema.exceptions import ValidationError
from openapi_spec_validator import validate_spec

ROOT = Path(__file__).resolve().parents[1]


def resolve(doc, value):
    if isinstance(value, dict):
        if '$ref' in value:
            assert set(value) == {'$ref'}, 'Reference siblings require explicit support'
            assert value['$ref'].startswith('#/'), 'External references unsupported'
            target = doc
            for part in value['$ref'][2:].split('/'):
                target = target[part.replace('~1', '/').replace('~0', '~')]
            return resolve(doc, target)
        return {k: resolve(doc, v) for k, v in value.items()}
    if isinstance(value, list):
        return [resolve(doc, v) for v in value]
    return value


def validate_schema(doc, schema, value):
    Draft202012Validator(resolve(doc, schema), format_checker=FormatChecker()).validate(value)


def normalized_headers(headers):
    # Preserve repeated Set-Cookie fields; do not coalesce them.
    return {name.lower(): values if isinstance(values, list) else [values]
            for name, values in headers.items()}


def validate_request(doc, path, method, body, headers, approved_origin="https://localhost:7443"):
    """Structural envelope only; cannot validate CSRF cryptography or cookie identity."""
    op = doc['paths'][path][method]
    if method == 'get':
        assert body is None
        return
    validate_schema(doc, op['requestBody']['content']['application/json']['schema'], body)
    wire = normalized_headers(headers)
    assert wire.get('content-type') == ['application/json']
    for declared in op['parameters']:
        declared = resolve(doc, declared)
        values = wire.get(declared['name'].lower(), [])
        assert len(values) <= 1
        assert values or not declared['required']
        for value in values:
            validate_schema(doc, declared['schema'], value)
    assert wire.get('origin') == [approved_origin]
    cookie = wire.get('cookie', [])
    assert len(cookie) == 1
    parsed = SimpleCookie(); parsed.load(cookie[0])
    for scheme in op['security'][0]:
        security = doc['components']['securitySchemes'][scheme]
        name = security['name']
        if security['in'] == 'header':
            values = wire.get(name.lower(), [])
            assert len(values) == 1 and 32 <= len(values[0]) <= 4096
        else:
            assert len(re.findall(r'(?:^|;)\s*'+re.escape(name)+r'=', cookie[0])) == 1
            assert name in parsed and parsed[name].value
            if scheme == 'bffSession':
                assert re.fullmatch(r'[A-Za-z0-9_-]{43}', parsed[name].value)


def validate_response(doc, path, method, status, body, headers):
    operation = doc['paths'][path][method]
    spec = resolve(doc, operation['responses'][str(status)])
    if 'content' in spec:
        validate_schema(doc, spec['content']['application/json']['schema'], body)
    else:
        assert body is None, 'Body forbidden for no-content response'
    wire = normalized_headers(headers)
    if 'content' in spec:
        assert len(wire.get('content-type', [])) == 1
        assert wire['content-type'][0].split(';')[0].strip().lower() == 'application/json'
    for name, declared in spec.get('headers', {}).items():
        declared = resolve(doc, declared)
        values = wire.get(name.lower(), [])
        assert values or not declared.get('x-required'), 'Missing required response header'
        if name.lower() != 'set-cookie':
            assert len(values) <= 1, 'Unexpected duplicate response header'
        for value in values:
            validate_schema(doc, declared['schema'], value)
    if str(status) in ('429', '503'):
        assert int(wire['retry-after'][0]) == body['retryAfterSeconds'], 'Retry mismatch'
    cookies = {}
    for field in wire.get('set-cookie', []):
        parsed = SimpleCookie()
        parsed.load(field)
        assert len(parsed) == 1, 'Use one cookie per header field'
        name, item = next(iter(parsed.items()))
        assert name not in cookies, 'Duplicate cookie actions'
        assert name in ('__Host-smartcore-session', '__Host-smartcore-antiforgery')
        assert item['secure'] and item['httponly'] and item['path'] == '/'
        assert item['samesite'].lower() == 'lax' and not item['domain']
        cookies[name] = item
    actions = spec.get('x-cookie-actions', {})
    assert set(cookies) <= set(actions), 'Undeclared cookie mutation'
    for name, action in actions.items():
        if action == 'set-if-absent' and name not in cookies:
            continue
        assert name in cookies, 'Required cookie action missing'
        if action == 'clear':
            assert cookies[name].value == '' and cookies[name]['max-age'] == '0'
        else:
            assert cookies[name].value and cookies[name]['max-age'] != '0'
            if name == '__Host-smartcore-session':
                assert re.fullmatch(r'[A-Za-z0-9_-]{43}', cookies[name].value)
                assert 0 < int(cookies[name]['max-age']) <= 86400


def main():
    doc = yaml.safe_load((ROOT/'contracts/bff-browser.openapi.yaml').read_text())
    assert doc['openapi'] == '3.1.0' and doc['x-runtime-implemented'] is False
    validate_spec(doc)
    expected = {'/bff/csrf': 'get', '/bff/login': 'post', '/bff/me': 'post',
                '/bff/me/action': 'post', '/bff/logout': 'post'}
    assert set(doc['paths']) == set(expected)
    for path, method in expected.items():
        assert set(doc['paths'][path]) == {method}
    for schema in doc['components']['schemas'].values():
        Draft202012Validator.check_schema(resolve(doc, schema))
    positives = 0
    for path, method in expected.items():
        op = doc['paths'][path][method]
        if method == 'post':
            required = {p['name'] for p in map(lambda p: resolve(doc, p), op['parameters']) if p['required']}
            assert 'Origin' in required
            assert 'csrfHeader' in op['security'][0] and 'antiforgeryCookie' in op['security'][0]
            request = op['requestBody']['content']['application/json']
            for example in request['examples'].values():
                validate_schema(doc, request['schema'], example['value']); positives += 1
        for raw_response in op['responses'].values():
            response = resolve(doc, raw_response)
            assert resolve(doc, response['headers']['Cache-Control'])['x-required']
            for media in response.get('content', {}).values():
                for example in media['examples'].values():
                    validate_schema(doc, media['schema'], example['value']); positives += 1
    fixtures = json.loads((ROOT/'contracts/examples/bff-browser-b1.json').read_text())
    negative = 0
    for case in fixtures:
        try:
            if case['kind'] == 'request':
                validate_request(doc, case['path'], case['method'], case['body'], case['headers'])
            else:
                validate_response(doc, case['path'], case['method'], case['status'], case['body'], case['headers'])
        except (AssertionError, ValidationError) as failure:
            if case['valid']:
                raise AssertionError(case['name']) from failure
            negative += 1
        else:
            assert case['valid'], 'Unexpected valid negative control: '+case['name']
            positives += 1
    print(f'B1 draft: OpenAPI 3.1 validated; five paths; {positives} valid schema/wire examples; {negative} rejected negative controls.')
    print('Not checked: client generation, live host/Origin/CSRF cryptography, proxy or browser behavior.')


if __name__ == '__main__':
    main()
