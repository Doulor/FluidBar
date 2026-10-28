export interface PublicSong {
  id: string;
  title: string;
  artist: string;
  kind: string;
  cover: string;
  audio: string;
  duration: number;
  lyrics: string[];
}

const asset = (path: string) => `${import.meta.env.BASE_URL}${path}`;

export const PUBLIC_SONGS: PublicSong[] = [
  {
    id: 'songbie',
    title: '送别',
    artist: '李叔同 / John P. Ordway',
    kind: '公版学堂乐歌 · 器乐改编',
    cover: asset('posters/cover-songbie.png'),
    audio: asset('audio/songbie.mp3'),
    duration: 53,
    lyrics: [
      '长亭外，古道边，芳草碧连天。',
      '晚风拂柳笛声残，夕阳山外山。',
      '天之涯，地之角，知交半零落。',
      '一壶浊酒尽余欢，今宵别梦寒。',
    ],
  },
  {
    id: 'kangding',
    title: '康定情歌',
    artist: '四川康定民歌',
    kind: '公版民歌 · 器乐改编',
    cover: asset('posters/cover-kangding.png'),
    audio: asset('audio/kangding.mp3'),
    duration: 44,
    lyrics: [
      '跑马溜溜的山上，一朵溜溜的云哟。',
      '端端溜溜的照在，康定溜溜的城哟。',
      '月亮弯弯，康定溜溜的城哟。',
      '世间溜溜的女子，任我溜溜地爱哟。',
    ],
  },
];
