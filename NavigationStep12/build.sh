#!/usr/bin/env bash
set -euo pipefail
if [[ $# != 1 ]]; then
    echo 'Usage: bash NavigationStep12/build.sh /path/to/vmangos-core/dep/recastnavigation/Detour' >&2
    exit 2
fi
detour_root=$(realpath "$1")
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$detour_root"
sha256sum --check --quiet "$repo_root/NavigationStep12/native/detour.sha256"
cd "$repo_root"
cmake -S NavigationStep12/native -B obj/navigation-native -DDETOUR_ROOT="$detour_root" -DCMAKE_BUILD_TYPE=Release
cmake --build obj/navigation-native --parallel 4
ctest --test-dir obj/navigation-native --output-on-failure
env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj
