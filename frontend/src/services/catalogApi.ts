import { equipmentCatalog } from '../data/equipmentCatalog'
import { isDemo, loadDemoProducts } from './demoMode'
import type { EquipmentCategory, EquipmentDefinition, EquipmentModel } from '../types/editor'

export interface ApiCatalogProduct {
  id: string
  brand: string
  category: string
  categorySlug: string
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
  editorModel: string
  displayColor: string
  imageUrl: string | null
  isVerified: boolean
  dataQuality: string
}

export interface ApiProductSpecification {
  group: string
  name: string
  value: string
  unit: string | null
  sourceUrl: string | null
  lastCheckedAt: string
}

export interface ApiCatalogProductDetail {
  product: ApiCatalogProduct
  attributes: Array<{
    code: string
    name: string
    valueType: string
    unit: string | null
    numericValue: number | null
    textValue: string | null
    booleanValue: boolean | null
  }>
  specifications: ApiProductSpecification[]
  sources: Array<{
    provider: string
    sourceUrl: string
    externalId: string | null
    licenseName: string | null
    lastCheckedAt: string
  }>
}

const categories: EquipmentCategory[] = [
  'display', 'computer', 'input', 'audio', 'imaging', 'network', 'gaming',
  'mobile', 'furniture', 'lighting', 'power', 'office', 'accessory',
]
const models: EquipmentModel[] = [
  'monitor', 'tower', 'keyboard', 'mouse', 'laptop', 'speaker', 'headphone', 'mat', 'lamp', 'stand',
  'desk', 'chair', 'router', 'camera', 'printer', 'phone', 'tablet', 'console', 'power', 'generic',
]

function isCategory(value: string): value is EquipmentCategory {
  return categories.includes(value as EquipmentCategory)
}

function isModel(value: string): value is EquipmentModel {
  return models.includes(value as EquipmentModel)
}

export function mapProduct(product: ApiCatalogProduct): EquipmentDefinition | null {
  if (!isCategory(product.categorySlug) || !isModel(product.editorModel)) return null

  const bundledDefinition = equipmentCatalog.find((item) => item.databaseId === product.id)
  return {
    id: bundledDefinition?.id ?? `database-${product.id}`,
    databaseId: product.id,
    name: product.displayName,
    brand: product.brand,
    category: product.categorySlug,
    width: product.widthCm,
    height: product.heightCm,
    depth: product.depthCm,
    footprintWidth: product.footprintWidthCm,
    footprintDepth: product.footprintDepthCm,
    defaultRotation: 0,
    color: product.displayColor,
    model: product.editorModel,
  }
}

export interface DiscoveryCatalogInput {
  provider: string
  productName: string
  brand: string
  modelName: string
  manufacturerPartNumber: string | null
  gtin: string | null
  widthCm: number
  heightCm: number
  depthCm: number
  footprintWidthCm: number
  footprintDepthCm: number
  weightKg: number | null
  categorySlug: EquipmentCategory
  editorModel: EquipmentModel
  displayColor: string
  sourceUrl: string
  measurementStatus: string
  specifications: Array<{ group: string; name: string; value: string; unit: string | null }>
}

export async function importDiscoveredProduct(input: DiscoveryCatalogInput): Promise<EquipmentDefinition> {
  const response = await fetch('/api/catalog/products/from-discovery', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { message?: string } | null
    throw new Error(problem?.message ?? `Ürün kataloğa alınamadı: ${response.status}`)
  }
  const definition = mapProduct(await response.json() as ApiCatalogProduct)
  if (!definition) throw new Error('Ürün kaydedildi ancak 3D model türü desteklenmiyor.')
  return definition
}

export async function getCatalog(): Promise<EquipmentDefinition[]> {
  // The API serves the bundled starter models from its seed data; the demo adds them itself.
  if (isDemo) return [...equipmentCatalog, ...(await loadDemoProducts()).map(mapProduct).filter((item): item is EquipmentDefinition => item !== null)]
  const pageSize = 250
  const products: ApiCatalogProduct[] = []
  for (let offset = 0; ; offset += pageSize) {
    const response = await fetch(`/api/catalog/products?offset=${offset}&limit=${pageSize}`)
    if (!response.ok) throw new Error(`Katalog alınamadı: ${response.status}`)
    const page = await response.json() as ApiCatalogProduct[]
    products.push(...page)
    if (page.length < pageSize) break
  }
  return products.map(mapProduct).filter((item): item is EquipmentDefinition => item !== null)
}

export async function getCatalogProductDetail(id: string): Promise<ApiCatalogProductDetail> {
  if (isDemo) {
    const product = (await loadDemoProducts()).find((item) => item.id === id)
    if (!product) throw new Error('Ürün demo kataloğunda yok.')
    const { sourceUrl, sourceProvider, ...rest } = product
    return {
      product: rest,
      attributes: [],
      specifications: [],
      sources: sourceUrl ? [{ provider: sourceProvider ?? '', sourceUrl, externalId: null, licenseName: null, lastCheckedAt: '' }] : [],
    }
  }
  const response = await fetch(`/api/catalog/products/${id}`)
  if (!response.ok) throw new Error(`Ürün ayrıntıları alınamadı: ${response.status}`)
  return response.json()
}

export async function deleteCatalogProduct(id: string): Promise<void> {
  const response = await fetch(`/api/catalog/products/${id}`, { method: 'DELETE' })
  if (!response.ok) throw new Error(`Ürün katalogdan kaldırılamadı: ${response.status}`)
}
