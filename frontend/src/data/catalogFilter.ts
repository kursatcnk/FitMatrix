import type { EquipmentCategory, EquipmentDefinition } from '../types/editor'

export const CATALOG_PAGE_SIZE = 150

const normalize = (value: string) => value.toLocaleLowerCase('tr-TR').normalize('NFKD').replace(/\p{M}/gu, '')

export function searchTokens(query: string) {
  return normalize(query).split(/[^\p{L}\p{N}]+/u).filter(Boolean)
}

// Every word must appear somewhere in the name, so "mx logitech" and "27 dell" match as users expect.
export function filterCatalog(catalog: EquipmentDefinition[], category: EquipmentCategory | 'all', query: string) {
  const tokens = searchTokens(query)
  return catalog.filter((item) => {
    if (category !== 'all' && item.category !== category) return false
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
