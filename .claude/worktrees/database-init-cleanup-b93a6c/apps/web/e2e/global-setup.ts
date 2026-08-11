import { spawnSync } from "node:child_process";
import path from "node:path";
import type { FullConfig } from "@playwright/test";

async function isUp(baseURL: string) {
  try {
    const response = await fetch(`${baseURL}/login`, {
      signal: AbortSignal.timeout(5_000),
    });

    return response.ok;
  } catch {
    return false;
  }
}

async function waitUntilUp(baseURL: string, timeoutMs: number) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    if (await isUp(baseURL)) {
      return true;
    }

    await new Promise((resolve) => setTimeout(resolve, 3_000));
  }

  return false;
}

export default async function globalSetup(config: FullConfig) {
  const baseURL = process.env.PLANVEXA_E2E_BASE_URL ?? "http://localhost:3000";

  if (await isUp(baseURL)) {
    return;
  }

  if (process.env.PLANVEXA_E2E_ASSUME_UP) {
    throw new Error(
      `Planvexa web is not answering at ${baseURL}/login and PLANVEXA_E2E_ASSUME_UP is set, so the dev stack was not started. Start it with scripts/dev-up.ps1 or unset PLANVEXA_E2E_ASSUME_UP.`,
    );
  }

  const devUp = path.resolve(config.rootDir, "../../../scripts/dev-up.ps1");
  console.log(`[e2e] ${baseURL} is down — starting the dev stack via ${devUp}`);
  spawnSync("pwsh", ["-NoProfile", "-File", devUp], { stdio: "inherit" });

  if (!(await waitUntilUp(baseURL, 180_000))) {
    throw new Error(
      `Planvexa web never came up at ${baseURL}/login after running ${devUp}. Check the dev stack logs (web, api, keycloak) and retry.`,
    );
  }
}
