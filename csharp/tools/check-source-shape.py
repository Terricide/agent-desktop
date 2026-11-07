"""Source-shape rules for the C# port of agent-desktop.

Fails when a hand-written file passes the 400-line cap, when a non-doc comment
appears, when a type carries more than 7 fields, or when a method takes more
than 5 parameters. Each rule is planted in the self-test before the tree is
scanned, so a rule that has stopped detecting anything fails the gate rather
than passing it in silence.

Usage: check-source-shape.py <root> [--skip-self-test]
"""
import os
import re
import sys
import tempfile

LINE_CAP = 400
FIELD_CAP = 7
PARAM_CAP = 5

DOC_COMMENT = "///"
TYPE_DECL = re.compile(r"^\s*(?:public|internal|private)?\s*(?:sealed|abstract|static|partial)*\s*(record|class|struct|interface)\s")
FIELD = re.compile(r"^\s*(?:public|internal|private|protected)\s+[A-Za-z_][\w<>\?\[\]\.]*\s+[A-Za-z_]\w*\s*(\{|;)")
METHOD = re.compile(r"^\s*(?:(?:public|internal|private|protected|static|virtual|override|async|partial)\s+)*[A-Za-z_][\w<>\?\[\]\.]*\s+[A-Za-z_]\w*\s*\(([^)]*)\)\s*(=>|\{|;|\n)")


def strip_doc_comments(text):
    """Report comment markers that are real code comments, not text inside a literal."""
    violations = []
    index = 0
    line = 1
    length = len(text)
    while index < length:
        char = text[index]
        if char == "\n":
            line += 1
            index += 1
        elif text.startswith('"""', index):
            end = text.find('"""', index + 3)
            stop = length if end < 0 else end + 3
            line += text.count("\n", index, stop)
            index = stop
        elif char in "\"'":
            verbatim = index > 0 and text[index - 1] == "@"
            index += 1
            while index < length:
                if text[index] == "\\" and not verbatim:
                    index += 2
                elif text[index] == char:
                    if verbatim and index + 1 < length and text[index + 1] == char:
                        index += 2
                    else:
                        index += 1
                        break
                elif text[index] == "\n":
                    line += 1
                    index += 1
                else:
                    index += 1
        elif text.startswith("//", index):
            if text.startswith("///", index) or text.startswith("//!", index):
                end = text.find("\n", index)
                index = length if end < 0 else end
            else:
                violations.append((line, "inline comment"))
                end = text.find("\n", index)
                index = length if end < 0 else end
        elif text.startswith("/*", index):
            violations.append((line, "block comment"))
            end = text.find("*/", index + 2)
            stop = length if end < 0 else end + 2
            line += text.count("\n", index, stop)
            index = stop
        else:
            index += 1
    return violations


def count_lines(text):
    if not text.strip():
        return 0
    return len(text.splitlines())


def is_generated(text):
    head = "\n".join(text.splitlines()[:5])
    return "@generated" in head


def field_violations(text):
    """Count fields declared inside each type body, one type at a time."""
    found = []
    body_depth = 0
    fields = 0
    current = None
    for index, line in enumerate(text.splitlines(), start=1):
        if TYPE_DECL.match(line):
            current = line.strip()
            body_depth = 0
            fields = 0
            continue
        if current is None:
            continue
        body_depth += line.count("{") - line.count("}")
        match = FIELD.match(line)
        if match and body_depth >= 1:
            fields += 1
        if body_depth <= 0 and fields > FIELD_CAP:
            found.append((index, f"{current} carries {fields} fields"))
            current = None
            fields = 0
    return found


def param_violations(text):
    found = []
    for index, line in enumerate(text.splitlines(), start=1):
        match = METHOD.match(line)
        if not match:
            continue
        signature = match.group(1)
        if not signature.strip():
            continue
        count = len([p for p in signature.split(",") if p.strip()])
        if count > PARAM_CAP:
            found.append((index, f"{line.strip()} takes {count} parameters"))
    return found


def scan(path):
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    found = []
    if not is_generated(text):
        lines = count_lines(text)
        if lines > LINE_CAP:
            found.append((0, f"{lines} lines exceeds the {LINE_CAP} cap"))
        for index, reason in strip_doc_comments(text):
            found.append((index, reason))
        for index, reason in field_violations(text):
            found.append((index, reason))
        for index, reason in param_violations(text):
            found.append((index, reason))
    return found


def self_test():
    """Plant one violation of every rule and confirm each one fires."""
    fixtures = {
        "cap.cs": "using System;\n" + "class C { int X; }\n" * (LINE_CAP + 1),
        "comment.cs": "using System;\n// a comment\nclass C { }\n",
        "fields.cs": "using System;\npublic sealed record Big\n{\n" + "".join(
            f"    public int F{i} {{ get; init; }}\n" for i in range(FIELD_CAP + 1)
        ) + "}\n",
        "params.cs": "using System;\npublic static class M\n{\n    public static int Take(int a, int b, int c, int d, int e, int f) => 0;\n}\n",
        "literal.cs": "class C {\n    void M() { Console.WriteLine(\"// not a comment\"); }\n}\n",
        "blockliteral.cs": "class C {\n    string S = @\"/* not a comment */\";\n}\n",
        "doc.cs": "//! module doc\n/// public doc\nclass C { }\n",
    }
    expected = {"cap.cs": 1, "comment.cs": 1, "fields.cs": 1, "params.cs": 1,
                "literal.cs": 0, "blockliteral.cs": 0, "doc.cs": 0}
    with tempfile.TemporaryDirectory() as directory:
        for name, body in fixtures.items():
            with open(os.path.join(directory, name), "w", encoding="utf-8") as handle:
                handle.write(body)
        for name, count in expected.items():
            found = scan(os.path.join(directory, name))
            if len(found) < count:
                print(f"self-test failed: the {name} fixture planted a violation the gate did not detect")
                return False
    return True


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    skip = "--skip-self-test" in sys.argv
    if not skip:
        if not self_test():
            return 1
    failures = []
    for directory, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
        for name in names:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(directory, name)
            for line, reason in scan(path):
                failures.append(f"{path}:{line}: {reason}")
    for failure in failures:
        print(failure)
    if failures:
        print(f"{len(failures)} source-shape violations")
        return 1
    print("source shape is clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
