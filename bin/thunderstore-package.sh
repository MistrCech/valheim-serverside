#!/usr/bin/env bash

set -euo pipefail

SS_VERSION=$(grep -oP '(?<=PluginVersion = ")\d+\.\d+\.\d+(?=";)' src/Valheim_Serverside/ServersidePlugin.cs)

cat > manifest.json <<- EOM
{
  "name": "SarkasticEU_Dedicated_Simulation",
  "description": "Run world and monster simulations on a dedicated server. Fork of Serverside Simulations, updated for Valheim 1.0.",
  "version_number": "$SS_VERSION",
  "dependencies": ["denikson-BepInExPack_Valheim-5.4.2350"],
  "website_url": "https://github.com/MistrCech/valheim-serverside"
}
EOM

zip thunderstore-package.zip SarkasticEU_Dedicated_Simulation.dll icon.png manifest.json README.md CHANGELOG.md
