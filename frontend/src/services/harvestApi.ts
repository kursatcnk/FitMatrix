import { t } from '../i18n'

export interface HarvestEntry {
  query: string
  outcome: 'Imported' | 'Updated' | 'AlreadyInCatalog' | 'NoVerifiedMatch' | 'Failed'
  productName: string | null
  message: string | null
  finishedAt: string
}

export interface HarvestStatus {
  isRunning: boolean
  startedAt: string | null
  finishedAt: string | null
  total: number
  processed: number
  imported: number
  updated: number
  alreadyInCatalog: number
  noVerifiedMatch: number
  failed: number
  currentQuery: string | null
  recent: HarvestEntry[]
}

export interface ManufacturerSource {
  name: string
  brand: string
  sitemaps: string[]
}

// The admin endpoints exist only in the API's Development environment; elsewhere they answer 404.
export class HarvestUnavailableError extends Error {}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)
  if (response.status === 404) throw new HarvestUnavailableError(t('Katalog toplama yalnızca geliştirme ortamında açıktır.'))
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { message?: string } | null
    throw new Error(problem?.message ?? t('İstek başarısız: {status}', { status: response.status }))
  }
  return response.status === 204 || response.status === 202 && response.headers.get('content-length') === '0'
    ? undefined as T
    : await response.json() as T
}

export const getHarvestStatus = () => request<HarvestStatus>('/api/admin/catalog/harvest')

export const getManufacturerSources = () => request<ManufacturerSource[]>('/api/admin/catalog/harvest/manufacturers')

export const startManufacturerHarvest = (sources: string[]) => request<HarvestStatus>('/api/admin/catalog/harvest/manufacturers', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ sources, delaySeconds: 3 }),
})

export const cancelHarvest = () => fetch('/api/admin/catalog/harvest', { method: 'DELETE' }).then(() => undefined)

export const exportCatalogSnapshot = () => request<{ version: string; productCount: number; path: string }>('/api/admin/catalog/snapshot', { method: 'POST' })
