import { ArrowUpRight, Play } from '@phosphor-icons/react';
import './netease-player.css';

const SONG_URL = 'https://music.163.com/#/song?id=26092806';
const COVER_URL =
  'https://p2.music.126.net/AoVFMKkLVMOmTMo_A6x02g==/109951163403515653.jpg?param=200y200';

export function NeteasePlayer() {
  return (
    <div className="netease-card">
      <a
        className="netease-card__cover"
        href={SONG_URL}
        target="_blank"
        rel="noopener noreferrer"
        aria-label="在网易云音乐打开 Take Me Hand"
      >
        <img
          src={COVER_URL}
          alt="Take Me Hand 官方封面"
          width="200"
          height="200"
          loading="eager"
          referrerPolicy="no-referrer"
        />
        <span className="netease-card__play" aria-hidden="true">
          <Play size={13} weight="fill" />
        </span>
      </a>

      <div className="netease-card__main">
        <span className="netease-card__eyebrow">OFFICIAL PREVIEW · 网易云音乐</span>
        <strong>Take Me Hand</strong>
        <span className="netease-card__artist">DAISHI DANCE / Cécile Corbel</span>
        <div className="netease-card__actions">
          <a href={SONG_URL} target="_blank" rel="noopener noreferrer">
            在网易云试听
            <ArrowUpRight size={11} aria-hidden="true" />
          </a>
          <a href={SONG_URL} target="_blank" rel="noopener noreferrer">
            官方歌词
            <ArrowUpRight size={11} aria-hidden="true" />
          </a>
        </div>
      </div>
    </div>
  );
}
