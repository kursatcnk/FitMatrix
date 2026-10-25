import { expect, test, type Page } from '@playwright/test'

const openEditor = async (page: Page) => {
  await page.goto('./')
  // The catalog loads after the first paint; the badge shows the full count once it has arrived.
  await expect.poll(async () => Number(await page.locator('.count-badge').textContent())).toBeGreaterThan(1000)
}

const turnRoomOn = (page: Page) =>
  page.getByRole('group', { name: 'Odayı göster' }).getByRole('button', { name: 'Açık' }).click()

test('opens the demo with the shipped catalog and the server tools hidden', async ({ page }) => {
  await openEditor(page)

  await expect(page.getByRole('link', { name: /Canlı demo/ })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Ürün bul' })).toHaveCount(0)
  await expect(page.locator('canvas')).toBeVisible()
})

test('adds a product from the catalog search and selects it', async ({ page }) => {
  await openEditor(page)

  await page.getByLabel('Katalogda ara').fill('mx master 3s')
  const product = page.locator('.catalog-item').first()
  const name = (await product.locator('strong').textContent())!
  await product.click()

  await expect(page.locator('.inspector-panel h2')).toHaveText(name)
  await expect(page.locator('.placement-card')).toHaveClass(/(^|\s)valid(\s|$)/)
})

test('a share link reopens the same layout in a new tab', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await openEditor(page)
  await page.getByLabel('Proje adı').fill('Paylaşım testi')
  await turnRoomOn(page)

  await page.getByRole('button', { name: 'Paylaşım bağlantısını kopyala' }).click()
  await expect(page.getByText('Bağlantı kopyalandı')).toBeVisible()
  const link = await page.evaluate(() => navigator.clipboard.readText())
  expect(link).toContain('#plan=v1.')

  // A share link replaces whatever draft the browser already holds, room included.
  await page.getByLabel('Proje adı').fill('Yerel taslak')
  await page.getByRole('group', { name: 'Odayı göster' }).getByRole('button', { name: 'Kapalı' }).click()
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('fitmatrix-project') ?? '{}').room?.enabled)).toBe(false)
  const shared = await context.newPage()
  await shared.goto(link)
  await expect(shared.getByLabel('Proje adı')).toHaveValue('Paylaşım testi')
  await expect(shared.getByText('Sandalye alanı')).toBeVisible()
})

test('turning the room on adds the room checks to the ergonomics list', async ({ page }) => {
  await openEditor(page)
  await expect(page.getByText('Sandalye alanı')).toHaveCount(0)

  await turnRoomOn(page)
  await page.getByLabel('Oda derinliği').fill('150')

  await expect(page.getByText('Odaya sığma')).toBeVisible()
  await expect(page.locator('.ergonomics-check.warning', { hasText: 'Sandalye alanı' })).toBeVisible()
})

test('switches the interface to English and remembers the choice', async ({ page }) => {
  await openEditor(page)

  await page.getByRole('group', { name: 'Arayüz dili' }).getByRole('button', { name: 'EN' }).click()
  await expect(page.getByRole('button', { name: 'New layout' })).toBeVisible()
  await expect(page.getByText('Ergonomics check')).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')

  await page.reload()
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeVisible()
})

test('fits a phone screen and switches between the catalog and the inspector', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await openEditor(page)

  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  await expect(page.getByLabel('Katalogda ara')).toBeVisible()
  await expect(page.getByText('Masa ölçüleri')).toBeHidden()

  await page.getByRole('tab', { name: 'Düzenleyici' }).click()
  await expect(page.getByText('Masa ölçüleri')).toBeVisible()
  await expect(page.getByLabel('Katalogda ara')).toBeHidden()

  // Adding a product selects it, which brings the inspector forward on its own.
  await page.getByRole('tab', { name: 'Katalog' }).click()
  await page.getByLabel('Katalogda ara').fill('mx master 3s')
  await page.locator('.catalog-item').first().click()
  await expect(page.locator('.placement-card')).toBeVisible()
})

test('downloads the 3D view as a PNG', async ({ page }) => {
  await openEditor(page)

  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('button', { name: 'PNG' }).click(),
  ])

  expect(download.suggestedFilename()).toMatch(/-gorunum\.png$/)
})
