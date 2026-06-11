import type { EquipmentDefinition, PlacedEquipment } from '../types/editor'

export const equipmentCatalog: EquipmentDefinition[] = [
  { id: 'monitor-27', databaseId: '30000000-0000-0000-0000-000000000001', name: '27” Monitör', category: 'display', width: 61, height: 46, depth: 20, footprintWidth: 24, footprintDepth: 20, defaultRotation: 0, color: '#252a2e', model: 'monitor' },
  { id: 'monitor-24', databaseId: '30000000-0000-0000-0000-000000000002', name: '24” Monitör', category: 'display', width: 54, height: 41, depth: 18, footprintWidth: 22, footprintDepth: 18, defaultRotation: 0, color: '#343a3f', model: 'monitor' },
  { id: 'mid-tower', databaseId: '30000000-0000-0000-0000-000000000003', name: 'Mid Tower Kasa', category: 'computer', width: 22, height: 47, depth: 46, footprintWidth: 22, footprintDepth: 46, defaultRotation: 0, color: '#30383a', model: 'tower' },
  { id: 'laptop-14', databaseId: '30000000-0000-0000-0000-000000000004', name: '14” Laptop', category: 'computer', width: 32, height: 20, depth: 22, footprintWidth: 32, footprintDepth: 22, defaultRotation: 0, color: '#667076', model: 'laptop' },
  { id: 'keyboard-tkl', databaseId: '30000000-0000-0000-0000-000000000005', name: 'TKL Klavye', category: 'input', width: 36, height: 3, depth: 14, footprintWidth: 36, footprintDepth: 14, defaultRotation: 0, color: '#444b50', model: 'keyboard' },
  { id: 'mouse', databaseId: '30000000-0000-0000-0000-000000000006', name: 'Mouse', category: 'input', width: 7, height: 4, depth: 12, footprintWidth: 7, footprintDepth: 12, defaultRotation: 0, color: '#3d4448', model: 'mouse' },
  { id: 'speaker-pair', databaseId: '30000000-0000-0000-0000-000000000007', name: 'Masaüstü Hoparlör', category: 'audio', width: 12, height: 22, depth: 14, footprintWidth: 12, footprintDepth: 14, defaultRotation: 0, color: '#2e3437', model: 'speaker' },
  { id: 'desk-mat', databaseId: '30000000-0000-0000-0000-000000000008', name: 'Geniş Mousepad', category: 'accessory', width: 80, height: 0.4, depth: 30, footprintWidth: 80, footprintDepth: 30, defaultRotation: 0, color: '#4f5f5b', model: 'mat' },
  { id: 'desk-lamp', databaseId: '30000000-0000-0000-0000-000000000009', name: 'Masa Lambası', category: 'accessory', width: 16, height: 48, depth: 16, footprintWidth: 16, footprintDepth: 16, defaultRotation: 0, color: '#b5a57d', model: 'lamp' },
  { id: 'monitor-32', databaseId: '30000000-0000-0000-0000-000000000010', name: '32” Monitör', category: 'display', width: 72, height: 51, depth: 24, footprintWidth: 28, footprintDepth: 24, defaultRotation: 0, color: '#202629', model: 'monitor' },
  { id: 'monitor-ultrawide-34', databaseId: '30000000-0000-0000-0000-000000000011', name: '34” Ultrawide Monitör', category: 'display', width: 81, height: 47, depth: 25, footprintWidth: 31, footprintDepth: 25, defaultRotation: 0, color: '#2a3033', model: 'monitor' },
  { id: 'full-tower', databaseId: '30000000-0000-0000-0000-000000000012', name: 'Full Tower Kasa', category: 'computer', width: 24, height: 52, depth: 51, footprintWidth: 24, footprintDepth: 51, defaultRotation: 0, color: '#242a2d', model: 'tower' },
  { id: 'mini-pc', databaseId: '30000000-0000-0000-0000-000000000013', name: 'Mini PC', category: 'computer', width: 13, height: 5, depth: 13, footprintWidth: 13, footprintDepth: 13, defaultRotation: 0, color: '#596064', model: 'tower' },
  { id: 'laptop-15', databaseId: '30000000-0000-0000-0000-000000000014', name: '15.6” Laptop', category: 'computer', width: 36, height: 23, depth: 24, footprintWidth: 36, footprintDepth: 24, defaultRotation: 0, color: '#596368', model: 'laptop' },
  { id: 'keyboard-full', databaseId: '30000000-0000-0000-0000-000000000015', name: 'Tam Boy Klavye', category: 'input', width: 44, height: 3, depth: 14, footprintWidth: 44, footprintDepth: 14, defaultRotation: 0, color: '#383e42', model: 'keyboard' },
  { id: 'monitor-riser', databaseId: '30000000-0000-0000-0000-000000000016', name: 'Monitör Yükseltici', category: 'accessory', width: 55, height: 10, depth: 24, footprintWidth: 55, footprintDepth: 24, defaultRotation: 0, color: '#756752', model: 'stand' },
]

export const getEquipmentDefinition = (id: string) =>
  equipmentCatalog.find((item) => item.id === id)

export function resolveEquipmentDefinition(
  item: PlacedEquipment,
  catalog: EquipmentDefinition[] = equipmentCatalog,
): EquipmentDefinition | undefined {
  const definition = catalog.find((candidate) => candidate.id === item.definitionId)
  return definition && item.dimensions ? { ...definition, ...item.dimensions } : definition
}
