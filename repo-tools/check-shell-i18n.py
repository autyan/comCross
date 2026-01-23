#!/usr/bin/env python3
from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Optional, Tuple


@dataclass(frozen=True)
class Finding:
    file: Path
    line: int
    column: int
    literal: str
    reason: str


KEY_LIKE_RE = re.compile(r"^[a-z0-9]+([._-][a-z0-9]+)+$", re.IGNORECASE)
HAS_WORD_RE = re.compile(r"[A-Za-z]{3,}")
HAS_CJK_RE = re.compile(r"[\u4e00-\u9fff]")
TIME_FORMAT_RE = re.compile(r"^[Hhmsf:\.\-_/ ]+$")


def _is_key_like(text: str) -> bool:
    # Typical i18n keys: dot/underscore/dash separated tokens, no spaces.
    if " " in text or "\t" in text or "\n" in text:
        return False
    if len(text) < 3:
        return False
    return bool(KEY_LIKE_RE.match(text))


def _looks_like_path_or_identifier(text: str) -> bool:
    if text.startswith("/") or text.startswith("\\"):
        return True
    if "://" in text:
        return True
    if text.endswith(".axaml") or text.endswith(".cs") or text.endswith(".json"):
        return True
    # Very short tokens are often non-UI (e.g., "OK", "RX")
    if len(text) <= 2:
        return True
    return False


def _is_probable_ui_copy(text: str) -> bool:
    # Heuristic: treat anything that looks like human-facing copy (words or CJK)
    # as a violation, unless it is a key-like token.
    if _is_key_like(text):
        return False
    if _looks_like_path_or_identifier(text):
        return False

    # If it has any CJK, it's almost certainly UI copy.
    if HAS_CJK_RE.search(text):
        return True

    # For non-CJK: only treat phrases with whitespace as probable UI copy.
    # This avoids flagging control names like "NameTextBox".
    has_words = bool(HAS_WORD_RE.search(text))
    has_whitespace = any(ws in text for ws in (" ", "\t", "\n"))
    if has_words and has_whitespace:
        return True

    return False


def _is_logging_context(line_text: str) -> bool:
    # Logs are allowed to be raw English.
    lowered = line_text.lower()
    if "logger." in lowered or "ilogger" in lowered:
        return True
    if (
        "console.writeline(" in lowered
        or "console.error.writeline(" in lowered
        or "console.out.writeline(" in lowered
        or "debug.writeline(" in lowered
        or "trace.writeline(" in lowered
    ):
        return True
    # Common logging APIs (Microsoft.Extensions.Logging + Serilog-like)
    if re.search(r"(?i)\.log(debug|information|warning|error|critical)\(", line_text):
        return True
    if re.search(r"(?i)\b(log\.|serilog\.log\.)", line_text):
        # Often used with .Debug/.Information/etc
        if re.search(r"(?i)\.(debug|information|warning|error|fatal)\(", line_text):
            return True

    # Common custom log services: _appLogService.Info("...")
    if "logservice" in lowered and re.search(r"(?i)\.(info|warn|warning|error|debug|trace)\(", line_text):
        return True
    return False


def _has_ignore_marker(prev_line: str, line_text: str) -> bool:
    marker = "i18n-ignore"
    return (marker in prev_line) or (marker in line_text)


def _extract_string_literals(source: str) -> Iterable[Tuple[int, int, str]]:
    """Yield (line, col, value) for string literals.

    Handles:
    - regular strings: "..."
    - verbatim strings: @"..."
    - interpolated strings: $"..." and $@"..." / @$"..."

    Note: We intentionally ignore content inside comments.
    """

    i = 0
    line = 1
    col = 1

    def advance(ch: str) -> None:
        nonlocal i, line, col
        i += 1
        if ch == "\n":
            line += 1
            col = 1
        else:
            col += 1

    in_block_comment = False

    while i < len(source):
        ch = source[i]

        # Handle comments
        if in_block_comment:
            if ch == "*" and i + 1 < len(source) and source[i + 1] == "/":
                advance(ch)
                advance(source[i])
                in_block_comment = False
                continue
            advance(ch)
            continue

        if ch == "/" and i + 1 < len(source):
            nxt = source[i + 1]
            if nxt == "/":
                # line comment: skip until newline
                while i < len(source) and source[i] != "\n":
                    advance(source[i])
                continue
            if nxt == "*":
                advance(ch)
                advance(nxt)
                in_block_comment = True
                continue

        # Detect string start prefixes: $, @, $@, @$
        start_i = i
        start_line = line
        start_col = col

        is_interpolated = False
        is_verbatim = False

        if ch in ("$", "@"):  # possible prefix
            # Look ahead for combinations before the opening quote
            if ch == "$":
                is_interpolated = True
                if i + 1 < len(source) and source[i + 1] == "@":
                    is_verbatim = True
                    advance(ch)
                    advance("@")
                else:
                    advance(ch)
            elif ch == "@":
                is_verbatim = True
                if i + 1 < len(source) and source[i + 1] == "$":
                    is_interpolated = True
                    advance(ch)
                    advance("$")
                else:
                    advance(ch)

            if i >= len(source) or source[i] != '"':
                # Not actually a string literal
                i = start_i
                line = start_line
                col = start_col
            else:
                # Proceed, we are at opening quote
                pass

        ch = source[i]
        if ch != '"':
            advance(ch)
            continue

        # We are at opening quote
        literal_line = line
        literal_col = col
        advance('"')

        buf: List[str] = []

        if is_verbatim:
            # Verbatim string: "" escapes a quote
            while i < len(source):
                ch = source[i]
                if ch == '"':
                    if i + 1 < len(source) and source[i + 1] == '"':
                        buf.append('"')
                        advance('"')
                        advance('"')
                        continue
                    advance('"')
                    break
                buf.append(ch)
                advance(ch)
        else:
            # Regular string: backslash escapes
            while i < len(source):
                ch = source[i]
                if ch == '"':
                    advance('"')
                    break
                if ch == "\\":
                    # keep escape sequence as-is for heuristics
                    if i + 1 < len(source):
                        buf.append(ch)
                        advance(ch)
                        buf.append(source[i])
                        advance(source[i])
                        continue
                buf.append(ch)
                advance(ch)

        yield (literal_line, literal_col, "".join(buf))


def main(argv: List[str]) -> int:
    repo_root = Path(argv[1]) if len(argv) > 1 else Path.cwd()
    shell_dir = repo_root / "src" / "Shell"

    if not shell_dir.exists():
        print(f"[i18n] Shell dir not found: {shell_dir}", file=sys.stderr)
        return 1

    findings: List[Finding] = []

    for file in shell_dir.rglob("*.cs"):
        if any(part in ("bin", "obj") for part in file.parts):
            continue

        try:
            source = file.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            source = file.read_text(encoding="utf-8", errors="replace")

        lines = source.splitlines()

        for (ln, col, text) in _extract_string_literals(source):
            if ln <= 0 or ln > len(lines):
                continue

            line_text = lines[ln - 1]
            prev_line = lines[ln - 2] if ln - 2 >= 0 else ""
            prev2_line = lines[ln - 3] if ln - 3 >= 0 else ""

            context_text = f"{prev2_line} {prev_line} {line_text}".strip()

            if _has_ignore_marker(prev_line, line_text):
                continue

            if _is_logging_context(context_text):
                continue

            # Exception messages are typically not UI copy (UI should map errors to i18n).
            # Allow them by default to reduce noise.
            if "throw new" in context_text:
                continue

            # Allow obvious formatting strings (e.g., time formats)
            if ".ToString(" in context_text and TIME_FORMAT_RE.match(text) and not HAS_CJK_RE.search(text):
                continue

            # Common debug marker strings
            if text.startswith("[") and "]" in text and " " in text:
                # e.g. "[Shell] ..." etc.
                continue

            # Allow obvious localization key usage patterns.
            if "GetString(" in context_text or ".GetString(" in context_text:
                continue

            if _is_probable_ui_copy(text):
                reason = "probable UI raw string in Shell .cs (use i18n key)"
                findings.append(Finding(file=file, line=ln, column=col, literal=text, reason=reason))

    if findings:
        print("[i18n] FAIL: Raw UI strings detected in src/Shell/**/*.cs", file=sys.stderr)
        for f in findings:
            rel = f.file.relative_to(repo_root)
            snippet = f.literal.replace("\n", "\\n")
            if len(snippet) > 80:
                snippet = snippet[:77] + "..."
            print(f"- {rel}:{f.line}:{f.column}  {f.reason}: \"{snippet}\"", file=sys.stderr)
        print("[i18n] If this is not UI copy, add // i18n-ignore on the same line or the line above.", file=sys.stderr)
        return 1

    print("[i18n] OK: No obvious raw UI strings in src/Shell/**/*.cs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
