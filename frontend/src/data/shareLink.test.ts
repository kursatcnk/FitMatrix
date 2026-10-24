import { describe, expect, it } from 'vitest'
import { createShareHash, readShareHash, type SharedPlan } from './shareLink'

const plan: SharedPlan = {
  projectName: 'Çift monitörlü düzen',
  desk: { width: 160, depth: 80, height: 72 },
  items: [
    { instanceId: 'a', definitionId: 'monitor-27', position: { x: -30, z: -20 }, rotation: 0, mountMode: 'vesa', elevation: 12 },
    { instanceId: 'b', definitionId: 'keyboard-tkl', position: { x: 0, z: 15 }, rotation: 5 },
  ],
  catalog: [],
}

describe('share links', () => {
  it('round-trips a layout through the URL fragment', async () => {
    const hash = await createShareHash(plan)

    expect(hash.startsWith('#plan=v1.')).toBe(true)
    expect(hash.slice('#plan=v1.'.length)).not.toMatch(/[+/=]/)
    expect(await readShareHash(hash)).toEqual(plan)
  })

  it('rejects other fragments and corrupted data', async () => {
    expect(await readShareHash('#something-else')).toBeNull()
    expect(await readShareHash('#plan=v1.not-deflate-data')).toBeNull()
  })

  it('drops items without a usable position', async () => {
    const broken = { ...plan, items: [...plan.items, { instanceId: 'c', definitionId: 'mouse', position: { x: Number.NaN, z: 0 }, rotation: 0 }] }

    const read = await readShareHash(await createShareHash(broken))

    expect(read?.items).toHaveLength(2)
  })
})
