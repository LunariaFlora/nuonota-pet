"""Build a portable archive. Python is needed only for packaging, not for running the pet."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
destination = root / "exports" / "NuonotaPet-portable-v0.6.zip"
destination.parent.mkdir(exist_ok=True)
files = [root / "app" / "NuonotaPet.exe", root / "使用说明.txt", root / "assets" / "moon-spirit" / "idle.png"]
for folder in ("assets/original", "assets/model", "docs"):
    files.extend(path for path in (root / folder).rglob("*") if path.is_file())
files.extend(path for path in (root / "src").iterdir() if path.is_file())
with ZipFile(destination, "w", ZIP_DEFLATED, compresslevel=6) as archive:
    for path in files:
        archive.write(path, Path("NuonotaPet") / path.relative_to(root))
with ZipFile(destination) as archive:
    assert archive.testzip() is None
    assert "NuonotaPet/app/NuonotaPet.exe" in archive.namelist()
print(destination)
print(f"Verified: {destination.stat().st_size / 1024 / 1024:.1f} MB; {len(files)} files")
