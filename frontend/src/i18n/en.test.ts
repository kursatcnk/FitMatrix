import { describe, expect, it } from 'vitest'
import { equipmentCatalog } from '../data/equipmentCatalog'
import { en } from './en'

// Source text of every module, keyed by path relative to this folder.
const sources = Object.fromEntries(Object.entries(import.meta.glob<string>(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
})))
const source = (path: string) => sources[path] ?? ''

const unescape = (text: string) => text.replace(/\\(.)/g, '$1')
const literals = (source: string, pattern: RegExp) => [...source.matchAll(pattern)].map((match) => unescape(match[1]))
const objectValues = (source: string, declaration: string) => {
  const body = source.slice(source.indexOf(declaration)).split('\n}')[0]
  return literals(body, /: '((?:[^'\\]|\\.)*)'/g)
}

// Every Turkish source text that can reach the screen through t().
function translatableTexts() {
  const texts = new Set<string>()
  for (const text of Object.values(sources)) {
    for (const found of literals(text, /\bt\('((?:[^'\\]|\\.)*)'/g)) texts.add(found)
    for (const found of literals(text, /<NumberField label="([^"]+)"/g)) texts.add(found)
  }
  objectValues(source('../App.tsx'), 'const categoryLabels').forEach((text) => texts.add(text))
  objectValues(source('../editor/HarvestPanel.tsx'), 'const outcomeLabels').forEach((text) => texts.add(text))
  equipmentCatalog.forEach((definition) => texts.add(definition.name))
  // Single letters and units read the same in both languages.
  return [...texts].filter((text) => /\p{L}{2}/u.test(text))
}

const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort()

describe('English dictionary', () => {
  const texts = translatableTexts()

  it('finds the texts to check', () => {
    expect(texts.length).toBeGreaterThan(200)
  })

  it('translates every text shown through t()', () => {
    expect(texts.filter((text) => !(text in en))).toEqual([])
  })

  it('keeps the same placeholders as the Turkish source', () => {
    const mismatched = texts.filter((text) => text in en && placeholders(text).join() !== placeholders(en[text]).join())
    expect(mismatched).toEqual([])
  })

  it('has no stale entries', () => {
    expect(Object.keys(en).filter((text) => !texts.includes(text))).toEqual([])
  })
})
