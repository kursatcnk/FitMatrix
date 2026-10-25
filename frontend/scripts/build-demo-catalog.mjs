// Turns the shipped catalog snapshot into the static catalog used by the GitHub Pages demo.
// Usage: node scripts/build-demo-catalog.mjs [output directory, default dist]
import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = dirname(dirname(fileURLToPath(import.meta.url)))
const snapshotPath = join(root, '..', 'backend', 'Data', 'Catalog', 'catalog-snapshot.json')
const outputDirectory = join(root, process.argv[2] ?? 'dist')

// Same as CatalogSnapshot.StableGuid on the backend: the first 16 bytes of a SHA-256, laid out the way
// .NET's Guid(byte[]) reads them (first three groups little-endian), so ids match a database seeded
// from the same snapshot and shared layouts resolve to the same products.
export function stableGuid(value) {
  const bytes = createHash('sha256').update(`fitmatrix:${value.toLowerCase()}`, 'utf8').digest().subarray(0, 16)
  const hex = (start, end, reverse = false) => {
    const part = [...bytes.subarray(start, end)]
    return (reverse ? part.reverse() : part).map((byte) => byte.toString(16).padStart(2, '0')).join('')
  }
  return `${hex(0, 4, true)}-${hex(4, 6, true)}-${hex(6, 8, true)}-${hex(8, 10)}-${hex(10, 16)}`
}

const snapshot = JSON.parse(readFileSync(snapshotPath, 'utf8'))
const products = snapshot.products.map((item) => ({
  id: stableGuid(`snapshot:${item.brandSlug}:${item.gtin ?? item.manufacturerPartNumber ?? item.modelName}`),
  brand: item.brand,
  category: item.category,
  categorySlug: item.category,
  displayName: item.displayName,
  modelName: item.modelName,
  manufacturerPartNumber: item.manufacturerPartNumber,
  gtin: item.gtin,
  widthCm: item.widthCm,
  heightCm: item.heightCm,
  depthCm: item.depthCm,
  footprintWidthCm: item.footprintWidthCm,
  footprintDepthCm: item.footprintDepthCm,
  weightKg: item.weightKg,
  editorModel: item.editorModel,
  displayColor: item.displayColor,
  imageUrl: null,
  isVerified: item.isVerified,
  dataQuality: item.dataQuality,
  sourceUrl: item.sources[0]?.url ?? null,
  sourceProvider: item.sources[0]?.provider ?? null,
}))

mkdirSync(outputDirectory, { recursive: true })
writeFileSync(join(outputDirectory, 'demo-catalog.json'), JSON.stringify(products))
console.log(`demo-catalog.json: ${products.length} products from snapshot ${snapshot.version}`)
