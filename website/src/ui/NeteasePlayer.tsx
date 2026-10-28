import { ArrowUpRight } from '@phosphor-icons/react';
import './netease-player.css';

const SONG_URL = 'https://music.163.com/#/song?id=26092806';

export function NeteasePlayer() {
  return (
    <div className="netease-card">
      <div className="netease-card__head">
        <span className="mono">OFFICIAL PREVIEW</span>
        <span className="netease-card__source">网易云音乐</span>
      </div>

      <div className="netease-card__frame">
        <iframe
          title="网易云音乐：Take Me Hand - DAISHI DANCE / Cécile Corbel"
          src="https://music.163.com/outchain/player?type=2&id=26092806&auto=0&height=66"
          width="540"
          height="66"
          frameBorder="0"
          allow="autoplay; encrypted-media"
          loading="eager"
        />
      </div>

      <div className="netease-card__foot">
        <span>官方封面 · 官方音源</span>
        <a href={SONG_URL} target="_blank" rel="noopener noreferrer">
          查看官方歌词
          <ArrowUpRight size={11} aria-hidden="true" />
        </a>
      </div>
    </div>
  );
}
