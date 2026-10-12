"""Deterministic browser types derived exclusively from the reviewed browser OpenAPI."""
from pathlib import Path
import hashlib
import sys
import yaml
root=Path(__file__).resolve().parents[1]
source=root/'contracts/bff-browser.openapi.yaml'
contract=yaml.safe_load(source.read_text())
def kind(schema):
    if '$ref' in schema:return schema['$ref'].rsplit('/',1)[-1]
    if 'const' in schema:return repr(schema['const']).replace("'",'"')
    if 'enum' in schema:return ' | '.join(repr(value).replace("'",'"') for value in schema['enum'])
    value=schema.get('type')
    if isinstance(value,list):return ' | '.join(kind({**schema,'type':item}) for item in value)
    if value=='object':
        fields=schema.get('properties',{});required=schema.get('required',[])
        return '{'+''.join(f' {name}{"" if name in required else "?"}: {kind(field)};' for name,field in fields.items())+' }'
    return {'string':'string','integer':'number','number':'number','boolean':'boolean','null':'null'}.get(value,'unknown')
lines=['// Generated from contracts/bff-browser.openapi.yaml; do not edit.', '// SHA256 '+hashlib.sha256(source.read_bytes()).hexdigest()]
for name,schema in contract['components']['schemas'].items():lines.append(f'export type {name} = {kind(schema)};')
lines.append('export type BrowserPostPath = '+ ' | '.join(repr(path) for path,definition in contract['paths'].items() if 'post' in definition)+';')
text='\n'.join(lines)+'\n';target=root/'web/identity/src/lib/bff/generated.ts'
if '--check' in sys.argv:
    assert target.read_text()==text,'Browser contract generation drift'
else:
    target.parent.mkdir(parents=True,exist_ok=True);target.write_text(text)
print('Browser client contract generation PASS')
