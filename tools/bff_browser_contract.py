"""Compose the additive B2 extension with the unchanged reviewed five-path B1 contract."""
from pathlib import Path
import copy
import yaml
ROOT = Path(__file__).resolve().parents[1]
def load_contract(registration=False):
    doc = yaml.safe_load((ROOT/'contracts/bff-browser.openapi.yaml').read_text())
    if not registration:
        return doc
    extension = yaml.safe_load((ROOT/'contracts/bff-browser-registration.openapi.yaml').read_text())
    doc = copy.deepcopy(doc)
    doc['info'] = extension['info']
    for path, operation in extension['paths'].items():
        assert path not in doc['paths'], 'B2 must not replace a B1 route'
        doc['paths'][path] = operation
    for section, items in extension['components'].items():
        for name, value in items.items():
            assert name not in doc['components'].setdefault(section, {}), 'B2 must not replace a B1 component'
            doc['components'][section][name] = value
    return doc
