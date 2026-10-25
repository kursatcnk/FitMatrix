import { beforeEach, describe, expect, it } from 'vitest'
import { getPlacementStatus } from '../editor/placement'
import { useEditorStore } from './editorStore'

const baseProject = {
  version: 1 as const,
  projectId: null,
  projectName: 'Test düzeni',
  desk: { width: 160, depth: 80, height: 75 },
  items: [],
}

describe('editor history', () => {
  beforeEach(() => useEditorStore.getState().loadProject(baseProject))

  it('undoes and redoes an equipment addition', () => {
    useEditorStore.getState().addEquipment('monitor-27')
    expect(useEditorStore.getState().items).toHaveLength(1)

    useEditorStore.getState().undo()
    expect(useEditorStore.getState().items).toHaveLength(0)

    useEditorStore.getState().redo()
    expect(useEditorStore.getState().items).toHaveLength(1)
  })

  it('stores a transform batch as one history step', () => {
    useEditorStore.getState().addEquipment('mouse')
    const id = useEditorStore.getState().items[0].instanceId
    useEditorStore.getState().beginHistoryBatch()
    useEditorStore.getState().updateItem(id, { position: { x: 12, z: 8 } })
    useEditorStore.getState().updateItem(id, { position: { x: 20, z: 10 } })
    useEditorStore.getState().endHistoryBatch()

    useEditorStore.getState().undo()
    expect(useEditorStore.getState().items[0].position).toEqual({ x: 0, z: 0 })
  })

  it('duplicates an item without sharing mutable dimensions', () => {
    useEditorStore.getState().addEquipment('keyboard-tkl')
    const source = useEditorStore.getState().items[0]
    useEditorStore.getState().updateItem(source.instanceId, {
      dimensions: { width: 40, height: 3, depth: 14, footprintWidth: 40, footprintDepth: 14 },
    })
    useEditorStore.getState().duplicateItem(source.instanceId)

    const [original, duplicate] = useEditorStore.getState().items
    expect(duplicate.instanceId).not.toBe(original.instanceId)
    expect(duplicate.dimensions).toEqual(original.dimensions)
    expect(duplicate.dimensions).not.toBe(original.dimensions)
    expect(getPlacementStatus(duplicate, [original, duplicate], useEditorStore.getState().desk, useEditorStore.getState().catalog).isValid).toBe(true)
  })
})

describe('project room', () => {
  it('loads the room saved with a project and hides it for projects without one', () => {
    const room = { enabled: true, width: 280, depth: 260, wallGap: 3 }
    useEditorStore.getState().loadProject({ ...baseProject, room })
    expect(useEditorStore.getState().room).toEqual(room)

    useEditorStore.getState().loadProject(baseProject)
    expect(useEditorStore.getState().room.enabled).toBe(false)
  })
})
