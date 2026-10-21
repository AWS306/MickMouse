#!/usr/bin/env python3
"""Create a Beanstalk deployment ZIP without creating any AWS resources.

Run from any directory: python scripts/publish-beanstalk.py
Requires .NET 10 SDK on PATH. Uses a temporary publish directory, so no previous
output or local App_Data can leak into the deployment bundle.
"""

import argparse
import json
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import zipfile


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    parser.add_argument("--output", type=Path, default=root / "artifacts" / "mick-mouse-beanstalk.zip")
    args = parser.parse_args()
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    project = root / "src" / "MickMouse.Web" / "MickMouse.Web.csproj"
    with tempfile.TemporaryDirectory(prefix="mick-publish-") as temporary:
        publish = Path(temporary) / "publish"
        subprocess.run([args.dotnet, "publish", str(project), "-c", "Release", "-o", str(publish)], check=True)
        shutil.copyfile(root / "deploy" / "Procfile", publish / "Procfile")
        shutil.copytree(root / "deploy" / ".platform", publish / ".platform")
        settings_path = publish / "appsettings.json"
        settings = json.loads(settings_path.read_text(encoding="utf-8"))
        settings["App"]["Mode"] = "Aws"
        settings["App"]["SeedDemo"] = False
        settings_path.write_text(json.dumps(settings, indent=2) + "\n", encoding="utf-8")
        if (publish / "App_Data").exists():
            shutil.rmtree(publish / "App_Data")
        for required in ("MickMouse.Web.dll", "MickMouse.Web.runtimeconfig.json", "media-catalog.json", "Procfile"):
            if not (publish / required).is_file():
                raise RuntimeError(f"Missing publish output: {required}")
        for hook in (publish / ".platform").rglob("*.sh"):
            hook.write_bytes(hook.read_bytes().replace(b"\r\n", b"\n"))
            hook.chmod(0o755)
        with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for source in sorted(publish.rglob("*")):
                if not source.is_file():
                    continue
                relative = source.relative_to(publish).as_posix()
                info = zipfile.ZipInfo(relative)
                info.create_system = 3
                mode = 0o755 if relative.endswith(".sh") else 0o644
                info.external_attr = (stat.S_IFREG | mode) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, source.read_bytes())
        with zipfile.ZipFile(output) as archive:
            bad = archive.testzip()
            if bad:
                raise RuntimeError(f"ZIP validation failed: {bad}")
            names = archive.namelist()
            if "Procfile" not in names or "MickMouse.Web.dll" not in names:
                raise RuntimeError("Deployment files must be at ZIP root.")
    print(f"Created {output} ({output.stat().st_size:,} bytes). No AWS resources were changed.")


if __name__ == "__main__":
    main()
