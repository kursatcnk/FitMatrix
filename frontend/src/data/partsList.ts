import { resolveEquipmentDefinition } from './equipmentCatalog'
import { getLocale, t } from '../i18n'
import type { DeskDimensions, EquipmentDefinition, PlacedEquipment } from '../types/editor'

export interface PartsListRow {
  name: string
  category: string
  quantity: number
  widthCm: number
  depthCm: number
  heightCm: number
  sourceUrl: string
}

// One row per distinct product (identical items are counted), desk first, sorted by name.
export function buildPartsList(
  items: PlacedEquipment[],
  catalog: EquipmentDefinition[],
  desk: DeskDimensions,
  categoryLabel: (category: EquipmentDefinition['category']) => string,
  sources: Record<string, string> = {},
): PartsListRow[] {
  const rows = new Map<string, PartsListRow>()
  for (const item of items) {
    const definition = resolveEquipmentDefinition(item, catalog)
    if (!definition) continue
    const key = `${definition.id}|${definition.width}|${definition.depth}|${definition.height}`
    const existing = rows.get(key)
    if (existing) {
      existing.quantity++
      continue
    }
    rows.set(key, {
      name: t(definition.name),
      category: categoryLabel(definition.category),
      quantity: 1,
      widthCm: definition.width,
      depthCm: definition.depth,
      heightCm: definition.height,
      sourceUrl: definition.databaseId ? sources[definition.databaseId] ?? '' : '',
    })
  }

  const deskRow: PartsListRow = {
    name: t('Masa'),
    category: categoryLabel('furniture'),
    quantity: 1,
    widthCm: desk.width,
    depthCm: desk.depth,
    heightCm: desk.height,
    sourceUrl: '',
  }
  return [deskRow, ...[...rows.values()].sort((first, second) => first.name.localeCompare(second.name, getLocale()))]
}

const csvCell = (value: string | number) => {
  const text = typeof value === 'number' ? String(Math.round(value * 100) / 100) : value
  return /[";\n\r]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
}

// Semicolon-separated with a BOM so Excel in Turkish (comma decimal) locales opens it in columns as UTF-8.
export function partsListToCsv(rows: PartsListRow[]) {
  const header = [t('Ürün'), t('Kategori'), t('Adet'), t('Genişlik (cm)'), t('Derinlik (cm)'), t('Yükseklik (cm)'), t('Kaynak')]
  const lines = [header, ...rows.map((row) => [row.name, row.category, row.quantity, row.widthCm, row.depthCm, row.heightCm, row.sourceUrl])]
    .map((cells) => cells.map(csvCell).join(';'))
  return `﻿${lines.join('\r\n')}\r\n`
}
