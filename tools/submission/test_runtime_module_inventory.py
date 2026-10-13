# Copyright (c) 2026 Neil Colvin. MIT licensed.
import ast
import json
from pathlib import Path
import re
import unittest

class RuntimeModuleInventoryTests(unittest.TestCase):
    def test_packaged_commands_include_every_local_import_dependency(self):
        root=Path(__file__).resolve().parent
        builder=(root.parent/'BuildSubmissionRuntime.ps1').read_text()
        block=builder.split('$modules = @(',1)[1].split(')',1)[0]
        included=set(re.findall(r"'([^']+)'",block))
        local={p.stem for p in root.glob('*.py')}
        pending=list(json.loads((root/'commands.json').read_text()).values())+['bundled_entry']
        seen=set()
        while pending:
            module=pending.pop()
            if module in seen:continue
            seen.add(module)
            self.assertIn(module,included,'Runtime omits '+module)
            for node in ast.walk(ast.parse((root/(module+'.py')).read_text(encoding='utf-8'))):
                names=([node.module.split('.')[0]] if isinstance(node,ast.ImportFrom) and node.module else
                       [a.name.split('.')[0] for a in node.names] if isinstance(node,ast.Import) else [])
                pending.extend(n for n in names if n in local)
