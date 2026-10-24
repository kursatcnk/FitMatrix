import { resolveEquipmentDefinition } from '../data/equipmentCatalog'
import type { DeskDimensions, EquipmentDefinition, PlacedEquipment } from '../types/editor'

export type ErgonomicStatus = 'good' | 'warning' | 'missing'

export interface ErgonomicCheck {
  id: 'desk-height' | 'screen-height' | 'viewing-distance' | 'screen-centering' | 'viewing-arc' | 'keyboard-reach'
  title: string
  status: ErgonomicStatus
  value?: string
  message: string
}

export interface ErgonomicReport {
  userHeight: number
  eyeHeight: number
  checks: ErgonomicCheck[]
  score: number
}

export const MIN_USER_HEIGHT = 140
export const MAX_USER_HEIGHT = 210
export const DEFAULT_USER_HEIGHT = 175

export type Posture = 'sitting' | 'standing'

// Anthropometric ratios for a seated user whose chair is set so the elbows meet the desk surface.
const ELBOW_HEIGHT_RATIO = .4
const EYE_ABOVE_ELBOW_RATIO = .31
// Standing: the desk meets the standing elbow and the eyes sit at a fixed share of body height.
const STANDING_ELBOW_RATIO = .63
const STANDING_EYE_RATIO = .935
// The user's eyes sit roughly this far in front of the desk's front edge.
const EYE_OFFSET_FROM_EDGE = 15

const round = (value: number) => Math.round(value * 10) / 10

export const clampUserHeight = (height: number) =>
  Number.isFinite(height) ? Math.min(MAX_USER_HEIGHT, Math.max(MIN_USER_HEIGHT, height)) : DEFAULT_USER_HEIGHT

// Where the user's eyes are, in front of the desk. Seated, the chair is set so the elbows meet the desk,
// which puts the eyes a fixed distance above it; standing, eye height depends on body height alone.
export function eyePoint(desk: DeskDimensions, userHeight: number, posture: Posture = 'sitting') {
  const height = clampUserHeight(userHeight)
  return {
    height: posture === 'standing' ? height * STANDING_EYE_RATIO : desk.height + height * EYE_ABOVE_ELBOW_RATIO,
    z: desk.depth / 2 + EYE_OFFSET_FROM_EDGE,
  }
}

export const idealDeskHeight = (userHeight: number, posture: Posture = 'sitting') =>
  clampUserHeight(userHeight) * (posture === 'standing' ? STANDING_ELBOW_RATIO : ELBOW_HEIGHT_RATIO)

interface ResolvedItem {
  item: PlacedEquipment
  definition: EquipmentDefinition
}

function screenHeight({ item, definition }: ResolvedItem) {
  return item.mountMode === 'vesa' && item.screenOrientation === 'portrait' ? definition.width : definition.height
}

function checkDeskHeight(desk: DeskDimensions, userHeight: number, posture: Posture): ErgonomicCheck {
  const ideal = idealDeskHeight(userHeight, posture)
  const difference = desk.height - ideal
  const base = { id: 'desk-height' as const, title: 'Masa yüksekliği', value: `${round(desk.height)} / ideal ${round(ideal)} cm` }
  if (Math.abs(difference) <= 4) return { ...base, status: 'good', message: 'Dirsekler masa hizasında kalır; omuzlar rahat.' }
  return difference > 0
    ? { ...base, status: 'warning', message: posture === 'standing'
      ? `Masa ${round(difference)} cm yüksek. Ayakta çalışırken masayı alçalt.`
      : `Masa ${round(difference)} cm yüksek. Sandalyeyi yükseltip ayak desteği kullan.` }
    : { ...base, status: 'warning', message: `Masa ${round(-difference)} cm alçak. Bileklerin aşağı bükülmemesi için masayı yükselt.` }
}

function checkScreenHeight(primary: ResolvedItem | undefined, desk: DeskDimensions, eyeHeight: number): ErgonomicCheck {
  const base = { id: 'screen-height' as const, title: 'Ekran üst kenarı' }
  if (!primary) return { ...base, status: 'missing', message: 'Değerlendirme için sahneye bir monitör ekle.' }
  const top = desk.height + (primary.item.elevation ?? 0) + screenHeight(primary)
  const offset = top - eyeHeight
  const value = `${round(top)} cm · göz ${round(eyeHeight)} cm`
  if (offset > 2) return { ...base, value, status: 'warning', message: `Üst kenar göz hizasından ${round(offset)} cm yukarıda; boyun geriye eğilir.` }
  if (offset < -12) return { ...base, value, status: 'warning', message: `Üst kenar göz hizasından ${round(-offset)} cm aşağıda; boyun öne eğilir.` }
  return { ...base, value, status: 'good', message: 'Üst kenar göz hizasında veya biraz altında.' }
}

function checkViewingDistance(primary: ResolvedItem | undefined, desk: DeskDimensions, eyeHeight: number): ErgonomicCheck {
  const base = { id: 'viewing-distance' as const, title: 'Görüş mesafesi' }
  if (!primary) return { ...base, status: 'missing', message: 'Değerlendirme için sahneye bir monitör ekle.' }
  const eyeZ = eyePoint(desk, DEFAULT_USER_HEIGHT).z
  const screenCenter = desk.height + (primary.item.elevation ?? 0) + screenHeight(primary) / 2
  const distance = Math.hypot(eyeZ - primary.item.position.z, eyeHeight - screenCenter)
  // Wide panels need more distance to keep the edges within a comfortable field of view.
  const minimum = primary.definition.width >= 70 ? 60 : 50
  const maximum = 85
  const value = `${round(distance)} cm · önerilen ${minimum}–${maximum} cm`
  if (distance < minimum) return { ...base, value, status: 'warning', message: 'Ekran çok yakın; monitörü masanın arkasına doğru it.' }
  if (distance > maximum) return { ...base, value, status: 'warning', message: 'Ekran uzak; yazıları okumak için öne eğilmen gerekebilir.' }
  return { ...base, value, status: 'good', message: 'Ekran yaklaşık bir kol boyu uzaklıkta.' }
}

// Horizontal angle the screens span as seen from the eyes. Up to ~70° is covered by eye movement and a
// little head turn; a wider arc keeps the neck rotated toward the side screens.
function checkViewingArc(monitors: ResolvedItem[], eyeZ: number): ErgonomicCheck {
  const base = { id: 'viewing-arc' as const, title: 'Yatay görüş açısı' }
  if (!monitors.length) return { ...base, status: 'missing', message: 'Değerlendirme için sahneye bir monitör ekle.' }
  const angles = monitors.flatMap(({ item, definition }) => {
    const halfWidth = (item.mountMode === 'vesa' && item.screenOrientation === 'portrait' ? definition.height : definition.width) / 2
    const radians = (item.rotation * Math.PI) / 180
    return [-1, 1].map((side) => {
      const x = item.position.x + side * halfWidth * Math.cos(radians)
      const z = item.position.z + side * halfWidth * Math.sin(radians)
      return Math.atan2(x, eyeZ - z) * 180 / Math.PI
    })
  })
  const arc = Math.max(...angles) - Math.min(...angles)
  const value = `${round(arc)}°`
  if (arc > 100) return { ...base, value, status: 'warning', message: 'Ekranlar çok geniş bir yay çiziyor; yan ekranları içe döndür ya da biraz uzaklaştır.' }
  if (arc > 70) return { ...base, value, status: 'good', message: 'Yan ekranlar için hafif baş dönüşü gerekiyor; sık kullanılanı ortaya al.' }
  return { ...base, value, status: 'good', message: 'Ekranlar göz hareketiyle taranabilen açının içinde.' }
}

function checkScreenCentering(monitors: ResolvedItem[]): ErgonomicCheck {
  const base = { id: 'screen-centering' as const, title: 'Ekran ortalama' }
  if (!monitors.length) return { ...base, status: 'missing', message: 'Değerlendirme için sahneye bir monitör ekle.' }
  const center = monitors.reduce((sum, { item }) => sum + item.position.x, 0) / monitors.length
  const value = `${round(Math.abs(center))} cm ${center < 0 ? 'solda' : center > 0 ? 'sağda' : 'merkezde'}`
  if (Math.abs(center) > 15) return { ...base, value, status: 'warning', message: 'Ekran düzeni masanın ortasından kaymış; boyun sürekli bir yana döner.' }
  return { ...base, value, status: 'good', message: monitors.length > 1 ? 'Ekranlar oturma noktasına göre dengeli.' : 'Ekran oturma noktasının karşısında.' }
}

function checkKeyboardReach(keyboard: ResolvedItem | undefined, desk: DeskDimensions): ErgonomicCheck {
  const base = { id: 'keyboard-reach' as const, title: 'Klavye mesafesi' }
  if (!keyboard) return { ...base, status: 'missing', message: 'Değerlendirme için sahneye bir klavye ekle.' }
  const quarterTurn = Math.abs(keyboard.item.rotation % 180) === 90
  const depth = quarterTurn ? keyboard.definition.footprintWidth : keyboard.definition.footprintDepth
  const gap = desk.depth / 2 - (keyboard.item.position.z + depth / 2)
  const value = `ön kenara ${round(gap)} cm`
  if (gap < 3) return { ...base, value, status: 'warning', message: 'Klavye kenara çok yakın; bilekler masa kenarına dayanır.' }
  if (gap > 20) return { ...base, value, status: 'warning', message: 'Klavye uzak; omuzlar öne uzanır. Kenara 5–15 cm yaklaştır.' }
  return { ...base, value, status: 'good', message: 'Önkollar için yeterli destek alanı var.' }
}

export function analyzeErgonomics(
  items: PlacedEquipment[],
  desk: DeskDimensions,
  userHeight: number,
  catalog?: EquipmentDefinition[],
  posture: Posture = 'sitting',
): ErgonomicReport {
  const height = clampUserHeight(userHeight)
  const eyeHeight = eyePoint(desk, height, posture).height
  const resolved = items.flatMap((item) => {
    const definition = resolveEquipmentDefinition(item, catalog)
    return definition ? [{ item, definition }] : []
  })
  const byDistanceToCenter = (first: ResolvedItem, second: ResolvedItem) => Math.abs(first.item.position.x) - Math.abs(second.item.position.x)
  const monitors = resolved.filter(({ definition }) => definition.model === 'monitor').sort(byDistanceToCenter)
  const keyboard = resolved.filter(({ definition }) => definition.model === 'keyboard').sort(byDistanceToCenter)[0]

  const checks = [
    checkDeskHeight(desk, height, posture),
    checkScreenHeight(monitors[0], desk, eyeHeight),
    checkViewingDistance(monitors[0], desk, eyeHeight),
    checkScreenCentering(monitors),
    checkViewingArc(monitors, eyePoint(desk, height, posture).z),
    checkKeyboardReach(keyboard, desk),
  ]
  const applicable = checks.filter((check) => check.status !== 'missing')
  const score = applicable.length ? Math.round((applicable.filter((check) => check.status === 'good').length / applicable.length) * 100) : 0
  return { userHeight: height, eyeHeight: round(eyeHeight), checks, score }
}
