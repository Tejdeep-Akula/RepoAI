#!/usr/bin/env sh
set -eu

: "${REPOAI_IMAGE_NAMESPACE:?Set REPOAI_IMAGE_NAMESPACE to the approved Docker Hub namespace}"
: "${REPOAI_IMAGE_TAG:?Set REPOAI_IMAGE_TAG to the approved release version}"
: "${REPOAI_GOOGLE_CLIENT_ID:?Set REPOAI_GOOGLE_CLIENT_ID for the production frontend build}"

platforms="${REPOAI_IMAGE_PLATFORMS:-linux/amd64,linux/arm64}"
dotnet_image="${REPOAI_IMAGE_NAMESPACE}/repoai-api:${REPOAI_IMAGE_TAG}"
worker_image="${REPOAI_IMAGE_NAMESPACE}/repoai-worker:${REPOAI_IMAGE_TAG}"
frontend_image="${REPOAI_IMAGE_NAMESPACE}/repoai-frontend:${REPOAI_IMAGE_TAG}"

docker buildx build \
    --platform "$platforms" \
    --file Dockerfile.dotnet \
    --build-arg APP_PROJECT=RepoAI.Api \
    --tag "$dotnet_image" \
    --push .

docker buildx build \
    --platform "$platforms" \
    --file Dockerfile.dotnet \
    --build-arg APP_PROJECT=RepoAI.Worker \
    --tag "$worker_image" \
    --push .

docker buildx build \
    --platform "$platforms" \
    --file Dockerfile.frontend \
    --build-arg VITE_API_BASE_URL= \
    --build-arg "VITE_GOOGLE_CLIENT_ID=${REPOAI_GOOGLE_CLIENT_ID}" \
    --tag "$frontend_image" \
    --push .

printf 'Published %s, %s, and %s for %s\n' \
    "$dotnet_image" "$worker_image" "$frontend_image" "$platforms"
