# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Render a pinned help DOCX headlessly and reject missing or incomplete PDF output."""

import argparse
import hashlib
import io
import json
import os
import signal
from pathlib import Path
import subprocess
import sys
import tempfile
import unicodedata
from zipfile import ZipFile

from pypdf import PdfReader

from build_help import NS, xml


def normalized(value):
    return "".join(unicodedata.normalize("NFKC", value).replace("\u00ad", "").split())


def verify_pdf(docx_bytes, pdf):
    reader = PdfReader(pdf, strict=True)
    if reader.is_encrypted or not 1 <= len(reader.pages) <= 64:
        raise ValueError("Help PDF must be readable and contain 1 to 64 pages")
    if reader.attachments:
        raise ValueError("Help PDF must not contain embedded attachments")
    actual = normalized("\n".join(page.extract_text() or "" for page in reader.pages))
    with ZipFile(io.BytesIO(docx_bytes)) as archive:
        document = xml(archive.read("word/document.xml"))
    expected = [normalized(value) for value in document.xpath("//w:t/text()", namespaces=NS) if value.strip()]
    if not expected or any(value not in actual for value in expected):
        raise ValueError("Help PDF text does not contain all source document text")
    return len(reader.pages)


def run_process(arguments, timeout):
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    with subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          creationflags=flags, start_new_session=os.name != "nt") as process:
        try:
            stdout, stderr = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            # Stop only this renderer process tree before removing its private scratch files.
            if os.name == "nt":
                taskkill = Path(os.environ["SystemRoot"]) / "System32" / "taskkill.exe"
                subprocess.run([str(taskkill), "/PID", str(process.pid), "/T", "/F"],
                               capture_output=True, timeout=15, creationflags=flags)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.kill()
            process.communicate(timeout=15)
            raise
        return subprocess.CompletedProcess(arguments, process.returncode, stdout, stderr)


def render(docx, expected_digest, soffice, output_directory, timeout=120):
    docx, soffice, output_directory = map(lambda p: Path(p).resolve(), (docx, soffice, output_directory))
    if not docx.is_file() or docx.suffix.lower() != ".docx" or not soffice.is_file():
        raise ValueError("Provide an existing DOCX and an explicit LibreOffice executable")
    if not 1 <= timeout <= 600:
        raise ValueError("Rendering timeout must be 1 to 600 seconds")
    source = docx.read_bytes()
    digest = hashlib.sha256(source).hexdigest()
    if digest != expected_digest:
        raise ValueError("Help DOCX differs from the pinned build output")
    output_directory.mkdir(parents=True, exist_ok=True)
    output = output_directory / (docx.stem + ".pdf")
    if output.exists():
        raise ValueError("PDF output already exists; use a fresh output directory")
    # Deep MSBuild output paths can exceed the external renderer's path limits.
    # Keep only a short private working copy/profile in the system temporary directory;
    # verified output is still written to the caller's requested destination.
    temporary_root = Path(tempfile.gettempdir()).resolve()
    with tempfile.TemporaryDirectory(prefix="ch-help-", dir=temporary_root) as scratch:
        work = Path(scratch).resolve()
        if not work.is_relative_to(temporary_root):
            raise ValueError("Renderer scratch directory escaped its temporary root")
        candidate = work / "help.docx"
        candidate.write_bytes(source)
        profile = (work / "profile").as_uri()
        common = [str(soffice), "-env:UserInstallation=" + profile, "--headless", "--nologo", "--norestore"]
        version = run_process(common + ["--version"], min(timeout, 30))
        if version.returncode != 0:
            raise ValueError("Cannot determine the LibreOffice renderer version")
        version_text = version.stdout.decode("utf-8", errors="replace").strip()
        if not version_text.startswith("LibreOffice ") or len(version_text) > 256:
            raise ValueError("Unexpected renderer version response")
        result = run_process(common + ["--convert-to", "pdf:writer_pdf_Export", "--outdir", str(work), str(candidate)], timeout)
        converted = work / "help.pdf"
        if result.returncode != 0 or not converted.is_file():
            raise ValueError("LibreOffice did not produce the expected help PDF")
        pages = verify_pdf(source, converted)
        data = converted.read_bytes()
        with output.open("xb") as destination:
            destination.write(data)
    return {"schemaVersion": 1, "docxSha256": digest, "pdfSha256": hashlib.sha256(data).hexdigest(),
            "rendererVersion": version_text, "pageCount": pages, "sourceTextVerified": True,
            "visualReviewRequired": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("docx", "docx-sha256", "soffice", "output-directory"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--timeout", type=int, default=120)
    args = parser.parse_args()
    try:
        print(json.dumps(render(args.docx, args.docx_sha256, args.soffice, args.output_directory, args.timeout), indent=2))
        return 0
    except (ValueError, OSError, subprocess.SubprocessError) as error:
        print(f"Help rendering failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
