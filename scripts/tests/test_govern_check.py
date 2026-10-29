"""Tests for scripts/govern_check.py: one failing fixture per check, plus a clean one.

    python3 -I -m unittest discover -s scripts/tests
"""

import contextlib
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "scripts"))

import govern_check  # noqa: E402

ADR = """---
id: ADR-0001
status: Accepted
date: 2026-10-29
decides: "primary datastore"
owner: "alice"
approved-by: [ "alice" ]
decision-makers: [ "alice" ]
consulted: [ ]
informed: [ ]
depends-on: [ ]
supersedes: [ ]
superseded-by: ""
---

# Use a datastore

## Decision Outcome

Chosen option: "X".

## Sources

- None
"""

STORY = """---
id: US-0001
status: Ready
date: 2026-10-29
owner: ""
approved-by: [ "alice" ]
priority: Must
ssdlc-phase: Planning
depends-on: [ ADR-0001 ]
---

# User story: export orders

## Sources

- `docs/adrs/0001-use-a-datastore.md` (ADR-0001, Decision Outcome)
"""

SKILLS_README = """# Skills

### Provenance of imported skills

| Skill(s) | Source | Version | License | Imported   |
|----------|--------|---------|---------|------------|
| `demo`   | x/demo | abc123  | MIT     | 2026-10-29 |
"""

SDP = """---
id: SDP-orders-api
status: Draft
date: 2026-10-29
version: "0.1"
owner: ""
approved-by: [ ]
depends-on: [ ]
---

# Service Design Package: orders API
"""

THREAT_MODEL = """---
id: TM-0001
status: Accepted
date: 2026-10-29
scope: "orders API"
owner: "alice"
approved-by: [ "alice" ]
depends-on: [ ]
supersedes: [ ]
superseded-by: ""
---

# Threat model: orders API
"""

CHANGE_RECORD = """---
id: CR-0001
status: Approved
date: 2026-10-29
owner: "alice"
approved-by: [ "alice" ]
risk: Low
change-type: Standard
requested-by: "alice"
planned-window: ""
depends-on: [ ]
---

# Change record: rotate keys

## Post-implementation

- **Actual outcome:** pending
- **Follow-ups:** {tickets, problem records if needed}

## Sources

- None
"""

ADR_PATH = "docs/adrs/0001-use-a-datastore.md"
STORY_PATH = "docs/user-stories/US-0001-export-orders.md"
CR_PATH = "docs/change-records/CR-0001-rotate-keys.md"


class Fixture(unittest.TestCase):
    """A clean repository: one Accepted ADR, one Ready story that depends on it, one skill with provenance."""

    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        for template in REPO.glob("docs/*/*-template*.md"):
            target = self.root / template.relative_to(REPO)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(template, target)
        self.write(ADR_PATH, ADR)
        self.write(STORY_PATH, STORY)
        self.write(".agents/skills/demo/SKILL.md", "---\nname: demo\n---\n")
        self.write(".agents/skills/README.md", SKILLS_README)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def write(self, rel: str, text: str) -> None:
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def edit(self, rel: str, old: str, new: str) -> None:
        path = self.root / rel
        text = path.read_text(encoding="utf-8")
        self.assertIn(old, text)
        path.write_text(text.replace(old, new, 1), encoding="utf-8")

    def checks(self, base: str | None = None) -> set[str]:
        findings, _ = govern_check.run_checks(self.root, base)
        return {finding.check for finding in findings}


class GovernCheckTest(Fixture):
    def test_clean_fixture_has_no_findings(self) -> None:
        findings, checked = govern_check.run_checks(self.root)
        self.assertEqual(findings, [])
        self.assertEqual(checked, 2)

    def test_gov00_rejects_yaml_outside_the_flat_subset(self) -> None:
        self.edit(ADR_PATH, 'decides: "primary datastore"\n', 'decides: "primary datastore"\n  nested: x\n')
        self.assertEqual(self.checks(), {"GOV-00"})

    def test_gov01_missing_template_key(self) -> None:
        self.edit(STORY_PATH, "priority: Must\n", "")
        self.assertEqual(self.checks(), {"GOV-01"})

    def test_gov01_list_key_with_scalar_value(self) -> None:
        self.edit(ADR_PATH, "consulted: [ ]", 'consulted: "bob"')
        self.assertEqual(self.checks(), {"GOV-01"})

    def test_gov02_status_not_in_template(self) -> None:
        self.edit(ADR_PATH, "status: Accepted", "status: Approved")
        self.assertEqual(self.checks(), {"GOV-02"})

    def test_gov03_id_does_not_match_file_name(self) -> None:
        self.edit(STORY_PATH, "id: US-0001", "id: US-0002")
        self.assertEqual(self.checks(), {"GOV-03"})

    def test_gov04_duplicate_number_under_two_titles(self) -> None:
        second = ADR.replace("status: Accepted", "status: Proposed").replace('"primary datastore"', '"cache"')
        self.write("docs/adrs/0001-use-a-cache.md", second)
        self.assertEqual(self.checks(), {"GOV-04"})

    def test_gov05_unknown_dependency(self) -> None:
        self.edit(STORY_PATH, "depends-on: [ ADR-0001 ]", "depends-on: [ ADR-0001, ADR-0009 ]")
        self.assertEqual(self.checks(), {"GOV-05"})

    def test_gov06_sources_cites_artifact_missing_from_depends_on(self) -> None:
        self.edit(STORY_PATH, "depends-on: [ ADR-0001 ]", "depends-on: [ ]")
        self.assertEqual(self.checks(), {"GOV-06"})

    def test_gov06_cited_path_does_not_exist(self) -> None:
        self.edit(ADR_PATH, "- None", "- `docs/sdps/missing.md`")
        self.assertEqual(self.checks(), {"GOV-06"})

    def test_gov07_binding_status_without_approver(self) -> None:
        self.edit(ADR_PATH, 'approved-by: [ "alice" ]', "approved-by: [ ]")
        self.assertEqual(self.checks(), {"GOV-07"})

    def test_gov08_ready_story_depends_on_rejected_decision(self) -> None:
        self.edit(ADR_PATH, "status: Accepted", "status: Rejected")
        self.assertEqual(self.checks(), {"GOV-08"})

    def test_gov09_two_accepted_decisions_on_one_concern(self) -> None:
        self.write("docs/adrs/0002-use-another-datastore.md", ADR.replace("ADR-0001", "ADR-0002"))
        self.assertEqual(self.checks(), {"GOV-09"})

    def test_gov11_skill_without_provenance(self) -> None:
        self.write(".agents/skills/other/SKILL.md", "---\nname: other\n---\n")
        self.assertEqual(self.checks(), {"GOV-11"})

    def test_gov11_unverified_license(self) -> None:
        self.edit(".agents/skills/README.md", "| MIT     |", "| None declared (unverified) |")
        self.assertEqual(self.checks(), {"GOV-11"})

    def test_artifact_with_template_in_its_title_is_still_checked(self) -> None:
        broken = ADR.replace("ADR-0001", "ADR-0002").replace("consulted: [ ]\n", "").replace("Accepted", "Proposed")
        self.write("docs/adrs/0002-adopt-a-template-engine.md", broken)
        self.assertEqual(self.checks(), {"GOV-01"})

    def test_utf8_byte_order_mark_is_accepted(self) -> None:
        self.write(ADR_PATH, "\ufeff" + ADR)
        self.assertEqual(self.checks(), set())

    def test_gov00_file_that_is_not_utf8(self) -> None:
        (self.root / "docs/adrs/0002-bad-bytes.md").write_bytes(b"---\nid: ADR-0002\xff\n---\n")
        self.assertEqual(self.checks(), {"GOV-00"})

    def test_gov00_broken_template_is_reported(self) -> None:
        self.edit("docs/user-stories/user-story-template.md", "id: US-NNNN", "id: US-NNNN\n  nested: x")
        self.assertEqual(self.checks(), {"GOV-00"})

    def test_sdp_named_after_its_service_passes(self) -> None:
        self.write("docs/sdps/SDP-orders-api.md", SDP)
        self.assertEqual(self.checks(), set())

    def test_gov03_sdp_name_not_kebab_case(self) -> None:
        self.write("docs/sdps/SDP-Orders-API.md", SDP.replace("SDP-orders-api", "SDP-Orders-API"))
        self.assertEqual(self.checks(), {"GOV-03"})

    def test_gov06_cited_path_leaves_the_repository(self) -> None:
        self.edit(ADR_PATH, "- None", "- `../outside/notes.md`")
        self.assertEqual(self.checks(), {"GOV-06"})

    def test_gov09_two_accepted_threat_models_on_one_scope(self) -> None:
        self.write("docs/threat-models/TM-0001-orders-api.md", THREAT_MODEL)
        self.write("docs/threat-models/TM-0002-orders-api-v2.md", THREAT_MODEL.replace("TM-0001", "TM-0002"))
        self.assertEqual(self.checks(), {"GOV-09"})

    @unittest.skipUnless(hasattr(os, "symlink"), "symlinks are not supported")
    def test_gov11_ignores_symlinked_skill_folders(self) -> None:
        try:
            os.symlink(self.root / ".agents/skills/demo", self.root / ".agents/skills/alias")
        except OSError:
            self.skipTest("cannot create symlinks here")
        self.assertEqual(self.checks(), set())

    def test_main_returns_2_when_git_is_missing(self) -> None:
        with mock.patch.object(govern_check.subprocess, "run", side_effect=FileNotFoundError(2, "No such file", "git")):
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(govern_check.main(["--root", str(self.root), "--base", "HEAD"]), 2)

    def test_main_exit_codes(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(govern_check.main(["--root", str(self.root)]), 0)
            self.edit(ADR_PATH, "status: Accepted", "status: Approved")
            self.assertEqual(govern_check.main(["--root", str(self.root)]), 1)
            self.edit(ADR_PATH, "status: Approved", "status: [ Approved")
            self.assertEqual(govern_check.main(["--root", str(self.root)]), 2)


@unittest.skipUnless(shutil.which("git"), "git is not installed")
class ImmutabilityTest(Fixture):
    """GOV-10 needs a git history, so these fixtures are committed first."""

    def setUp(self) -> None:
        super().setUp()
        self.write(CR_PATH, CHANGE_RECORD)
        self.git("init", "-q")
        self.git("add", "-A")
        self.git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-qm", "base")

    def git(self, *args: str) -> None:
        subprocess.run(["git", "-C", str(self.root), *args], check=True, capture_output=True)

    def test_gov10_body_of_accepted_record_changed(self) -> None:
        self.edit(ADR_PATH, 'Chosen option: "X".', 'Chosen option: "Y".')
        self.assertEqual(self.checks(base="HEAD"), {"GOV-10"})

    def test_gov10_status_fields_may_change(self) -> None:
        self.edit(ADR_PATH, "date: 2026-10-29", "date: 2026-10-30")
        self.edit(ADR_PATH, 'approved-by: [ "alice" ]', 'approved-by: [ "alice", "bob" ]')
        self.assertEqual(self.checks(base="HEAD"), set())

    def test_gov10_deleted_accepted_record(self) -> None:
        (self.root / ADR_PATH).unlink()
        self.assertIn("GOV-10", self.checks(base="HEAD"))

    def test_gov10_post_implementation_may_be_appended(self) -> None:
        self.edit(CR_PATH, "## Sources", "- **Verified by:** bob\n\n## Sources")
        self.assertEqual(self.checks(base="HEAD"), set())

    def test_gov10_post_implementation_placeholder_may_be_filled(self) -> None:
        self.edit(CR_PATH, "{tickets, problem records if needed}", "PRB-0007")
        self.assertEqual(self.checks(base="HEAD"), set())

    def test_gov10_post_implementation_text_may_not_be_rewritten(self) -> None:
        self.edit(CR_PATH, "- **Actual outcome:** pending", "- **Actual outcome:** done")
        self.assertEqual(self.checks(base="HEAD"), {"GOV-10"})

    def test_gov10_rejects_option_as_ref(self) -> None:
        with self.assertRaises(govern_check.UsageError):
            govern_check.run_checks(self.root, base="--output=x")


class DriftTest(unittest.TestCase):
    """The checker's hardcoded tables must agree with the templates and READMEs they mirror (Rule 02)."""

    def test_hardcoded_statuses_exist_in_the_templates(self) -> None:
        for kind in govern_check.KINDS:
            schema = govern_check.load_schema(REPO / "docs" / kind.folder)
            self.assertIsNotNone(schema, kind.folder)
            self.assertLessEqual(kind.approved, schema.statuses, kind.folder)
        every_status = set().union(*(govern_check.load_schema(REPO / "docs" / k.folder).statuses for k in govern_check.KINDS))
        self.assertLessEqual(govern_check.STALE_DEPENDENT | govern_check.STALE_DEPENDENCY, every_status)

    def test_readme_naming_examples_match_the_checker(self) -> None:
        for kind in govern_check.KINDS:
            readme = (REPO / "docs" / kind.folder / "README.md").read_text(encoding="utf-8")
            naming = readme.split("## Naming", 1)[1].split("\n## ", 1)[0]
            examples = [span for span in re.findall(r"`([^`]+\.md)`", naming) if not any(mark in span for mark in ("NNNN", "<", "/"))]
            self.assertTrue(examples, f"no naming example in docs/{kind.folder}/README.md")
            for example in examples:
                self.assertRegex(example, kind.name_re, f"docs/{kind.folder}/README.md")
                self.assertFalse(govern_check.is_template(example), example)

    def test_every_shipped_template_is_recognized(self) -> None:
        for template in REPO.glob("docs/*/*-template*.md"):
            self.assertTrue(govern_check.is_template(template.name), template.name)


if __name__ == "__main__":
    unittest.main()
