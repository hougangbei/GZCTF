import path from 'node:path'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  resolve: {
    alias: {
      '@Api': path.resolve(import.meta.dirname, 'src/Api.ts'),
      '@Utils': path.resolve(import.meta.dirname, 'src/utils'),
    },
  },
  test: {
    include: ['src/**/*.test.ts'],
  },
})
