#!/usr/bin/env python3
"""Build, launch, and test a disposable Local preview in one process tree."""

import argparse
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    parser.add_argument("--skip-build", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    web = root / "src/MickMouse.Web"
    if not args.skip_build:
        subprocess.run([args.dotnet, "build", str(web / "MickMouse.Web.csproj"), "--nologo"], check=True, cwd=root)
    dll = web / "bin/Debug/net10.0/MickMouse.Web.dll"
    if not dll.is_file(): raise SystemExit("Debug build output is missing; rerun without --skip-build.")
    with socket.socket() as available:
        available.bind(("127.0.0.1", 0))
        port = available.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    with tempfile.TemporaryDirectory(prefix="mick-http-checks-") as temporary:
        data = Path(temporary)
        env = os.environ.copy()
        env.update({"App__Mode": "Local", "App__SeedDemo": "true", "App__DataDirectory": str(data),
                    "ASPNETCORE_ENVIRONMENT": "Development"})
        with (data / "server.log").open("w+") as log:
            server = subprocess.Popen([args.dotnet, "exec", str(dll), "--urls", base], cwd=web, env=env,
                                      stdout=log, stderr=subprocess.STDOUT)
            try:
                ready = False
                deadline = time.monotonic() + 35
                while time.monotonic() < deadline and server.poll() is None:
                    try:
                        with urllib.request.urlopen(base + "/health", timeout=1) as response:
                            if response.status == 200: ready = True; break
                    except Exception:
                        time.sleep(0.25)
                if not ready: raise RuntimeError("Disposable preview did not start")
                for script, extra in [("http_smoke.py", []), ("episode_flow.py", [str(data / "mick-mouse.db")]), ("account_deletion.py", [str(data / "mick-mouse.db")])]:
                    subprocess.run([sys.executable, str(root / "tests" / script), base, *extra], check=True, cwd=root)
                print("PASS All HTTP suites completed against a disposable Local preview.")
            except Exception:
                log.flush()
                print("SERVER LOG TAIL\n" + (data / "server.log").read_text()[-8000:], file=sys.stderr)
                raise
            finally:
                server.terminate()
                try: server.wait(timeout=10)
                except subprocess.TimeoutExpired: server.kill(); server.wait()


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"FAIL {error}", file=sys.stderr)
        sys.exit(1)
