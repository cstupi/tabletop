#!/bin/sh
# The bucket, a policy scoped to its objects, and the service user the servers sign with (never the root user). Idempotent:
# the compose `minio-init` service runs it on every start, in dev (docker-compose.yml) and prod (deploy/docker-compose.prod.yml).
# Inputs: MINIO_URL, MINIO_ROOT_USER, MINIO_ROOT_PASSWORD, S3_BUCKET, S3_APP_ACCESS_KEY, S3_APP_SECRET_KEY.
# s3:ListBucket on the bucket itself is there so a HEAD on a missing object answers 404, not 403 (S3BlobStore.Size).
set -eu
: "${MINIO_URL:?}" "${MINIO_ROOT_USER:?}" "${MINIO_ROOT_PASSWORD:?}" "${S3_BUCKET:?}" "${S3_APP_ACCESS_KEY:?}" "${S3_APP_SECRET_KEY:?}"
export MC_CONFIG_DIR="${MC_CONFIG_DIR:-/tmp/mc}"
policy="${S3_BUCKET}-objects"
mc alias set local "$MINIO_URL" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
mc mb --ignore-existing "local/$S3_BUCKET"
cat > "$MC_CONFIG_DIR/policy.json" <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"], "Resource": ["arn:aws:s3:::${S3_BUCKET}/*"] },
    { "Effect": "Allow", "Action": ["s3:ListBucket"], "Resource": ["arn:aws:s3:::${S3_BUCKET}"] }
  ]
}
EOF
mc admin policy create local "$policy" "$MC_CONFIG_DIR/policy.json"
mc admin user add local "$S3_APP_ACCESS_KEY" "$S3_APP_SECRET_KEY" >/dev/null
case "$(mc admin user info local "$S3_APP_ACCESS_KEY" --json)" in
  *"\"$policy\""*) ;;
  *) mc admin policy attach local "$policy" --user "$S3_APP_ACCESS_KEY" ;;
esac
echo "bucket $S3_BUCKET ready; service user $S3_APP_ACCESS_KEY holds $policy"

# The release publisher (scripts/release-client upload and builtin), when S3_RELEASE_ACCESS_KEY is set: Put under releases/
# and builtin/, and Get on releases/latest.json (release-client compares its source with the commit's to skip an unchanged
# client). The service user above already reads both (GetObject on the bucket's objects), which GET /v1/client/latest and
# GET /v1/builtin need.
if [ -n "${S3_RELEASE_ACCESS_KEY:-}" ]; then
  : "${S3_RELEASE_SECRET_KEY:?}"
  release_policy="${S3_BUCKET}-releases"
  cat > "$MC_CONFIG_DIR/release-policy.json" <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": ["s3:PutObject"], "Resource": ["arn:aws:s3:::${S3_BUCKET}/releases/*", "arn:aws:s3:::${S3_BUCKET}/builtin/*"] },
    { "Effect": "Allow", "Action": ["s3:GetObject"], "Resource": ["arn:aws:s3:::${S3_BUCKET}/releases/latest.json"] }
  ]
}
EOF
  mc admin policy create local "$release_policy" "$MC_CONFIG_DIR/release-policy.json"
  mc admin user add local "$S3_RELEASE_ACCESS_KEY" "$S3_RELEASE_SECRET_KEY" >/dev/null
  case "$(mc admin user info local "$S3_RELEASE_ACCESS_KEY" --json)" in
    *"\"$release_policy\""*) ;;
    *) mc admin policy attach local "$release_policy" --user "$S3_RELEASE_ACCESS_KEY" ;;
  esac
  echo "release user $S3_RELEASE_ACCESS_KEY holds $release_policy"
fi
