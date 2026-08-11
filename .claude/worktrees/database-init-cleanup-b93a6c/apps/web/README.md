# Planvexa Web

The Planvexa Next.js web client: App Router, React, TypeScript, Tailwind CSS, and React Query.

## Scripts

- `npm run dev` - start the local development server.
- `npm run lint` - run ESLint.
- `npm run build` - create a production build.
- `npm run start` - serve the production build.

## Environment

Create `.env.local` when needed:

```env
NEXT_PUBLIC_API_BASE_URL=http://localhost:8080
NEXT_PUBLIC_KEYCLOAK_URL=http://localhost:8081
NEXT_PUBLIC_KEYCLOAK_REALM=planvexa
NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=planvexa-web
```

`NEXT_PUBLIC_API_BASE_URL` defaults to `http://localhost:8080`. The Keycloak variables are documented for the future OIDC integration; the current login screen does not call Keycloak.
