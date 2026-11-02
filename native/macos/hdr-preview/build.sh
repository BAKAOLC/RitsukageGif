#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
arch="${1:-$(uname -m)}"
out="${2:-build}"
case "$arch" in arm64|x86_64) ;; *) echo "Unsupported architecture: $arch" >&2; exit 1;; esac
mkdir -p "$out"
xcrun -sdk macosx metal -c -mmacosx-version-min=13.0 HdrPreview.metal -o "$out/HdrPreview.air"
xcrun -sdk macosx metallib "$out/HdrPreview.air" -o "$out/RitsukageHdrPreview.metallib"
xcrun swiftc -swift-version 5 -parse-as-library -O -emit-library -module-name RitsukageHdrPreview \
  -target "${arch}-apple-macosx13.0" -framework AppKit -framework Metal -framework QuartzCore \
  -Xlinker -install_name -Xlinker @rpath/libRitsukageHdrPreview.dylib \
  HdrPreview.swift -o "$out/libRitsukageHdrPreview.dylib"
