#!/usr/bin/env sh
# Downloads the Tailwind CSS standalone CLI into .tools/ (no Node needed) and checks it against
# the release's SHA-256. Once it is there, building the project regenerates
# PomodoroRelax4/wwwroot/css/tailwind.css (see the TailwindCss target in the .csproj).
#   sh tools/get-tailwind.sh
# Runs on Linux (the container build) and in Git Bash on Windows.
set -eu

version=v3.4.17
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)            asset=tailwindcss-linux-x64;       sha=7d24f7fa191d2193b78cd5f5a42a6093e14409521908529f42d80b11fde1f1d4; out=tailwindcss ;;
  Linux-aarch64)           asset=tailwindcss-linux-arm64;     sha=69b1378b8133192d7d2feb12a116fa12d035594f58db3eff215879e4ad8cf39b; out=tailwindcss ;;
  MINGW*-x86_64|MSYS*-x86_64) asset=tailwindcss-windows-x64.exe; sha=67f1c5e3f5a03406a7bf5badf5ada09b79f3ae78ec43450c15f7e983068da346; out=tailwindcss.exe ;;
  *) echo "No pinned Tailwind CLI for $(uname -s)-$(uname -m); add it to tools/get-tailwind.sh" >&2; exit 1 ;;
esac

dir="$(cd "$(dirname "$0")/.." && pwd)/.tools"
mkdir -p "$dir"
target="$dir/$out"

if [ -f "$target" ] && echo "$sha  $target" | sha256sum -c --status; then
  echo "Tailwind CLI $version already in $target"
  exit 0
fi

curl -fsSL -o "$target.download" "https://github.com/tailwindlabs/tailwindcss/releases/download/$version/$asset"
echo "$sha  $target.download" | sha256sum -c --quiet
mv "$target.download" "$target"
chmod +x "$target"
echo "Tailwind CLI $version saved to $target"
