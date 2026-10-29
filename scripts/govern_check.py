#!/usr/bin/env python3
"""Check the governance rules that a machine can verify (.agents/rules/02-governance-checks.md).

    python3 -I scripts/govern_check.py [--root DIR] [--base GIT-REF]

Read-only: it writes nothing, uses no network, and calls `git` only with --base.
Exit codes: 0 clean, 1 findings, 2 usage or parse errors (GOV-00).

Every check mirrors a text rule in `.agents/` or `docs/*/README.md`. Those texts stay canonical; if this script and a
text rule disagree, the text wins and the script is the bug.
"""

import argparse
import fnmatch
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

MIN_PYTHON = (3, 13)

NOT_CHECKED = (
    "tier correctness, duplicate artifacts by meaning, status and ownership conflicts across branches, "
    "approved-by vs the MR/PR approver, content quality"
)

KEBAB = r"[a-z0-9]+(?:-[a-z0-9]+)*"
KEY_RE = re.compile(r"[a-z][a-z0-9-]*")
ID_RE = re.compile(rf"\b(?:ADR|US|CR|PRB|TM)-\d{{4}}\b|\bSDP-{KEBAB}\b")
BACKTICK_RE = re.compile(r"`([^`]+)`")
PLACEHOLDERS = ("…", "...", "{", "}", "*", "<", ">", "NNNN", "YYYY", "://")
TEMPLATE_RE = re.compile(r"[a-z][a-z-]*-template(?:-[a-z-]+)?\.md")  # e.g., adr-template-bare.md; no artifact name matches
PLACEHOLDER_RE = re.compile(r"\{[^}]*\}")


@dataclass(frozen=True)
class Kind:
    """One artifact folder under docs/."""

    folder: str
    name_re: re.Pattern[str]  # group 1 is the number, or the whole id when id_prefix is ""
    id_prefix: str
    approved: frozenset[str]  # binding status and every status that can only follow it


# Naming: each folder's README → Naming. Approved: .agents/protocols/multi-human-collaboration.md → Binding statuses.
KINDS = (
    Kind("adrs", re.compile(rf"(\d{{4}})-{KEBAB}\.md"), "ADR-", frozenset({"Accepted", "Deprecated", "Superseded"})),
    Kind("user-stories", re.compile(rf"US-(\d{{4}})-{KEBAB}\.md"), "US-", frozenset({"Ready", "In progress", "Done"})),
    Kind(
        "change-records",
        re.compile(rf"CR-(\d{{4}})-{KEBAB}\.md"),
        "CR-",
        frozenset({"Approved", "Implemented", "Rolled back", "Closed"}),
    ),
    Kind("problem-records", re.compile(rf"PRB-(\d{{4}})-{KEBAB}\.md"), "PRB-", frozenset({"Closed"})),
    Kind("threat-models", re.compile(rf"TM-(\d{{4}})-{KEBAB}\.md"), "TM-", frozenset({"Accepted", "Superseded"})),
    Kind("sdps", re.compile(rf"(SDP-{KEBAB})\.md"), "", frozenset({"Accepted", "Retired"})),
)

# Reconciliation check 5 (.agents/protocols/multi-human-collaboration.md).
STALE_DEPENDENT = frozenset({"Proposed", "Ready", "In progress"})
STALE_DEPENDENCY = frozenset({"Rejected", "Deprecated", "Superseded"})

# .agents/AGENTS.md → Immutable audit trail.
IMMUTABLE_FOLDERS = ("adrs", "change-records")
MUTABLE_KEYS = frozenset({"status", "date", "approved-by", "superseded-by"})
APPEND_ONLY_SECTION = "## Post-implementation"

REFERENCE_KEYS = ("depends-on", "supersedes", "superseded-by")
UNIQUE_WHEN_ACCEPTED = (("adrs", "decides"), ("threat-models", "scope"))


@dataclass(frozen=True, order=True)
class Finding:
    path: str
    line: int
    check: str
    message: str

    def __str__(self) -> str:
        return f"{self.path}:{self.line}: {self.check} {self.message}"


class ParseError(Exception):
    def __init__(self, line: int, message: str) -> None:
        super().__init__(message)
        self.line = line
        self.message = message


class UsageError(Exception):
    pass


# --- Front matter: a strict parser for the flat YAML subset the templates use --------------------------------------


@dataclass
class FrontMatter:
    values: dict[str, str | list[str]] = field(default_factory=dict)
    lines: dict[str, int] = field(default_factory=dict)
    comments: dict[str, str] = field(default_factory=dict)
    end_line: int = 0


def split_comment(line: str) -> tuple[str, str]:
    in_quote = False
    for i, char in enumerate(line):
        if char == '"':
            in_quote = not in_quote
        elif char == "#" and not in_quote and (i == 0 or line[i - 1].isspace()):
            return line[:i], line[i + 1 :].strip()
    return line, ""


def parse_scalar(raw: str, line: int) -> str:
    if raw.startswith('"'):
        if len(raw) < 2 or not raw.endswith('"') or '"' in raw[1:-1]:
            raise ParseError(line, f"malformed quoted value {raw!r}")
        return raw[1:-1]
    if not raw:
        return ""  # `key:` with nothing after it, as in the bare ADR templates
    if raw[0] in "[]{}&*!|>'%@`,":
        raise ParseError(line, f"unsupported YAML value {raw!r}")
    return raw


def parse_value(raw: str, line: int) -> str | list[str]:
    if not raw.startswith("["):
        return parse_scalar(raw, line)
    if not raw.endswith("]"):
        raise ParseError(line, f"unterminated list {raw!r}")
    inner = raw[1:-1].strip()
    if not inner:
        return []
    items, current, in_quote = [], [], False
    for char in inner:
        if char == '"':
            in_quote = not in_quote
        if char == "," and not in_quote:
            items.append("".join(current))
            current = []
        else:
            current.append(char)
    items.append("".join(current))
    if any(not item.strip() for item in items):
        raise ParseError(line, f"empty item in list {raw!r}")
    return [parse_scalar(item.strip(), line) for item in items]


def parse_document(text: str) -> tuple[FrontMatter | None, list[tuple[int, str]]]:
    """Return the front matter (None if the file has none) and the numbered body lines after it."""
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        return None, list(enumerate(lines, start=1))
    front = FrontMatter()
    for number, line in enumerate(lines[1:], start=2):
        if line.strip() == "---":
            front.end_line = number
            return front, list(enumerate(lines[number:], start=number + 1))
        content, comment = split_comment(line)
        if not content.strip():
            continue
        if line[0].isspace():
            raise ParseError(number, "indented or nested front matter is not supported")
        key, colon, raw = content.partition(":")
        key = key.strip()
        if not colon or not KEY_RE.fullmatch(key):
            raise ParseError(number, f"expected 'key: value', got {line.strip()!r}")
        if key in front.values:
            raise ParseError(number, f"duplicate key {key!r}")
        front.values[key] = parse_value(raw.strip(), number)
        front.lines[key] = number
        front.comments[key] = comment
    raise ParseError(1, "front matter is not closed with '---'")


def decode(data: bytes) -> str:
    try:
        return data.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise ParseError(1, f"not valid UTF-8 (byte {error.start})") from None


def read_text(path: Path) -> str:
    return decode(path.read_bytes())


def is_template(name: str) -> bool:
    return TEMPLATE_RE.fullmatch(name) is not None


# --- Repository model ------------------------------------------------------------------------------------------------


@dataclass
class Schema:
    keys: list[str]
    list_keys: set[str]
    statuses: set[str]


@dataclass
class Artifact:
    path: str
    kind: Kind
    front: FrontMatter
    body: list[tuple[int, str]]

    @property
    def id(self) -> str | None:
        value = self.front.values.get("id")
        return value if isinstance(value, str) else None

    @property
    def status(self) -> str | None:
        value = self.front.values.get("status")
        return value if isinstance(value, str) else None

    def ids(self, key: str) -> list[str]:
        value = self.front.values.get(key, [])
        if isinstance(value, str):
            return [value] if value else []
        return value

    def line(self, key: str) -> int:
        return self.front.lines.get(key, 1)


def load_schema(folder: Path) -> Schema | None:
    """Read the required keys, list-typed keys, and allowed statuses from the folder's templates."""
    templates = sorted(path for path in folder.glob("*.md") if is_template(path.name))
    if not templates:
        return None
    key_sets, list_keys, statuses = [], set(), set()
    for template in templates:
        front, _ = parse_document(read_text(template))
        if front is None:
            raise ParseError(1, f"template {template.name} has no front matter")
        key_sets.append(list(front.values))
        list_keys |= {key for key, value in front.values.items() if isinstance(value, list)}
        if "|" in front.comments.get("status", ""):
            statuses |= {status.strip() for status in front.comments["status"].split("|")}
    keys = [key for key in key_sets[0] if all(key in other for other in key_sets[1:])]
    return Schema(keys, list_keys, statuses)


def section(body: list[tuple[int, str]], heading: str) -> list[tuple[int, str]]:
    lines, inside = [], False
    for number, text in body:
        if text.strip() == heading:
            inside = True
        elif inside and text.startswith("## "):
            break
        elif inside:
            lines.append((number, text))
    return lines


def is_repo_path(text: str) -> bool:
    return "/" in text and " " not in text and not text.startswith(("/", "~")) and not any(
        marker in text for marker in PLACEHOLDERS
    )


# --- Checks ----------------------------------------------------------------------------------------------------------


def check_schema(artifact: Artifact, schema: Schema) -> list[Finding]:
    """GOV-01 (every template key, right shape) and GOV-02 (allowed status)."""
    findings = []
    for key in schema.keys:
        value = artifact.front.values.get(key)
        if value is None:
            findings.append(Finding(artifact.path, 1, "GOV-01", f"missing front-matter key '{key}'"))
        elif key in schema.list_keys and not isinstance(value, list):
            findings.append(Finding(artifact.path, artifact.line(key), "GOV-01", f"'{key}' must be a list"))
        elif key not in schema.list_keys and isinstance(value, list):
            findings.append(Finding(artifact.path, artifact.line(key), "GOV-01", f"'{key}' must be a single value"))
    status = artifact.status
    if status is not None and schema.statuses and status not in schema.statuses:
        allowed = ", ".join(sorted(schema.statuses))
        findings.append(
            Finding(artifact.path, artifact.line("status"), "GOV-02", f"status '{status}' is not one of: {allowed}")
        )
    return findings


def check_name(artifact: Artifact) -> list[Finding]:
    """GOV-03: the file name follows the folder's naming scheme and matches `id`."""
    name = PurePosixPath(artifact.path).name
    match = artifact.kind.name_re.fullmatch(name)
    if not match:
        return [Finding(artifact.path, 1, "GOV-03", f"file name does not follow the docs/{artifact.kind.folder} naming")]
    expected = artifact.kind.id_prefix + match.group(1)
    if artifact.id is not None and artifact.id != expected:
        return [
            Finding(
                artifact.path, artifact.line("id"), "GOV-03", f"id '{artifact.id}' does not match the file name ({expected})"
            )
        ]
    return []


def check_duplicate_ids(artifacts: list[Artifact]) -> list[Finding]:
    """GOV-04 (reconciliation check 1)."""
    by_id: dict[str, list[Artifact]] = {}
    for artifact in artifacts:
        if artifact.id:
            by_id.setdefault(artifact.id, []).append(artifact)
    findings = []
    for ident, group in by_id.items():
        for artifact in group if len(group) > 1 else ():
            others = ", ".join(other.path for other in group if other is not artifact)
            findings.append(Finding(artifact.path, artifact.line("id"), "GOV-04", f"id '{ident}' is also used by {others}"))
    return findings


def check_references(artifact: Artifact, known: set[str]) -> list[Finding]:
    """GOV-05 (reconciliation check 4, front-matter half)."""
    return [
        Finding(artifact.path, artifact.line(key), "GOV-05", f"'{key}' cites {ident}, which does not exist")
        for key in REFERENCE_KEYS
        for ident in artifact.ids(key)
        if ident not in known
    ]


def check_sources(artifact: Artifact, by_path: dict[str, Artifact], root: Path) -> list[Finding]:
    """GOV-06: cited paths exist, and Sources cites no governance artifact missing from `depends-on`."""
    findings, cited = [], {}
    for number, text in section(artifact.body, "## Sources"):
        for span in BACKTICK_RE.findall(text):
            target = span.split("#", 1)[0].strip()
            if not is_repo_path(target):
                continue
            if ".." in PurePosixPath(target).parts:
                findings.append(Finding(artifact.path, number, "GOV-06", f"cited path '{target}' leaves the repository"))
            elif not (root / target).exists():
                findings.append(Finding(artifact.path, number, "GOV-06", f"cited path '{target}' does not exist"))
            elif target in by_path and by_path[target].id:
                cited.setdefault(by_path[target].id, number)
        for ident in ID_RE.findall(text):
            cited.setdefault(ident, number)
    depends_on = set(artifact.ids("depends-on"))
    for ident, number in cited.items():
        if ident != artifact.id and ident not in depends_on:
            findings.append(
                Finding(artifact.path, number, "GOV-06", f"Sources cites {ident}, which is missing from depends-on")
            )
    return findings


def check_approval(artifact: Artifact) -> list[Finding]:
    """GOV-07 (reconciliation check 8, front-matter half)."""
    if artifact.status in artifact.kind.approved and not artifact.ids("approved-by"):
        return [
            Finding(
                artifact.path,
                artifact.line("approved-by"),
                "GOV-07",
                f"status '{artifact.status}' requires a human in 'approved-by'",
            )
        ]
    return []


def check_stale(artifact: Artifact, by_id: dict[str, Artifact]) -> list[Finding]:
    """GOV-08 (reconciliation check 5)."""
    if artifact.status not in STALE_DEPENDENT:
        return []
    return [
        Finding(artifact.path, artifact.line("depends-on"), "GOV-08", f"depends on {ident}, which is {by_id[ident].status}")
        for ident in artifact.ids("depends-on")
        if ident in by_id and by_id[ident].status in STALE_DEPENDENCY
    ]


def check_unique_decisions(artifacts: list[Artifact]) -> list[Finding]:
    """GOV-09 (reconciliation check 3, exact-match half)."""
    findings = []
    for folder, key in UNIQUE_WHEN_ACCEPTED:
        groups: dict[str, list[Artifact]] = {}
        for artifact in artifacts:
            value = artifact.front.values.get(key)
            if artifact.kind.folder == folder and artifact.status == "Accepted" and isinstance(value, str):
                groups.setdefault(value.strip(), []).append(artifact)
        for value, group in groups.items():
            for artifact in group if len(group) > 1 else ():
                others = ", ".join(other.id or other.path for other in group if other is not artifact)
                findings.append(
                    Finding(artifact.path, artifact.line(key), "GOV-09", f"'{key}' \"{value}\" is also Accepted in {others}")
                )
    return findings


def git(root: Path, *args: str) -> bytes:
    try:
        result = subprocess.run(["git", "-C", str(root), *args], capture_output=True)
    except OSError as error:
        raise UsageError(f"cannot run git: {error.strerror or error}") from None
    if result.returncode != 0:
        raise UsageError(f"git {' '.join(args)} failed: {result.stderr.decode(errors='replace').strip()}")
    return result.stdout


def comparable_body(body: list[tuple[int, str]], folder: str) -> list[str]:
    lines = [text.rstrip() for _, text in body]
    if folder == "change-records":
        lines, inside = [], False
        for _, text in body:
            if text.strip() == APPEND_ONLY_SECTION:
                inside = True
            elif inside and text.startswith("## "):
                inside = False
            if not inside:
                lines.append(text.rstrip())
    while lines and not lines[-1]:
        lines.pop()
    return lines


def keeps_appended_section(old_body: list[tuple[int, str]], new_body: list[tuple[int, str]]) -> bool:
    """True if every non-placeholder line of the old Post-implementation section is still there, in order."""
    old = [text.rstrip() for _, text in section(old_body, APPEND_ONLY_SECTION) if text.strip()]
    remaining = iter(text.rstrip() for _, text in section(new_body, APPEND_ONLY_SECTION))
    return all(any(line == new for new in remaining) for line in old if not PLACEHOLDER_RE.search(line))


def check_immutable(root: Path, base: str) -> list[Finding]:
    """GOV-10: approved ADRs and change records changed only their status fields since `base`."""
    if base.startswith("-"):
        raise UsageError(f"--base must be a git ref, not an option: {base!r}")
    git(root, "rev-parse", "--verify", "--quiet", f"{base}^{{commit}}")
    findings = []
    for kind in (k for k in KINDS if k.folder in IMMUTABLE_FOLDERS):
        listing = git(root, "ls-tree", "-r", "--name-only", "-z", base, "--", f"docs/{kind.folder}")
        for rel in sorted(filter(None, listing.decode("utf-8", errors="replace").split("\0"))):
            name = PurePosixPath(rel).name
            if not name.endswith(".md") or name == "README.md" or is_template(name):
                continue
            try:
                old, old_body = parse_document(decode(git(root, "show", f"{base}:{rel}")))
            except ParseError as error:
                findings.append(Finding(rel, error.line, "GOV-00", f"at {base}: {error.message}"))
                continue
            if old is None or old.values.get("status") not in kind.approved:
                continue
            current = root / rel
            if not current.is_file():
                findings.append(Finding(rel, 1, "GOV-10", f"approved record was deleted or renamed since {base}"))
                continue
            try:
                new, new_body = parse_document(read_text(current))
            except ParseError:
                continue  # already reported as GOV-00 by the main pass
            if new is None:
                continue  # already reported as GOV-01
            for key in sorted((set(old.values) | set(new.values)) - MUTABLE_KEYS):
                if old.values.get(key) != new.values.get(key):
                    findings.append(
                        Finding(
                            rel,
                            new.lines.get(key, 1),
                            "GOV-10",
                            f"'{key}' changed since {base}; only status, date, approved-by, superseded-by may change",
                        )
                    )
            if kind.folder == "change-records" and not keeps_appended_section(old_body, new_body):
                findings.append(
                    Finding(
                        rel,
                        new.end_line + 1,
                        "GOV-10",
                        f"{APPEND_ONLY_SECTION.lstrip('# ')} lost or rewrote text since {base}; it is append-only",
                    )
                )
            if comparable_body(old_body, kind.folder) != comparable_body(new_body, kind.folder):
                findings.append(
                    Finding(rel, new.end_line + 1, "GOV-10", f"body changed since {base}; supersede the record instead")
                )
    return findings


@dataclass
class ProvenanceRow:
    line: int
    patterns: list[str]
    cells: dict[str, str]


PROVENANCE_COLUMNS = ("skill", "source", "version", "license", "imported")


def read_provenance(readme: Path) -> list[ProvenanceRow]:
    lines = read_text(readme).splitlines()
    start = next((i for i, text in enumerate(lines) if text.startswith("#") and "Provenance" in text), None)
    if start is None:
        return []
    table = []
    for number, text in enumerate(lines[start + 1 :], start=start + 2):
        if text.startswith("|"):
            table.append((number, [cell.strip() for cell in text.strip().strip("|").split("|")]))
        elif table:
            break
    if not table:
        return []
    header = [cell.lower() for cell in table[0][1]]
    columns = {name: next((i for i, cell in enumerate(header) if cell.startswith(name)), None) for name in PROVENANCE_COLUMNS}
    rows = []
    for number, cells in table[1:]:
        if all(re.fullmatch(r"[-: ]*", cell) for cell in cells):
            continue
        values = {name: cells[i] if i is not None and i < len(cells) else "" for name, i in columns.items()}
        rows.append(ProvenanceRow(number, BACKTICK_RE.findall(values["skill"]), values))
    return rows


def check_skills(root: Path) -> list[Finding]:
    """GOV-11: every skill has complete provenance (.agents/rules/10-imported-assets.md)."""
    skills_dir = root / ".agents" / "skills"
    if not skills_dir.is_dir():
        return []
    skills = sorted(
        path.name for path in skills_dir.iterdir() if path.is_dir() and not path.is_symlink() and (path / "SKILL.md").is_file()
    )
    readme = skills_dir / "README.md"
    readme_rel = ".agents/skills/README.md"
    findings = []
    try:
        rows = read_provenance(readme) if readme.is_file() else []
    except ParseError as error:
        return [Finding(readme_rel, error.line, "GOV-00", error.message)]
    for skill in skills:
        matches = [row for row in rows if any(fnmatch.fnmatchcase(skill, pattern) for pattern in row.patterns)]
        if not matches:
            findings.append(
                Finding(f".agents/skills/{skill}/SKILL.md", 1, "GOV-11", f"no provenance row in {readme_rel}")
            )
        for row in matches:
            missing = [name for name in PROVENANCE_COLUMNS[1:] if not row.cells[name]]
            if missing:
                findings.append(
                    Finding(readme_rel, row.line, "GOV-11", f"provenance for '{skill}' lacks: {', '.join(missing)}")
                )
    return findings


def is_unlicensed(license_cell: str) -> bool:
    text = license_cell.casefold()
    return "unverified" in text or text.startswith("none")


def unlicensed_skills(root: Path) -> list[str]:
    """Skills accepted without a verified license (Rule 10): reported as notes, never as findings."""
    readme = root / ".agents" / "skills" / "README.md"
    try:
        rows = read_provenance(readme) if readme.is_file() else []
    except ParseError:
        return []  # reported as GOV-00 by check_skills
    return sorted({pattern for row in rows if is_unlicensed(row.cells["license"]) for pattern in row.patterns})


# --- Runner ----------------------------------------------------------------------------------------------------------


def run_checks(root: Path, base: str | None = None) -> tuple[list[Finding], int]:
    """Return all findings and the number of artifact files checked."""
    findings: list[Finding] = []
    artifacts: list[Artifact] = []
    unparsed_ids: set[str] = set()  # ids implied by the names of files that failed to parse, to avoid cascades
    for kind in KINDS:
        folder = root / "docs" / kind.folder
        if not folder.is_dir():
            continue
        candidates = [
            path for path in sorted(folder.glob("*.md")) if path.name != "README.md" and not is_template(path.name)
        ]
        try:
            schema = load_schema(folder)
        except ParseError as error:
            findings.append(Finding(f"docs/{kind.folder}", error.line, "GOV-00", f"template: {error.message}"))
            continue
        if schema is None:
            if candidates:
                findings.append(Finding(f"docs/{kind.folder}", 1, "GOV-00", "no template to read the schema from"))
            continue
        for path in candidates:
            rel = path.relative_to(root).as_posix()
            try:
                front, body = parse_document(read_text(path))
            except ParseError as error:
                findings.append(Finding(rel, error.line, "GOV-00", error.message))
                if match := kind.name_re.fullmatch(path.name):
                    unparsed_ids.add(kind.id_prefix + match.group(1))
                continue
            if front is None:
                findings.append(Finding(rel, 1, "GOV-01", "no front matter"))
                continue
            artifact = Artifact(rel, kind, front, body)
            findings += check_schema(artifact, schema) + check_name(artifact)
            artifacts.append(artifact)

    by_id = {artifact.id: artifact for artifact in artifacts if artifact.id}
    by_path = {artifact.path: artifact for artifact in artifacts}
    findings += check_duplicate_ids(artifacts)
    findings += check_unique_decisions(artifacts)
    for artifact in artifacts:
        findings += check_references(artifact, set(by_id) | unparsed_ids)
        findings += check_sources(artifact, by_path, root)
        findings += check_approval(artifact)
        findings += check_stale(artifact, by_id)
    findings += check_skills(root)
    if base is not None:
        findings += check_immutable(root, base)
    return sorted(set(findings)), len(artifacts)


def main(argv: list[str] | None = None) -> int:
    if sys.version_info < MIN_PYTHON:
        print(f"govern_check: Python {'.'.join(map(str, MIN_PYTHON))} or later is required", file=sys.stderr)
        return 2
    parser = argparse.ArgumentParser(description="Check the mechanically verifiable governance rules (Rule 02).")
    parser.add_argument(
        "--root", type=Path, default=Path(__file__).resolve().parent.parent, help="repository root (default: ..)"
    )
    parser.add_argument("--base", help="git ref to compare approved records against (enables GOV-10)")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    if not (root / "docs").is_dir():
        print(f"govern_check: {root} has no docs/ folder; pass --root", file=sys.stderr)
        return 2
    try:
        findings, checked = run_checks(root, args.base)
    except UsageError as error:
        print(f"govern_check: {error}", file=sys.stderr)
        return 2
    for finding in findings:
        print(finding)
    print(f"govern_check: {len(findings)} finding(s), {checked} artifact(s) checked.", file=sys.stderr)
    if args.base is None:
        print("Skipped: GOV-10 (pass --base <git-ref> to compare approved records).", file=sys.stderr)
    print(f"Not checked (human review): {NOT_CHECKED}.", file=sys.stderr)
    for skill in unlicensed_skills(root):
        print(
            f"Note: skill '{skill}' has no verified license; accepted under Rule 10 and redistributed at the "
            "maintainer's risk.",
            file=sys.stderr,
        )
    if any(finding.check == "GOV-00" for finding in findings):
        return 2
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
