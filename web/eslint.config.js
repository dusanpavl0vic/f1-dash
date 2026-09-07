import js from "@eslint/js";
import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist", "node_modules"] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ["**/*.{ts,tsx}"],
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    plugins: { "react-hooks": reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_" }],
      // Enforces the token rule from IMPL-03: every colour lives in
      // styles/tokens.css and is referenced as var(--token).
      "no-restricted-syntax": [
        "error",
        {
          selector: "Literal[value=/^#[0-9a-fA-F]{3,8}$/]",
          message:
            "Raw hex colours are not allowed. Add the value to styles/tokens.css and use var(--token).",
        },
      ],
    },
  },
  {
    // Team colours arrive from the F1 feed as raw hex at runtime and change
    // between seasons. They are DATA, not design tokens, so the development
    // stand-in for that feed data is exempt from the hex rule.
    // Team colours arrive from the feed as raw hex at runtime, so the fixtures
    // and the tests that pin real feed values are data, not design tokens.
    files: [
      "src/features/live/model/constants.ts",
      // Schedule and standings come from Jolpica, which supplies a constructor
      // NAME and nothing else — there is no feed to take a colour from on those
      // pages, so a lookup table is the only option. Team colours are brand
      // data, not design tokens.
      "src/lib/teams.ts",
      "**/*.test.ts",
      "**/*.test.tsx",
    ],
    rules: { "no-restricted-syntax": "off" },
  },
);
