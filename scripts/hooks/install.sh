#!/bin/sh
# Enable the versioned git hooks in scripts/hooks/ for this clone
# (idempotent — safe to re-run after every fresh clone).
cd "$(dirname "$0")/../.." || exit 1
git config core.hooksPath scripts/hooks
echo "pre-push gate enabled (core.hooksPath = scripts/hooks)"
