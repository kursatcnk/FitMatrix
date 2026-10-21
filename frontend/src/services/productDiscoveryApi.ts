export interface ProductDiscoveryResult {
  id: string
  brand: string
  displayName: string
  modelName: string
  manufacturerPartNumber: string | null
  gtin: string | null
  widthCm: number
  heightCm: number
  depthCm: number
  footprintWidthCm: number
  footprintDepthCm: number
  weightKg: number | null
  dataQuality: string
  isVerified: boolean
  sourceCount: number
  matchScore: number
}

export interface ExternalSpecification {
  group: string
  name: string
  value: string
  unit: string | null
}

export interface ExternalProductCandidate {
  provider: string
  productName: string
  brand: string | null
  modelName: string | null
  manufacturerPartNumber: string | null
  gtin: string | null
  widthCm: number | null
  heightCm: number | null
  depthCm: number | null
  weightKg: number | null
  sourceUrl: string | null
  measurementStatus: 'official-structured' | 'source-structured' | 'provider-order' | 'identity-only'
  measurementNote: string
  specifications: ExternalSpecification[]
  suggestedCategory: string
  suggestedEditorModel: string
  suggestedColor: string
}

export interface ProductDiscoveryResponse {
  query: string
  localMatches: ProductDiscoveryResult[]
  externalMatch: ExternalProductCandidate | null
  externalMatches: ExternalProductCandidate[]
  provider: {
    provider: string
    isConfigured: boolean
    message: string
  }
}

export async function discoverProduct(query: string): Promise<ProductDiscoveryResponse> {
  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), 120_000)
  try {
    const response = await fetch(`/api/product-discovery/search?query=${encodeURIComponent(query)}`, { signal: controller.signal })
    if (!response.ok) {
      const problem = await response.json().catch(() => null) as { message?: string } | null
      throw new Error(problem?.message ?? `Ürün araması başarısız: ${response.status}`)
    }
    return response.json()
  } catch (error) {
    if (controller.signal.aborted) throw new Error('Ürün araması 120 saniyede tamamlanamadı. Biraz sonra yeniden dene.')
    throw error
  } finally {
    window.clearTimeout(timeout)
  }
}
