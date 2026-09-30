"""Portable build-input checks, NOT a substitute for compiling/running XCTest."""
import ast
from pathlib import Path
import re
import unittest

PATCH = Path(__file__).resolve().parents[1]


class NativeBuildWiringTests(unittest.TestCase):
    def test_every_native_source_is_in_both_export_lists(self):
        expected = {path.name for path in (PATCH / "Sources/Classes").iterdir()
                    if path.suffix in {".h", ".m", ".mm"}}
        tree = ast.parse((PATCH / "Scripts/patch_pbxproj.py").read_text(encoding="utf-8"))
        names = next(ast.literal_eval(node.value) for node in tree.body
                     if isinstance(node, ast.Assign)
                     and any(isinstance(target, ast.Name) and target.id == "DEFAULT_PATCH_FILES"
                             for target in node.targets))
        shell = (PATCH / "patch.sh").read_text(encoding="utf-8")
        block = re.search(r"declare -a PATCH_FILES=\((.*?)\)", shell, re.S).group(1)
        copied = set(re.findall(r'"([^"\n]+)"', block))
        self.assertEqual(expected, set(names))
        self.assertEqual(expected, copied)

    def test_simulator_compiles_diagnostics_and_real_permission_tests(self):
        ruby = (PATCH / "Tests/create_project.rb").read_text(encoding="utf-8")
        production = re.search(r"production = %w\[(.*?)\]", ruby, re.S).group(1).split()
        self.assertIn("PLLaunchDiagnostics.m", production)
        self.assertIn("PreloadViewController.mm", production)
        self.assertIn("PLPushRegistration.m", production)
        self.assertIn("PermissionFlowTests.m", ruby)
        self.assertIn("PushRegistrationTests.m", ruby)
        for source in production:
            self.assertTrue((PATCH / "Sources/Classes" / source).is_file(), source)


if __name__ == "__main__":
    unittest.main()
