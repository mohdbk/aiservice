/** Small national flags for the language tabs and switcher (drawn here, nothing loaded from outside). */
export function FlagUae({ className = "h-3.5 w-5" }: { className?: string }) {
  return (
    <svg viewBox="0 0 12 6" className={`${className} flex-none rounded-[2px] ring-1 ring-black/10`} aria-hidden="true">
      <rect width="12" height="2" fill="#00732F" />
      <rect y="2" width="12" height="2" fill="#FFFFFF" />
      <rect y="4" width="12" height="2" fill="#000000" />
      <rect width="3" height="6" fill="#FF0000" />
    </svg>
  );
}

export function FlagUs({ className = "h-3.5 w-5" }: { className?: string }) {
  const stripes = Array.from({ length: 13 }, (_, i) => i);
  return (
    <svg viewBox="0 0 19 10" className={`${className} flex-none rounded-[2px] ring-1 ring-black/10`} aria-hidden="true">
      {stripes.map((i) => <rect key={i} y={(i * 10) / 13} width="19" height={10 / 13} fill={i % 2 ? "#FFFFFF" : "#B22234"} />)}
      <rect width="7.6" height={(10 * 7) / 13} fill="#3C3B6E" />
      {[0, 1, 2, 3].map((r) => [0, 1, 2, 3, 4].map((c) => (
        <circle key={`${r}-${c}`} cx={0.9 + c * 1.45 + (r % 2 ? 0.7 : 0)} cy={0.8 + r * 1.3} r="0.28" fill="#FFFFFF" />
      )))}
    </svg>
  );
}

export const LangFlag = ({ lang, className }: { lang: string; className?: string }) =>
  lang === "ar" ? <FlagUae className={className} /> : <FlagUs className={className} />;
