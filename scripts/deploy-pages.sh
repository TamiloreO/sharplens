#!/usr/bin/env bash
# Publish the site to the gh-pages branch.
#
# GitHub Pages serves this repository from that branch. The build output is
# pushed as plain files because Pages will not serve the .br and .gz copies,
# and .nojekyll keeps the _framework folder from being skipped.
#
# Usage: scripts/deploy-pages.sh

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

remote="$(git remote get-url origin)"
echo "publishing to $remote (gh-pages)"

dotnet publish src/Sharplens.Web -c Release -o publish > /dev/null

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

cp -R publish/wwwroot/. "$staging/"
find "$staging" -name "*.br" -delete
find "$staging" -name "*.gz" -delete
find "$staging" -name "*.pdb" -delete
touch "$staging/.nojekyll"

cd "$staging"
git init -q
git checkout -q -b gh-pages
git add -A
git -c user.name="TamiloreO" \
    -c user.email="75487658+TamiloreO@users.noreply.github.com" \
    commit -q -m "deploy: publish the site to gh-pages"
git push -q --force "$remote" gh-pages

echo "done. size: $(du -sh "$staging" | cut -f1)"
