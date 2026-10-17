"""Validate composed B2 schemas and rejected boundary controls without weakening B1."""
from bff_browser_contract import load_contract
from check_bff_browser_contract import validate_request, validate_response, validate_schema, resolve
from jsonschema import Draft202012Validator
from jsonschema.exceptions import ValidationError
from openapi_spec_validator import validate_spec
import copy

doc=load_contract(registration=True)
validate_spec(doc)
paths={'/bff/register','/bff/register/verify','/bff/register/resend','/bff/register/setup','/bff/register/complete'}
assert set(doc['paths'])-set(load_contract()['paths'])==paths
for schema in doc['components']['schemas'].values():Draft202012Validator.check_schema(resolve(doc,schema))
flow='A'*43
headers={'Content-Type':'application/json','Origin':'https://localhost:7443','X-CSRF-Token':'A'*32,
         'Cookie':'__Host-smartcore-antiforgery='+ 'A'*64+'; __Host-smartcore-registration='+ 'B'*64}
start={'flowId':flow,'email':'new@example.test','password':'private test password','displayName':'Test'}
for path in paths:
    body=start if path=='/bff/register' else {'flowId':flow} if path.endswith('resend') else {'flowId':flow,'code':'123456'}
    if path.endswith('complete'):body['newPassword']='new private test password'
    validate_request(doc,path,'POST',body,headers)
    for extra in ['personId','bindingSecret','verificationSessionId','setupChallengeId','accessToken']:
        try:validate_request(doc,path,'POST',{**body,extra:'forbidden'},headers)
        except ValidationError:pass
        else:raise AssertionError('Internal property accepted: '+extra)
    for malformed in ['A'*42,'A'*44,'!'*43]:
        try:validate_request(doc,path,'POST',{**body,'flowId':malformed},headers)
        except ValidationError:pass
        else:raise AssertionError('Malformed flow accepted')
retry_headers={'Content-Type':'application/json','Cache-Control':'no-store','Retry-After':'3600'}
validate_response(doc,'/bff/register','POST',429,{'code':'RATE_LIMITED','retryAfterSeconds':3600},retry_headers)
for seconds in [0,3601]:
    try:validate_response(doc,'/bff/register','POST',429,{'code':'RATE_LIMITED','retryAfterSeconds':seconds},{**retry_headers,'Retry-After':str(seconds)})
    except (ValidationError,AssertionError):pass
    else:raise AssertionError('Invalid cooldown accepted')
try:validate_response(load_contract(),'/bff/login','POST',429,{'code':'RATE_LIMITED','retryAfterSeconds':3600},retry_headers)
except (ValidationError,AssertionError):pass
else:raise AssertionError('B2 weakened B1 retry bound')
try:validate_schema(doc,doc['components']['schemas']['RegistrationCompleted'],{'status':'Ready','credentialOutcome':'Undetermined'})
except ValidationError:pass
else:raise AssertionError('Ready without known credential outcome')
print('B2 contract PASS: exact five additive paths, strict browser DTOs/internal-field rejection, 3600-second retry boundary; B1 one-minute bound unchanged.')
