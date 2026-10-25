import type { ApiCatalogProduct } from './catalogApi'
import { t } from '../i18n'

// The static demo (GitHub Pages) has no API: the catalog comes from a JSON file generated from the
// shipped catalog snapshot, projects stay in the browser and server-side tools are hidden.
export const isDemo = import.meta.env.VITE_DEMO === 'true'

export const REPOSITORY_URL = 'https://github.com/kursatcnk/FitMatrix'

export type DemoProduct = ApiCatalogProduct & { sourceUrl: string | null; sourceProvider: string | null }

let demoProducts: Promise<DemoProduct[]> | null = null

export function loadDemoProducts() {
  demoProducts ??= fetch(`${import.meta.env.BASE_URL}demo-catalog.json`).then((response) => {
    if (!response.ok) throw new Error(t('Demo kataloğu alınamadı: {status}', { status: response.status }))
    return response.json() as Promise<DemoProduct[]>
  })
  return demoProducts
}
