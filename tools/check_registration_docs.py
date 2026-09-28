#!/usr/bin/env python3
"""Limited documentary checks; not a runtime test or full 065 validator."""
from pathlib import Path
import hashlib,json,re
root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'docs/registration/source-manifest.json').read_text())
assert len(manifest['files'])==20
for item in manifest['files']:
 data=(root/item['path']).read_bytes()
 assert len(data)==item['bytes'] and hashlib.sha256(data).hexdigest()==item['sha256'],item['path']
current=[root/'README.md',root/'docs/IDENTITY_VISION.md',root/'docs/KIMIA_IDENTITY_MVP.md',*(root/'docs/registration').glob('*.md')]
current.append(root/'docs/reference/uploaded-blueprint/capability.validation_notes.md')
links=0
for p in current:
 s=p.read_text()
 assert s.count('```')%2==0,p
 for target in re.findall(r'\[[^\]]+\]\(([^)]+)\)',s):
  if '://' not in target and not target.startswith('#'):
   assert (p.parent/target.split('#')[0]).exists(),(p,target)
   links+=1
assert manifest['archiveIsNormative'] is False
assert 'BLOCKED' in manifest['status']
print(f'20 archived files match byte hashes; {len(current)} current Markdown documents have closed fences; {links} relative links resolve.')
print('Not checked: remote links/anchors, full Blueprint/schema/065 validation, runtime/security behavior or visual rendering.')
