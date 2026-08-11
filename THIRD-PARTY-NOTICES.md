# Third-Party Notices

This file records third-party components detected during the AGPL-3.0-only
migration audit on 2026-08-06. Third-party components remain under their
own licences; they are not relicensed by Planvexa.

## Summary

- No vendored third-party source code was detected in the repository after removal of unused template SVG assets.
- The repository depends on third-party components through NuGet, npm, and container images.
- Direct dependency metadata was collected from restored package metadata where available.
- Manual legal review remains required for the favicon provenance, base container-image notices, and transitive native packages listed below.

## Direct NuGet dependencies

| Component | Version | Licence | Copyright holder / authors | Source |
| --- | --- | --- | --- | --- |
| dbup-core | 6.1.1 | MIT | Paul Stovell, Jim Burger, Jake Ginnivan, Damian Maclennan | https://dbup.github.io/ |
| dbup-postgresql | 6.1.2 | MIT | DbUp contributors | https://dbup.github.io/ |
| EFCore.NamingConventions | 10.0.1 | Apache-2.0 | Shay Rojansky | https://github.com/npgsql/efcore.pg |
| FluentValidation | 12.1.1 | Apache-2.0 | Jeremy Skinner | https://fluentvalidation.net/ |
| FluentValidation.DependencyInjectionExtensions | 12.1.1 | Apache-2.0 | Jeremy Skinner | https://fluentvalidation.net/ |
| MessagePack | 3.1.8 | MIT | neuecc, aarnott | https://github.com/MessagePack-CSharp/MessagePack-CSharp |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.10 | MIT | Microsoft | https://asp.net/ |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.10 | MIT | Microsoft | https://asp.net/ |
| Microsoft.AspNetCore.OpenApi | 10.0.10 | MIT | Microsoft | https://asp.net/ |
| Microsoft.OpenApi | 2.7.5 | MIT | Microsoft | https://github.com/Microsoft/OpenAPI.NET |
| Microsoft.AspNetCore.SignalR.Client | 10.0.10 | MIT | Microsoft | https://asp.net/ |
| Microsoft.EntityFrameworkCore | 10.0.10 | MIT | Microsoft | https://docs.microsoft.com/ef/core/ |
| Microsoft.EntityFrameworkCore.Relational | 10.0.10 | MIT | Microsoft | https://docs.microsoft.com/ef/core/ |
| Microsoft.NET.Test.Sdk | 18.8.1 | MIT | Microsoft | https://github.com/microsoft/vstest |
| Npgsql | 10.0.3 | PostgreSQL | Npgsql contributors | https://github.com/npgsql/npgsql |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | PostgreSQL | Npgsql contributors | https://github.com/npgsql/efcore.pg |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.17.0 | Apache-2.0 | OpenTelemetry Authors | https://opentelemetry.io/ |
| OpenTelemetry.Extensions.Hosting | 1.17.0 | Apache-2.0 | OpenTelemetry Authors | https://opentelemetry.io/ |
| OpenTelemetry.Instrumentation.AspNetCore | 1.17.0 | Apache-2.0 | OpenTelemetry Authors | https://opentelemetry.io/ |
| OpenTelemetry.Instrumentation.Http | 1.17.0 | Apache-2.0 | OpenTelemetry Authors | https://opentelemetry.io/ |
| Scalar.AspNetCore | 2.16.16 | MIT | Scalar | https://scalar.com/ |
| Shouldly | 4.3.0 | BSD-3-Clause | Jake Ginnivan, Joseph Woodward, Simon Cropp | https://docs.shouldly.org/ |
| Testcontainers.PostgreSql | 4.13.0 | MIT | Andre Hofmeister and contributors | https://dotnet.testcontainers.org/ |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | xUnit.net contributors | https://xunit.net/ |
| xunit.v3 | 3.2.2 | Apache-2.0 | xUnit.net contributors | https://xunit.net/ |

## Direct npm dependencies

| Component | Version | Licence | Source |
| --- | --- | --- | --- |
| @axe-core/playwright | 4.12.1 | MPL-2.0 | https://github.com/dequelabs/axe-core-npm.git |
| @dnd-kit/core | 6.3.1 | MIT | https://github.com/clauderic/dnd-kit.git |
| @dnd-kit/sortable | 10.0.0 | MIT | https://github.com/clauderic/dnd-kit.git |
| @dnd-kit/utilities | 3.2.2 | MIT | https://github.com/clauderic/dnd-kit.git |
| @microsoft/signalr | 10.0.0 | MIT | https://github.com/dotnet/aspnetcore.git |
| @playwright/test | 1.62.1 | Apache-2.0 | https://github.com/microsoft/playwright.git |
| @tailwindcss/postcss | 4.3.3 | MIT | https://github.com/tailwindlabs/tailwindcss.git |
| @tanstack/react-query | 5.101.4 | MIT | https://github.com/TanStack/query.git |
| @tanstack/react-table | 8.21.3 | MIT | https://github.com/TanStack/table.git |
| @tanstack/react-virtual | 3.14.9 | MIT | https://github.com/TanStack/virtual.git |
| @testing-library/jest-dom | 7.0.0 | MIT | https://github.com/testing-library/jest-dom |
| @testing-library/react | 16.3.2 | MIT | https://github.com/testing-library/react-testing-library |
| @testing-library/user-event | 14.6.1 | MIT | https://github.com/testing-library/user-event |
| @types/node | 20.19.43 | MIT | https://github.com/DefinitelyTyped/DefinitelyTyped.git |
| @types/react | 19.2.17 | MIT | https://github.com/DefinitelyTyped/DefinitelyTyped.git |
| @types/react-dom | 19.2.3 | MIT | https://github.com/DefinitelyTyped/DefinitelyTyped.git |
| @vitejs/plugin-react | 6.0.5 | MIT | https://github.com/vitejs/vite-plugin-react.git |
| eslint | 9.39.5 | MIT | https://github.com/eslint/eslint |
| eslint-config-next | 16.2.12 | MIT | https://github.com/vercel/next.js |
| jsdom | 30.0.1 | MIT | https://github.com/jsdom/jsdom.git |
| next | 16.2.12 | MIT | https://github.com/vercel/next.js |
| react | 19.2.4 | MIT | https://github.com/facebook/react.git |
| react-dom | 19.2.4 | MIT | https://github.com/facebook/react.git |
| tailwindcss | 4.3.3 | MIT | https://github.com/tailwindlabs/tailwindcss.git |
| typescript | 5.9.3 | Apache-2.0 | https://github.com/microsoft/TypeScript.git |
| vitest | 4.1.10 | MIT | https://github.com/vitest-dev/vitest.git |

## Required notices

- Preserve upstream licence texts and notices distributed with third-party packages and container images.
- Keep package-manager lockfiles intact for dependency provenance.
- Do not rewrite third-party licences to AGPL-3.0-only.
