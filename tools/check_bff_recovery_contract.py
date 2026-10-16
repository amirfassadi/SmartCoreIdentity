"""B3 contract-first boundary review; does not claim runtime/browser implementation."""
from bff_browser_contract import load_contract
from check_bff_browser_contract import validate_request, validate_schema, resolve
from jsonschema import Draft202012Validator
from jsonschema.exceptions import ValidationError
from openapi_spec_validator import validate_spec

doc=load_contract(recovery=True)
validate_spec(doc)
assert set(doc['paths'])-set(load_contract(registration=True)['paths'])=={
    '/bff/recovery/enroll','/bff/password/change','/bff/password/reset','/bff/password/reset/complete'}
for schema in doc['components']['schemas'].values():Draft202012Validator.check_schema(resolve(doc,schema))
flow='A'*43
headers={'Content-Type':'application/json','Origin':'https://localhost:7443','X-CSRF-Token':'A'*32,
    'Cookie':'__Host-smartcore-session='+flow+'; __Host-smartcore-antiforgery='+ 'B'*64+'; __Host-smartcore-reset='+ 'C'*64}
requests={
    '/bff/recovery/enroll':{'flowId':flow,'currentPassword':'private current password','acceptLossRisk':True},
    '/bff/password/change':{'flowId':flow,'currentPassword':'private current password','newPassword':'private replacement password'},
    '/bff/password/reset':{'flowId':flow,'email':'private@example.test'},
    '/bff/password/reset/complete':{'flowId':flow,'code':'12345678','recoveryCode':'ABCDEF0123456789'*2,'newPassword':'private replacement password'}}
for path,body in requests.items():
    validate_request(doc,path,'POST',body,headers)
    for name in ['personId','sessionId','operationId','challengeId','bindingSecret','verified']:
        try:validate_request(doc,path,'POST',{**body,name:'forbidden'},headers)
        except ValidationError:pass
        else:raise AssertionError('Internal authority accepted: '+name)
    for field in body:
        try:validate_request(doc,path,'POST',{k:v for k,v in body.items() if k!=field},headers)
        except ValidationError:pass
        else:raise AssertionError('Required proof/field absent: '+field)
schema=doc['components']['schemas']['RecoveryEnrollment']
validate_schema(doc,schema,{'status':'Issued','recoveryCode':'A'*32})
validate_schema(doc,schema,{'status':'AlreadyIssued'})
for result in [{'status':'Issued'},{'status':'AlreadyIssued','recoveryCode':'A'*32}]:
    try:validate_schema(doc,schema,result)
    except ValidationError:pass
    else:raise AssertionError('One-time secret contract violated')
print('B3 contract PASS: four additive paths; session/current-password consent and both reset factors required; no internal IDs/authority fields; one-time enrollment replay has no code. Runtime acceptance remains separate.')
