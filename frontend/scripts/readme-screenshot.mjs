// Recreates docs/images/editor.png from the running demo build, so the README picture can be refreshed
// whenever the interface changes:
//   npm run build:demo && npx vite preview --mode demo --port 4173
//   node scripts/readme-screenshot.mjs [url]
import { chromium } from '@playwright/test'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const url = process.argv[2] ?? 'http://localhost:4173/FitMatrix/'
const output = join(dirname(dirname(fileURLToPath(import.meta.url))), '..', 'docs', 'images', 'editor.png')

// A dual-monitor desk in a room, loaded as the browser's draft so every item sits where intended.
const item = (definitionId, x, z, rotation = 0) => ({ instanceId: `${definitionId}-${x}`, definitionId, position: { x, z }, rotation })
const draft = {
  version: 1,
  projectId: null,
  projectName: 'Çift monitörlü çalışma masası',
  desk: { width: 160, depth: 80, height: 72 },
  items: [
    item('monitor-27', -32, -22, 10),
    item('monitor-27', 32, -22, -10),
    item('keyboard-tkl', -4, 20),
    item('mouse', 28, 20),
    item('speaker-pair', -70, -22),
    item('speaker-pair', 70, -22),
  ],
  room: { enabled: true, width: 320, depth: 300, wallGap: 5 },
  catalog: [],
}

const browser = await chromium.launch({ channel: process.env.CI ? undefined : 'msedge' })
const page = await browser.newPage({ viewport: { width: 1600, height: 940 }, locale: 'tr-TR' })
await page.addInitScript((project) => {
  localStorage.setItem('fitmatrix-project', JSON.stringify(project))
  localStorage.setItem('fitmatrix.language', 'tr')
}, draft)
await page.goto(url)
await page.waitForFunction(() => Number(document.querySelector('.count-badge')?.textContent) > 1000)
// The picture shows the editor itself, not the demo badge.
await page.addStyleTag({ content: '.demo-chip { display: none !important; }' })
await page.waitForTimeout(1500)

await page.screenshot({ path: output })
await browser.close()
console.log(`saved ${output}`)
