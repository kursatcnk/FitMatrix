import type { DeskDimensions } from '../types/editor'
import type { ErgonomicCheck, Posture } from './ergonomics'
import { t } from '../i18n'

// The room around the desk. The desk stays centred left to right with its back edge `wallGap`
// away from the back wall, so the scene origin (the desk centre) does not move.
export interface RoomSettings {
  enabled: boolean
  width: number
  depth: number
  wallGap: number
}

export const DEFAULT_ROOM: RoomSettings = { enabled: false, width: 320, depth: 300, wallGap: 5 }
export const WALL_HEIGHT = 260

// Room needed behind the desk's front edge to sit down and push the chair back, or to stand.
const CHAIR_SPACE = { sitting: 90, standing: 60 }
const TIGHT_CHAIR_SPACE = { sitting: 70, standing: 45 }

const clamp = (value: number, min: number, max: number, fallback: number) =>
  Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback

export function clampRoom(room: Partial<RoomSettings>): RoomSettings {
  return {
    enabled: Boolean(room.enabled),
    width: clamp(Number(room.width), 100, 1200, DEFAULT_ROOM.width),
    depth: clamp(Number(room.depth), 100, 1200, DEFAULT_ROOM.depth),
    wallGap: clamp(Number(room.wallGap), 0, 300, DEFAULT_ROOM.wallGap),
  }
}

export function roomBounds(desk: DeskDimensions, room: RoomSettings) {
  const backWallZ = -desk.depth / 2 - room.wallGap
  return {
    backWallZ,
    frontWallZ: backWallZ + room.depth,
    leftWallX: -room.width / 2,
    rightWallX: room.width / 2,
  }
}

const round = (value: number) => Math.round(value * 10) / 10

export function analyzeRoom(desk: DeskDimensions, room: RoomSettings, posture: Posture = 'sitting'): ErgonomicCheck[] {
  if (!room.enabled) return []
  const bounds = roomBounds(desk, room)
  const sideSpace = (room.width - desk.width) / 2
  const fits: ErgonomicCheck = sideSpace < 0
    ? { id: 'room-fit', title: t('Odaya sığma'), status: 'warning', value: t('{cm} cm taşıyor', { cm: round(-sideSpace * 2) }), message: t('Masa oda genişliğinden uzun; daha kısa bir masa seç ya da yan duvara dik yerleştir.') }
    : { id: 'room-fit', title: t('Odaya sığma'), status: 'good', value: t('yanlarda {cm} cm', { cm: round(sideSpace) }), message: t('Masa duvarlar arasına sığıyor.') }

  const chairSpace = bounds.frontWallZ - desk.depth / 2
  const needed = CHAIR_SPACE[posture]
  const base = { id: 'chair-space' as const, title: posture === 'standing' ? t('Arkada durma alanı') : t('Sandalye alanı'), value: `${round(chairSpace)} / ${needed} cm` }
  const space: ErgonomicCheck = chairSpace >= needed
    ? { ...base, status: 'good', message: posture === 'standing' ? t('Masanın önünde rahat durup geri çekilebilirsin.') : t('Sandalyeyi rahatça geri itip kalkabilirsin.') }
    : chairSpace >= TIGHT_CHAIR_SPACE[posture]
      ? { ...base, status: 'warning', message: t('Alan dar; {cm} cm daha boşluk kalkarken sandalyenin duvara çarpmasını önler.', { cm: round(needed - chairSpace) }) }
      : { ...base, status: 'warning', message: t('Masanın önünde oturup kalkmaya yetecek yer yok; masayı duvara yaklaştır ya da odanın daha geniş tarafına al.') }

  return [fits, space]
}
