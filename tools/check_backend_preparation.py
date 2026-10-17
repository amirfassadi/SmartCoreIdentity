#!/usr/bin/env python3
"""Candidate contract checks only; not runtime or full 065 verification."""
import copy
import json
from pathlib import Path
from jsonschema import Draft202012Validator, FormatChecker

root = Path(__file__).resolve().parents[1]
schema = json.loads((root / 'contracts/internal/refresh-events.schema.json').read_text())
Draft202012Validator.check_schema(schema)
validator = Draft202012Validator(schema, format_checker=FormatChecker())
example = {
    'eventId': '11111111-1111-4111-8111-111111111111',
    'schemaVersion': '0.1.0', 'type': 'RefreshTokenRotated',
    'payload': {
        'sessionId': '22222222-2222-4222-8222-222222222222',
        'familyId': '33333333-3333-4333-8333-333333333333',
        'generation': 1, 'occurredAt': '2026-10-10T09:00:00Z',
        'reason': 'RefreshAccepted'
    }
}
validator.validate(example)
reuse = copy.deepcopy(example)
reuse['type'] = 'RefreshTokenReuseDetected'
reuse['payload'].update(generation=0, reason='PreviouslyConsumedGeneration')
validator.validate(reuse)
negative = []
for key in ['token', 'verifier', 'hash', 'ip', 'metadata']:
    item = copy.deepcopy(example)
    item['payload'][key] = 'forbidden'
    negative.append(item)
for field, value in [('generation', -1), ('generation', True), ('generation', 0),
                     ('reason', 'PreviouslyConsumedGeneration'),
                     ('sessionId', 'invalid'), ('occurredAt', 'yesterday')]:
    item = copy.deepcopy(example)
    item['payload'][field] = value
    negative.append(item)
for key in example['payload']:
    item = copy.deepcopy(example)
    del item['payload'][key]
    negative.append(item)
item = copy.deepcopy(example)
item['unexpected'] = 'forbidden'
negative.append(item)
for item in negative:
    assert list(validator.iter_errors(item)), item
print(f'Candidate schema: 2 valid examples and {len(negative)} rejected negative controls.')
print('Not checked: backend compilation, database transactions, runtime security, delivery or full 065.')
