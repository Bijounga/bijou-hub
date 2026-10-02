# Builds the Stream Deck plugin and packs it into dist/com.bijounga.bijouhub.streamDeckPlugin.
# BijouHub.csproj embeds that file (when present) so the app's Stream Deck window can install it,
# so run this before publishing BijouHub.
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path node_modules)) { npm ci }
    npm run build
    npx streamdeck validate com.bijounga.bijouhub.sdPlugin
    npx streamdeck pack com.bijounga.bijouhub.sdPlugin --output dist --force --no-update-check
}
finally {
    Pop-Location
}
