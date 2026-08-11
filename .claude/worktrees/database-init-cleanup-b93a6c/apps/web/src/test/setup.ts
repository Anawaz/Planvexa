import "@testing-library/jest-dom/vitest";
// jsdom does not implement IndexedDB; polyfill it globally so offline-store tests can run
// against a real (in-memory) IDBFactory instead of mocking the API.
import "fake-indexeddb/auto";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

afterEach(cleanup);
