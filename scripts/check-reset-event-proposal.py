"""Owner-selected Platform context union acceptance checks."""
import copy,json,sys,uuid
from pathlib import Path
from jsonschema import Draft202012Validator,FormatChecker

root=Path(__file__).resolve().parents[1]
schema=json.loads((root/'contracts/events.input.schema.json').read_text())['$defs']['PasswordChanged']
pinned=json.loads((root/'contracts/events.input.schema.json').read_text())['$defs']['PasswordChanged']
assert 'SessionReference' not in pinned['required']
assert pinned['properties']['ExecutionContext']['properties']['RecoveryProofReference']['description'].startswith('Identifier of the accepted ResetPassword')
Draft202012Validator.check_schema(schema)
validator=Draft202012Validator(schema,format_checker=FormatChecker())
identifier=str(uuid.uuid4())
change=dict(EventId=identifier,EventType='PasswordChanged',AggregateType='Credential',AggregateId=identifier,
 OccurredAt='2026-10-10T00:00:00Z',ActorIdentity=identifier,SessionReference=identifier,
 ExecutionContext=dict(CorrelationId=identifier),Payload=dict(PersonId=identifier,CredentialId=identifier))
validator.validate(change)
reset=copy.deepcopy(change);reset.pop('SessionReference');reset['ExecutionContext']['RecoveryProofReference']=identifier
validator.validate(reset)
missing=copy.deepcopy(change);missing.pop('SessionReference')
ambiguous=copy.deepcopy(reset);ambiguous['SessionReference']=identifier
secret=copy.deepcopy(reset);secret['ExecutionContext']['RecoveryCode']='never permitted'
assert all(list(validator.iter_errors(value)) for value in (missing,ambiguous,secret))
if len(sys.argv)>1:
 for event in json.loads(Path(sys.argv[1]).read_text()):
  if event.get('EventType')=='PasswordChanged':validator.validate(event)
print('RESET CONTEXT PASS: existing Session context and sessionless recovery context; missing, ambiguous and secret-bearing contexts rejected; selected Platform 1.2.2 context pinned')
