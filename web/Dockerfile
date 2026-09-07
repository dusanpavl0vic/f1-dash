# syntax=docker/dockerfile:1

# ---- build ----------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Vite inlines these at build time, so they are build args rather than runtime
# environment variables.
ARG VITE_LIVE_URL=ws://localhost:4000/ws
ARG VITE_API_URL=http://localhost:4000
ENV VITE_LIVE_URL=$VITE_LIVE_URL VITE_API_URL=$VITE_API_URL
RUN npm run build

# ---- runtime --------------------------------------------------------------
FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 3000
HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
  CMD wget -qO- http://127.0.0.1:3000/ >/dev/null || exit 1
