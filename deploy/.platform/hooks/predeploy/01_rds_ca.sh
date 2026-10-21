#!/usr/bin/env bash
set -euo pipefail

# Beanstalk runs predeploy hooks as root. Preserve TLS verification for SQL Server
# by trusting only the self-signed RDS roots from AWS's official HTTPS bundle.
python3 - <<'PY'
from pathlib import Path
import hashlib
import re
import subprocess
import tempfile
import urllib.request

url = "https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem"
with urllib.request.urlopen(url, timeout=45) as response:
    bundle = response.read(2 * 1024 * 1024)
certificates = re.findall(rb"-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----", bundle, re.S)
anchors = Path("/etc/pki/ca-trust/source/anchors")
anchors.mkdir(parents=True, exist_ok=True)
count = 0
with tempfile.TemporaryDirectory(prefix="mick-rds-ca-") as temporary:
    for certificate in certificates:
        source = Path(temporary) / "certificate.pem"
        source.write_bytes(certificate + b"\n")
        result = subprocess.run(
            ["openssl", "x509", "-in", str(source), "-noout", "-subject", "-issuer", "-nameopt", "RFC2253"],
            check=True, capture_output=True, text=True,
        )
        names = dict(line.split("=", 1) for line in result.stdout.strip().splitlines())
        subject, issuer = names.get("subject", "").strip(), names.get("issuer", "").strip()
        if subject == issuer and "Amazon RDS" in subject:
            name = "mick-rds-root-" + hashlib.sha256(certificate).hexdigest()[:20] + ".pem"
            target = anchors / name
            target.write_bytes(certificate + b"\n")
            target.chmod(0o644)
            count += 1
if count == 0:
    raise SystemExit("No RDS root CA found in the official bundle; deployment stopped.")
subprocess.run(["update-ca-trust", "extract"], check=True)
print(f"Installed {count} RDS root CAs for verified SQL Server TLS.")
PY
