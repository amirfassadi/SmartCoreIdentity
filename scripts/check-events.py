"""Validate real integration-test event output against the pinned Platform contract."""
import json
import sys
from pathlib import Path
from jsonschema import Draft202012Validator, FormatChecker

root = Path(__file__).resolve().parents[1]
schema = json.loads((root / "contracts/events.input.schema.json").read_text())
events = json.loads(Path(sys.argv[1]).read_text())
assert events, "Expected integration-test events"
validator = Draft202012Validator(schema, format_checker=FormatChecker())
for event in events:
    errors = list(validator.iter_errors(event))
    assert not errors, f"Event contract failed: {event['EventType']}"
print(f"PASS: {len(events)} emitted events match pinned Platform schema")
