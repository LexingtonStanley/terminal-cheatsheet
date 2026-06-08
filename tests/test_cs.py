#!/usr/bin/env python3
"""Tests for cs. Run: python3 -m pytest tests/  (or: python3 tests/test_cs.py)

Each test runs the real `cs` executable against an isolated CS_DATA_DIR, so it
exercises the same code path agents/humans hit. No network, no real store.
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

CS = str(Path(__file__).resolve().parent.parent / "cs")


def run(args, data_dir, inp=None, env=None):
    e = dict(os.environ)
    e["CS_DATA_DIR"] = str(data_dir)
    e["CS_HOME"] = str(Path(CS).parent)
    if env:
        e.update(env)
    return subprocess.run([sys.executable, CS, *args], input=inp, text=True,
                          capture_output=True, env=e)


class CsTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="cs_test_")
        self.data = Path(self.tmp) / "data"
        self.data.mkdir(parents=True)

    def add_basic(self, **over):
        args = ["add", "--command", over.get("command", "grep -rn <pat> <dir>"),
                "--name", "recursive grep", "--desc", "search recursively",
                "--category", over.get("category", "grep"), "--tags", "grep,recursive",
                "--reviewed"]
        return run(args, self.data)

    # acceptance #1: add creates exactly one; re-run is a no-op (dedup)
    def test_add_and_dedup(self):
        r = self.add_basic()
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("added:", r.stdout)
        r2 = self.add_basic()
        self.assertIn("skipped", r2.stderr + r2.stdout)
        lines = (self.data / "grep.jsonl").read_text().strip().splitlines()
        self.assertEqual(len(lines), 1)

    # id is a stable hash of command+category
    def test_id_stable(self):
        self.add_basic()
        e = json.loads((self.data / "grep.jsonl").read_text().splitlines()[0])
        self.assertEqual(len(e["id"]), 12)
        self.assertIn("added", e)

    # acceptance #3: --stdin accepts a JSON array, adds all, dedups
    def test_stdin_array(self):
        arr = [
            {"command": "ssh <u>@<h>", "name": "ssh", "description": "login",
             "category": "net", "tags": ["ssh"]},
            {"command": "rsync -a <s> <d>", "name": "rsync", "description": "sync",
             "category": "transfer", "tags": ["rsync"]},
        ]
        r = run(["add", "--stdin"], self.data, inp=json.dumps(arr))
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stdout.count("added:"), 2)
        # dedup on re-feed
        r2 = run(["add", "--stdin"], self.data, inp=json.dumps(arr))
        self.assertEqual(r2.stdout.count("added:"), 0)

    # agent adds (no --reviewed) are flagged needs_review=true
    def test_needs_review_default(self):
        r = run(["add", "--command", "x <a>", "--name", "n", "--desc", "d",
                 "--category", "misc", "--tags", "t"], self.data)
        self.assertIn("needs_review", r.stdout)
        e = json.loads((self.data / "misc.jsonl").read_text().splitlines()[0])
        self.assertTrue(e["needs_review"])

    # validation rejects missing required fields with non-zero exit
    def test_validation_rejects(self):
        r = run(["add", "--command", "x", "--category", "misc"], self.data)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("missing required field", r.stderr)

    # --on-dup update replaces; --dry-run writes nothing
    def test_on_dup_update_and_dry_run(self):
        self.add_basic()
        r = run(["add", "--command", "grep -rn <pat> <dir>", "--name", "NEWNAME",
                 "--desc", "d2", "--category", "grep", "--tags", "g",
                 "--reviewed", "--on-dup", "update"], self.data)
        self.assertIn("updated", r.stdout)
        e = json.loads((self.data / "grep.jsonl").read_text().splitlines()[0])
        self.assertEqual(e["name"], "NEWNAME")
        # dry-run does not write a new category
        r2 = run(["add", "--command", "ping <h>", "--name", "p", "--desc", "d",
                  "--category", "neverwritten", "--tags", "t", "--dry-run"], self.data)
        self.assertIn("would-add", r2.stdout)
        self.assertFalse((self.data / "neverwritten.jsonl").exists())

    # render produces markdown with the command and examples
    def test_render(self):
        run(["add", "--command", "grep -rn <pat> <dir>", "--name", "rg",
             "--desc", "d", "--category", "grep", "--tags", "g",
             "--example", "grep -rn TODO src/ ::: todos", "--reviewed"], self.data)
        r = run(["render", "grep"], self.data)
        self.assertIn("grep -rn", r.stdout)
        self.assertIn("examples", r.stdout)
        self.assertIn("todos", r.stdout)

    # list marks needs_review entries; stats counts
    def test_list_and_stats(self):
        self.add_basic()
        run(["add", "--command", "y <a>", "--name", "n", "--desc", "d",
             "--category", "misc", "--tags", "t"], self.data)  # needs_review
        r = run(["list"], self.data)
        self.assertIn("recursive grep", r.stdout)
        self.assertIn("*", r.stdout)  # the needs_review marker
        s = run(["stats"], self.data)
        self.assertIn("entries: 2", s.stdout)
        self.assertIn("needs_review: 1", s.stdout)


class ValidationTest(unittest.TestCase):
    """§7 regex validation pass — import the cs module directly."""

    @classmethod
    def setUpClass(cls):
        import importlib.util
        from importlib.machinery import SourceFileLoader
        loader = SourceFileLoader("cs_mod", CS)  # CS has no .py extension
        spec = importlib.util.spec_from_loader("cs_mod", loader)
        cls.m = importlib.util.module_from_spec(spec)
        loader.exec_module(cls.m)

    def test_grep_mode(self):
        self.assertEqual(self.m.grep_mode('grep "x" f'), "BRE")
        self.assertEqual(self.m.grep_mode('grep -E "x" f'), "ERE")
        self.assertEqual(self.m.grep_mode('grep -rn "x" f'), "BRE")
        self.assertEqual(self.m.grep_mode('grep -P "x" f'), "PCRE")
        self.assertEqual(self.m.grep_mode("egrep 'x' f"), "ERE")
        self.assertIsNone(self.m.grep_mode("ls -la"))

    def test_bre_misuse_flagged(self):
        # plain grep with ERE-only operators -> flagged
        self.assertTrue(self.m.regex_lint('grep "colo?r" f'))
        self.assertTrue(self.m.regex_lint('grep "error{2,}" f'))
        self.assertTrue(self.m.regex_lint('grep "error|warn" f'))
        # -E version is clean (operators are valid there)
        self.assertFalse(self.m.regex_lint('grep -E "error|warn" f'))
        # escaped operators in BRE are fine
        self.assertFalse(self.m.regex_lint(r'grep "\(error\|warn\)" f'))
        # '*' and '.' are valid BRE operators -> not flagged
        self.assertFalse(self.m.regex_lint('grep "go.*ld" f'))

    def test_pcre_portability(self):
        self.assertTrue(any("GNU-only" in i for i in
                            self.m.regex_lint('grep -P "(?<=u=)p" f')))

    def test_corrections_applied(self):
        e = {"command": 'grep "go*ld" f', "name": "n", "description": "WRONG",
             "category": "regex", "tags": ["g"]}
        e = self.m.normalize_entry(e)
        e, notes = self.m.apply_validation(e)
        self.assertIn("zero-or-more", e["description"])
        self.assertTrue(e["needs_review"])
        self.assertTrue(notes)


if __name__ == "__main__":
    unittest.main(verbosity=2)
