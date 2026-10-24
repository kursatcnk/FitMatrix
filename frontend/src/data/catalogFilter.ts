import type { EquipmentCategory, EquipmentDefinition } from '../types/editor'

export const CATALOG_PAGE_SIZE = 150

const normalize = (value: string) => value.toLocaleLowerCase('tr-TR').normalize('NFKD').replace(/\p{M}/gu, '')

export function searchTokens(query: string) {
  return normalize(query).split(/[^\p{L}\p{N}]+/u).filter(Boolean)
}

// Brand names differ only in case between sources ("razer", "Razer"); they are grouped case-insensitively.
export const brandKey = (brand: string | undefined) => (brand ?? '').trim().toLowerCase()

// Every word must appear somewhere in the name, so "mx logitech" and "27 dell" match as users expect.
export function filterCatalog(catalog: EquipmentDefinition[], category: EquipmentCategory | 'all', query: string, brand = '') {
  const tokens = searchTokens(query)
  return catalog.filter((item) => {
    if (category !== 'all' && item.category !== category) return false
    if (brand && brandKey(item.brand) !== brand) return false
    if (!tokens.length) return true
    const name = normalize(item.name)
    return tokens.every((token) => name.includes(token))
  })
}

export function countByCategory(catalog: EquipmentDefinition[]) {
  const counts = new Map<EquipmentCategory, number>()
  for (const item of catalog) counts.set(item.category, (counts.get(item.category) ?? 0) + 1)
  return counts
}

// Brands present in the catalog with product counts, most common first; the label keeps the first spelling seen.
export function listBrands(catalog: EquipmentDefinition[]) {
  const brands = new Map<string, { key: string; label: string; count: number }>()
  for (const item of catalog) {
    const key = brandKey(item.brand)
    if (!key) continue
    const entry = brands.get(key)
    if (entry) entry.count++
    else brands.set(key, { key, label: item.brand!.trim(), count: 1 })
  }
  return [...brands.values()].sort((first, second) => second.count - first.count || first.label.localeCompare(second.label, 'tr'))
}
