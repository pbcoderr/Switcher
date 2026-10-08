# sing-box 1.14.2 — bundled component

Copyright (C) 2022 nekohasekai and contributors.
The original notice is in LICENSE; the full GNU GPL v3 text is in COPYING.
Keep these files with the component when sharing a Switcher build.

The files sing-box.exe and libcronet.dll are unmodified files from the official
Windows amd64 release. The original archive is embedded in Switcher.exe and
these two files are extracted automatically. Switcher starts sing-box as a
separate process.

Release and official downloads:
https://github.com/SagerNet/sing-box/releases/tag/v1.14.2

Original archive: sing-box-1.14.2-windows-amd64.zip
SHA256: c2d8bfff918755808781dfdeeb8581b6c91eb3a243d9a7b55483cfc0c0684d32

Corresponding sing-box source, including build scripts and dependency versions:
SOURCE.zip in this directory, exported from the unmodified v1.14.2 tag.
Commit: af6e64c3b69e6132ebaee0e1a3d24e93903f6709
Upstream source: https://github.com/SagerNet/sing-box/tree/v1.14.2
Source archive: https://github.com/SagerNet/sing-box/archive/refs/tags/v1.14.2.zip
Build instructions: https://sing-box.sagernet.org/installation/build-from-source/

The source tree contains go.mod and go.sum identifying Go dependencies and
the release build configuration. Their original licenses continue to apply.
The bundled libcronet.dll is an upstream optional runtime dependency; the
Tailscale routing configuration in Switcher does not use the Naive protocol.
Its source and build instructions are maintained at:
https://github.com/SagerNet/cronet-go/tree/0d28acc44093
See also the dependency versions and submodule references in the source tree.

Switcher is by pb_coder. It is not an official SagerNet product.
