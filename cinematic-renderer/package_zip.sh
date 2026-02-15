#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."
zip -r cinematic-renderer.zip cinematic-renderer
printf 'Created %s/cinematic-renderer.zip\n' "$(pwd)"
