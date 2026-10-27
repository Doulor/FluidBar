interface BrandMarkProps {
  size?: number;
  className?: string;
}

export function BrandMark({ size = 28, className = '' }: BrandMarkProps) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 32 32"
      aria-hidden="true"
      focusable="false"
    >
      <rect width="32" height="32" rx="10" fill="#08080a" />
      <rect x="8" y="13" width="16" height="6" rx="3" fill="#f4f5f7" />
      <circle cx="21.5" cy="16" r="1.8" fill="#4cc9f0" />
    </svg>
  );
}
