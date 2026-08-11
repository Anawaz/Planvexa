// Run: node --experimental-strip-types src/components/work/positioning.check.mjs
import assert from "node:assert/strict";
import { dropPosition, midpoint } from "./positioning.ts";

const column = [
  { id: "a", position: 1000 },
  { id: "b", position: 2000 },
  { id: "c", position: 3000 },
];

assert.equal(dropPosition(column, "a", "b"), 2500, "down onto b lands between b and c");
assert.equal(dropPosition(column, "c", "b"), 1500, "up onto b lands between a and b");
assert.equal(dropPosition(column, "c", "a"), 1000 - 1024, "up onto the first card lands before it");
assert.equal(dropPosition(column, "a"), 3000 + 1024, "empty drop area appends");
assert.equal(dropPosition([], "x"), 1024, "empty column");
assert.equal(dropPosition(column, "x", "a"), 1000 - 1024, "cross-column drop inserts before");
assert.equal(midpoint(undefined, undefined), 1024);

console.log("positioning ok");
