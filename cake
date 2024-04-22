#!/usr/bin/env bash
set -eou pipefail

cd -- "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")"

dotnet tool restore
dotnet cake build.cake -t "${1:-default}" "${@:2}"
