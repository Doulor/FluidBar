import { Pause, Play } from '@phosphor-icons/react';
import { useEffect, useRef, useState } from 'react';
import { PUBLIC_SONGS } from '../data/publicSongs';
import './public-song-player.css';

interface PublicSongPlayerProps {
  index: number;
  onSelect: (index: number) => void;
}

function formatTime(value: number): string {
  if (!Number.isFinite(value) || value < 0) return '0:00';
  const minutes = Math.floor(value / 60);
  const seconds = Math.floor(value % 60);
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

export function PublicSongPlayer({ index, onSelect }: PublicSongPlayerProps) {
  const audioRef = useRef<HTMLAudioElement>(null);
  const [playing, setPlaying] = useState(false);
  const [time, setTime] = useState(0);
  const [duration, setDuration] = useState(PUBLIC_SONGS[index].duration);
  const song = PUBLIC_SONGS[index];
  const progress = duration > 0 ? Math.min(1, time / duration) : 0;
  const activeLine = Math.min(song.lyrics.length - 1, Math.floor(progress * song.lyrics.length));

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;
    audio.pause();
    audio.currentTime = 0;
    setPlaying(false);
    setTime(0);
    setDuration(song.duration);
  }, [song.id, song.duration]);

  async function togglePlayback() {
    const audio = audioRef.current;
    if (!audio) return;
    if (playing) {
      audio.pause();
      setPlaying(false);
      return;
    }
    try {
      await audio.play();
      setPlaying(true);
    } catch {
      setPlaying(false);
    }
  }

  return (
    <div className="public-song">
      <audio
        ref={audioRef}
        src={song.audio}
        preload="metadata"
        onTimeUpdate={(event) => setTime(event.currentTarget.currentTime)}
        onLoadedMetadata={(event) => setDuration(event.currentTarget.duration || song.duration)}
        onEnded={() => {
          setPlaying(false);
          setTime(0);
        }}
      />

      <div className="public-song__top">
        <div className="public-song__cover">
          <img src={song.cover} alt={`${song.title} 封面`} width="800" height="800" />
          <span className="public-song__number">0{index + 1}</span>
        </div>

        <div className="public-song__identity">
          <span className="public-song__eyebrow">PUBLIC DOMAIN · 器乐改编</span>
          <strong>{song.title}</strong>
          <span>{song.artist}</span>
        </div>

        <button
          className="public-song__play"
          type="button"
          onClick={togglePlayback}
          aria-label={playing ? `暂停${song.title}` : `播放${song.title}`}
        >
          {playing ? <Pause size={15} weight="fill" /> : <Play size={15} weight="fill" />}
        </button>
      </div>

      <div className="public-song__lyrics" aria-live="polite">
        {song.lyrics.map((line, lineIndex) => (
          <p className={lineIndex === activeLine ? 'is-active' : ''} key={line}>
            {line}
          </p>
        ))}
      </div>

      <div className="public-song__bottom">
        <div className="public-song__tabs" role="tablist" aria-label="公版曲目">
          {PUBLIC_SONGS.map((item, itemIndex) => (
            <button
              key={item.id}
              type="button"
              role="tab"
              aria-selected={itemIndex === index}
              className={itemIndex === index ? 'is-active' : ''}
              onClick={() => onSelect(itemIndex)}
            >
              {item.title}
            </button>
          ))}
        </div>
        <span className="public-song__time">
          {formatTime(time)} / {formatTime(duration)}
        </span>
      </div>

      <div className="public-song__progress" aria-hidden="true">
        <span style={{ width: `${progress * 100}%` }} />
      </div>
    </div>
  );
}
