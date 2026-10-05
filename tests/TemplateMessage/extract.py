from pathlib import Path
import re
import sys

source = (Path(__file__).resolve().parents[2] / 'Modules/TemplateManager.cs').read_text(encoding='utf-8-sig')
method = re.search(r'(?ms)^    internal static \(string Title, string Body\) ParseTemplate\(string text\)\n    \{.*?^    \}', source)
if not method:
    raise SystemExit('Production template parser signature changed; update the harness')
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text('using System;\nnamespace TOHE;\ninternal static class TemplateManager\n{\n' + method[0] + '\n}\n', encoding='utf-8')
