# Railway pre-deploy step: applies pending EF Core migrations as the rideforge_migrator role before
# the new API version takes traffic. Any non-zero exit stops the deploy and leaves the previous
# version serving — a broken migration never takes the API down.
#
# Wired up in the Railway dashboard, not in the repo (Config as Code is deprecated and was never
# enabled for this service): API service → Settings → Deploy → Pre-deploy Command:
#     /bin/sh /app/migrate.sh
#
# Run with /bin/sh explicitly: Railway starts Dockerfile commands in exec form, which expands no
# variables, and the Dockerfile strips CRLF so a Windows checkout cannot break this file.
set -eu

# The runtime role (ConnectionStrings__RideForge) has no DDL rights by design. Refuse loudly here
# rather than let the bundle fall back to some other connection and fail on permissions.
if [ -z "${ConnectionStrings__RideForgeMigrations:-}" ]; then
    echo "ConnectionStrings__RideForgeMigrations is not set; refusing to deploy without migrating." >&2
    exit 1
fi

exec "$(dirname "$0")/efbundle" --connection "$ConnectionStrings__RideForgeMigrations"
