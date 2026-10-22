"""Puts the current app version and the headline of the release notes into site/index.html before deploy.

The version comes from APP_VERSION in .github/workflows/build.yml (the same value the release is built with),
the headline from the first bold item under «Что нового» in .github/release-notes.md.
"""
import html
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[2]
build = (root / ".github/workflows/build.yml").read_text(encoding="utf-8")
notes = (root / ".github/release-notes.md").read_text(encoding="utf-8")
page_path = root / "site/index.html"
page = page_path.read_text(encoding="utf-8")

m = re.search(r'APP_VERSION:\s*"(\d+\.\d+\.\d+)"', build)
if not m:
    sys.exit("APP_VERSION not found in build.yml")
version = m.group(1)
# 1.3.0 -> 1.3, 1.2.1 stays 1.2.1
short = version[:-2] if version.endswith(".0") else version

h = re.search(r"\*\*(.+?)\*\*", notes.split("## Что было")[0])
headline = h.group(1).strip().rstrip(".") if h else ""

page = re.sub(r'(<span class="ver">)[^<]*(</span>)', lambda x: x.group(1) + short + x.group(2), page)
if headline:
    page = re.sub(r'(<span class="ver-note">)[^<]*(</span>)', lambda x: x.group(1) + html.escape(headline) + x.group(2), page)

page_path.write_text(page, encoding="utf-8")
print(f"site: version {short}, headline: {headline}")
