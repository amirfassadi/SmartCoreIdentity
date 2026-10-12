"""Compose the additive B2 extension with the unchanged reviewed five-path B1 contract."""
from pathlib import Path
import copy
import yaml
ROOT = Path(__file__).resolve().parents[1]
def load_contract(registration=False, recovery=False):
    doc = yaml.safe_load((ROOT/'contracts/bff-browser.openapi.yaml').read_text())
    if not registration and not recovery:
        return doc
    doc = copy.deepcopy(doc)
    sources = ['bff-browser-registration.openapi.yaml']
    if recovery:
        sources.append('bff-browser-recovery.openapi.yaml')
    for source in sources:
        extension = yaml.safe_load((ROOT/'contracts'/source).read_text())
        doc['info'] = extension['info']
        for path, operation in extension['paths'].items():
            assert path not in doc['paths'], 'Extensions must not replace an existing route'
            doc['paths'][path] = operation
        for section, items in extension['components'].items():
            for name, value in items.items():
                assert name not in doc['components'].setdefault(section, {}), 'Extensions must not replace an existing component'
                doc['components'][section][name] = value
    return doc
