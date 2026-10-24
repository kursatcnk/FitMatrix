# Build context is the repository root (see docker-compose.yml).
FROM node:24-alpine AS build
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
# The checked-in .npmrc points at a local D: cache; the image uses its own.
RUN npm ci --cache /tmp/npm-cache --no-audit --no-fund
COPY frontend/ ./
RUN npm run build -- --logLevel warn

FROM nginx:1.27-alpine
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
