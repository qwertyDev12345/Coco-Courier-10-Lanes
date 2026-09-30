"""Guard Objective-C commas in XCTest macros; not an Objective-C compiler.

C preprocessing groups macro arguments with parentheses, not [] or {}. An
unparenthesized message/collection/block comma can silently split an assertion.
"""
from pathlib import Path
import re
import unittest


TESTS = Path(__file__).resolve().parent
IGNORED = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|"(?:\\[\s\S]|[^"\\])*"|\'(?:\\[\s\S]|[^\'\\])*\'')
ASSERTION = re.compile(r"\bXCTAssert\w*\s*\(")


def unsafe_assertion_commas(source):
    # Keep offsets/newlines so reports point at the source, not a rewritten copy.
    code = IGNORED.sub(lambda m: re.sub(r"[^\n]", " ", m.group()), source)
    failures = []
    for match in ASSERTION.finditer(code):
        parentheses, brackets, braces = 1, 0, 0
        for offset in range(match.end(), len(code)):
            char = code[offset]
            if char == "(":
                parentheses += 1
            elif char == ")":
                parentheses -= 1
                if parentheses == 0:
                    break
            elif char == "[":
                brackets += 1
            elif char == "]":
                brackets -= 1
            elif char == "{":
                braces += 1
            elif char == "}":
                braces -= 1
            elif char == "," and parentheses == 1 and (brackets > 0 or braces > 0):
                failures.append(source.count("\n", 0, offset) + 1)
    return failures


class XCTestArgumentTests(unittest.TestCase):
    def test_rejects_reported_format_expression(self):
        source = '''XCTAssertTrue([report containsString:[NSString stringWithFormat:@"op=%ld token sync HTTP status", (long)second]]);'''
        self.assertEqual(unsafe_assertion_commas(source), [1])

    def test_rejects_unprotected_collection_and_block_commas(self):
        for source in (
            'XCTAssertEqualObjects(value, @[@"a", @"b"]);',
            'XCTAssertEqualObjects(value, @{@"a": @1, @"b": @2});',
            'XCTAssertTrue([obj run:^{ int a = 1, b = 2; }]);',
        ):
            with self.subTest(source=source):
                self.assertTrue(unsafe_assertion_commas(source))

    def test_accepts_grouped_expressions_and_ordinary_macro_arguments(self):
        for source in (
            'XCTAssertTrue([report containsString:expected]);',
            'XCTAssertTrue(([report containsString:[NSString stringWithFormat:@"%ld", value]]));',
            'XCTAssertEqualObjects(value, (@[@"a", @"b"]));',
            'XCTAssertEqualObjects(value, (@{@"a": @1, @"b": @2}));',
            'XCTAssertEqual(function(a, b), expected, @"a,b: %d", count);',
            'XCTAssertTrue([text containsString:@"escaped \\\" quote, comma"]);',
            'XCTAssertTrue(/* ignored [, */ condition); // XCTAssertTrue([a,b])',
            'NSString *literal = @"XCTAssertTrue([a,b])";',
            "XCTAssertEqual(character, ',');",
        ):
            with self.subTest(source=source):
                self.assertEqual(unsafe_assertion_commas(source), [])

    def test_all_native_test_assertions_protect_objc_commas(self):
        sources = sorted(TESTS.rglob("*.m")) + sorted(TESTS.rglob("*.mm"))
        self.assertTrue(sources)
        for path in sources:
            with self.subTest(path=path.name):
                self.assertEqual(unsafe_assertion_commas(path.read_text(encoding="utf-8")), [],
                                 f"{path.name}: wrap the expression in parentheses or use a local variable")


if __name__ == "__main__":
    unittest.main()
