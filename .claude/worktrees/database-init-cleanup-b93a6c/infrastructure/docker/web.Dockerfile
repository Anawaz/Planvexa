# syntax=docker/dockerfile:1.7
FROM node:24-alpine AS deps
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY apps/web/package*.json ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi

FROM node:24-alpine AS builder
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY apps/web ./
RUN mkdir -p public && npm run build

FROM node:24-alpine AS runner
WORKDIR /app
LABEL org.opencontainers.image.title="Planvexa Web" \
      org.opencontainers.image.vendor="Planvexa contributors" \
      org.opencontainers.image.source="https://github.com/Anawaz/Planvexa" \
      org.opencontainers.image.licenses="AGPL-3.0-only"
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
RUN addgroup -S nodejs && adduser -S nextjs -G nodejs
COPY LICENSE NOTICE ADDITIONAL_TERMS.md TRADEMARKS.md THIRD-PARTY-NOTICES.md /usr/share/planvexa/legal/
COPY --from=builder --chown=nextjs:nodejs /app/public ./public
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
USER nextjs
EXPOSE 3000
# Browsers share localhost/domain cookies across apps; Node's default 16 KB header ceiling
# turns a full cookie jar into HTTP 431.
ENV NODE_OPTIONS="--max-http-header-size=65536"
CMD ["node", "server.js"]