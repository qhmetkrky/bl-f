import Constants from 'expo-constants';
import { Platform } from 'react-native';

export const API_PORT = 5080;

export type Profile = { id: string; name: string; token: string };
export type Leader = { id: string; name: string; wins: number; losses: number };

export type SeatView = {
  id: string;
  name: string;
  isBot: boolean;
  botLevel: BotLevel | null;
  connected: boolean;
  cardCount: number;
  wins: number;
};

export type ClaimView = { playerId: string; count: number; rank: string };

export type ChallengeView = {
  seq: number;
  challengerId: string;
  accusedId: string;
  declaredRank: string;
  revealed: string[];
  wasBluff: boolean;
  loserId: string;
  cardsTaken: number;
};

export type GameView = {
  currentPlayerId: string;
  roundRank: string | null;
  pileCount: number;
  declaredInRound: number;
  lastClaim: ClaimView | null;
  awaitingFinish: boolean;
  turnRemainingMs: number | null;
  hand: string[];
  winnerId: string | null;
  lastChallenge: ChallengeView | null;
};

export type RoomView = {
  code: string;
  status: 'lobby' | 'playing' | 'finished';
  version: number;
  hostId: string;
  you: string;
  turnSeconds: number;
  seats: SeatView[];
  game: GameView | null;
  log: string[];
};

export type BotLevel = 'easy' | 'medium' | 'hard';

export class UnauthorizedError extends Error {}

/** On a device, the backend usually runs on the same computer as the Expo dev server. */
export function defaultApiUrl(): string {
  const configured = process.env.EXPO_PUBLIC_API_URL;
  if (configured) return configured;

  // The production site is served by the backend itself, so it talks to its own origin.
  if (Platform.OS === 'web') {
    const { protocol, hostname, origin } = window.location;
    return __DEV__ ? `${protocol}//${hostname}:${API_PORT}` : origin;
  }

  const devHost = Constants.expoConfig?.hostUri?.split(':')[0];
  return `http://${devHost ?? 'localhost'}:${API_PORT}`;
}

async function request<T>(url: string, init: RequestInit = {}): Promise<T> {
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), 6000);
  try {
    const response = await fetch(url, {
      ...init,
      signal: abort.signal,
      headers: { 'Content-Type': 'application/json', ...init.headers },
    });
    if (response.status === 401) throw new UnauthorizedError();
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      throw new Error(body?.error ?? 'Sunucu isteği reddetti.');
    }
    return (await response.json()) as T;
  } catch (error) {
    if (error instanceof UnauthorizedError) throw error;
    if (error instanceof Error && error.message !== 'Network request failed' && error.name !== 'AbortError' && error.name !== 'TypeError') {
      throw error;
    }
    throw new Error('Sunucuya ulaşılamadı.');
  } finally {
    clearTimeout(timer);
  }
}

export const createPlayer = (baseUrl: string, name: string) =>
  request<Profile>(`${baseUrl}/api/players`, { method: 'POST', body: JSON.stringify({ name }) });

export const renamePlayer = (baseUrl: string, token: string, name: string) =>
  request<Leader>(`${baseUrl}/api/players/me`, {
    method: 'PUT',
    body: JSON.stringify({ name }),
    headers: { Authorization: `Bearer ${token}` },
  });

export const fetchLeaderboard = (baseUrl: string) => request<Leader[]>(`${baseUrl}/api/leaderboard`);
