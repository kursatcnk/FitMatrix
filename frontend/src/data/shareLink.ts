import type { DeskDimensions, EquipmentDefinition, PlacedEquipment } from '../types/editor'
import { clampRoom, type RoomSettings } from '../editor/room'

export interface SharedPlan {
  projectName: string
  desk: DeskDimensions
  items: PlacedEquipment[]
  catalog: EquipmentDefinition[]
  // Links made before rooms existed have none; they open with the walls hidden.
  room?: RoomSettings
}

const prefix = '#plan=v1.'

const toBase64Url = (bytes: Uint8Array) => {
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')
}

const fromBase64Url = (text: string) => {
  const binary = atob(text.replaceAll('-', '+').replaceAll('_', '/'))
  return Uint8Array.from(binary, (character) => character.charCodeAt(0))
}

async function transform(bytes: Uint8Array, stream: CompressionStream | DecompressionStream) {
  const output = new Blob([new Uint8Array(bytes)]).stream().pipeThrough(stream)
  return new Uint8Array(await new Response(output).arrayBuffer())
}

// The whole layout travels in the URL fragment, which browsers never send to a server.
export async function createShareHash(plan: SharedPlan) {
  const json = new TextEncoder().encode(JSON.stringify(plan))
  return prefix + toBase64Url(await transform(json, new CompressionStream('deflate-raw')))
}

export async function readShareHash(hash: string): Promise<SharedPlan | null> {
  if (!hash.startsWith(prefix)) return null
  try {
    const bytes = await transform(fromBase64Url(hash.slice(prefix.length)), new DecompressionStream('deflate-raw'))
    const plan = JSON.parse(new TextDecoder().decode(bytes)) as Partial<SharedPlan>
    const desk = plan.desk
    const validDesk = desk && [desk.width, desk.depth, desk.height].every((value) => typeof value === 'number' && value > 0 && value <= 1000)
    if (!validDesk || !Array.isArray(plan.items) || plan.items.length > 200 || !Array.isArray(plan.catalog)) return null
    const items = plan.items.filter((item) =>
      typeof item?.instanceId === 'string' && typeof item.definitionId === 'string' &&
      Number.isFinite(item.position?.x) && Number.isFinite(item.position?.z) && Number.isFinite(item.rotation))
    return {
      projectName: typeof plan.projectName === 'string' ? plan.projectName.slice(0, 120) : 'Paylaşılan düzen',
      desk,
      items,
      catalog: plan.catalog.filter((definition) => typeof definition?.id === 'string' && typeof definition.name === 'string'),
      room: plan.room && typeof plan.room === 'object' ? clampRoom(plan.room) : undefined,
    }
  } catch {
    return null
  }
}
