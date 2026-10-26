import { resolveEquipmentDefinition } from '../data/equipmentCatalog'
import type { EquipmentDefinition, EquipmentModel, PlacedEquipment } from '../types/editor'

// Models that take a mains socket of their own (or a charger plugged into one). Keyboards, mice,
// headsets and cameras usually run from the computer's USB ports and are left out.
const POWERED_MODELS: Partial<Record<EquipmentModel, string>> = {
  monitor: 'Monitör',
  tower: 'Bilgisayar kasası',
  laptop: 'Dizüstü şarj adaptörü',
  speaker: 'Hoparlör',
  lamp: 'Lamba',
  router: 'Modem/router',
  printer: 'Yazıcı',
  console: 'Oyun konsolu',
  phone: 'Telefon şarjı',
  tablet: 'Tablet şarjı',
}

// Common power strip sizes; one socket is kept free for a phone charger or a vacuum cleaner.
const STRIP_SIZES = [3, 4, 5, 6, 8, 10, 12]

export interface PowerPlan {
  devices: Array<{ model: EquipmentModel; label: string; count: number }>
  outlets: number
  strip: number | null
}

export function planPower(items: PlacedEquipment[], catalog?: EquipmentDefinition[]): PowerPlan {
  const counts = new Map<EquipmentModel, number>()
  for (const item of items) {
    const model = resolveEquipmentDefinition(item, catalog)?.model
    if (model && POWERED_MODELS[model]) counts.set(model, (counts.get(model) ?? 0) + 1)
  }
  const devices = [...counts].map(([model, count]) => ({ model, label: POWERED_MODELS[model]!, count }))
  const outlets = devices.reduce((sum, device) => sum + device.count, 0)
  return {
    devices,
    outlets,
    strip: outlets === 0 ? null : STRIP_SIZES.find((size) => size >= outlets + 1) ?? null,
  }
}
