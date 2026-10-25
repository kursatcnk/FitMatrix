import { describe, expect, it } from 'vitest'
import { analyzeRoom, clampRoom, DEFAULT_ROOM, roomBounds } from './room'
import type { DeskDimensions } from '../types/editor'

const desk: DeskDimensions = { width: 160, depth: 80, height: 75 }
const room = { ...DEFAULT_ROOM, enabled: true }

describe('room', () => {
  it('puts the back wall behind the desk by the chosen gap', () => {
    const bounds = roomBounds(desk, { ...room, wallGap: 10, depth: 300 })
    expect(bounds.backWallZ).toBe(-50)
    expect(bounds.frontWallZ).toBe(250)
    expect(bounds.rightWallX).toBe(room.width / 2)
  })

  it('adds no checks while the room is hidden', () => {
    expect(analyzeRoom(desk, DEFAULT_ROOM)).toEqual([])
  })

  it('flags a desk wider than the room', () => {
    const fit = analyzeRoom(desk, { ...room, width: 150 }).find((check) => check.id === 'room-fit')!
    expect(fit.status).toBe('warning')
    expect(fit.value).toBe('10 cm taşıyor')
  })

  it('needs more space behind a seated user than a standing one', () => {
    const tight = { ...room, depth: 160, wallGap: 5 }
    expect(analyzeRoom(desk, tight, 'sitting').find((check) => check.id === 'chair-space')?.status).toBe('warning')
    expect(analyzeRoom(desk, tight, 'standing').find((check) => check.id === 'chair-space')?.status).toBe('good')
  })

  it('keeps stored values inside sensible limits', () => {
    expect(clampRoom({ enabled: true, width: 20, depth: Number.NaN, wallGap: -4 })).toEqual({ enabled: true, width: 100, depth: DEFAULT_ROOM.depth, wallGap: 0 })
  })
})
