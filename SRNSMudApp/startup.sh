#!/bin/sh
set -e

cd /home/site/wwwroot

if [ -f "./SRNSMudApp" ]; then
    chmod +x ./SRNSMudApp 2>/dev/null || true
    echo "Starting self-contained executable SRNSMudApp..."
    exec ./SRNSMudApp
elif [ -f "./SRNSMudApp.dll" ]; then
    echo "Starting with dotnet runtime SRNSMudApp.dll..."
    exec dotnet ./SRNSMudApp.dll
else
    echo "Error: Neither SRNSMudApp nor SRNSMudApp.dll found in /home/site/wwwroot" >&2
    ls -la /home/site/wwwroot >&2
    exit 1
fi


