import { describe, expect, it } from 'vitest'
import { analyzeErgonomics, clampUserHeight } from './ergonomics'
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
