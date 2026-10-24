import { describe, expect, it } from 'vitest'
import { buildPartsList, partsListToCsv } from './partsList'
import type { DeskDimensions, EquipmentDefinition, PlacedEquipment } from '../types/editor'

const desk: DeskDimensions = { width: 160, depth: 80, height: 75 }
const monitor: EquipmentDefinition = {
  id: 'database-1',
  databaseId: '1',
  name: 'Dell U2723QE; "27"',
  category: 'display',
  width: 61.14,
  height: 53.52,
  depth: 18.5,
  footprintWidth: 61.14,
  footprintDepth: 18.5,
  defaultRotation: 0,
  color: '#252a2e',
  model: 'monitor',
}
const place = (instanceId: string): PlacedEquipment => ({ instanceId, definitionId: 'database-1', position: { x: 0, z: 0 }, rotation: 0 })

describe('buildPartsList', () => {
  it('lists the desk first and counts identical items', () => {
    const rows = buildPartsList([place('a'), place('b')], [monitor], desk, () => 'Ekran', { 1: 'https://www.dell.com/u2723qe' })

    expect(rows).toHaveLength(2)
    expect(rows[0].name).toBe('Masa')
    expect(rows[1]).toMatchObject({ quantity: 2, widthCm: 61.14, sourceUrl: 'https://www.dell.com/u2723qe' })
  })
})

describe('partsListToCsv', () => {
  it('writes a BOM, semicolons and quotes cells that need it', () => {
    const csv = partsListToCsv(buildPartsList([place('a')], [monitor], desk, () => 'Ekran'))

    expect(csv.startsWith('﻿Ürün;Kategori;Adet')).toBe(true)
    expect(csv).toContain('"Dell U2723QE; ""27""";Ekran;1;61.14;18.5;53.52;')
  })
})
