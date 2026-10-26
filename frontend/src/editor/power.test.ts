import { describe, expect, it } from 'vitest'
import { planPower } from './power'
import type { PlacedEquipment } from '../types/editor'

const item = (definitionId: string, x = 0): PlacedEquipment => ({ instanceId: `${definitionId}-${x}`, definitionId, position: { x, z: 0 }, rotation: 0 })

describe('power plan', () => {
  it('counts powered devices and leaves USB peripherals out', () => {
    const plan = planPower([item('monitor-27', -30), item('monitor-27', 30), item('mid-tower'), item('keyboard-tkl'), item('mouse')])

    expect(plan.outlets).toBe(3)
    expect(plan.devices).toEqual([
      { model: 'monitor', label: 'Monitör', count: 2 },
      { model: 'tower', label: 'Bilgisayar kasası', count: 1 },
    ])
  })

  it('suggests the smallest common strip with a spare socket', () => {
    expect(planPower([item('monitor-27'), item('mid-tower'), item('speaker-pair')]).strip).toBe(4)
    expect(planPower([item('monitor-27', 1), item('monitor-27', 2), item('mid-tower'), item('speaker-pair'), item('desk-lamp')]).strip).toBe(6)
  })

  it('suggests nothing for a desk without powered devices', () => {
    expect(planPower([item('keyboard-tkl')])).toEqual({ devices: [], outlets: 0, strip: null })
  })
})
