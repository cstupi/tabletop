#!/usr/bin/env bash
# The production database roles, run once as a superuser on the database the servers use. Idempotent: an existing role keeps
# its password. deploy/docker-compose.prod.yml mounts it as the Postgres image's init script; on a host, run it as postgres:
#   sudo -u postgres env POSTGRES_DB=tabletop TABLETOP_DB_OWNER_PASSWORD=... TABLETOP_DB_MANAGEMENT_PASSWORD=... \
#     TABLETOP_DB_GAME_PASSWORD=... scripts/postgres/roles.sh
#
#   tabletop_owner       owns the database and every object in it; only `Tabletop.Management --migrate` connects as it.
#   tabletop_management  runtime, member of tabletop_runtime.
#   tabletop_game        runtime, member of tabletop_runtime.
#   tabletop_runtime     NOLOGIN: CONNECT on the database, USAGE on the owner's schemas, SELECT/INSERT/UPDATE/DELETE on its tables
#                        and USAGE/SELECT on its sequences, all through default privileges, so a later migration's table is covered.
# The dev stack (docker-compose.yml) does not run this: its servers connect as the superuser `tabletop`.
set -euo pipefail
: "${POSTGRES_DB:?}" "${TABLETOP_DB_OWNER_PASSWORD:?}" "${TABLETOP_DB_MANAGEMENT_PASSWORD:?}" "${TABLETOP_DB_GAME_PASSWORD:?}"
psql_args=(-v ON_ERROR_STOP=1 --no-psqlrc --dbname "$POSTGRES_DB")
[[ -n "${POSTGRES_USER:-}" ]] && psql_args+=(--username "$POSTGRES_USER")
psql "${psql_args[@]}" \
  -v db="$POSTGRES_DB" \
  -v owner_password="$TABLETOP_DB_OWNER_PASSWORD" \
  -v management_password="$TABLETOP_DB_MANAGEMENT_PASSWORD" \
  -v game_password="$TABLETOP_DB_GAME_PASSWORD" <<'SQL'
select format('create role tabletop_owner login password %L', :'owner_password')
  where not exists (select from pg_roles where rolname = 'tabletop_owner') \gexec
select 'create role tabletop_runtime nologin'
  where not exists (select from pg_roles where rolname = 'tabletop_runtime') \gexec
select format('create role tabletop_management login password %L in role tabletop_runtime', :'management_password')
  where not exists (select from pg_roles where rolname = 'tabletop_management') \gexec
select format('create role tabletop_game login password %L in role tabletop_runtime', :'game_password')
  where not exists (select from pg_roles where rolname = 'tabletop_game') \gexec

-- The owner owns the database, so it may create schemas, and (Postgres 15+) the public schema, where DbUp keeps its journal.
alter database :"db" owner to tabletop_owner;
revoke all on database :"db" from public;
grant connect on database :"db" to tabletop_runtime;
revoke create on schema public from public;

alter default privileges for role tabletop_owner grant usage on schemas to tabletop_runtime;
alter default privileges for role tabletop_owner grant select, insert, update, delete on tables to tabletop_runtime;
alter default privileges for role tabletop_owner grant usage, select on sequences to tabletop_runtime;
SQL
echo "roles ready on $POSTGRES_DB"
