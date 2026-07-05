import { defineConfig } from "vitest/config";

// Unit tests target the pure logic in src/lib (parsing, shape-diff,
// serialization) - no DOM needed, so the fast node environment is enough.
export default defineConfig({
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
