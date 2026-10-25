import { defineConfig, devices } from '@playwright/test'

// End-to-end tests run against the static demo build, so they need neither the API nor SQL Server.
// Locally they use the installed Microsoft Edge; CI installs Playwright's Chromium.
const port = 4173

export default defineConfig({
  testDir: 'e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    ...devices['Desktop Chrome'],
    channel: process.env.CI ? undefined : 'msedge',
    baseURL: `http://localhost:${port}/FitMatrix/`,
    viewport: { width: 1440, height: 900 },
    // The interface follows the browser language; the tests are written against the Turkish one.
    locale: 'tr-TR',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: `npm run build:demo && npx vite preview --mode demo --port ${port} --strictPort`,
    url: `http://localhost:${port}/FitMatrix/`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
})
