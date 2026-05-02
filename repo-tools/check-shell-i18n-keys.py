#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Set, Tuple


@dataclass(frozen=True)
class MissingKey:
    file: Path
    line: int
    key: str
    context: str


# Keys are expected to look like: segment.segment or segment_segment etc.
KEY_TOKEN_RE = re.compile(r"^[a-z0-9]+([._-][a-z0-9]+)+$", re.IGNORECASE)

# Extraction from Core/Services/LocalizationService.cs (hardcoded en-US dictionary)
EN_DICT_KEY_RE = re.compile(r"\[\s*\"(?P<key>[^\"]+)\"\s*\]\s*=\s*")

# C# usage patterns
GETSTRING_RE = re.compile(r"\.GetString\(\s*\"(?P<key>[^\"]+)\"\s*(?:,|\))")
L_INDEXER_RE = re.compile(r"\bL\s*\[\s*\"(?P<key>[^\"]+)\"\s*\]")
STRINGS_INDEXER_RE = re.compile(r"\bStrings\s*\[\s*\"(?P<key>[^\"]+)\"\s*\]")
LOCALIZATION_INDEXER_RE = re.compile(r"\bLocalization\s*\[\s*\"(?P<key>[^\"]+)\"\s*\]")

# XAML binding patterns: {Binding L[foo.bar]} / {Binding Localization[foo.bar]}
XAML_L_INDEXER_RE = re.compile(r"\bL\[(?P<key>[A-Za-z0-9._-]+)\]")
XAML_LOCALIZATION_INDEXER_RE = re.compile(r"\bLocalization\[(?P<key>[A-Za-z0-9._-]+)\]")

# MarkupExtension usage (Avalonia): {converters:Localize dialog.connect.title} or {converters:Localize Key=dialog.connect.title}
XAML_LOCALIZE_POS_RE = re.compile(r"\bLocalize\s+(?P<key>[A-Za-z0-9._-]+)")
XAML_LOCALIZE_KV_RE = re.compile(r"\bLocalize\b[^\}]*\bKey\s*=\s*(?P<quote>\"|')(?P<key>[^\"']+)(?P=quote)")


def _is_probably_key(text: str) -> bool:
    return bool(KEY_TOKEN_RE.match(text))


def _load_en_us_keys(repo_root: Path) -> Set[str]:
    loc_service = repo_root / "src" / "Core" / "Services" / "LocalizationService.cs"
    if not loc_service.exists():
        raise FileNotFoundError(f"Localization service not found: {loc_service}")

    src = loc_service.read_text(encoding="utf-8")
    keys: Set[str] = set()
    for m in EN_DICT_KEY_RE.finditer(src):
        keys.add(m.group("key"))

    if not keys:
        raise RuntimeError("Failed to extract en-US keys from LocalizationService.cs")

    return keys


def _iter_shell_files(repo_root: Path) -> Iterable[Path]:
    shell_dir = repo_root / "src" / "Shell"
    if not shell_dir.exists():
        return []

    for file in shell_dir.rglob("*"):
        if file.is_dir():
            continue
        if any(part in ("bin", "obj") for part in file.parts):
            continue
        if file.suffix.lower() in (".cs", ".axaml"):
            yield file


def _extract_keys_from_line(file: Path, line_text: str) -> List[str]:
    keys: List[str] = []

    # C# patterns
    for rex in (GETSTRING_RE, L_INDEXER_RE, STRINGS_INDEXER_RE, LOCALIZATION_INDEXER_RE):
        for m in rex.finditer(line_text):
            k = m.group("key")
            if _is_probably_key(k):
                keys.append(k)

    # XAML patterns
    if file.suffix.lower() == ".axaml":
        for rex in (XAML_L_INDEXER_RE, XAML_LOCALIZATION_INDEXER_RE, XAML_LOCALIZE_POS_RE, XAML_LOCALIZE_KV_RE):
            for m in rex.finditer(line_text):
                k = m.group("key")
                if _is_probably_key(k):
                    keys.append(k)

    return keys


def main(argv: List[str]) -> int:
    repo_root = Path(argv[1]) if len(argv) > 1 else Path.cwd()

    en_keys = _load_en_us_keys(repo_root)

    missing: List[MissingKey] = []

    for file in _iter_shell_files(repo_root):
        try:
            lines = file.read_text(encoding="utf-8").splitlines()
        except UnicodeDecodeError:
            lines = file.read_text(encoding="utf-8", errors="replace").splitlines()

        for idx, line_text in enumerate(lines, start=1):
            for key in _extract_keys_from_line(file, line_text):
                if key not in en_keys:
                    missing.append(MissingKey(file=file, line=idx, key=key, context=line_text.strip()))

    if missing:
        print("[i18n] FAIL: Shell uses i18n keys missing from en-US resources", file=sys.stderr)
        for item in missing:
            rel = item.file.relative_to(repo_root)
            ctx = item.context
            if len(ctx) > 140:
                ctx = ctx[:137] + "..."
            print(f"- {rel}:{item.line} missing key '{item.key}'  ctx: {ctx}", file=sys.stderr)
        print("[i18n] Add missing keys to Core/Services/LocalizationService.GetEnglishTranslations().", file=sys.stderr)
        return 1

    print("[i18n] OK: All Shell-referenced i18n keys exist in en-US resources")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
