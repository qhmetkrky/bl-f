import { Platform } from 'react-native';

export const colors = {
  felt: '#0D2B26',
  feltLight: '#14403A',
  feltLine: '#2A5C53',
  surface: '#17352F',
  cream: '#F5EEDF',
  creamDim: '#B9C4B6',
  brass: '#E0AC47',
  brassDark: '#8D6A25',
  red: '#D8453A',
  redDark: '#8F2A23',
  green: '#4FBF8B',
  ink: '#1C2320',
  cardRed: '#C2302A',
  cardBack: '#7E2A2A',
};

/** Widest the table gets, so it stays one column on a desktop browser. */
export const GAME_MAX_WIDTH = 760;

export const serif = Platform.select({ ios: 'Georgia', android: 'serif', default: 'Georgia, serif' });
