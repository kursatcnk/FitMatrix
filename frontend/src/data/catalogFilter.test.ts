import { describe, expect, it } from 'vitest'
import { countByCategory, filterCatalog, searchTokens } from './catalogFilter'
import type { EquipmentDefinition } from '../types/editor'

const product = (id: string, name: string, category: EquipmentDefinition['category']): EquipmentDefinition => ({
  id,
  name,
  category,
  width: 10,
  height: 10,
  depth: 10,
  footprintWidth: 10,
  footprintDepth: 10,
  defaultRotation: 0,
  color: '#222222',
  model: 'generic',
})

const catalog = [
  product('mx', 'Logitech MX Master 3S', 'input'),
  product('dell', 'Dell U2723QE', 'display'),
  product('chair', 'IKEA JÄRVFJÄLLET', 'furniture'),
]

describe('filterCatalog', () => {
  it('matches every search word regardless of order', () => {
    expect(filterCatalog(catalog, 'all', 'master logitech').map((item) => item.id)).toEqual(['mx'])
  })

  it('combines category and text filters', () => {
    expect(filterCatalog(catalog, 'display', 'logitech')).toHaveLength(0)
    expect(filterCatalog(catalog, 'display', 'u2723')).toHaveLength(1)
  })

  it('ignores accents and Turkish casing', () => {
    expect(filterCatalog(catalog, 'all', 'jarvfjallet').map((item) => item.id)).toEqual(['chair'])
  })

  it('returns the whole category for an empty query', () => {
    expect(filterCatalog(catalog, 'all', '  ')).toHaveLength(3)
  })
})

describe('catalog helpers', () => {
  it('splits queries on punctuation', () => {
    expect(searchTokens('MX-Master, 3S')).toEqual(['mx', 'master', '3s'])
  })

  it('counts products per category', () => {
    expect(countByCategory(catalog).get('input')).toBe(1)
  })
})
