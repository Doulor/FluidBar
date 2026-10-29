export const TAKE_ME_HAND_LYRICS = [
  'In my dreams',
  'I feel your light',
  'I feel love is born again',
  'Fireflies in the moonlight',
  'Rising stars',
  'Remember the day',
  'I fell in love with you',
  "Darling, won't you break my heart?",
  'Take my hand now',
  'Stay close to me, be my lover',
  "Won't you let me go?",
  'Close your eyes now',
  'And you will see',
  "There's a rainbow",
  'For you and me',
  'As I wake up I see your face',
  'I feel love is born again',
  'Cherry blossom, flying birds',
  'In the sky',
  "Can't you see the sun",
  'That is shining on the fields?',
  'Is it shining in your heart?',
  'Take my hand now',
  'Stay close to me, be my lover',
  "Won't you let me go?",
  'Close your eyes now',
  'And you will see',
  "There's a rainbow",
  'For you and me',
];

export const TAKE_ME_HAND_DURATION = 214;

export interface LyricFrame {
  lineIndex: number;
  wordIndex: number;
  wordProgress: number;
  windowStart: number;
  lines: string[];
}

export function lyricFrameAt(time: number): LyricFrame {
  const position = Math.min(0.9999, Math.max(0, time / TAKE_ME_HAND_DURATION));
  const lineIndex = Math.min(
    TAKE_ME_HAND_LYRICS.length - 1,
    Math.floor(position * TAKE_ME_HAND_LYRICS.length),
  );
  const line = TAKE_ME_HAND_LYRICS[lineIndex];
  const words = line.split(/\s+/);
  const withinLine = position * TAKE_ME_HAND_LYRICS.length - lineIndex;
  const wordIndex = Math.min(words.length - 1, Math.max(0, Math.floor(withinLine * words.length)));
  const wordProgress = Math.min(1, Math.max(0, withinLine * words.length - wordIndex));
  const windowStart = Math.max(
    0,
    Math.min(TAKE_ME_HAND_LYRICS.length - 3, lineIndex - 1),
  );

  return {
    lineIndex,
    wordIndex,
    wordProgress,
    windowStart,
    lines: TAKE_ME_HAND_LYRICS.slice(windowStart, windowStart + 3),
  };
}
