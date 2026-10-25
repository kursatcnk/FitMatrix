import { resolveEquipmentDefinition } from '../data/equipmentCatalog'
import { t } from '../i18n'
import type { DeskDimensions, EquipmentDefinition, PlacedEquipment, Position2D } from '../types/editor'

interface PlacementStatus {
  isValid: boolean
  message: string
}

export interface AvailablePlacement {
  position: Position2D
  rotation: number
}

const toRadians = (degrees: number) => (degrees * Math.PI) / 180

export function getPlacementFootprint(item: PlacedEquipment, definition: EquipmentDefinition) {
  if (definition.model === 'monitor' && item.mountMode === 'vesa') {
    return {
      width: item.screenOrientation === 'portrait' ? definition.height : definition.width,
      depth: definition.depth,
    }
  }
  return { width: definition.footprintWidth, depth: definition.footprintDepth }
}

function getVerticalInterval(item: PlacedEquipment, definition: EquipmentDefinition) {
  const min = item.elevation ?? 0
  const height = definition.model === 'monitor' && item.mountMode === 'vesa' && item.screenOrientation === 'portrait'
    ? definition.width
    : definition.height
  return { min, max: min + height }
}

function getCorners(item: PlacedEquipment, catalog?: EquipmentDefinition[]): Position2D[] {
  const definition = resolveEquipmentDefinition(item, catalog)
  if (!definition) return []

  const footprint = getPlacementFootprint(item, definition)
  const halfWidth = footprint.width / 2
  const halfDepth = footprint.depth / 2
  const angle = toRadians(item.rotation)
  const cos = Math.cos(angle)
  const sin = Math.sin(angle)

  return [
    { x: -halfWidth, z: -halfDepth },
    { x: halfWidth, z: -halfDepth },
    { x: halfWidth, z: halfDepth },
    { x: -halfWidth, z: halfDepth },
  ].map((point) => ({
    x: item.position.x + point.x * cos - point.z * sin,
    z: item.position.z + point.x * sin + point.z * cos,
  }))
}

function projection(corners: Position2D[], axis: Position2D) {
  const values = corners.map((corner) => corner.x * axis.x + corner.z * axis.z)
  return { min: Math.min(...values), max: Math.max(...values) }
}

// Separating Axis Theorem keeps rotated rectangular footprints deterministic.
function overlaps(first: PlacedEquipment, second: PlacedEquipment, catalog?: EquipmentDefinition[]) {
  const firstDefinition = resolveEquipmentDefinition(first, catalog)
  const secondDefinition = resolveEquipmentDefinition(second, catalog)
  if (!firstDefinition || !secondDefinition) return false
  const firstVertical = getVerticalInterval(first, firstDefinition)
  const secondVertical = getVerticalInterval(second, secondDefinition)
  if (firstVertical.max <= secondVertical.min || secondVertical.max <= firstVertical.min) return false

  const firstCorners = getCorners(first, catalog)
  const secondCorners = getCorners(second, catalog)
  if (!firstCorners.length || !secondCorners.length) return false

  const axes = [first.rotation, first.rotation + 90, second.rotation, second.rotation + 90].map((angle) => ({
    x: Math.cos(toRadians(angle)),
    z: Math.sin(toRadians(angle)),
  }))

  return axes.every((axis) => {
    const a = projection(firstCorners, axis)
    const b = projection(secondCorners, axis)
    return a.max > b.min && b.max > a.min
  })
}

export function getPlacementStatus(
  item: PlacedEquipment,
  allItems: PlacedEquipment[],
  desk: DeskDimensions,
  catalog?: EquipmentDefinition[],
): PlacementStatus {
  const corners = getCorners(item, catalog)
  const outsideDesk = corners.some(
    (corner) => Math.abs(corner.x) > desk.width / 2 || Math.abs(corner.z) > desk.depth / 2,
  )

  if (outsideDesk) return { isValid: false, message: item.mountMode === 'vesa' ? t('VESA ekran gövdesi masa genişliği veya derinliğini aşıyor.') : t('Ekipmanın temas alanı masa sınırını aşıyor.') }

  const collision = allItems.find(
    (candidate) => candidate.instanceId !== item.instanceId && overlaps(item, candidate, catalog),
  )

  if (collision) {
    const other = resolveEquipmentDefinition(collision, catalog)
    return { isValid: false, message: other ? t('{name} ile çakışıyor.', { name: t(other.name) }) : t('Başka bir ekipmanla çakışıyor.') }
  }

  return {
    isValid: true,
    message: item.mountMode === 'vesa'
      ? t('VESA ekran yüksekliği, gövde sınırı ve diğer ekipmanlarla açıklık uygun.')
      : t('Temas alanı masa içinde ve başka bir nesneyle çakışmıyor.'),
  }
}

export function findAvailablePlacement(
  definition: EquipmentDefinition,
  allItems: PlacedEquipment[],
  desk: DeskDimensions,
  catalog: EquipmentDefinition[],
): AvailablePlacement | null {
  const placementCatalog = catalog.some((item) => item.id === definition.id) ? catalog : [...catalog, definition]
  const rotations = Array.from(new Set([definition.defaultRotation % 180, (definition.defaultRotation + 90) % 180]))

  for (const rotation of rotations) {
    const quarterTurn = Math.abs(rotation % 180) === 90
    const width = quarterTurn ? definition.footprintDepth : definition.footprintWidth
    const depth = quarterTurn ? definition.footprintWidth : definition.footprintDepth
    if (width > desk.width || depth > desk.depth) continue

    const minX = -desk.width / 2 + width / 2
    const maxX = desk.width / 2 - width / 2
    const minZ = -desk.depth / 2 + depth / 2
    const maxZ = desk.depth / 2 - depth / 2
    const candidates: Position2D[] = [{ x: 0, z: 0 }]
    const step = 2
    for (let z = minZ; z <= maxZ + .001; z += step) {
      for (let x = minX; x <= maxX + .001; x += step) candidates.push({ x, z })
    }
    candidates.sort((first, second) => first.x ** 2 + first.z ** 2 - (second.x ** 2 + second.z ** 2))

    for (const position of candidates) {
      const candidate: PlacedEquipment = {
        instanceId: '__fit-preview__',
        definitionId: definition.id,
        position,
        rotation,
      }
      if (getPlacementStatus(candidate, [...allItems, candidate], desk, placementCatalog).isValid)
        return { position, rotation }
    }
  }
  return null
}

export interface EdgeClearances {
  left: number
  right: number
  back: number
  front: number
  bounds: { minX: number; maxX: number; minZ: number; maxZ: number }
}

// Free space between an item's (rotated) footprint and each desk edge, in centimetres. Negative values
// mean the footprint sticks out past that edge.
export function getEdgeClearances(item: PlacedEquipment, definition: EquipmentDefinition, desk: DeskDimensions): EdgeClearances {
  const footprint = getPlacementFootprint(item, definition)
  const angle = toRadians(item.rotation)
  const cos = Math.abs(Math.cos(angle))
  const sin = Math.abs(Math.sin(angle))
  const halfX = (footprint.width / 2) * cos + (footprint.depth / 2) * sin
  const halfZ = (footprint.width / 2) * sin + (footprint.depth / 2) * cos
  const bounds = {
    minX: item.position.x - halfX,
    maxX: item.position.x + halfX,
    minZ: item.position.z - halfZ,
    maxZ: item.position.z + halfZ,
  }
  return {
    left: bounds.minX + desk.width / 2,
    right: desk.width / 2 - bounds.maxX,
    back: bounds.minZ + desk.depth / 2,
    front: desk.depth / 2 - bounds.maxZ,
    bounds,
  }
}
