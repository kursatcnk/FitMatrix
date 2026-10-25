import { en } from './en'

export type Language = 'tr' | 'en'

const LANGUAGE_KEY = 'fitmatrix.language'

// Turkish is the source language: every message is written in Turkish at its call site and looked up
// in the English dictionary by that text, so a missing translation falls back to readable Turkish.
function detectLanguage(): Language {
  if (typeof window === 'undefined') return 'tr'
  try {
    const stored = localStorage.getItem(LANGUAGE_KEY)
    if (stored === 'tr' || stored === 'en') return stored
  } catch {
    // Storage can be unavailable in private windows; fall back to the browser language.
  }
  return navigator.language.toLowerCase().startsWith('tr') ? 'tr' : 'en'
}

let current: Language = detectLanguage()
if (typeof document !== 'undefined') document.documentElement.lang = current

export const getLanguage = () => current
export const getLocale = () => (current === 'en' ? 'en-GB' : 'tr-TR')

export function applyLanguage(language: Language) {
  current = language
  document.documentElement.lang = language
  try {
    localStorage.setItem(LANGUAGE_KEY, language)
  } catch {
    // The choice still applies for this session.
  }
}

export function t(text: string, params?: Record<string, string | number>) {
  const template = current === 'en' ? en[text] ?? text : text
  return params ? template.replace(/\{(\w+)\}/g, (match, key: string) => (key in params ? String(params[key]) : match)) : template
}
