# The client-release builder: Godot 4.7.2 .NET (headless, Linux x86_64) with its Windows, Linux and macOS release
# templates, the .NET 10 SDK, zip, jq and the AWS CLI. scripts/release-client runs its export, package and upload steps in
# this image when Godot is not on PATH (or with --in-container), tagged tabletop-client-build:<GODOT_VERSION>.
# Every download is pinned and checked: Godot's against the release's SHA512-SUMS.txt, the AWS CLI's against its SHA-256.
# No credentials are baked in; the upload step receives RELEASE_S3_* at run time.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317

ARG GODOT_VERSION=4.7.2
ARG GODOT_SHA512=1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13
ARG TEMPLATES_SHA512=bb5c41d72370ed743660361f6228006f808ab04ca33abdc545d740b044f3fe057f32ae8cb7873a1bc86ddcd82ae683b9f6dfdfe4179852f2c0f1acde2ff6bd5a
ARG AWSCLI_VERSION=2.36.30
ARG AWSCLI_SHA256=a0a4f1dd3fe547f7be0569d127b3adb77646e232ef69820aa79edf4f7ea2a5a2

RUN apt-get update \
 && apt-get install -y --no-install-recommends zip unzip jq git ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

# Godot reads templates from $XDG_DATA_HOME/godot/export_templates/<version>.stable.mono. The container runs as the
# clone's owner (any uid), so the data dir is world-writable (sticky) and config, cache and HOME are under /tmp.
ENV XDG_DATA_HOME=/opt/godot/data XDG_CONFIG_HOME=/tmp/godot-config XDG_CACHE_HOME=/tmp/godot-cache \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 HOME=/tmp

# The templates first, in their own layer: 1.2 GB down, the three release templates kept.
RUN set -eu; tpl=/opt/godot/data/godot/export_templates/${GODOT_VERSION}.stable.mono; mkdir -p "$tpl" /tmp/t; cd /tmp/t \
 && curl -fsSL -o t.tpz "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_export_templates.tpz" \
 && echo "${TEMPLATES_SHA512}  t.tpz" | sha512sum -c - \
 && unzip -q t.tpz templates/version.txt templates/linux_release.x86_64 templates/windows_release_x86_64.exe templates/macos.zip \
 && mv templates/* "$tpl/" && chmod 0644 "$tpl"/* \
 && grep -qx "${GODOT_VERSION}.stable.mono" "$tpl/version.txt" \
 && chmod 1777 /opt/godot/data /opt/godot/data/godot && cd / && rm -rf /tmp/t

RUN set -eu; mkdir /tmp/g; cd /tmp/g \
 && curl -fsSL -o g.zip "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip" \
 && echo "${GODOT_SHA512}  g.zip" | sha512sum -c - \
 && unzip -q g.zip && mv "Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64" /opt/godot/editor \
 && ln -s "/opt/godot/editor/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64" /usr/local/bin/godot \
 && cd / && rm -rf /tmp/g && godot --headless --version

RUN set -eu; mkdir /tmp/a; cd /tmp/a \
 && curl -fsSL -o a.zip "https://awscli.amazonaws.com/awscli-exe-linux-x86_64-${AWSCLI_VERSION}.zip" \
 && echo "${AWSCLI_SHA256}  a.zip" | sha256sum -c - \
 && unzip -q a.zip && ./aws/install && cd / && rm -rf /tmp/a && aws --version

WORKDIR /tmp
