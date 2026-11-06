"""核验 Android SQLite 原生包与 APK 的版本、依赖、对齐及来源一致性。"""

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import struct
import subprocess
import zipfile


def inspect_elf(data):
    if data[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", data, 18)[0] != 183:
        raise ValueError("SQLite 库必须为小端 arm64 ELF64")
    offset = struct.unpack_from("<Q", data, 32)[0]
    size, count = struct.unpack_from("<HH", data, 54)
    headers = [struct.unpack_from("<IIQQQQQQ", data, offset + i * size) for i in range(count)]
    loads = [header for header in headers if header[0] == 1]
    if not loads or any(h[7] < 16384 or h[2] % 16384 != h[3] % 16384 for h in loads):
        raise ValueError("SQLite ELF LOAD 段不满足 16KB 对齐")
    dynamic = next(h for h in headers if h[0] == 2)
    entries = []
    for offset in range(dynamic[2], dynamic[2] + dynamic[5], 16):
        tag, value = struct.unpack_from("<qQ", data, offset)
        if tag == 0:
            break
        entries.append((tag, value))
    address = next(value for tag, value in entries if tag == 5)
    segment = next(h for h in loads if h[3] <= address < h[3] + h[5])
    strings = segment[2] + address - segment[3]
    needed = []
    for tag, value in entries:
        if tag == 1:
            start = strings + value
            needed.append(data[start:data.index(b"\0", start)].decode("ascii"))
    if not needed or set(needed) - {"libc.so", "libm.so", "libdl.so", "liblog.so", "libstdc++.so"}:
        raise ValueError(f"SQLite 含非预期 Android 动态依赖：{needed}")
    versions = set(re.findall(rb"(?<![0-9.])(3\.[0-9]+\.[0-9]+)\x00", data))
    if len(versions) != 1:
        raise ValueError(f"无法唯一识别 SQLite 版本：{versions}")
    version = versions.pop().decode("ascii")
    if tuple(map(int, version.split("."))) < (3, 50, 2):
        raise ValueError(f"SQLite {version} 低于安全基线 3.50.2")
    return {"sqliteVersion": version, "needed": needed,
            "loadAlignments": [h[7] for h in loads],
            "sha256": hashlib.sha256(data).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--apk", type=Path)
    parser.add_argument("--zipalign", type=Path)
    args = parser.parse_args()
    if args.apk and not args.zipalign:
        parser.error("检查 APK 时必须提供 --zipalign")
    package = args.package.read_bytes()
    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        aars = [name for name in archive.namelist() if name.endswith(".aar")]
        if len(aars) != 1:
            raise ValueError("原生包必须包含唯一 AAR")
        with zipfile.ZipFile(io.BytesIO(archive.read(aars[0]))) as aar:
            native = aar.read("jni/arm64-v8a/libe_sqlite3.so")
    result = {"packageSha256": hashlib.sha256(package).hexdigest(), "native": inspect_elf(native)}
    if args.apk:
        with zipfile.ZipFile(args.apk) as apk:
            libraries = [name for name in apk.namelist() if name.endswith("/libe_sqlite3.so")]
            if libraries != ["lib/arm64-v8a/libe_sqlite3.so"]:
                raise ValueError(f"APK SQLite 资产集合不符合单 arm64 约束：{libraries}")
            packaged = apk.read(libraries[0])
        if packaged != native:
            raise ValueError("APK 内 SQLite 与指定 NuGet 原生资产不同")
        alignment = subprocess.run([str(args.zipalign), "-c", "-P", "16", "4", str(args.apk)],
                                   capture_output=True, text=True)
        if alignment.returncode:
            raise ValueError(f"APK ZIP 对齐失败：{alignment.stdout}{alignment.stderr}")
        result.update(apkSha256=hashlib.sha256(args.apk.read_bytes()).hexdigest(),
                      apkNativeMatchesPackage=True, zipAlignmentPassed=True)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
