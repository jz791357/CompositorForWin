#!/bin/sh
# Portable test gate for non-Windows dev machines (macOS).
#
# The .NET/WPF suite only builds on Windows (see scripts/test.ps1 and CI), so
# this gate runs everything that IS portable:
#   1. compile-check every native C kernel (catches the class of breakage CI
#      would catch, before the push)
#   2. syntax-check the repo's Node helper scripts
# The pre-push hook calls this on macOS machines. Exit code is non-zero on
# any failure. Bypass for WIP pushes: git push --no-verify
set -e

repo=$(cd "$(dirname "$0")/.." && pwd)

# Pick the first compiler that can actually compile real code (the /usr/bin
# shims break while an Xcode update awaits its license; the CommandLineTools
# clang then needs an explicit -isysroot to see the SDK headers).
probe=$(mktemp -t compositor-gate).c
echo '#include <math.h>
int probe(void){return M_PI > 0;}' > "$probe"
clt_sdk=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk
cc=""
cflags=""
for candidate in \
    clang \
    /Library/Developer/CommandLineTools/usr/bin/clang \
    cc gcc
do
    command -v "$candidate" >/dev/null 2>&1 || continue
    if "$candidate" -c "$probe" -o /dev/null >/dev/null 2>&1; then
        cc=$candidate
        break
    fi
    if [ -d "$clt_sdk" ] && \
       "$candidate" -isysroot "$clt_sdk" -c "$probe" -o /dev/null >/dev/null 2>&1; then
        cc=$candidate
        cflags="-isysroot $clt_sdk"
        break
    fi
done
rm -f "$probe"
if [ -z "$cc" ]; then
    echo "error: no usable C compiler (run 'sudo xcodebuild -license accept' or" >&2
    echo "       install Command Line Tools: xcode-select --install)" >&2
    exit 1
fi

# gnu11 (not c11): keeps M_PI & friends visible the way MSVC sees them.
echo "== compile-checking native C kernels ($cc) =="
for src in "$repo"/src/Compositor.Native/c/*.c; do
    "$cc" -std=gnu11 -Wall $cflags -c "$src" -o /dev/null
done
echo "C kernels: OK"

if command -v node >/dev/null 2>&1; then
    echo "== syntax-checking node scripts =="
    for js in "$repo"/scripts/*.mjs; do
        node --check "$js"
    done
    echo "node scripts: OK"
fi

# The .NET logic suite (Compositor.Core + tests) is portable since M1.1: run it
# whenever a dotnet SDK is installed (~/.dotnet or PATH).
dotnet_bin=$(command -v dotnet || true)
[ -z "$dotnet_bin" ] && [ -x "$HOME/.dotnet/dotnet" ] && dotnet_bin="$HOME/.dotnet/dotnet"
if [ -n "$dotnet_bin" ]; then
    echo "== running .NET logic tests ($dotnet_bin) =="
    "$dotnet_bin" test "$repo/src/Compositor.Tests/Compositor.Tests.csproj" -c Release
    echo "dotnet tests: OK"
else
    echo "note: dotnet SDK not found — skipped .NET tests (install: curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0)"
fi

echo "macOS gate passed (full suite runs on Windows / CI)."
