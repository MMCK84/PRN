#!/bin/sh
# Baut dist/BilderUmbenenner.exe (benötigt Mono: apt install mono-mcs libmono-system-windows-forms4.0-cil)
set -e
cd "$(dirname "$0")"
mkdir -p dist
mcs -target:winexe -codepage:utf8 -optimize+ \
    -r:System.Windows.Forms.dll -r:System.Drawing.dll \
    -out:dist/BilderUmbenenner.exe src/*.cs
