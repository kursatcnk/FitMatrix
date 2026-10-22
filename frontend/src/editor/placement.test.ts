import { describe, expect, it } from 'vitest'
import { findAvailablePlacement, getEdgeClearances, getPlacementStatus } from './placement'
import type { DeskDimensions, EquipmentDefinition, PlacedEquipment } from '../types/editor'

const desk: DeskDimensions = { width: 160, depth: 80, height: 75 }

const item = (
  instanceId: string,
  definitionId: string,
  x: number,
  z: number,
  rotation = 0,
): PlacedEquipment => ({ instanceId, definitionId, position: { x, z }, rotation })

describe('getPlacementStatus', () => {
  it('accepts an item whose complete footprint is on the desk', () => {
    const monitor = item('monitor', 'monitor-27', 0, 0)
    expect(getPlacementStatus(monitor, [monitor], desk).isValid).toBe(true)
  })

  it('rejects a footprint that crosses the desk edge', () => {
    const monitor = item('monitor', 'monitor-27', 72, 0)
    const result = getPlacementStatus(monitor, [monitor], desk)
    expect(result.isValid).toBe(false)
    expect(result.message).toContain('masa sınırını')
  })

  it('uses the rotated footprint when checking boundaries', () => {
    const mat = item('mat', 'desk-mat', 55, 0, 90)
    expect(getPlacementStatus(mat, [mat], desk).isValid).toBe(true)
  })

  it('reports deterministic collisions between rotated rectangles', () => {
    const keyboard = item('keyboard', 'keyboard-tkl', 0, 0, 15)
    const mouse = item('mouse', 'mouse', 4, 2, -20)
    const result = getPlacementStatus(mouse, [keyboard, mouse], desk)
    expect(result.isValid).toBe(false)
    expect(result.message).toContain('çakışıyor')
  })

  it('allows vertically stacked VESA monitors when their height ranges do not overlap', () => {
    const catalog: EquipmentDefinition[] = [{
      id: 'screen', name: 'Screen', category: 'display', model: 'monitor', color: '#222222',
      width: 61, height: 36, depth: 5, footprintWidth: 61, footprintDepth: 5, defaultRotation: 0,
    }]
    const lower: PlacedEquipment = { ...item('lower', 'screen', 0, -20), mountMode: 'vesa', elevation: 5 }
    const upper: PlacedEquipment = { ...item('upper', 'screen', 0, -20), mountMode: 'vesa', elevation: 42 }
    expect(getPlacementStatus(lower, [lower, upper], desk, catalog).isValid).toBe(true)
    expect(getPlacementStatus(upper, [lower, upper], desk, catalog).isValid).toBe(true)
  })

  it('uses portrait panel width for VESA desk-boundary checks', () => {
    const catalog: EquipmentDefinition[] = [{
      id: 'screen', name: 'Screen', category: 'display', model: 'monitor', color: '#222222',
      width: 49.2, height: 28.3, depth: 4, footprintWidth: 49.2, footprintDepth: 4, defaultRotation: 0,
    }]
    const portrait: PlacedEquipment = {
      ...item('portrait', 'screen', 44, -20), mountMode: 'vesa', screenOrientation: 'portrait', elevation: 10,
    }
    expect(getPlacementStatus(portrait, [portrait], { width: 128, depth: 58, height: 72 }, catalog).isValid).toBe(true)
  })
})

describe('findAvailablePlacement', () => {
  const product = (width: number, depth: number): EquipmentDefinition => ({
    id: 'discovered-monitor',
    name: 'Bulunan monitör',
    category: 'display',
    width,
    height: 50,
    depth,
    footprintWidth: width,
    footprintDepth: depth,
    defaultRotation: 0,
    color: '#252a2e',
    model: 'monitor',
  })

  it('finds a collision-free location for a discovered real-size product', () => {
    const definition = product(61.44, 30.26)
    const placement = findAvailablePlacement(definition, [], desk, [definition])
    expect(placement).not.toBeNull()
    expect(placement?.position).toEqual({ x: 0, z: 0 })
  })

  it('tries a 90 degree orientation when the normal orientation does not fit', () => {
    const narrowDesk: DeskDimensions = { width: 80, depth: 110, height: 75 }
    const definition = product(100, 50)
    expect(findAvailablePlacement(definition, [], narrowDesk, [definition])?.rotation).toBe(90)
  })

  it('returns null when the product exceeds the desk in both orientations', () => {
    const definition = product(170, 90)
    expect(findAvailablePlacement(definition, [], desk, [definition])).toBeNull()
  })
})

describe('getEdgeClearances', () => {
  const definition: EquipmentDefinition = {
    id: 'box',
    name: 'Box',
    category: 'accessory',
    width: 40,
    height: 10,
    depth: 20,
    footprintWidth: 40,
    footprintDepth: 20,
    defaultRotation: 0,
    color: '#222222',
    model: 'generic',
  }

  it('measures the free space to every desk edge', () => {
    const clearances = getEdgeClearances(item('box', 'box', 10, -20), definition, desk)
    expect([clearances.left, clearances.right, clearances.back, clearances.front]).toEqual([70, 50, 10, 50])
  })

  it('uses the rotated outline of the footprint', () => {
    const clearances = getEdgeClearances(item('box', 'box', 0, 0, 90), definition, desk)
    expect(clearances.left).toBeCloseTo(70)
    expect(clearances.back).toBeCloseTo(20)
  })
})
