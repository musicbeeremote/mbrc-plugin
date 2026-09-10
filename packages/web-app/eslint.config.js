import oxlint from 'eslint-plugin-oxlint'
import pluginVue from 'eslint-plugin-vue'
import tsParser from '@typescript-eslint/parser'

/**
 * ESLint covers exactly one thing oxlint cannot: Vue templates.
 *
 * oxlint reads SFCs but has no `vue/*` rules, so `v-for` without `:key` and its
 * relatives go unseen. Everything in a `<script>` block stays oxlint's job, and
 * `eslint-plugin-oxlint` turns off the rules that would otherwise be checked
 * twice with two different opinions.
 */
export default [
  {
    ignores: ['dist/**', 'node_modules/**', '*.config.js', '*.config.ts'],
  },
  ...pluginVue.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    languageOptions: {
      parserOptions: { parser: tsParser, ecmaVersion: 'latest', sourceType: 'module' },
    },
  },
  {
    // Formatting is a formatter's job, and this package has no formatter yet.
    // Leaving these on buries the correctness rules that are the whole reason
    // ESLint runs here under 86 warnings about line breaks.
    rules: {
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/html-self-closing': 'off',
      'vue/html-indent': 'off',
      'vue/html-closing-bracket-newline': 'off',
      'vue/first-attribute-linebreak': 'off',
      'vue/attributes-order': 'off',
    },
  },
  ...oxlint.configs['flat/all'],
]
