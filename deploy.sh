#!/bin/bash

set -e

DOCKER_ENV_DIR="/mnt/Server/Docker/jellyfin-testing"
PLUGIN_TARGET_DIR="$DOCKER_ENV_DIR/jellyfin/plugins/OpenID-Connect"
CONTAINER_NAME="jellyfin_testing"

mkdir -p ./artifacts
jprm plugin build

ZIP_FILE=$(ls -t ./artifacts/*.zip | head -1)

if [ -z "$ZIP_FILE" ]; then
    echo "cant find file"
    exit 1
fi

sudo mkdir -p "$PLUGIN_TARGET_DIR"
sudo unzip -o -q "$ZIP_FILE" -d "$PLUGIN_TARGET_DIR"
sudo chown -R 1000:1000 "$PLUGIN_TARGET_DIR"

docker compose -f "$DOCKER_ENV_DIR/docker-compose.yml" restart "$CONTAINER_NAME"

echo "done"