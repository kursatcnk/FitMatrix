import { describe, expect, it } from 'vitest'
import { analyzeErgonomics, clampUserHeight, eyePoint } from './ergonomics'
import type { DeskDimensions, PlacedEquipment } from '../types/editor'

const desk: DeskDimensions = { width: 160, depth: 80, height: 70 }

const item = (instanceId: string, definitionId: string, x: number, z: number, extra: Partial<PlacedEquipment> = {}): PlacedEquipment => ({
  instanceId,
  definitionId,
  position: { x, z },
  rotation: 0,
  ...extra,
})

const check = (report: ReturnType<typeof analyzeErgonomics>, id: string) => report.checks.find((entry) => entry.id === id)!

describe('analyzeErgonomics', () => {
  it('rates a well arranged single-monitor setup as fully comfortable', () => {
    const items = [item('monitor', 'monitor-27', 0, -20), item('keyboard', 'keyboard-tkl', 0, 23)]
    const report = analyzeErgonomics(items, desk, 175)
    expect(report.checks.every((entry) => entry.status === 'good')).toBe(true)
    expect(report.score).toBe(100)
  })

  it('marks monitor and keyboard checks as missing on an empty desk', () => {
    const report = analyzeErgonomics([], desk, 175)
    expect(check(report, 'screen-height').status).toBe('missing')
    expect(check(report, 'keyboard-reach').status).toBe('missing')
    expect(report.score).toBe(100)
  })

  it('warns when the desk is far from elbow height', () => {
    const report = analyzeErgonomics([], { ...desk, height: 80 }, 165)
    expect(check(report, 'desk-height').status).toBe('warning')
    expect(check(report, 'desk-height').message).toContain('yüksek')
  })

  it('warns when a VESA monitor is raised above eye level', () => {
    const monitor = item('monitor', 'monitor-27', 0, -20, { mountMode: 'vesa', elevation: 30 })
    expect(check(analyzeErgonomics([monitor], desk, 175), 'screen-height').status).toBe('warning')
  })

  it('uses the panel width as height for portrait VESA monitors', () => {
    const landscape = item('monitor', 'monitor-27', 0, -20, { mountMode: 'vesa', elevation: 8 })
    const portrait = { ...landscape, screenOrientation: 'portrait' as const }
    expect(check(analyzeErgonomics([landscape], desk, 175), 'screen-height').status).toBe('good')
    expect(check(analyzeErgonomics([portrait], desk, 175), 'screen-height').status).toBe('warning')
  })

  it('asks wide monitors to sit further away', () => {
    const near = [item('monitor', 'monitor-ultrawide-34', 0, 15)]
    expect(check(analyzeErgonomics(near, desk, 175), 'viewing-distance').status).toBe('warning')
  })

  it('accepts a symmetric dual-monitor layout but flags an off-centre one', () => {
    const symmetric = [item('left', 'monitor-24', -28, -20), item('right', 'monitor-24', 28, -20)]
    const offCentre = [item('left', 'monitor-24', 20, -20), item('right', 'monitor-24', 60, -20)]
    expect(check(analyzeErgonomics(symmetric, desk, 175), 'screen-centering').status).toBe('good')
    expect(check(analyzeErgonomics(offCentre, desk, 175), 'screen-centering').status).toBe('warning')
  })

  it('flags a keyboard pushed too far from the front edge', () => {
    const keyboard = item('keyboard', 'keyboard-tkl', 0, -10)
    expect(check(analyzeErgonomics([keyboard], desk, 175), 'keyboard-reach').status).toBe('warning')
  })

  it('clamps unrealistic user heights', () => {
    expect(clampUserHeight(90)).toBe(140)
    expect(clampUserHeight(260)).toBe(210)
    expect(clampUserHeight(Number.NaN)).toBe(175)
  })
})

describe('standing posture', () => {
  it('expects a desk at standing elbow height', () => {
    const sitting = analyzeErgonomics([], { ...desk, height: 110 }, 175)
    const standing = analyzeErgonomics([], { ...desk, height: 110 }, 175, undefined, 'standing')

    expect(sitting.checks.find((entry) => entry.id === 'desk-height')?.status).toBe('warning')
    expect(standing.checks.find((entry) => entry.id === 'desk-height')?.status).toBe('good')
  })

  it('puts the eyes at a fixed share of body height regardless of the desk', () => {
    expect(eyePoint({ ...desk, height: 70 }, 180, 'standing').height).toBeCloseTo(168.3)
    expect(eyePoint({ ...desk, height: 110 }, 180, 'standing').height).toBeCloseTo(168.3)
    expect(eyePoint({ ...desk, height: 70 }, 180).height).toBeCloseTo(125.8)
  })

  it('asks to lower an over-height standing desk instead of raising the chair', () => {
    const report = analyzeErgonomics([], { ...desk, height: 125 }, 175, undefined, 'standing')

    expect(report.checks.find((entry) => entry.id === 'desk-height')?.message).toContain('alçalt')
  })
})

describe('viewing arc', () => {
  const arcOf = (items: PlacedEquipment[]) => analyzeErgonomics(items, desk, 175).checks.find((entry) => entry.id === 'viewing-arc')!

  it('keeps a single centred monitor well inside the comfortable arc', () => {
    const check = arcOf([item('monitor', 'monitor-27', 0, -20)])
    expect(check.status).toBe('good')
    expect(parseFloat(check.value!)).toBeLessThan(45)
  })

  it('flags three flat monitors in a row', () => {
    const check = arcOf([
      item('left', 'monitor-27', -62, -20),
      item('centre', 'monitor-27', 0, -20),
      item('right', 'monitor-27', 62, -20),
    ])
    expect(check.status).toBe('warning')
  })

  it('narrows the arc when side screens are angled inwards', () => {
    const flat = parseFloat(arcOf([item('l', 'monitor-27', -62, -20), item('r', 'monitor-27', 62, -20)]).value!)
    const angled = parseFloat(arcOf([item('l', 'monitor-27', -62, -20, { rotation: 25 }), item('r', 'monitor-27', 62, -20, { rotation: -25 })]).value!)
    expect(angled).toBeLessThan(flat)
  })
})
